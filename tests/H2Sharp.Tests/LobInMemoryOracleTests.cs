/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Character and binary large objects of an in-memory database, checked against the reference
 * running the same text. Every database goes through the LOB storage map, so the SQL surface
 * covers its read and write paths: short values, appended values, and one large value staged
 * through several blocks.
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
public sealed class LobInMemoryOracleTests
{
    [JavaOracleFact]
    public void InMemoryLobsAnswerTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT PRIMARY KEY, C CLOB, B BLOB)",
            "INSERT INTO T VALUES (1, 'hello', X'0102')",
            "SELECT C FROM T WHERE ID = 1",
            "SELECT LENGTH(C), LENGTH(B) FROM T WHERE ID = 1",
            "SELECT SUBSTRING(C FROM 2 FOR 3) FROM T WHERE ID = 1",
            // Appending goes through the stored value.
            "UPDATE T SET C = C || ' world' WHERE ID = 1",
            "SELECT C FROM T WHERE ID = 1",
            // A large value is staged through several blocks.
            "UPDATE T SET C = REPEAT('x', 200000) || 'end' WHERE ID = 1",
            "SELECT LENGTH(C) FROM T WHERE ID = 1",
            "SELECT SUBSTRING(C FROM 199999 FOR 4) FROM T WHERE ID = 1",
            // The binary value reads back the same.
            "SELECT HEX(SUBSTRING(B FROM 1 FOR 2)) FROM T WHERE ID = 1",
            "UPDATE T SET B = B || X'0304' WHERE ID = 1",
            "SELECT LENGTH(B), HEX(B) FROM T WHERE ID = 1",
            "DELETE FROM T WHERE ID = 1",
            "SELECT COUNT(*) FROM T",
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
            new Database(ConnectionInfo.Memory("lob_oracle")), "sa")
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:lob_oracle", "sa", "");
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
