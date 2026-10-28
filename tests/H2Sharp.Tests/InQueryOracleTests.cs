/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * IN subqueries are compared with the pinned H2 2.5 jar, statement by statement.
 */
using System.Globalization;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class InQueryOracleTests
{
    [JavaOracleFact]
    public void ValuesNullsAndEmptySubqueriesMatchTheReference()
    {
        Compare(
        [
            "CREATE TABLE T(ID INT)",
            "CREATE TABLE U(ID INT)",
            "INSERT INTO T VALUES (1), (2), (3), (NULL)",
            "INSERT INTO U VALUES (2), (2), (NULL)",
            "SELECT ID FROM T WHERE ID IN (SELECT ID FROM U) ORDER BY ID",
            "SELECT ID FROM T WHERE ID NOT IN (SELECT ID FROM U) ORDER BY ID",
            "SELECT 2 IN (SELECT ID FROM U) AS B",
            "SELECT 1 IN (SELECT ID FROM U) AS B",
            "SELECT 1 NOT IN (SELECT ID FROM U) AS B",
            "SELECT NULL IN (SELECT ID FROM U) AS B",
            "SELECT NULL IN (SELECT ID FROM U WHERE 1 = 0) AS B",
            "SELECT NULL NOT IN (SELECT ID FROM U WHERE 1 = 0) AS B",
            "SELECT 1 IN (SELECT NULL) AS B",
            "SELECT 1 IN (VALUES (1)) AS B",
            "SELECT 1 IN (VALUES (2), (1)) AS B",
            "SELECT 1 IN (VALUES (NULL), (1)) AS B",
            "SELECT 1 IN (VALUES (1), (NULL)) AS B",
            "SELECT 1 IN (SELECT C1 FROM (VALUES (1), (NULL))) AS B",
            "SELECT 1 NOT IN (VALUES (1), (NULL)) AS B",
            "SELECT 1 IN (SELECT ID FROM U WHERE ID IS NOT NULL) AS B",
            "SELECT 1 NOT IN (SELECT ID FROM U WHERE ID IS NOT NULL) AS B",
            "SELECT 1 IN (SELECT 2 UNION ALL SELECT 1) AS B",
            "SELECT 3 IN (SELECT ID FROM T ORDER BY ID LIMIT 2) AS B",
            "SELECT 2 IN (SELECT ID FROM U LIMIT 1 OFFSET 1) AS B",
            "SELECT 1 IN (SELECT ID, ID FROM U) AS B",
            "SELECT 1 IN (SELECT 'a') AS B",
        ]);
    }

    [JavaOracleFact]
    public void CorrelationNestedPredicatesAndIndexesMatchTheReference()
    {
        Compare(
        [
            "CREATE TABLE T(ID INT PRIMARY KEY, N INT)",
            "CREATE TABLE U(ID INT, N INT)",
            "INSERT INTO T VALUES (1, 10), (2, 20), (3, 30)",
            "INSERT INTO U VALUES (1, 20), (2, 20), (NULL, 30)",
            "SELECT t.ID FROM T AS t WHERE t.ID IN (SELECT u.ID FROM U AS u WHERE u.N = t.N) ORDER BY t.ID",
            "SELECT t.ID FROM T t WHERE t.ID NOT IN (SELECT u.ID FROM U u WHERE u.N = t.N) ORDER BY t.ID",
            "SELECT t.ID FROM T t WHERE t.ID IN (SELECT t.ID FROM U u WHERE u.N = t.N) ORDER BY t.ID",
            "SELECT ID FROM T WHERE ID IN (SELECT ID FROM U) ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (SELECT ID FROM U WHERE ID IN (SELECT ID FROM T WHERE N = 20)) ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (WITH C AS (SELECT ID FROM U) SELECT ID FROM C) ORDER BY ID",
            "SELECT t.ID FROM T t WHERE t.ID IN (SELECT ID FROM U WHERE N = t.N) ORDER BY t.ID",
            "UPDATE T SET N = 99 WHERE ID IN (SELECT ID FROM U WHERE ID IS NOT NULL)",
            "SELECT ID, N FROM T ORDER BY ID",
            "DELETE FROM T WHERE ID IN (SELECT ID FROM U)",
            "SELECT ID, N FROM T ORDER BY ID",
        ]);
    }

    private static void Compare(string[] statements)
    {
        using SessionLocal session = new(new Database(ConnectionInfo.Memory("in_query_oracle")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
        };
        List<string> port = [];
        for (int s = 0; s < statements.Length; s++)
        {
            string sql = statements[s];
            string prefix = "=" + s.ToString(CultureInfo.InvariantCulture) + ":";
            try
            {
                H2Sharp.Command.Command command = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
                if (command.IsQuery())
                {
                    using ResultInterface result = command.Query(0);
                    port.Add(prefix + "QUERY");
                    while (result.Next())
                    {
                        Value?[] row = result.CurrentRow();
                        port.Add(prefix + string.Join("|", row.Take(result.GetVisibleColumnCount())
                            .Select(value => value is null or ValueNull ? "NULL" : value.GetString())));
                    }
                }
                else
                {
                    port.Add(prefix + "UPDATE:" + command.ExecuteUpdate(false).GetUpdateCount()
                        .ToString(CultureInfo.InvariantCulture));
                }
            }
            catch (H2SharpException error)
            {
                port.Add(prefix + "ERROR:" + error.H2ErrorCode.ToString(CultureInfo.InvariantCulture));
            }
        }

        List<string> java = OracleRunner.Run(statements,
            """
            try (java.sql.Connection connection = java.sql.DriverManager.getConnection("jdbc:h2:mem:in_query_oracle")) {
              java.sql.Statement statement = connection.createStatement();
              for (int s = 0; s < inputs.length; s++) {
                String prefix = "=" + s + ":";
                try {
                  if (statement.execute(inputs[s])) {
                    try (java.sql.ResultSet rows = statement.getResultSet()) {
                      System.out.println(prefix + "QUERY");
                      int count = rows.getMetaData().getColumnCount();
                      while (rows.next()) {
                        StringBuilder line = new StringBuilder(prefix);
                        for (int c = 1; c <= count; c++) {
                          if (c > 1) line.append('|');
                          String value = rows.getString(c);
                          line.append(rows.wasNull() ? "NULL" : value);
                        }
                        System.out.println(line);
                      }
                    }
                  } else {
                    System.out.println(prefix + "UPDATE:" + statement.getUpdateCount());
                  }
                } catch (java.sql.SQLException error) {
                  System.out.println(prefix + "ERROR:" + error.getErrorCode());
                }
              }
            }
            """, extraJavaArgs: ["-Duser.language=en", "-Duser.timezone=UTC"]);
        Assert.True(java.SequenceEqual(port), "JVM:\n" + string.Join("\n", java)
            + "\n.NET:\n" + string.Join("\n", port));
    }
}
