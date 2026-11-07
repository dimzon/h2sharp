/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The dml/with.sql forms the port answers differently from the reference, kept red on
 * purpose (the BlockedByPort list is the defect list, see MIGRATION_STATUS):
 * a nested CTE must shadow the outer name only inside its own body (the port's flat
 * cte dictionary leaks it out, so the outer query loses its column: 42101 instead of
 * the row), a RECURSIVE CTE nested in another CTE body is refused by the reference with
 * 42001 (the port answers 42101), a view over a zero-column CTE answers TABLE V with one
 * empty row in the reference and VIEW_IS_INVALID_2 here, and FINAL TABLE over INSERT is
 * refused by the reference with 21002 where the port answers 42001.
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
[Trait("Category", "BlockedByPort")]
public sealed class WithCteBlockedPortTests
{
    [JavaOracleFact]
    public void WithFormsThePortStillAnswersDifferently()
    {
        string[] statements =
        [
            "CREATE TABLE TEST(A INT, B INT) AS SELECT 1, 2",
            // A nested CTE shadows the outer name only inside its own body.
            "WITH Q1(X) AS (VALUES 1), Q2 AS ( WITH Q1(Y) AS (VALUES 2) TABLE Q1 ) SELECT Q1.X, Q2.Y FROM Q1, Q2",
            // RECURSIVE nested in another CTE body is a syntax error on the reference.
            "WITH RECURSIVE R1(A) AS (WITH RECURSIVE R2(B) AS (SELECT 1) TABLE R2) TABLE R1",
            // A view over a zero-column CTE answers one empty row.
            "CREATE VIEW V AS WITH A AS (SELECT) TABLE A",
            "TABLE V",
            // FINAL TABLE over DML waits for the delta change tables.
            "WITH W AS (SELECT NULL FROM FINAL TABLE (INSERT INTO TEST VALUES 1, 2)) SELECT COUNT (*) FROM W",
            "DROP TABLE TEST",
            "DROP VIEW V",
        ];
        RunBoth(statements);
    }

    /// <summary>
    /// Runs the statements on both sides and asserts the same answers.
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
    /// Runs the statements here and writes what the question answered.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("with_cte_blocked_" + Guid.NewGuid().ToString("N"))), "sa")
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
                if (sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                    || sql.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase)
                    || sql.StartsWith("TABLE", StringComparison.OrdinalIgnoreCase)
                    || sql.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:with_cte_blocked", "sa", "");
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
