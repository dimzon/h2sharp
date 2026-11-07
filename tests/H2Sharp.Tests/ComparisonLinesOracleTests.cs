/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Comparison lines from the coverage data (uncovered.py): the negation table behind NOT of
 * every operator, the predicate case operands (CASE x WHEN > 1 THEN ...), boolean against
 * numeric comparisons, the row-constructor index conditions in both directions, and the
 * geometry intersect with a null.
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
public sealed class ComparisonLinesOracleTests
{
    [JavaOracleFact]
    public void ComparisonLinesAnswerTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(A INT, B BOOLEAN) AS VALUES (1, TRUE), (2, FALSE), (3, NULL)",
            // NOT of every operator: the optimizer rewrites it into the negated comparison.
            "SELECT A FROM T WHERE NOT (A > 1) ORDER BY A",
            "SELECT A FROM T WHERE NOT (A >= 1) ORDER BY A",
            "SELECT A FROM T WHERE NOT (A < 3) ORDER BY A",
            "SELECT A FROM T WHERE NOT (A <= 2) ORDER BY A",
            "SELECT A FROM T WHERE NOT (A = 1) ORDER BY A",
            "SELECT A FROM T WHERE NOT (A <> 1) ORDER BY A",
            "SELECT A FROM T WHERE NOT (A IS DISTINCT FROM 1) ORDER BY A",
            "SELECT A FROM T WHERE NOT (A IS NOT DISTINCT FROM 1) ORDER BY A",
            // A predicate as the when operand of the simple case.
            "SELECT CASE A WHEN > 1 THEN 'big' ELSE 'small' END FROM T ORDER BY A",
            "SELECT CASE A WHEN >= 2 THEN 'big' WHEN < 2 THEN 'small' ELSE 'mid' END FROM T ORDER BY A",
            // A boolean against a number: the comparison is boolean-valued either way.
            "SELECT A FROM T WHERE B = TRUE ORDER BY A",
            "SELECT A FROM T WHERE B <> TRUE ORDER BY A",
            "SELECT A FROM T WHERE TRUE = B ORDER BY A",
            "SELECT A FROM T WHERE B = 1 ORDER BY A",
            "SELECT A FROM T WHERE 0 = B ORDER BY A",
            // Row constructors as index conditions, in both directions.
            "CREATE INDEX T_AB ON T(A, B)",
            "SELECT A FROM T WHERE (A, B) > (1, FALSE) ORDER BY A",
            "SELECT A FROM T WHERE (1, FALSE) < (A, B) ORDER BY A",
            "SELECT A FROM T WHERE (A, B) = (2, FALSE) ORDER BY A",
            // Geometry intersect against a null answers null.
            "SELECT CAST('POINT(1 1)' AS GEOMETRY) && CAST(NULL AS GEOMETRY)",
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
            new Database(ConnectionInfo.Memory("comparison_lines_oracle")), "sa")
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:comparison_lines_oracle", "sa", "");
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
