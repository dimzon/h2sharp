/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * MERGE USING at prepare time: a WHEN clause whose AND condition is constant FALSE can never
 * fire and is dropped, one whose AND condition is constant TRUE becomes unguarded and fires
 * for every eligible row. Checked against the reference running the same text.
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
public sealed class MergeUsingClauseRemovalOracleTests
{
    [JavaOracleFact]
    public void ConstantClauseConditionsAnswerTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE P(ID INT PRIMARY KEY, NAME VARCHAR(10)) AS VALUES (1, 'a')",
            // Constant FALSE: the update branch is dropped, the row keeps its name.
            "MERGE INTO P USING (VALUES (1, 'b')) S(ID, NAME) ON (P.ID = S.ID) WHEN MATCHED AND 1 = 0 THEN UPDATE SET P.NAME = S.NAME",
            "SELECT ID, NAME FROM P ORDER BY ID",
            // Constant TRUE: the update fires, the insert clause (also constant FALSE) is dropped.
            "MERGE INTO P USING (VALUES (1, 'c'), (2, 'd')) S(ID, NAME) ON (P.ID = S.ID) WHEN MATCHED AND 1 = 1 THEN UPDATE SET P.NAME = S.NAME WHEN NOT MATCHED AND 1 = 0 THEN INSERT (ID, NAME) VALUES (S.ID, S.NAME)",
            "SELECT ID, NAME FROM P ORDER BY ID",
            // Constant TRUE on the insert branch: the missing row is inserted.
            "MERGE INTO P USING (VALUES (3, 'e')) S(ID, NAME) ON (P.ID = S.ID) WHEN NOT MATCHED AND 1 = 1 THEN INSERT (ID, NAME) VALUES (S.ID, S.NAME)",
            "SELECT ID, NAME FROM P ORDER BY ID",
            "DROP TABLE P",
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
            new Database(ConnectionInfo.Memory("merge_removal_oracle")), "sa")
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
    /// <returns>the lines</returns>
    private static List<string> RunJava(IReadOnlyList<string> statements)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:merge_removal_oracle", "sa", "");
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
