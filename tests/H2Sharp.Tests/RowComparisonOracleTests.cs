/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * SQL row comparisons against the pinned H2 2.5 JDBC implementation.
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
public sealed class RowComparisonOracleTests
{
    [JavaOracleFact]
    public void CompositeCursorAndRowComparisonsMatchJdbc()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INTEGER PRIMARY KEY, N INTEGER)",
            "INSERT INTO T VALUES (1, 10), (2, 20), (3, 80), (4, 20), (5, NULL)",
            // Exact reproduction from issue #18, followed by the equal-first-field case.
            "SELECT ID FROM T WHERE (N, ID) > (15, 1) ORDER BY ID",
            "SELECT ID FROM T WHERE (N, ID) > (20, 2) ORDER BY N, ID",
            "SELECT ID FROM T WHERE ROW(N, ID) > ROW(20, 2) ORDER BY N, ID",
            "SELECT ID FROM T WHERE (N, ID) >= (20, 2) ORDER BY N, ID",
            "SELECT ID FROM T WHERE (N, ID) < (20, 4) ORDER BY N, ID",
            "SELECT ID FROM T WHERE (N, ID) <= (20, 4) ORDER BY N, ID",
            "SELECT ID FROM T WHERE (N, ID) = (20, 2)",
            "SELECT ID FROM T WHERE (N, ID) <> (20, 2) ORDER BY ID",
            "SELECT t.ID FROM T AS t WHERE (t.N, t.ID) > (20, 2) ORDER BY t.N, t.ID LIMIT 1",
            "SELECT (1, NULL) < (2, 0), (1, NULL) > (0, 0), (1, NULL) < (1, 2)",
            "SELECT (1, NULL) = (2, NULL), (1, NULL) <> (2, NULL), (1, NULL) = (1, NULL)",
            "SELECT ROW(NULL, 1) IS NOT DISTINCT FROM ROW(NULL, 1), ROW(NULL, 1) IS DISTINCT FROM ROW(NULL, 2)",
            "SELECT ROW(1) = 1, ROW(1) < 2, (1, 2) = (1, 2, 3)",
            "SELECT ((1, 2), 3) > ((1, 1), 99)",
            "SELECT (1, 'b') > (1, 'a'), (1, 2.5) < (1, 3)",
            "SELECT () = (), ROW() = ()",
            "SELECT (1, 2) IS NULL, (NULL, NULL) IS NULL",
            "SELECT (N, ID) > (1, 2, 3) FROM T",
            "CREATE INDEX CURSOR_INDEX ON T(N, ID)",
            "SELECT ID FROM T WHERE (N, ID) > (20, 2) ORDER BY N, ID",
        ];
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
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:row_comparison")) {
                for (int i = 0; i < inputs.length; i++) {
                    try (java.sql.Statement s = conn.createStatement()) {
                        if (s.execute(inputs[i])) {
                            try (java.sql.ResultSet r = s.getResultSet()) {
                                while (r.next()) {
                                    StringBuilder line = new StringBuilder("=" + i + ":R:");
                                    for (int c = 1; c <= r.getMetaData().getColumnCount(); c++) {
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

    [JavaOracleFact]
    public void BoundCompositeCursorCanBeExecutedWithDifferentValues()
    {
        const string Sql = "SELECT ID FROM T WHERE (N, ID) > (?, ?) ORDER BY N, ID LIMIT 2";
        string[] inputs = ["15", "1", "20", "2", "20", "4", "80", "3"];
        using H2Connection connection = Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE T(ID INT PRIMARY KEY, N INT)";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO T VALUES (1, 10), (2, 20), (3, 80), (4, 20)";
        command.ExecuteNonQuery();
        command.CommandText = Sql;
        for (int i = 0; i < 2; i++)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.Value = 0;
            command.Parameters.Add(parameter);
        }

        command.Prepare();
        List<string> port = [];
        for (int i = 0; i < inputs.Length; i += 2)
        {
            command.Parameters[0].Value = int.Parse(inputs[i], CultureInfo.InvariantCulture);
            command.Parameters[1].Value = int.Parse(inputs[i + 1], CultureInfo.InvariantCulture);
            Record(command, port, i);
        }

        List<string> java = OracleRunner.Run(inputs,
            """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:row_parameters")) {
                java.sql.Statement setup = conn.createStatement();
                setup.execute("CREATE TABLE T(ID INT PRIMARY KEY, N INT)");
                setup.execute("INSERT INTO T VALUES (1, 10), (2, 20), (3, 80), (4, 20)");
                try (java.sql.PreparedStatement s = conn.prepareStatement("SELECT ID FROM T WHERE (N, ID) > (?, ?) ORDER BY N, ID LIMIT 2")) {
                    for (int i = 0; i < inputs.length; i += 2) {
                        s.setInt(1, Integer.parseInt(inputs[i]));
                        s.setInt(2, Integer.parseInt(inputs[i + 1]));
                        try (java.sql.ResultSet r = s.executeQuery()) {
                            while (r.next()) System.out.println("=" + i + ":R:" + r.getString(1));
                            System.out.println("=" + i + ":Q");
                        }
                    }
                }
            }
            """);
        Assert.Equal(java, port);
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("Data Source=row_compare_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        return connection;
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
            while (reader.Read())
            {
                List<string> cells = [];
                for (int i = 0; i < reader.FieldCount; i++) cells.Add(reader.IsDBNull(i) ? "NULL" : reader.GetString(i));
                lines.Add(prefix + "R:" + string.Join('|', cells));
            }

            lines.Add(prefix + "Q");
        }
        catch (H2SharpException e) { lines.Add(prefix + "E:" + e.H2ErrorCode.ToString(CultureInfo.InvariantCulture)); }
    }
}
