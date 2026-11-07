/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Comparison edges from the coverage data (uncovered.py): the negated type of an ANY/ALL
 * array predicate for every relational operator, a value on the left of a row list (the
 * reversed index-condition type), and, in MySQL mode, the numeric/boolean comparison the
 * mode allows. Checked against the reference running the same text.
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
public sealed class ComparisonEdgesOracleTests
{
    [JavaOracleFact]
    public void ArrayPredicatesAndRowListsAnswerTheReference()
    {
        string[] statements =
        [
            // Every relational operator asks for its negation when the predicate is built.
            "SELECT 1 = ANY(ARRAY[1, 2]), 1 = ALL(ARRAY[1, 1])",
            "SELECT 1 <> ANY(ARRAY[1, 2]), 1 <> ALL(ARRAY[1, 2])",
            "SELECT 1 > ANY(ARRAY[1, 2]), 1 >= ANY(ARRAY[1, 2])",
            "SELECT 1 < ANY(ARRAY[1, 2]), 1 <= ANY(ARRAY[1, 2])",
            // A value expression on the left of a row list: the index-condition type is
            // reversed, for every operator.
            "CREATE TABLE T(A INT, B INT) AS VALUES (1, 2), (3, 4)",
            "SELECT A FROM T WHERE ROW(A, B) = (1, 2) ORDER BY A",
            "SELECT A FROM T WHERE ROW(A, B) <> (1, 2) ORDER BY A",
            "SELECT A FROM T WHERE ROW(A, B) > (1, 2) ORDER BY A",
            "SELECT A FROM T WHERE ROW(A, B) >= (1, 2) ORDER BY A",
            "SELECT A FROM T WHERE ROW(A, B) < (3, 4) ORDER BY A",
            "SELECT A FROM T WHERE ROW(A, B) <= (3, 4) ORDER BY A",
            "DROP TABLE T",
        ];
        RunBoth(statements, null);
    }

    [JavaOracleFact]
    public void NumericBooleanComparisonsAnswerTheReferenceInMySqlMode()
    {
        string[] statements =
        [
            // The mode allows comparing a BOOLEAN with a number; the comparability check is
            // skipped and the comparison itself answers.
            "SELECT TRUE = 1, TRUE = 0, FALSE = 1, 1 = TRUE, 1 <> TRUE",
            "SELECT TRUE < 1, TRUE > 0, 0 < TRUE",
        ];
        RunBoth(statements, Mode.GetInstance("MySQL"));
    }

    /// <summary>
    /// Runs the statements on both sides and asserts the same answers.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <param name="mode">the mode both sides open with, or null</param>
    private static void RunBoth(IReadOnlyList<string> statements, Mode? mode)
    {
        List<string> port = RunPort(statements, mode);
        List<string> java = RunJava(statements, mode);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered: the rows, or the code of
    /// the error.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <param name="mode">the mode the database opens with, or null</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements, Mode? mode)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("comparison_edges_oracle", mode)), "sa")
        {
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        session.CastDataProvider = new SessionCastDataProvider(session, DefaultCastDataProvider.Instance);
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
    /// <param name="mode">the mode the database opens with, or null</param>
    /// <returns>the lines</returns>
    private static List<string> RunJava(IReadOnlyList<string> statements, Mode? mode)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:comparison_edges_oracleMODE", "sa", "");
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
            """.Replace(
                "comparison_edges_oracleMODE",
                "comparison_edges_oracle" + (mode is null ? "" : ";MODE=" + mode)),
            extraJavaArgs: ["-Duser.language=en"]);
}
