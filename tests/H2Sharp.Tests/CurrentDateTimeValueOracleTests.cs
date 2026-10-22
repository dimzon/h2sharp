/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The current date and time functions, checked against the reference running the same text.
 *
 * The functions read the clock of the session, so the two sides cannot answer with the same
 * instant. What is compared here is the shape: the type of the answer, the name of the
 * column and the error the reference gives for a scale it does not accept.
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
public sealed class CurrentDateTimeValueOracleTests
{
    /// <summary>
    /// The shape of the answers, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheCurrentDateTimeFunctionsAreTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT)",
            "INSERT INTO T VALUES (1)",
            "SELECT CURRENT_DATE FROM T",
            "SELECT CURRENT_TIME FROM T",
            "SELECT LOCALTIME FROM T",
            "SELECT CURRENT_TIMESTAMP FROM T",
            "SELECT LOCALTIMESTAMP FROM T",
            "SELECT CURRENT_TIMESTAMP(3) FROM T",
            "SELECT CURRENT_TIME(6) FROM T",
            "SELECT CURRENT_TIMESTAMP(99) FROM T",
            "SELECT CURRENT_TIMESTAMP(-1) FROM T",
            "SELECT CURRENT_DATE(3) FROM T",
            "SELECT CURDATE() FROM T",
            "SELECT CURTIME() FROM T",
            "SELECT CURTIME(3) FROM T",
            "SELECT NOW() FROM T",
            "SELECT CURRENT_TIMESTAMP() FROM T",
            "CREATE TABLE D(C TIMESTAMP DEFAULT CURRENT_TIMESTAMP, X INT)",
            "INSERT INTO D(X) VALUES (1)",
            "SELECT COUNT(*) FROM D",
            "SELECT C IS NOT NULL FROM D",
            "SELECT X FROM D",
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
    /// Runs the statements here and writes what the question answered: the type of the value
    /// and the error, and not the value itself, which is the time of the machine.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(new ConnectionInfo("jdbc:h2:mem:current_time_oracle")), "sa")
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
                            cells.Add(value is null or ValueNull
                                ? "-"
                                : Value.GetTypeName(value.GetValueType()));
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
                    ? "=" + h2.H2ErrorCode.ToString(CultureInfo.InvariantCulture) + " " + h2.OriginalMessage
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:current_time_oracle");
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
                      line.append(rs.getMetaData().getColumnTypeName(c));
                    }
                    System.out.println(line);
                  }
                }
              } catch (java.sql.SQLException e) {
                String m = e.getMessage();
                int k = m.indexOf("; SQL statement:");
                System.out.println("=" + e.getErrorCode() + " " + (k >= 0 ? m.substring(0, k) : m));
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}
