/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0.
 * EXISTS queries compared statement by statement with pinned H2 2.5 JDBC. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ExistsOracleTests
{
    [JavaOracleFact]
    public void ExistsEmptyNullAndLimitedQueriesMatchJdbc()
    {
        Compare([
            "SELECT EXISTS (SELECT 1), NOT EXISTS (SELECT 1), EXISTS (SELECT NULL)",
            "SELECT EXISTS (SELECT 1 WHERE FALSE), NOT EXISTS (SELECT 1 WHERE FALSE)",
            "SELECT (EXISTS (SELECT 1)) = TRUE, CASE WHEN EXISTS (SELECT 1) THEN 'yes' ELSE 'no' END",
            "SELECT EXISTS (VALUES (NULL)), EXISTS (VALUES (1), (2))",
            "CREATE TABLE T(ID INT PRIMARY KEY, N INT)",
            "SELECT EXISTS (SELECT * FROM T), NOT EXISTS (SELECT * FROM T)",
            "INSERT INTO T VALUES (1, 10), (2, 20), (3, NULL)",
            "SELECT EXISTS (SELECT ID, N FROM T), EXISTS (SELECT N FROM T WHERE N IS NULL)",
            "SELECT EXISTS (SELECT 1 FROM T LIMIT 0), EXISTS (SELECT 1 FROM T LIMIT 1 OFFSET 2), EXISTS (SELECT 1 FROM T OFFSET 3 ROWS)",
            "SELECT EXISTS (SELECT DISTINCT N FROM T LIMIT 1 OFFSET 2), EXISTS (SELECT DISTINCT N FROM T LIMIT 1 OFFSET 3)",
            "SELECT EXISTS (SELECT ID FROM T ORDER BY MOD(N, 7) NULLS LAST LIMIT 1 OFFSET 2)",
            "SELECT EXISTS (SELECT COUNT(*) FROM T WHERE FALSE), EXISTS (SELECT N FROM T WHERE FALSE GROUP BY N)",
            "SELECT EXISTS (SELECT COUNT(*) FROM T LIMIT 0), EXISTS (SELECT COUNT(*) FROM T OFFSET 1 ROW)",
            "SELECT EXISTS (SELECT SUM(N) FROM T WHERE FALSE)",
            "SELECT EXISTS (SELECT N FROM T GROUP BY N HAVING COUNT(*) > 1)",
            "SELECT EXISTS (SELECT 1 WHERE FALSE UNION ALL SELECT 2), EXISTS (SELECT 1 WHERE FALSE UNION SELECT 2 WHERE FALSE)",
            "SELECT EXISTS (WITH C AS (SELECT ID FROM T) SELECT ID FROM C WHERE ID = 2)",
            "SELECT EXISTS (SELECT 1 FROM UNNEST(ARRAY[1, 2]) a WHERE a.C1 = 2)",
            "SELECT EXISTS (SELECT 1 FROM UNNEST(ARRAY[]) a)",
            "SELECT EXISTS (SELECT 1 FROM UNNEST(JSON '[1, 2]') a WHERE CAST(a.C1 AS INTEGER) = 2)",
            "CREATE TABLE A(ID INT PRIMARY KEY, V INT ARRAY)",
            "INSERT INTO A VALUES (1, ARRAY[1, 2]), (2, ARRAY[]), (3, NULL)",
            "SELECT a.ID FROM A a WHERE EXISTS (SELECT 1 FROM UNNEST(a.V) u WHERE u.C1 = 2) ORDER BY a.ID",
            "SELECT EXISTS (SELECT MISSING FROM T)",
            "SELECT EXISTS (SELECT 1 FROM MISSING)",
            "DELETE FROM T",
            "SELECT EXISTS (SELECT ID FROM T), NOT EXISTS (SELECT ID FROM T)",
        ]);
    }

    [JavaOracleFact]
    public void CorrelationNestedExistsAndDataChangesMatchJdbc()
    {
        Compare([
            "CREATE TABLE \"Topics\"(\"Id\" INT PRIMARY KEY)",
            "CREATE TABLE \"Posts\"(\"Id\" INT PRIMARY KEY, \"TopicId\" INT, \"Approved\" BOOLEAN)",
            "INSERT INTO \"Topics\" VALUES (1), (2), (3), (4)",
            "INSERT INTO \"Posts\" VALUES (1, 1, TRUE), (2, 1, FALSE), (3, 2, TRUE), (4, NULL, TRUE), (5, 4, NULL)",
            "SELECT \"t\".\"Id\" FROM \"Topics\" \"t\" WHERE EXISTS (SELECT 1 FROM \"Posts\" \"p\" WHERE \"p\".\"TopicId\" = \"t\".\"Id\") ORDER BY \"t\".\"Id\"",
            "SELECT \"t\".\"Id\" FROM \"Topics\" AS \"t\" WHERE NOT EXISTS (SELECT 1 FROM \"Posts\" AS \"p\" WHERE \"p\".\"TopicId\" = \"t\".\"Id\") ORDER BY \"t\".\"Id\"",
            "SELECT \"t\".\"Id\", EXISTS (SELECT NULL FROM \"Posts\" \"p\" WHERE \"p\".\"TopicId\" = \"t\".\"Id\") FROM \"Topics\" \"t\" ORDER BY \"t\".\"Id\"",
            "SELECT \"t\".\"Id\" FROM \"Topics\" \"t\" WHERE NOT EXISTS (SELECT 1 FROM \"Posts\" \"p\" WHERE \"p\".\"TopicId\" = \"t\".\"Id\" AND NOT \"p\".\"Approved\") ORDER BY \"t\".\"Id\"",
            "SELECT \"t\".\"Id\" FROM \"Topics\" \"t\" WHERE EXISTS (SELECT 1 FROM \"Posts\" \"p\" WHERE \"p\".\"TopicId\" = \"t\".\"Id\" AND EXISTS (SELECT 1 FROM \"Posts\" \"q\" WHERE \"q\".\"TopicId\" = \"t\".\"Id\" AND \"q\".\"Id\" <> \"p\".\"Id\")) ORDER BY \"t\".\"Id\"",
            "SELECT \"t\".\"Id\" FROM \"Topics\" \"t\" WHERE EXISTS (SELECT 1 FROM \"Posts\" \"p\" WHERE \"p\".\"TopicId\" = \"t\".\"Id\" GROUP BY \"p\".\"TopicId\" HAVING COUNT(*) > 1) ORDER BY \"t\".\"Id\"",
            "SELECT \"Id\" FROM \"Topics\" WHERE EXISTS (SELECT 1 FROM \"Posts\" WHERE \"TopicId\" = \"Topics\".\"Id\") ORDER BY \"Id\"",
            "CREATE INDEX POSTS_TOPIC ON \"Posts\"(\"TopicId\")",
            "SELECT \"t\".\"Id\" FROM \"Topics\" \"t\" WHERE EXISTS (SELECT 1 FROM \"Posts\" \"p\" WHERE \"p\".\"TopicId\" = \"t\".\"Id\" LIMIT 1 OFFSET 1) ORDER BY \"t\".\"Id\"",
            "UPDATE \"Topics\" SET \"Id\" = \"Id\" + 10 WHERE EXISTS (SELECT 1 FROM \"Posts\" WHERE \"TopicId\" = \"Topics\".\"Id\")",
            "SELECT \"Id\" FROM \"Topics\" ORDER BY \"Id\"",
            "SELECT EXISTS (SELECT 1 FROM \"Posts\" WHERE \"TopicId\" = 3)",
            "INSERT INTO \"Posts\" VALUES (6, 3, TRUE)",
            "SELECT EXISTS (SELECT 1 FROM \"Posts\" WHERE \"TopicId\" = 3)",
            "DELETE FROM \"Topics\" WHERE NOT EXISTS (SELECT 1 FROM \"Posts\" WHERE \"TopicId\" = \"Topics\".\"Id\")",
            "SELECT \"Id\" FROM \"Topics\" ORDER BY \"Id\"",
        ]);
    }

    [JavaOracleFact]
    public void BoundExistsCanBeReexecutedAfterWrites()
    {
        string[] inputs = ["1", "2", "3", "1"];
        using H2Connection connection = Open();
        using DbCommand setup = connection.CreateCommand();
        setup.CommandText = "CREATE TABLE T(ID INT PRIMARY KEY)";
        setup.ExecuteNonQuery();
        setup.CommandText = "INSERT INTO T VALUES (1), (2)";
        setup.ExecuteNonQuery();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM T WHERE ID = ?)";
        DbParameter parameter = command.CreateParameter();
        parameter.Value = 1;
        command.Parameters.Add(parameter);
        command.Prepare();
        List<string> port = [];
        for (int i = 0; i < inputs.Length; i++)
        {
            parameter.Value = int.Parse(inputs[i], CultureInfo.InvariantCulture);
            Record(command, port, i);
        }
        setup.CommandText = "DELETE FROM T WHERE ID = 1";
        setup.ExecuteNonQuery();
        Record(command, port, 4);
        setup.CommandText = "INSERT INTO T VALUES (1)";
        setup.ExecuteNonQuery();
        Record(command, port, 5);
        parameter.Value = DBNull.Value;
        Record(command, port, 6);
        List<string> java = OracleRunner.Run(inputs,
            """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:exists_parameters")) {
                java.sql.Statement setup = conn.createStatement();
                setup.execute("CREATE TABLE T(ID INT PRIMARY KEY)"); setup.execute("INSERT INTO T VALUES (1), (2)");
                try (java.sql.PreparedStatement s = conn.prepareStatement("SELECT EXISTS (SELECT 1 FROM T WHERE ID = ?)")) {
                    for (int i = 0; i < inputs.length; i++) {
                        s.setInt(1, Integer.parseInt(inputs[i]));
                        try (java.sql.ResultSet r = s.executeQuery()) {
                            r.next(); System.out.println("=" + i + ":R:" + r.getString(1)); System.out.println("=" + i + ":Q");
                        }
                    }
                    setup.execute("DELETE FROM T WHERE ID = 1");
                    try (java.sql.ResultSet r = s.executeQuery()) { r.next(); System.out.println("=4:R:" + r.getString(1)); System.out.println("=4:Q"); }
                    setup.execute("INSERT INTO T VALUES (1)");
                    try (java.sql.ResultSet r = s.executeQuery()) { r.next(); System.out.println("=5:R:" + r.getString(1)); System.out.println("=5:Q"); }
                    s.setNull(1, java.sql.Types.INTEGER);
                    try (java.sql.ResultSet r = s.executeQuery()) { r.next(); System.out.println("=6:R:" + r.getString(1)); System.out.println("=6:Q"); }
                }
            }
            """);
        Assert.Equal(java, port);
    }

    [JavaOracleFact]
    public void LikeAnyAndAllSyntaxAndExistsAlternativesMatchJdbc()
    {
        List<string> reference = OracleRunner.Run([
            "SELECT 'abc' LIKE ANY(ARRAY['a%', 'b%'])",
            "SELECT 'abc' LIKE ALL(ARRAY['a%', '%c'])",
            "SELECT 'abc' LIKE ANY(SELECT 'a%')",
            "SELECT 'abc' LIKE ALL(SELECT 'a%')",
        ], """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:like_quantifiers")) {
                for (int i = 0; i < inputs.length; i++) {
                    try (java.sql.ResultSet r = conn.createStatement().executeQuery(inputs[i])) {
                        r.next(); System.out.println("=" + i + ":R:" + r.getString(1));
                    } catch (java.sql.SQLException e) { System.out.println("=" + i + ":E:" + e.getErrorCode()); }
                }
            }
            """);
        // ANY is parsed as the aggregate, not a LIKE quantifier; ALL is invalid here.
        Assert.Equal(new[] { "=0:E:22018", "=1:E:42000", "=2:E:22018", "=3:E:42000" }, reference);
        Compare([
            "SELECT EXISTS (SELECT 1 FROM UNNEST(ARRAY['a%', 'b%']) p WHERE 'abc' LIKE p.C1)",
            "SELECT NOT EXISTS (SELECT 1 FROM UNNEST(ARRAY['a%', '%c']) p WHERE NOT ('abc' LIKE p.C1))",
            "SELECT EXISTS (SELECT 1 FROM UNNEST(ARRAY[]) p WHERE 'abc' LIKE p.C1)",
            "SELECT NOT EXISTS (SELECT 1 FROM UNNEST(ARRAY[]) p WHERE NOT ('abc' LIKE p.C1))",
        ]);
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("jdbc:h2:mem:exists_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        return connection;
    }

    private static void Compare(string[] statements)
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
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:exists_oracle")) {
                for (int i = 0; i < inputs.length; i++) {
                    try (java.sql.Statement s = conn.createStatement()) {
                        if (s.execute(inputs[i])) {
                            try (java.sql.ResultSet r = s.getResultSet()) {
                                while (r.next()) {
                                    StringBuilder line = new StringBuilder("=" + i + ":R:");
                                    for (int c = 1; c <= r.getMetaData().getColumnCount(); c++) {
                                        if (c > 1) line.append('|');
                                        String value = r.getString(c); line.append(r.wasNull() ? "NULL" : value);
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
