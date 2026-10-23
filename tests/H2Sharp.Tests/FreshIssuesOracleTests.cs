/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0.
 * Fresh provider SQL gaps checked against the pinned H2 2.5 JDBC implementation. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class FreshIssuesOracleTests
{
    [JavaOracleFact]
    public void Issue38CountFiltersDoNotUseUnfilteredQuickCounts()
    {
        Compare([
            "CREATE TABLE PF(ID INT, H BOOLEAN)",
            "INSERT INTO PF VALUES (1, TRUE), (2, FALSE), (3, FALSE), (4, NULL)",
            "SELECT COUNT(*) FILTER (WHERE H) FROM PF",
            "SELECT COUNT(*) FILTER (WHERE H), COUNT(*) FILTER (WHERE NOT H), COUNT(*) FROM PF",
            "SELECT COUNT(*) FILTER (WHERE FALSE), COUNT(*) FILTER (WHERE TRUE), COUNT(*) FILTER (WHERE NULL) FROM PF",
            "SELECT COUNT(ID) FILTER (WHERE H), SUM(ID) FILTER (WHERE H) FROM PF",
            "SELECT COUNT(*) FILTER (WHERE ID > 2) FROM PF",
            "SELECT H, COUNT(*) FILTER (WHERE ID > 1) FROM PF GROUP BY H ORDER BY H NULLS LAST",
            "SELECT (SELECT CAST(COUNT(*) FILTER (WHERE H) AS INTEGER) FROM PF)",
        ]);
    }

    [JavaOracleFact]
    public void Issue34SavepointRollbackAndReleaseMatchJdbc()
    {
        Compare([
            "CREATE TABLE T(ID INT PRIMARY KEY, N INT)",
            "BEGIN", "INSERT INTO T VALUES (1, 10)", "SAVEPOINT s1",
            "INSERT INTO T VALUES (2, 20)", "SAVEPOINT s2", "UPDATE T SET N = 99 WHERE ID = 1",
            "ROLLBACK TO SAVEPOINT s2", "SELECT ID, N FROM T ORDER BY ID",
            "ROLLBACK TO SAVEPOINT s1", "SELECT ID, N FROM T ORDER BY ID",
            "RELEASE SAVEPOINT s1", "INSERT INTO T VALUES (3, 30)", "ROLLBACK TO SAVEPOINT s1",
            "SELECT ID, N FROM T ORDER BY ID", "COMMIT", "ROLLBACK TO SAVEPOINT s1",
            "BEGIN", "SAVEPOINT \"Mixed\"", "INSERT INTO T VALUES (4, 40)", "SAVEPOINT \"Mixed\"",
            "INSERT INTO T VALUES (5, 50)", "ROLLBACK TO SAVEPOINT \"Mixed\"", "SELECT ID, N FROM T ORDER BY ID",
            "ROLLBACK", "SELECT ID, N FROM T ORDER BY ID", "ROLLBACK TO SAVEPOINT \"Mixed\"",
        ]);
    }

    [JavaOracleFact]
    public void Issue33SetOperationsPrecedenceTypesAndPagingMatchJdbc()
    {
        Compare([
            "SELECT ID FROM (SELECT 1 ID) i INTERSECT SELECT ID FROM (SELECT 1 ID) j",
            "SELECT ID FROM (SELECT 1 ID) i EXCEPT SELECT ID FROM (SELECT 2 ID) j",
            "CREATE TABLE L(ID INT, N INT)", "CREATE TABLE R(ID INT, N INT)",
            "INSERT INTO L VALUES (1, 10), (1, 10), (2, 20), (NULL, NULL)",
            "INSERT INTO R VALUES (2, 20), (3, 30), (3, 30), (NULL, NULL)",
            "SELECT ID, N FROM L INTERSECT SELECT ID, N FROM R ORDER BY ID NULLS LAST",
            "SELECT ID, N FROM L EXCEPT SELECT ID, N FROM R ORDER BY ID NULLS LAST",
            "SELECT ID FROM L UNION SELECT ID FROM R ORDER BY ID DESC NULLS LAST LIMIT 2 OFFSET 1",
            "SELECT 1 AS X UNION SELECT 2 INTERSECT SELECT 2 ORDER BY X",
            "SELECT 1 AS X EXCEPT SELECT 1 UNION SELECT 2 ORDER BY X",
            "(SELECT 2 AS X UNION SELECT 1 ORDER BY X LIMIT 1) UNION ALL SELECT 3 ORDER BY X DESC",
            "SELECT CAST(1 AS SMALLINT) AS X UNION SELECT CAST(2 AS BIGINT) ORDER BY X",
            "SELECT 1, 2 INTERSECT SELECT 1", "SELECT 1 INTERSECT ALL SELECT 1",
            "SELECT EXISTS (SELECT 1 INTERSECT SELECT 2)",
            "SELECT 2 IN (SELECT 1 UNION SELECT 2 EXCEPT SELECT 1)",
            "SELECT * FROM (VALUES (1), (2)) a INTERSECT SELECT * FROM (VALUES (2), (3)) b",
        ]);
    }

    [JavaOracleFact]
    public void Issue35ListaggWithinGroupAndFiltersMatchJdbc()
    {
        Compare([
            "CREATE TABLE F(\"Id\" INT PRIMARY KEY, \"Name\" VARCHAR)",
            "INSERT INTO F VALUES (3, 'c'), (1, 'a'), (2, 'b'), (4, NULL)",
            "SELECT LISTAGG(\"Name\", ',') WITHIN GROUP (ORDER BY \"Id\") FROM F",
            "SELECT LISTAGG(\"Name\", ',') WITHIN GROUP (ORDER BY \"Id\" DESC) FILTER (WHERE \"Id\" < 3), "
                + "LISTAGG(\"Name\", ':') WITHIN GROUP (ORDER BY \"Id\") FILTER (WHERE \"Id\" > 1) FROM F",
            "SELECT LISTAGG(DISTINCT \"Name\", ',') WITHIN GROUP (ORDER BY \"Name\" NULLS LAST) FROM F",
            "SELECT LISTAGG(\"Name\", ',') WITHIN GROUP (ORDER BY \"Id\") FILTER (WHERE FALSE) FROM F",
            "SELECT LISTAGG(\"Name\", ',') WITHIN GROUP (ORDER BY MOD(\"Id\", 2), \"Id\" DESC) FROM F",
            "SELECT STRING_AGG(\"Name\", ',' ORDER BY \"Id\" DESC) FROM F",
            "SELECT GROUP_CONCAT(\"Name\" ORDER BY \"Id\" DESC SEPARATOR ':') FROM F",
        ]);
    }

    [JavaOracleFact]
    public void Issue36EscapeStringSyntaxMatchesThePinnedJdbc()
    {
        Compare([
            "SELECT BTRIM(' a ', E' \\t\\n\\r')",
            "SELECT E'hello\\nworld'", "SELECT e'\\t'",
            "SELECT E'\\\\n', E'a\\\\b', E'it''s', E''",
            "SELECT BTRIM(' a ', ' ' || CHR(9) || CHR(10) || CHR(13))",
        ]);
    }

    [JavaOracleFact]
    public void Issue37ScalarSubqueryCastAggregateAndCorrelationMatchJdbc()
    {
        Compare([
            "CREATE TABLE F(\"Id\" INT PRIMARY KEY, N INT)",
            "INSERT INTO F VALUES (1, 10), (2, 20), (3, NULL)",
            "SELECT (SELECT CAST(COUNT(*) AS INTEGER) FROM F f WHERE f.\"Id\" = 1)",
            "SELECT (SELECT CAST(COUNT(*) AS INTEGER) FROM F f WHERE f.\"Id\" = 99)",
            "SELECT f.\"Id\", (SELECT CAST(COUNT(*) AS INTEGER) FROM F g WHERE g.\"Id\" <= f.\"Id\") FROM F f ORDER BY f.\"Id\"",
            "SELECT f.\"Id\", (SELECT f.\"Id\") FROM F f ORDER BY f.\"Id\"",
            "SELECT (SELECT \"Id\", N FROM F WHERE \"Id\" = 1) = ROW(1, 10)",
            "SELECT (SELECT \"Id\", N FROM F WHERE \"Id\" = 99) IS NULL",
            "SELECT (SELECT N FROM F WHERE \"Id\" = 99)",
            "SELECT (SELECT N FROM F)", "SELECT (SELECT 1 UNION ALL SELECT 2)",
            "SELECT (SELECT CAST(SUM(N) AS BIGINT) FROM F)",
            "SELECT (SELECT 1)", "SELECT (SELECT 1 WHERE FALSE)",
            "SELECT (SELECT COUNT(*) FROM F) + 1",
        ]);
    }

    internal static void Compare(string[] statements)
    {
        using H2Connection connection = new("jdbc:h2:mem:fresh_issues_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        List<string> port = [];
        for (int i = 0; i < statements.Length; i++)
        {
            string prefix = "=" + i + ':';
            using DbCommand command = connection.CreateCommand();
            command.CommandText = statements[i];
            try
            {
                bool query = statements[i].StartsWith("SELECT", StringComparison.Ordinal) || statements[i].StartsWith('(');
                if (!query) { port.Add(prefix + "U:" + command.ExecuteNonQuery()); continue; }
                using DbDataReader rows = command.ExecuteReader();
                while (rows.Read())
                {
                    List<string> cells = [];
                    for (int c = 0; c < rows.FieldCount; c++) cells.Add(rows.IsDBNull(c) ? "NULL" : rows.GetString(c));
                    port.Add(prefix + "R:" + string.Join('|', cells));
                }
                port.Add(prefix + "Q");
            }
            catch (H2SharpException error) { port.Add(prefix + "E:" + error.H2ErrorCode.ToString(CultureInfo.InvariantCulture)); }
        }
        List<string> java = OracleRunner.Run(statements, """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:fresh_issues")) {
                for (int i = 0; i < inputs.length; i++) {
                    String prefix = "=" + i + ":";
                    try (java.sql.Statement s = conn.createStatement()) {
                        if (!s.execute(inputs[i])) { System.out.println(prefix + "U:" + s.getUpdateCount()); continue; }
                        try (java.sql.ResultSet r = s.getResultSet()) {
                            while (r.next()) {
                                StringBuilder line = new StringBuilder(prefix + "R:");
                                for (int c = 1; c <= r.getMetaData().getColumnCount(); c++) {
                                    if (c > 1) line.append('|');
                                    String value = r.getString(c); line.append(r.wasNull() ? "NULL" : value);
                                }
                                System.out.println(line);
                            }
                            System.out.println(prefix + "Q");
                        }
                    } catch (java.sql.SQLException error) { System.out.println(prefix + "E:" + error.getErrorCode()); }
                }
            }
            """);
        Assert.True(java.SequenceEqual(port), "JVM:\n" + string.Join('\n', java) + "\nPort:\n" + string.Join('\n', port));
    }
}
