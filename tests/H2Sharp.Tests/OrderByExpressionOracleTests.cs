/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0.
 * SQL expression ordering against the pinned H2 2.5 JDBC implementation. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class OrderByExpressionOracleTests
{
    [JavaOracleFact]
    public void ExpressionsAliasesAndOrdinalsMatchJdbc()
    {
        CompareStatements([
            "CREATE TABLE T(ID INTEGER PRIMARY KEY, N INTEGER, NAME VARCHAR, BIRTH_DATE DATE)",
            "INSERT INTO T VALUES (1, 10, 'Zed', DATE '2024-03-02'), (2, 20, 'amy', DATE '2024-03-03'), "
                + "(3, 80, 'Bob', DATE '2024-03-04'), (4, -4, 'alice', DATE '2024-03-01'), (5, NULL, NULL, NULL)",
            "SELECT ID FROM T ORDER BY MOD(ID, 7)",
            "SELECT ID FROM T ORDER BY ID % 7",
            "SELECT ID, MOD(N, 7) AS M FROM T ORDER BY M NULLS LAST, ID",
            "SELECT ID FROM T ORDER BY MOD(N, 7) NULLS LAST, ID",
            "SELECT ID FROM T ORDER BY MOD(N, 7) DESC NULLS FIRST, ID DESC",
            "SELECT ID FROM T AS t ORDER BY MOD(ISO_DAY_OF_WEEK(t.BIRTH_DATE), 7) NULLS LAST, t.ID",
            "SELECT \"u\".\"ID\" FROM \"T\" AS \"u\" ORDER BY MOD(ISO_DAY_OF_WEEK(\"u\".\"BIRTH_DATE\"), 7) NULLS LAST, \"u\".\"ID\"",
            "SELECT ID FROM T ORDER BY 100 - ID",
            "SELECT ID FROM T ORDER BY CASE WHEN N IS NULL THEN 0 ELSE N END DESC, ID",
            "SELECT ID FROM T ORDER BY LOWER(NAME) NULLS LAST, ID",
            "SELECT ID FROM T ORDER BY N > 15 NULLS LAST, ID",
            "SELECT ID FROM T ORDER BY (N, ID) DESC NULLS LAST",
            "SELECT ID FROM T ORDER BY MOD(N, 7) NULLS LAST, ID LIMIT 2 OFFSET 1",
            "SELECT ID FROM T ORDER BY 1 DESC",
            "SELECT ID, N FROM T ORDER BY 2 NULLS LAST, 1",
            "SELECT ID FROM T ORDER BY 0",
            "SELECT ID FROM T ORDER BY 2",
            "SELECT ID FROM T ORDER BY 1 + 0, ID DESC",
            "SELECT ID FROM T ORDER BY (1), ID DESC",
            "SELECT ID FROM T ORDER BY -1, ID DESC",
            "SELECT ID FROM T ORDER BY 'constant', ID DESC",
            "SELECT ID AS A FROM T ORDER BY -A",
            "SELECT ID, MOD(N, 7) AS M FROM T ORDER BY -M NULLS LAST, ID",
            "SELECT ID, MOD(N, 7) AS M FROM T ORDER BY MOD(N, 7) NULLS LAST, ID",
            "SELECT DISTINCT N FROM T ORDER BY MOD(N, 7) NULLS LAST, N",
            "SELECT DISTINCT MOD(N, 7) AS M FROM T ORDER BY MOD(N, 7) NULLS LAST",
            "SELECT DISTINCT N FROM T ORDER BY ID",
            "SELECT ID FROM T ORDER BY MOD(MISSING, 7)",
            "SELECT MOD(ID, 2) AS G, COUNT(*) AS C FROM T GROUP BY MOD(ID, 2) ORDER BY COUNT(*) DESC, G",
        ]);
    }

    [JavaOracleFact]
    public void BoundSortExpressionsCanBeReexecuted()
    {
        const string Sql = "SELECT ID FROM T ORDER BY MOD(N, ?) NULLS LAST, ID LIMIT ?";
        string[] inputs = ["7", "3", "9", "2", "3", "4"];
        using H2Connection connection = Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE T(ID INT PRIMARY KEY, N INT)";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO T VALUES (1, 10), (2, 20), (3, 80), (4, NULL)";
        command.ExecuteNonQuery();
        command.CommandText = Sql;
        for (int i = 0; i < 2; i++)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.Value = int.Parse(inputs[i], CultureInfo.InvariantCulture);
            command.Parameters.Add(parameter);
        }
        command.Prepare();
        List<string> port = [];
        for (int i = 0; i < inputs.Length; i += 2)
        {
            for (int p = 0; p < 2; p++) command.Parameters[p].Value = int.Parse(inputs[i + p], CultureInfo.InvariantCulture);
            Record(command, port, i);
        }
        List<string> java = OracleRunner.Run(inputs,
            """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:sort_parameters")) {
                try (java.sql.Statement setup = conn.createStatement()) {
                    setup.execute("CREATE TABLE T(ID INT PRIMARY KEY, N INT)");
                    setup.execute("INSERT INTO T VALUES (1, 10), (2, 20), (3, 80), (4, NULL)");
                }
                try (java.sql.PreparedStatement s = conn.prepareStatement("SELECT ID FROM T ORDER BY MOD(N, ?) NULLS LAST, ID LIMIT ?")) {
                    for (int i = 0; i < inputs.length; i += 2) {
                        s.setInt(1, Integer.parseInt(inputs[i])); s.setInt(2, Integer.parseInt(inputs[i + 1]));
                        try (java.sql.ResultSet r = s.executeQuery()) {
                            System.out.println("=" + i + ":C:" + r.getMetaData().getColumnCount());
                            while (r.next()) System.out.println("=" + i + ":R:" + r.getString(1));
                            System.out.println("=" + i + ":Q");
                        }
                    }
                }
            }
            """);
        Assert.Equal(java, port);
    }

    [JavaOracleFact]
    public void PositionalOrderParameterNeedsCommandRecompilation()
    {
        using H2Connection connection = Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE T(ID INT PRIMARY KEY, N INT)";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO T VALUES (1, 80), (2, 10), (3, 20)";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT ID, N FROM T ORDER BY ?";
        DbParameter parameter = command.CreateParameter();
        parameter.Value = 2;
        command.Parameters.Add(parameter);
        List<string> java = OracleRunner.Run([], """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:order_position_parameter")) {
                try (java.sql.Statement setup = conn.createStatement()) {
                    setup.execute("CREATE TABLE T(ID INT PRIMARY KEY, N INT)");
                    setup.execute("INSERT INTO T VALUES (1, 80), (2, 10), (3, 20)");
                }
                try (java.sql.PreparedStatement s = conn.prepareStatement("SELECT ID, N FROM T ORDER BY ?")) {
                    s.setInt(1, 2);
                    try (java.sql.ResultSet r = s.executeQuery()) {
                        System.out.println("=0:C:" + r.getMetaData().getColumnCount());
                        while (r.next()) System.out.println("=0:R:" + r.getString(1) + "|" + r.getString(2));
                        System.out.println("=0:Q");
                    }
                }
            }
            """);
        List<string> port = [];
        Record(command, port, 0);
        Assert.Equal(java, port);
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("Data Source=order_expression_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        return connection;
    }

    private static void CompareStatements(string[] statements)
    {
        using H2Connection connection = Open();
        List<string> port = [];
        for (int i = 0; i < statements.Length; i++)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = statements[i];
            Record(command, port, i);
        }
        List<string> java = OracleRunner.Run(statements,
            """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:order_expressions")) {
                for (int i = 0; i < inputs.length; i++) {
                    try (java.sql.Statement s = conn.createStatement()) {
                        if (s.execute(inputs[i])) {
                            try (java.sql.ResultSet r = s.getResultSet()) {
                                int count = r.getMetaData().getColumnCount();
                                System.out.println("=" + i + ":C:" + count);
                                while (r.next()) {
                                    StringBuilder line = new StringBuilder("=" + i + ":R:");
                                    for (int c = 1; c <= count; c++) {
                                        if (c > 1) line.append('|');
                                        String text = r.getString(c);
                                        line.append(r.wasNull() ? "NULL" : text);
                                    }
                                    System.out.println(line);
                                }
                                System.out.println("=" + i + ":Q");
                            }
                        } else System.out.println("=" + i + ":U:" + s.getUpdateCount());
                    } catch (java.sql.SQLException e) { System.out.println("=" + i + ":E:" + e.getErrorCode()); }
                }
            }
            """);
        Assert.True(java.SequenceEqual(port), "JVM:\n" + string.Join('\n', java) + "\nPort:\n" + string.Join('\n', port));
    }

    private static void Record(DbCommand command, List<string> lines, int index)
    {
        string prefix = "=" + index.ToString(CultureInfo.InvariantCulture) + ':';
        try
        {
            if (!command.CommandText.StartsWith("SELECT", StringComparison.Ordinal))
            {
                lines.Add(prefix + "U:" + command.ExecuteNonQuery().ToString(CultureInfo.InvariantCulture));
                return;
            }
            using DbDataReader reader = command.ExecuteReader();
            lines.Add(prefix + "C:" + reader.FieldCount.ToString(CultureInfo.InvariantCulture));
            while (reader.Read())
            {
                List<string> cells = [];
                for (int c = 0; c < reader.FieldCount; c++) cells.Add(reader.IsDBNull(c) ? "NULL" : reader.GetString(c));
                lines.Add(prefix + "R:" + string.Join('|', cells));
            }
            lines.Add(prefix + "Q");
        }
        catch (H2SharpException error) { lines.Add(prefix + "E:" + error.H2ErrorCode.ToString(CultureInfo.InvariantCulture)); }
    }
}
