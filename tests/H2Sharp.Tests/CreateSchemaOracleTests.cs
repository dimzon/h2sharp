/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The CREATE SCHEMA statement, checked against the reference running the same text.
 *
 * A schema is a named set of tables. The statement creates one, and the objects that follow
 * go into it.
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
public sealed class CreateSchemaOracleTests
{
    /// <summary>
    /// The forms of the statement, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheCreateSchemaIsTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE SCHEMA S1",
            "CREATE TABLE S1.T(ID INT)",
            "INSERT INTO S1.T VALUES (1)",
            "SELECT * FROM S1.T",
            "CREATE SCHEMA IF NOT EXISTS S1",
            "CREATE SCHEMA S1",
            "CREATE SCHEMA S2",
            "CREATE TABLE S2.T(ID INT)",
            "SELECT * FROM S2.T",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        AssertTrue(java, port);
    }

    /// <summary>
    /// Compares the answers of the two sides.
    /// </summary>
    /// <param name="java">the answers of the reference</param>
    /// <param name="port">the answers of the port</param>
    private static void AssertTrue(List<string> java, List<string> port)
    {
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("create_schema_oracle")), "sa")
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:create_schema_oracle");
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
