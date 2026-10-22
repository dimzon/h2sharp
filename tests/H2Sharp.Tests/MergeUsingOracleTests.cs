/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * MERGE INTO ... USING ... ON ... WHEN ..., checked against the reference running the same text.
 *
 * The statement walks the source and, for every row, either updates or deletes the target row
 * the ON condition joins to, or inserts a new one. The clauses are tried in the order they were
 * written, and the first one whose AND condition holds runs.
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
public sealed class MergeUsingOracleTests
{
    /// <summary>
    /// The statement, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheMergeUsingIsTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(10))",
            "CREATE TABLE U(ID INT, NAME VARCHAR(10))",
            "INSERT INTO T VALUES (1, 'a'), (2, 'b')",
            "INSERT INTO U VALUES (1, 'x'), (3, 'y')",
            // A matched row is updated, a missed one is inserted.
            "MERGE INTO T AS t USING U AS u ON t.ID = u.ID"
                + " WHEN MATCHED THEN UPDATE SET NAME = u.NAME"
                + " WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (u.ID, u.NAME)",
            "SELECT ID, NAME FROM T ORDER BY ID",
            // A matched row is deleted instead.
            "MERGE INTO T USING U ON T.ID = U.ID WHEN MATCHED THEN DELETE",
            "SELECT ID, NAME FROM T ORDER BY ID",
            // Two WHEN MATCHED clauses: the first that applies wins.
            "MERGE INTO T USING U ON T.ID = U.ID"
                + " WHEN MATCHED AND U.NAME = 'zzz' THEN DELETE"
                + " WHEN MATCHED THEN UPDATE SET NAME = 'm'",
            "SELECT ID, NAME FROM T ORDER BY ID",
            // MERGE without a WHEN clause for one side changes only that side.
            "MERGE INTO T USING U ON T.ID = U.ID WHEN MATCHED THEN UPDATE SET NAME = 'only'",
            "SELECT ID, NAME FROM T ORDER BY ID",
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
            new Database(new ConnectionInfo("jdbc:h2:mem:merge_using_oracle")), "sa")
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
                    _ = container.ExecuteUpdate(false);
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:merge_using_oracle");
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
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}
