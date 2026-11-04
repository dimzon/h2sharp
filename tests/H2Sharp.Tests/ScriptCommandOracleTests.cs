/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * SCRIPT and its options: the SQL the writer prints for a schema with domains, sequences,
 * a table with identity, generated and default expressions, an explicit primary key, an
 * index, a view, LOB data, a second schema and comments, under the default, COLUMNS,
 * SIMPLE, DROP, NODATA and BLOCKSIZE forms.
 *
 * The reference prints a CREATE USER line for its built-in user and the port has no users
 * (D-004), so those lines are dropped before the comparison. RUNSCRIPT is not ported at
 * all (there is no RunScriptCommand or ScriptReader), which is recorded as BlockedByPort.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ScriptCommandOracleTests
{
    private static readonly string[] SchemaStatements =
    [
        "CREATE SCHEMA S",
        "CREATE DOMAIN D AS INT",
        "CREATE SEQUENCE SEQ START WITH 10 INCREMENT BY 2",
        "CREATE TABLE T(ID BIGINT GENERATED ALWAYS AS IDENTITY, V INT DEFAULT 5, "
            + "G INT GENERATED ALWAYS AS (V + 1), S VARCHAR(20), B BLOB, C CLOB, N NUMERIC(10,2), "
            + "CONSTRAINT PK_T PRIMARY KEY(ID))",
        "CREATE INDEX IDX_T_V ON T(V)",
        "CREATE TABLE S.T2(A INT PRIMARY KEY, B INT)",
        "INSERT INTO T(V, S, B, C, N) VALUES (1, 'one', X'0102', 'clob1', 1.25), "
            + "(NULL, NULL, NULL, NULL, NULL), (3, 'three', X'FF', 'clob3', -2.5)",
        "INSERT INTO S.T2 VALUES (1, 10), (2, 20)",
        "CREATE VIEW V AS SELECT ID, V FROM T WHERE V IS NOT NULL",
        "COMMENT ON TABLE T IS 'table comment'",
        "COMMENT ON COLUMN T.V IS 'column comment'",
    ];

    [JavaOracleFact]
    public void TheDefaultScriptMatchesJdbc() => CompareScript("NOPASSWORDS NOSETTINGS NOVERSION");

    [JavaOracleFact]
    public void TheColumnsScriptMatchesJdbc() => CompareScript("COLUMNS NOPASSWORDS NOSETTINGS NOVERSION");

    [JavaOracleFact]
    public void TheSimpleScriptMatchesJdbc() => CompareScript("SIMPLE NOPASSWORDS NOSETTINGS NOVERSION");

    [JavaOracleFact]
    public void TheDropScriptMatchesJdbc() => CompareScript("NOPASSWORDS NOSETTINGS NOVERSION DROP");

    [JavaOracleFact]
    public void TheNoDataScriptMatchesJdbc() => CompareScript("NODATA NOPASSWORDS NOSETTINGS NOVERSION");

    [JavaOracleFact]
    public void TheBlockSizeScriptMatchesJdbc() => CompareScript("NOPASSWORDS NOSETTINGS NOVERSION BLOCKSIZE 4");

    [JavaOracleFact]
    [Trait("Category", "BlockedByPort")]
    public void RunScriptRoundTripsTheScript()
    {
        // The reference's RunScriptCommand, its ScriptBase and ScriptReader are not ported:
        // RUNSCRIPT FROM is not a parser command in the port. Until they are, the text this
        // suite compares is the only part of the write/read pair that can be checked.
        string file = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "h2sharp_runscript_" + Guid.NewGuid().ToString("N") + ".sql");
        string quoted = "'" + file.Replace('\\', '/') + "'";
        List<string> statements = [.. SchemaStatements];
        statements.Add("SCRIPT NOPASSWORDS NOSETTINGS NOVERSION TO " + quoted);
        statements.Add("DROP ALL OBJECTS");
        statements.Add("RUNSCRIPT FROM " + quoted);

        using H2Connection connection = new(
            "Data Source=runscript_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        foreach (string sql in statements)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            _ = command.ExecuteNonQuery();
        }

        using (DbCommand probe = connection.CreateCommand())
        {
            probe.CommandText = "SELECT COUNT(*) FROM T";
            Assert.Equal("0", probe.ExecuteScalar()!.ToString());
        }
    }

    private static void CompareScript(string options)
    {
        List<string> statements = [.. SchemaStatements];
        statements.Add("SCRIPT " + options);

        // The port returns one row per statement and a statement may span lines; the Java
        // side captures the physical lines the reference prints, so both are flattened the
        // same way before they are compared.
        List<string> port = [];
        foreach (string row in RunPort(statements))
        {
            port.AddRange(row.Replace("\r", "").Split('\n'));
        }

        List<string> java = OracleRunner.Run(statements, JavaBody, extraJavaArgs: ["-Duser.language=en"]);
        port.RemoveAll(static line => line.StartsWith("CREATE USER", StringComparison.Ordinal));
        java.RemoveAll(static line => line.StartsWith("CREATE USER", StringComparison.Ordinal));

        Assert.Equal(java.Count, port.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == port[i], "line " + i + ":\n  jvm:  " + java[i] + "\n  port: " + port[i]);
        }
    }

    private static List<string> RunPort(List<string> statements)
    {
        using H2Connection connection = new(
            "Data Source=script_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        List<string> output = [];
        foreach (string sql in statements)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            if (sql.StartsWith("SELECT", StringComparison.Ordinal)
                || sql.StartsWith("SCRIPT", StringComparison.Ordinal))
            {
                using DbDataReader rows = command.ExecuteReader();
                while (rows.Read())
                {
                    List<string> cells = [];
                    for (int c = 0; c < rows.FieldCount; c++)
                    {
                        cells.Add(rows.IsDBNull(c) ? "NULL" : rows.GetString(c));
                    }

                    output.Add(string.Join('|', cells));
                }
            }
            else
            {
                _ = command.ExecuteNonQuery();
            }
        }

        return output;
    }

    private const string JavaBody = """
        try (java.sql.Connection conn = java.sql.DriverManager.getConnection(
                "jdbc:h2:mem:" + java.util.UUID.randomUUID().toString().replace("-", ""), "sa", "")) {
            for (String sql : inputs) {
                try (java.sql.Statement s = conn.createStatement()) {
                    if (s.execute(sql)) {
                        try (java.sql.ResultSet r = s.getResultSet()) {
                            while (r.next()) {
                                StringBuilder line = new StringBuilder();
                                for (int c = 1; c <= r.getMetaData().getColumnCount(); c++) {
                                    if (c > 1) line.append('|');
                                    String v = r.getString(c);
                                    line.append(r.wasNull() ? "NULL" : v);
                                }
                                System.out.println(line);
                            }
                        }
                    }
                }
            }
        }
        """;
}
