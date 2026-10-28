/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A literal with a type in front of it, checked against the reference running the same text.
 *
 * TIMESTAMP '2035-09-25 00:00:00' says what the literal is, and a column may take one as
 * its default. The providers write exactly that when a default value has a type.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class TypedLiteralOracleTests
{
    /// <summary>
    /// The typed literals, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheTypedLiteralsAreTheSameAsTheReference()
    {
        string[] statements =
        [
            "SELECT TIMESTAMP '2035-09-25 00:00:00'",
            "SELECT DATE '2035-09-25'",
            "SELECT TIME '00:00:00'",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        AssertSame(java, port);
    }

    /// <summary>
    /// A default with a typed literal, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheTypedDefaultIsTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T1(ID INT, D TIMESTAMP NOT NULL DEFAULT TIMESTAMP '2035-09-25T00:00:00')",
            "CREATE TABLE T2(ID INT, D TIMESTAMP DEFAULT '2035-09-25 00:00:00')",
            "CREATE TABLE T3(ID INT, D TIMESTAMP DEFAULT TIMESTAMP '2035-09-25 00:00:00')",
            "INSERT INTO T1(ID) VALUES(1)",
            "INSERT INTO T2(ID) VALUES(1)",
            "INSERT INTO T3(ID) VALUES(1)",
            "SELECT D FROM T1",
            "SELECT D FROM T2",
            "SELECT D FROM T3",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        AssertSame(java, port);
    }

    /// <summary>
    /// Compares the answers of the two sides.
    /// </summary>
    /// <param name="java">the answers of the reference</param>
    /// <param name="port">the answers of the port</param>
    private static void AssertSame(List<string> java, List<string> port)
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
            new Database(ConnectionInfo.Memory("typed_literal_oracle")), "sa")
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:typed_literal_oracle");
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
