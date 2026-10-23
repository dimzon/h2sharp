/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * UPDATE of the primary key, checked against the reference running the same text.
 *
 * A new key moves the row: the old rows go first and the new ones after, so two rows
 * swapping their keys do not collide halfway, and a failed update leaves nothing behind.
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
public sealed class UpdateKeyOracleTests
{
    /// <summary>
    /// A new key moves the row, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void ANewKeyMovesTheRowAsTheReferenceMovesIt()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT PRIMARY KEY, V VARCHAR(20))",
            "INSERT INTO T VALUES (1, 'a'), (2, 'b'), (3, 'c')",
            "UPDATE T SET ID = 10 WHERE ID = 1",
            "SELECT ID, V FROM T ORDER BY ID",
            "UPDATE T SET ID = ID + 100",
            "SELECT ID, V FROM T ORDER BY ID",
            "UPDATE T SET ID = 102 WHERE ID = 110",
            "SELECT COUNT(*) FROM T",
            "SELECT ID, V FROM T ORDER BY ID",
            "UPDATE T SET V = 'z' WHERE ID = 102",
            "SELECT V FROM T WHERE ID = 102",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Two rows swapping their keys, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TwoRowsSwappingTheirKeysAreTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT PRIMARY KEY, V VARCHAR(20))",
            "INSERT INTO T VALUES (1, 'a'), (2, 'b')",
            "UPDATE T SET ID = CASE WHEN ID = 1 THEN 2 WHEN ID = 2 THEN 1 ELSE ID END",
            "SELECT ID, V FROM T ORDER BY ID",
            "UPDATE T SET ID = 1 WHERE ID = 2",
            "SELECT ID, V FROM T ORDER BY ID",
            "CREATE INDEX VI ON T(V)",
            "UPDATE T SET V = 'c' WHERE ID = 1",
            "SELECT V FROM T WHERE ID = 1",
            "UPDATE T SET ID = 5, V = 'd' WHERE ID = 1",
            "SELECT ID, V FROM T ORDER BY ID",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered: the error, or the rows.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(new ConnectionInfo("jdbc:h2:mem:update_key_oracle")), "sa")
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
                    : "=" + e.GetType().Name);
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:update_key_oracle");
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
