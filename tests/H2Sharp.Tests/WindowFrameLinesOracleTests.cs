/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Window frame lines from the coverage data (uncovered.py): the GROUPS unit with its
 * group-hopping offsets, the EXCLUDE GROUP/TIES/CURRENT ROW iterators over ROWS and RANGE,
 * and the RANGE refusal for a sort key that has no arithmetic.
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
public sealed class WindowFrameLinesOracleTests
{
    [JavaOracleFact]
    public void WindowFrameLinesAnswerTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT PRIMARY KEY, G INT, V INT) AS VALUES (1, 1, 10), (2, 1, 20), (3, 2, 30), (4, 2, 40), (5, 3, 50)",
            // GROUPS: the offsets hop whole peer groups, not rows.
            "SELECT ID, SUM(V) OVER (ORDER BY G GROUPS CURRENT ROW) S FROM T ORDER BY ID",
            "SELECT ID, SUM(V) OVER (ORDER BY G GROUPS BETWEEN 1 PRECEDING AND 1 FOLLOWING) S FROM T ORDER BY ID",
            "SELECT ID, SUM(V) OVER (ORDER BY G GROUPS BETWEEN 2 PRECEDING AND CURRENT ROW) S FROM T ORDER BY ID",
            "SELECT ID, SUM(V) OVER (ORDER BY G GROUPS BETWEEN UNBOUNDED PRECEDING AND 1 FOLLOWING) S FROM T ORDER BY ID",
            // EXCLUDE over ROWS and RANGE: the iterators that cut the frame.
            "SELECT ID, ARRAY_AGG(ID) OVER (ORDER BY G RANGE BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING EXCLUDE GROUP) A FROM T ORDER BY ID",
            "SELECT ID, ARRAY_AGG(ID) OVER (ORDER BY ID ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING EXCLUDE GROUP) A FROM T ORDER BY ID",
            "SELECT ID, ARRAY_AGG(ID) OVER (ORDER BY ID ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING EXCLUDE TIES) A FROM T ORDER BY ID",
            "SELECT ID, ARRAY_AGG(ID) OVER (ORDER BY ID ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING EXCLUDE CURRENT ROW) A FROM T ORDER BY ID",
            "SELECT ID, ARRAY_AGG(ID) OVER (ORDER BY ID ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING EXCLUDE NO OTHERS) A FROM T ORDER BY ID",
            "SELECT ID, ARRAY_AGG(ID) OVER (ORDER BY G RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW EXCLUDE TIES) A FROM T ORDER BY ID",
            // RANGE with a sort key that has no arithmetic refuses.
            "SELECT SUM(V) OVER (ORDER BY G RANGE BETWEEN 1 PRECEDING AND UNBOUNDED FOLLOWING) S FROM T ORDER BY ID",
            "DROP TABLE T",
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
            new Database(ConnectionInfo.Memory("window_lines_oracle")), "sa")
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:window_lines_oracle", "sa", "");
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
