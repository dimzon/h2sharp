/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A simple CASE whose operand is itself a comparison: the parser stores the operand as
 * EQUAL_NULL_SAFE with the whenOperand flag, and evaluation runs it through the when-value
 * path instead of the plain condition path. Checked against the reference on the same text:
 * TRUE/FALSE/NULL operands, the NULL short-circuit, and a nested CASE in the result.
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
public sealed class CaseOperandOracleTests
{
    [JavaOracleFact]
    public void ComparisonOperandsOfSimpleCaseAnswerTheReference()
    {
        string[] statements =
        [
            // The operand is a comparison: it answers TRUE or FALSE, matched by the WHEN value.
            "SELECT CASE 1 > 2 WHEN TRUE THEN 'a' WHEN FALSE THEN 'b' END",
            "SELECT CASE 2 = 2 WHEN TRUE THEN 'a' ELSE 'b' END",
            // The NULL short-circuit: a NULL operand never reaches the right side.
            "SELECT CASE NULL = 1 WHEN FALSE THEN 'a' ELSE 'b' END",
            "SELECT CASE NULL IS NULL WHEN TRUE THEN 'a' ELSE 'b' END",
            // IS DISTINCT FROM keeps the NULL operands, so the comparison is not short-circuited.
            "SELECT CASE NULL IS DISTINCT FROM NULL WHEN FALSE THEN 'a' ELSE 'b' END",
            // A nested CASE in the result of the matched branch.
            "SELECT CASE 1 < 2 WHEN TRUE THEN CASE 3 > 4 WHEN FALSE THEN 'x' ELSE 'y' END ELSE 'z' END",
            // A non-constant comparison operand stays unwrapped and answers through the
            // plain when-value path.
            "CREATE TABLE C(A INT) AS VALUES (1), (-1), (NULL)",
            "SELECT A, CASE A > 0 WHEN TRUE THEN 'p' ELSE 'n' END FROM C ORDER BY A",
            "SELECT A, CASE A = 1 WHEN TRUE THEN 'one' WHEN FALSE THEN 'other' END FROM C ORDER BY A",
            "DROP TABLE C",
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
            new Database(ConnectionInfo.Memory("case_operand_oracle")), "sa")
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:case_operand_oracle", "sa", "");
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
