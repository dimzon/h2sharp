/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 84: line-targeted coverage of TableValueConstructor — aggregate stages,
 * determinism visits, unknown-type columns and the column resolver.
 *
 * Only SELECT over a VALUES derived table is compared: the shared oracle helper
 * runs a statement as a query only when it starts with SELECT/EXPLAIN, while a
 * bare VALUES is a query on both sides. Plans with a pushed condition are left
 * out because the port's plan renderer does not print the condition marker.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using H2Sharp;
using H2Sharp.Data;
using H2Sharp.JavaLegacy;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class TableValueConstructorCoverageOracleTests
{
    [JavaOracleFact]
    public void AggregatesOverValuesMatchJdbc()
    {
        ExplainOracleTests.Check([
            "SELECT SUM(X) FROM (VALUES (1), (2), (3)) T(X)",
            "SELECT COUNT(*), MIN(X), MAX(X) FROM (VALUES (1), (2), (3)) T(X)",
            "SELECT X, COUNT(*) FROM (VALUES (1), (1), (2)) T(X) GROUP BY X ORDER BY X",
            "SELECT * FROM (VALUES (1), (2)) T(X) ORDER BY X",
        ]);
    }

    [JavaOracleFact]
    public void ValuesPlansMatchJdbc()
    {
        ExplainOracleTests.Check([
            "EXPLAIN SELECT * FROM (VALUES (1), (2)) T(X)",
            "SELECT * FROM (VALUES (1), (2)) T(X) WHERE X = 1",
            "SELECT * FROM (VALUES (1), (2)) T(X) WHERE X = 2",
        ]);
    }

    [JavaOracleFact]
    public void UnknownTypeAndResolversMatchJdbc()
    {
        ExplainOracleTests.Check([
            "SELECT * FROM (VALUES (NULL)) T(X)",
            "SELECT * FROM (VALUES (NULL), (1)) T(X)",
            "SELECT X FROM (VALUES (1)) T(X)",
            "SELECT T.X FROM (VALUES (1), (2)) T(X) WHERE T.X > 1",
        ]);
    }

    /// <summary>
    /// A non-deterministic expression in VALUES is visited while the query cache
    /// decides whether the statement is reusable. The value itself is not compared:
    /// the default session generator is System.Random (a documented difference), so
    /// only <see cref="ValuesRandWithJavaLegacyMatchesJdbc"/> compares the draws.
    /// </summary>
    /// <summary>
    /// Values built from parameters: the cell types are unknown while the constructor is
    /// initialized, so the columns fall back to VARCHAR exactly as they do on the JVM.
    /// Every statement takes two parameters, bound to 1 and 2 on both sides.
    /// </summary>
    [JavaOracleFact]
    public void ParameterizedValuesMatchJdbc()
    {
        string[] statements =
        [
            "SELECT X FROM (VALUES (?), (?)) T(X) ORDER BY X",
            "SELECT * FROM (VALUES (?), (?)) T(X)",
            "SELECT * FROM (VALUES (?), (?)) T(X) WHERE X > 1",
            "SELECT COUNT(*), MIN(X), MAX(X) FROM (VALUES (?), (?)) T(X)",
        ];
        List<string> port = RunPortParameters(statements);
        List<string> java = OracleRunner.Run(statements, """
            java.sql.Connection c = java.sql.DriverManager.getConnection("jdbc:h2:mem:tvc_params", "sa", "");
            int[] values = { 1, 2 };
            for (int i = 0; i < inputs.length; i++) {
              try {
                java.sql.PreparedStatement s = c.prepareStatement(inputs[i]);
                for (int p = 0; p < values.length; p++) {
                  s.setObject(p + 1, values[p]);
                }
                java.sql.ResultSet r = s.executeQuery();
                java.sql.ResultSetMetaData m = r.getMetaData();
                while (r.next()) {
                  StringBuilder b = new StringBuilder("=");
                  for (int j = 1; j <= m.getColumnCount(); j++) {
                    if (j > 1) {
                      b.append(':');
                    }
                    Object o = r.getObject(j);
                    b.append(o == null ? "-" : o.toString());
                  }
                  System.out.println(b);
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            c.close();
            """);
        Assert.True(java.Count == port.Count,
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == port[i],
                "line " + i + ":\n  jvm:  " + java[i] + "\n  port: " + port[i]);
        }
    }

    private static List<string> RunPortParameters(IReadOnlyList<string> statements)
    {
        using H2Connection connection = new(
            "Data Source=tvc_params_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        List<string> lines = [];
        foreach (string sql in statements)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (int value in new[] { 1, 2 })
            {
                DbParameter parameter = command.CreateParameter();
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }

            try
            {
                using DbDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    List<string> cells = [];
                    for (int c = 0; c < reader.FieldCount; c++)
                    {
                        cells.Add(reader.IsDBNull(c)
                            ? "-"
                            : Convert.ToString(reader.GetValue(c),
                                System.Globalization.CultureInfo.InvariantCulture) ?? "-");
                    }

                    lines.Add("=" + string.Join(':', cells));
                }
            }
            catch (Exception e)
            {
                lines.Add("=" + (e as H2SharpException)?.H2ErrorCode.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) ?? e.GetType().Name);
            }
        }

        return lines;
    }

    [Fact]
    public void NonDeterministicValuesRun()
    {
        using H2Connection connection = new(
            "Data Source=tvc_rand_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT X FROM (VALUES (RAND())) T(X)";
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.False(reader.IsDBNull(0));
    }

    /// <summary>
    /// The same seeded draws as the JVM when the connection opts into the JDK generator.
    /// Without that opt-in the session uses System.Random (a documented difference), which
    /// is why only this variant compares the numbers.
    /// </summary>
    [JavaOracleFact]
    public void ValuesRandWithJavaLegacyMatchesJdbc()
    {
        string[] statements =
        [
            "SELECT * FROM (VALUES (RAND(1))) T(X)",
            "SELECT X FROM (VALUES (RAND(1)), (RAND(1))) T(X)",
        ];
        List<string> port = RunPortWithJavaRandom(statements);
        List<string> java = OracleRunner.Run(statements, """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:tvc_rand");
            java.sql.Statement stat = conn.createStatement();
            for (int i = 0; i < inputs.length; i++) {
              java.sql.ResultSet rs = stat.executeQuery(inputs[i]);
              while (rs.next()) {
                String s = rs.getString(1);
                System.out.println("=" + (rs.wasNull() ? "-" : s));
              }
            }
            conn.close();
            """, extraJavaArgs: ["-Duser.language=en"]);
        Assert.True(java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    private static List<string> RunPortWithJavaRandom(IReadOnlyList<string> statements)
    {
        using H2Connection connection = new(
            "Data Source=tvc_rand_java_" + Guid.NewGuid().ToString("N"));
        connection.UseJavaLegacy();
        connection.Open();
        List<string> lines = [];
        foreach (string sql in statements)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            using DbDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                lines.Add("=" + (reader.IsDBNull(0)
                    ? "-"
                    : Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        return lines;
    }
}