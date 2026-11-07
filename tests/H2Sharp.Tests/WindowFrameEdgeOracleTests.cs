/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Window-frame lines from the coverage data (uncovered.py): GROUPS frames whose bound on the
 * other side is of the opposite kind (a PRECEDING as the end, a FOLLOWING as the start, zero
 * offsets), RANGE bounds that land exactly on a peer value, EXCLUDE TIES where the current
 * row sits inside a peer group of three, and the two RANGE refusals (overflow and a sort key
 * type with no arithmetic). Checked against the reference running the same text.
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
public sealed class WindowFrameEdgeOracleTests
{
    [JavaOracleFact]
    public void FrameEdgesAnswerTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT PRIMARY KEY, G INT, V INT, NAME VARCHAR(5)) AS VALUES "
                + "(1, 1, 10, 'a'), (2, 1, 20, 'b'), (3, 1, 30, 'c'), (4, 2, 40, 'd'), (5, 2, 50, 'e')",
            // GROUPS: the end bound is a PRECEDING one.
            "SELECT ID, SUM(V) OVER (ORDER BY G GROUPS BETWEEN 3 PRECEDING AND 1 PRECEDING) S FROM T ORDER BY ID",
            // GROUPS: the start bound is a FOLLOWING one.
            "SELECT ID, SUM(V) OVER (ORDER BY G GROUPS BETWEEN 1 FOLLOWING AND 3 FOLLOWING) S FROM T ORDER BY ID",
            // GROUPS: zero offsets on both sides.
            "SELECT ID, SUM(V) OVER (ORDER BY G GROUPS BETWEEN 0 FOLLOWING AND 1 FOLLOWING) S FROM T ORDER BY ID",
            "SELECT ID, SUM(V) OVER (ORDER BY G GROUPS BETWEEN 1 PRECEDING AND 0 PRECEDING) S FROM T ORDER BY ID",
            // RANGE: the computed bound lands exactly on peer values (a binary-search hit
            // that walks over the peers).
            "SELECT ID, ARRAY_AGG(ID) OVER (ORDER BY G RANGE BETWEEN 0 PRECEDING AND 0 FOLLOWING) A FROM T ORDER BY ID",
            // EXCLUDE TIES with peers of three: the current row is inside its peer group,
            // both with the frame walked forward and backward.
            "SELECT ID, ARRAY_AGG(ID) OVER (ORDER BY G ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING EXCLUDE TIES) A FROM T ORDER BY ID",
            "SELECT ID, ARRAY_AGG(ID) OVER (ORDER BY G DESC ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING EXCLUDE TIES) A FROM T ORDER BY ID",
            // RANGE arithmetic that overflows the sort key's type answers no bound.
            "SELECT ID, SUM(V) OVER (ORDER BY G RANGE BETWEEN 9999999999999 PRECEDING AND CURRENT ROW) S FROM T ORDER BY ID",
            // A sort key with no arithmetic is refused.
            "SELECT ID, SUM(V) OVER (ORDER BY NAME RANGE BETWEEN 1 PRECEDING AND CURRENT ROW) S FROM T ORDER BY ID",
            "DROP TABLE T",
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
    /// Runs the statements here and writes what the question answered: the rows, or the code of
    /// the error.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("window_frame_edge_oracle")), "sa")
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:window_frame_edge_oracle", "sa", "");
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
