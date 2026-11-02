/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The IS JSON predicate, checked against the reference running the same text. The matrix
 * follows the reference's own datatypes/json.sql and other/case.sql: every item type over a
 * varchar, a varbinary, a blob and a stored JSON value, unique keys, the NOT forms, the CASE
 * WHEN operand and the printed plan.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class JsonPredicateOracleTests
{
    /// <summary>
    /// The statements, the same on both sides. The TEST table holds the reference's own rows:
    /// an object with duplicate keys, an object with unique keys, an unterminated object and
    /// a NULL — as a varchar, a varbinary and a stored JSON value.
    /// </summary>
    [JavaOracleFact]
    public void TheJsonPredicateAnswersTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE TEST(ID INT PRIMARY KEY, S VARCHAR, B VARBINARY, L BLOB, J JSON)",
            "INSERT INTO TEST VALUES (1, '{\"a\":1,\"a\":2}', STRINGTOUTF8('{\"a\":1,\"a\":2}'), "
                + "'{\"a\":1,\"a\":2}' FORMAT JSON, '{\"a\":1,\"a\":2}' FORMAT JSON), "
                + "(2, '{\"a\":1,\"b\":2}', STRINGTOUTF8('{\"a\":1,\"b\":2}'), "
                + "'{\"a\":1,\"b\":2}' FORMAT JSON, '{\"a\":1,\"b\":2}' FORMAT JSON), "
                + "(3, '{\"a\":1,\"b\":2', STRINGTOUTF8('{\"a\":1,\"b\":2'), NULL, NULL), "
                + "(4, NULL, NULL, NULL, NULL)",
            // Without unique keys the duplicates are fine; with them the first row fails.
            "SELECT S IS JSON, B IS JSON WITHOUT UNIQUE, L IS JSON, J IS JSON WITHOUT UNIQUE KEYS "
                + "FROM TEST ORDER BY ID",
            "SELECT S IS NOT JSON, B IS NOT JSON WITHOUT UNIQUE, L IS NOT JSON, J IS NOT JSON WITHOUT UNIQUE KEYS "
                + "FROM TEST ORDER BY ID",
            "SELECT S IS JSON WITH UNIQUE KEYS, B IS JSON WITH UNIQUE, L IS JSON WITH UNIQUE KEYS, "
                + "J IS JSON WITH UNIQUE KEYS FROM TEST ORDER BY ID",
            "SELECT S IS NOT JSON WITH UNIQUE KEYS, B IS NOT JSON WITH UNIQUE, J IS NOT JSON WITH UNIQUE KEYS "
                + "FROM TEST ORDER BY ID",
            // A non-text value is never JSON.
            "SELECT 1 IS JSON, 1 IS NOT JSON FROM TEST ORDER BY ID",
            // The item types over the varchar and over the stored JSON.
            "SELECT S IS JSON, S IS JSON VALUE, S IS JSON ARRAY, S IS JSON OBJECT, S IS JSON SCALAR "
                + "FROM TEST ORDER BY ID",
            "SELECT J IS JSON, J IS JSON VALUE, J IS JSON ARRAY, J IS JSON OBJECT, J IS JSON SCALAR "
                + "FROM TEST ORDER BY ID",
            "SELECT J IS JSON WITH UNIQUE KEYS, J IS JSON VALUE WITH UNIQUE KEYS, "
                + "J IS JSON ARRAY WITH UNIQUE KEYS, J IS JSON OBJECT WITH UNIQUE KEYS, "
                + "J IS JSON SCALAR WITH UNIQUE KEYS FROM TEST ORDER BY ID",
            // The predicate as a CASE WHEN operand.
            "SELECT J, CASE J WHEN IS JSON ARRAY THEN 1 WHEN IS NOT JSON OBJECT THEN 2 ELSE 3 END "
                + "FROM TEST ORDER BY ID",
            // The NOT folds into the predicate.
            "SELECT NOT S IS JSON FROM TEST ORDER BY ID",
            // The predicate as a search condition: the plan prints it in parentheses.
            "EXPLAIN SELECT ID FROM TEST WHERE S IS JSON OBJECT WITH UNIQUE KEYS",
            // The predicate as an UPDATE filter.
            "UPDATE TEST SET J = S FORMAT JSON WHERE S IS JSON",
            "SELECT ID, S IS JSON FROM TEST ORDER BY ID",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        // The EXPLAIN answer is one layout question: the reference spreads it over indented
        // lines, the port writes it inline, and a multi-line cell arrives as several captured
        // lines, so the folded text as a whole is what is compared.
        string javaText = Regex.Replace(string.Join(" ", java), "\\s+", " ").Trim();
        string portText = Regex.Replace(string.Join(" ", port), "\\s+", " ").Trim();
        Assert.True(javaText == portText, "java:\n" + javaText + "\nport:\n" + portText);
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
            new Database(ConnectionInfo.Memory("json_predicate_oracle")), "sa")
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
                if (sql.StartsWith("SELECT", StringComparison.Ordinal)
                    || sql.StartsWith("EXPLAIN", StringComparison.Ordinal))
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
                            cells.Add(value is null or ValueNull
                                ? "-"
                                : Regex.Replace(value.GetString() ?? "-", "\\s+", " "));
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:json_predicate_oracle");
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
                      line.append(rs.wasNull() ? "-" : s.replaceAll("\\\\s+", " "));
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
