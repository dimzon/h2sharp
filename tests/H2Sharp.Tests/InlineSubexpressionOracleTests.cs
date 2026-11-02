/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The inlining of subexpressions in n-ary operations, checked against the reference running
 * the same text.
 *
 * A parenthesized chain folds back into one operation only when the inner operation survives
 * optimization — that is, when its operands are not constants: (V || 'b') || 'c' over a
 * column becomes a single three-argument concatenation, while ('a' || 'b') || 'c' folds the
 * bracket into a literal first and has nothing to inline. CONCAT and GCD do the same with
 * their own calls.
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
public sealed class InlineSubexpressionOracleTests
{
    /// <summary>
    /// The statements, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheInlinedOperationsAnswerTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE P(ID INT PRIMARY KEY, V VARCHAR, X VARBINARY)",
            "INSERT INTO P VALUES (1, 'a', X'01'), (2, 'b', X'02')",
            // The n-ary chain the parser builds directly.
            "SELECT V || 'b' || 'c' FROM P ORDER BY ID",
            "SELECT X || X'02' || X'03' FROM P ORDER BY ID",
            // The bracket survives optimization over a column, so the outer operation
            // inlines it into one three-argument call.
            "SELECT (V || 'b') || 'c' FROM P ORDER BY ID",
            "SELECT (X || X'02') || X'03' FROM P ORDER BY ID",
            // A bracket of constants folds before the inlining can see it.
            "SELECT ('a' || 'b') || 'c' FROM P ORDER BY ID",
            // CONCAT inlines its own nested calls.
            "SELECT CONCAT(V, 'b', 'c') FROM P ORDER BY ID",
            "SELECT CONCAT(CONCAT(V, 'b'), 'c') FROM P ORDER BY ID",
            // GCD likewise.
            "SELECT GCD(GCD(ID + 8, ID + 12), ID + 30) FROM P ORDER BY ID",
            "SELECT GCD(ID + 8, ID + 12, ID + 30) FROM P ORDER BY ID",
            // NULL propagates the same way through every shape.
            "SELECT (V || NULL) || 'c' FROM P ORDER BY ID",
            "SELECT CONCAT(CONCAT(V, NULL), 'c') FROM P ORDER BY ID",
            // Two brackets inline at once: the many-subexpression copy path.
            "SELECT (V || 'b') || (V || 'c') FROM P ORDER BY ID",
            // The operation inside an aggregate and inside a condition: the visitors and the
            // cost model walk it.
            "SELECT MAX(V || 'b') FROM P",
            "SELECT ID FROM P WHERE (V || 'b') = 'ab' ORDER BY ID",
            // A non-inlinable argument between two inlined brackets.
            "SELECT (V || 'b') || ID || (V || 'c') FROM P ORDER BY ID",
            // The update-aggregate walk over a group query's select list.
            "SELECT (V || 'b') || 'c' FROM P GROUP BY ID ORDER BY ID",
        ];
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
            new Database(ConnectionInfo.Memory("inline_oracle")), "sa")
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:inline_oracle");
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
