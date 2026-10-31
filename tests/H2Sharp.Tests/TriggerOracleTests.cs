/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The behavior of the first stage of the trigger boundary, checked against the reference
 * running the same DML: a BEFORE trigger rewrites the row, a statement trigger fires once
 * per statement in the same transaction, and a throwing trigger aborts the statement.
 * The reference registers its triggers with CREATE TRIGGER ... AS; the port registers the
 * equivalent .NET triggers through the data source, and the observable answers (selected
 * values, audit rows, error markers) must match line for line.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class TriggerOracleTests
{
    /// <summary>
    /// The statements the two sides run after their triggers are in place.
    /// </summary>
    private static readonly string[] Statements =
    [
        "INSERT INTO TEST VALUES (1, 5)",
        "SELECT VAL FROM TEST WHERE ID=1",
        "INSERT INTO TEST VALUES (2, 6),(3, 7)",
        "SELECT COUNT(*) FROM AUDIT",
        "INSERT INTO BAD VALUES (7)",
        "SELECT COUNT(*) FROM BAD",
    ];

    [JavaOracleFact]
    public void TheTriggerBehaviorIsTheSameAsTheReference()
    {
        List<string> port = RunPort();
        List<string> java = RunJava();
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Runs the statements on the port with .NET triggers registered by predicate, and
    /// writes what the question answered.
    /// </summary>
    private static List<string> RunPort()
    {
        using H2DataSource dataSource = new H2DataSourceBuilder(
            "Data Source=trigger_oracle;IGNORE_UNKNOWN_SETTINGS=TRUE;Storage=Memory").Build();
        using H2Connection connection = (H2Connection)dataSource.CreateConnection();
        connection.Open();

        Exec(connection, "CREATE TABLE TEST(ID INT, VAL INT)");
        Exec(connection, "CREATE TABLE AUDIT(ID INT)");
        Exec(connection, "CREATE TABLE BAD(ID INT)");

        dataSource.RegisterTrigger(
            "RW", (_, name) => name == "TEST", H2TriggerEvent.Insert,
            before: true, rowBased: true, () => new RewriteTrigger());
        dataSource.RegisterTrigger(
            "STMT", (_, name) => name == "TEST", H2TriggerEvent.Insert,
            before: false, rowBased: false, () => new AuditStatementTrigger());
        dataSource.RegisterTrigger(
            "BAD", (_, name) => name == "BAD", H2TriggerEvent.Insert,
            before: true, rowBased: true, () => new ThrowingTrigger());

        List<string> lines = [];
        foreach (string sql in Statements)
        {
            try
            {
                if (sql.StartsWith("SELECT", StringComparison.Ordinal))
                {
                    lines.Add("=" + Scalar(connection, sql));
                }
                else
                {
                    Exec(connection, sql);
                }
            }
            catch
            {
                lines.Add("=ERR");
            }
        }

        return lines;
    }

    /// <summary>
    /// Runs the statements on the reference with CREATE TRIGGER ... AS triggers, and
    /// writes what the question answered.
    /// </summary>
    private static List<string> RunJava()
        => OracleRunner.Run(
            Statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:trigger_oracle");
            java.sql.Statement stat = conn.createStatement();
            stat.execute("CREATE TABLE TEST(ID INT, VAL INT)");
            stat.execute("CREATE TABLE AUDIT(ID INT)");
            stat.execute("CREATE TABLE BAD(ID INT)");
            stat.execute("CREATE TRIGGER T_RW BEFORE INSERT ON TEST FOR EACH ROW AS 'org.h2.api.Trigger create() { return new org.h2.tools.TriggerAdapter() { public void fire(java.sql.Connection c, java.sql.ResultSet oldRow, java.sql.ResultSet newRow) throws java.sql.SQLException { newRow.updateInt(\"VAL\", 999); } }; }'");
            stat.execute("CREATE TRIGGER T_STMT AFTER INSERT ON TEST FOR EACH STATEMENT AS 'org.h2.api.Trigger create() { return new org.h2.api.Trigger() { public void init(java.sql.Connection c, String s1, String s2, String s3, boolean b, int t) {} public void fire(java.sql.Connection conn, Object[] oldRow, Object[] newRow) throws java.sql.SQLException { conn.setAutoCommit(false); conn.createStatement().executeUpdate(\"INSERT INTO AUDIT(ID) VALUES (1)\"); } public void close() {} public void remove() {} }; }'");
            stat.execute("CREATE TRIGGER T_BAD BEFORE INSERT ON BAD FOR EACH ROW AS 'org.h2.api.Trigger create() { return new org.h2.api.Trigger() { public void init(java.sql.Connection c, String s1, String s2, String s3, boolean b, int t) {} public void fire(java.sql.Connection conn, Object[] oldRow, Object[] newRow) throws java.sql.SQLException { throw new java.sql.SQLException(\"boom\"); } public void close() {} public void remove() {} }; }'");
            for (int i = 0; i < inputs.length; i++) {
              try {
                boolean isQuery = stat.execute(inputs[i]);
                if (isQuery) {
                  java.sql.ResultSet rs = stat.getResultSet();
                  while (rs.next()) {
                    String s = rs.getString(1);
                    System.out.println("=" + (rs.wasNull() ? "-" : s));
                  }
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=ERR");
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);

    private static void Exec(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Scalar(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? "-"
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "-";
    }

    /// <summary>
    /// The port's rewrite trigger: the BEFORE INSERT trigger sets VAL to 999, the same
    /// observable result as the reference's TriggerAdapter updateInt.
    /// </summary>
    private sealed class RewriteTrigger : IH2Trigger
    {
        public void Init(IH2TriggerContext context)
        {
        }

        public void Fire(IH2TriggerRow? oldRow, IH2TriggerRow? newRow)
        {
            int index = 0;
            for (int i = 0; i < newRow!.ColumnNames.Count; i++)
            {
                if (string.Equals(newRow.ColumnNames[i], "VAL", StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                }
            }

            newRow.SetValue(index, 999);
        }

        public void Close()
        {
        }

        public void Remove()
        {
        }
    }

    /// <summary>
    /// The port's statement trigger: one row in AUDIT per INSERT statement, through the
    /// same transaction as the firing statement.
    /// </summary>
    private sealed class AuditStatementTrigger : IH2Trigger
    {
        private IH2TriggerContext? context;

        public void Init(IH2TriggerContext context) => this.context = context;

        public void Fire(IH2TriggerRow? oldRow, IH2TriggerRow? newRow)
        {
            H2Transaction transaction = context!.GetCurrentTransaction();
            using var command = new H2Command("INSERT INTO AUDIT(ID) VALUES (1)", transaction.Connection!);
            command.Transaction = transaction;
            command.ExecuteNonQuery();
        }

        public void Close()
        {
        }

        public void Remove()
        {
        }
    }

    /// <summary>
    /// The port's throwing trigger: aborts the statement, as the reference's throwing
    /// trigger does.
    /// </summary>
    private sealed class ThrowingTrigger : IH2Trigger
    {
        public void Init(IH2TriggerContext context)
        {
        }

        public void Fire(IH2TriggerRow? oldRow, IH2TriggerRow? newRow)
            => throw new InvalidOperationException("boom");

        public void Close()
        {
        }

        public void Remove()
        {
        }
    }
}
