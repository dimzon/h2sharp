/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * ALTER DOMAIN SET/DROP DEFAULT and SET/DROP ON UPDATE, checked against the reference running
 * the same text: the new expression answers at insert time, copies onto the columns and the
 * dependent domains, and the dropped one answers null.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class AlterDomainDefaultOnUpdateOracleTests
{
    [JavaOracleFact]
    public void DomainDefaultsAndOnUpdateAnswerTheReference()
    {
        string[] statements =
        [
            "CREATE DOMAIN D1 AS INT DEFAULT 1",
            "CREATE TABLE T(A D1, B INT)",
            "INSERT INTO T(A) VALUES (DEFAULT)",
            "SELECT A, B FROM T",
            // The new default copies onto the column and answers at insert time.
            "ALTER DOMAIN D1 SET DEFAULT 5",
            "INSERT INTO T(A) VALUES (DEFAULT)",
            "SELECT A, B FROM T ORDER BY A",
            // Dropping answers null.
            "ALTER DOMAIN D1 DROP DEFAULT",
            "INSERT INTO T(A) VALUES (DEFAULT)",
            "SELECT A, B FROM T ORDER BY A",
            // ON UPDATE runs on an update of a dependent column.
            "CREATE DOMAIN D2 AS TIMESTAMP DEFAULT '2000-01-01 00:00:00'",
            "ALTER TABLE T ADD COLUMN C D2",
            "INSERT INTO T(A, B) VALUES (7, 7)",
            "ALTER DOMAIN D2 SET ON UPDATE TIMESTAMP '2001-02-03 04:05:06'",
            "UPDATE T SET B = 8 WHERE A = 7",
            "SELECT C FROM T WHERE B = 8",
            "ALTER DOMAIN D2 DROP ON UPDATE",
            "UPDATE T SET B = 9 WHERE A = 7",
            "SELECT C FROM T WHERE B = 9",
            // A domain over a domain takes the parent's change too.
            "CREATE DOMAIN D3 AS D1",
            "ALTER DOMAIN D1 SET DEFAULT 11",
            "CREATE TABLE U(X D3)",
            "INSERT INTO U(X) VALUES (DEFAULT)",
            "SELECT X FROM U",
            "DROP TABLE U",
            "DROP DOMAIN D3",
            "DROP TABLE T",
            "DROP DOMAIN D1",
            "DROP DOMAIN D2",
        ];
        RunBoth(statements);
    }

    /// <summary>
    /// Runs the statements on both sides and asserts the same answers: the rows, or the code
    /// of the error.
    /// </summary>
    /// <param name="statements">the statements</param>
    private static void RunBoth(IReadOnlyList<string> statements)
    {
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered: the rows, or the code of
    /// the error.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("domain_expr_oracle")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        List<string> lines = [];
        foreach (string sql in statements)
        {
            try
            {
                CommandContainer container =
                    Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
                if (sql.StartsWith("SELECT", StringComparison.Ordinal))
                {
                    ResultInterface result = container.Query(-1);
                    int visible = result.GetVisibleColumnCount();
                    while (result.Next())
                    {
                        Value?[] row = result.CurrentRow();
                        List<string> cells = [];
                        for (int i = 0; i < visible; i++)
                        {
                            Value? value = row[i];
                            cells.Add(value is null or ValueNull ? "-" : value.GetString() ?? "-");
                        }

                        lines.Add("=" + string.Join(":", cells));
                    }
                }
                else
                {
                    lines.Add("=" + container.ExecuteUpdate(false).GetUpdateCount());
                }
            }
            catch (Exception e)
            {
                lines.Add(e is H2SharpException h2
                    ? "=" + h2.H2ErrorCode.ToString(CultureInfo.InvariantCulture)
                    : "=" + e.GetType().Name + ":" + e.Message);
            }
        }

        session.Close();
        return lines;
    }

    /// <summary>
    /// Runs the statements on the reference and writes what the question answered.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunJava(IReadOnlyList<string> statements)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:domain_expr_oracle", "sa", "");
            java.sql.Statement stat = conn.createStatement();
            for (int i = 0; i < inputs.length; i++) {
              try {
                boolean isQuery = stat.execute(inputs[i]);
                if (isQuery) {
                  java.sql.ResultSet rs = stat.getResultSet();
                  int columns = rs.getMetaData().getColumnCount();
                  while (rs.next()) {
                    StringBuilder line = new StringBuilder("=");
                    for (int c = 1; c <= columns; c++) {
                      if (c > 1) {
                        line.append(':');
                      }
                      String s = rs.getString(c);
                      line.append(rs.wasNull() ? "-" : s);
                    }
                    System.out.println(line);
                  }
                } else {
                  System.out.println("=" + stat.getUpdateCount());
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}
