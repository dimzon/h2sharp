/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The limits of a query, checked against the reference running the same text.
 *
 * OFFSET and FETCH say how many rows to skip and how many to read; the MySQL form of LIMIT
 * says the same in the other order. The counts are expressions, so a limit may name a
 * parameter — that is the form the linq2db provider writes for Take() and First().
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
public sealed class LimitOracleTests
{
    /// <summary>
    /// The forms of the limit, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheLimitFormsAreTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT)",
            "INSERT INTO T VALUES (1), (2), (3), (4), (5)",
            "SELECT ID FROM T ORDER BY ID LIMIT 2",
            "SELECT ID FROM T ORDER BY ID LIMIT 2 OFFSET 1",
            "SELECT ID FROM T ORDER BY ID OFFSET 2",
            "SELECT ID FROM T ORDER BY ID FETCH FIRST 2 ROWS ONLY",
            "SELECT ID FROM T ORDER BY ID FETCH NEXT 2 ROWS ONLY",
            "SELECT ID FROM T ORDER BY ID LIMIT 1, 3",
            "SELECT ID FROM T ORDER BY ID LIMIT 100",
            "SELECT ID FROM T ORDER BY ID FETCH FIRST 50 PERCENT ROWS ONLY",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        AssertSame(java, port);
    }

    /// <summary>
    /// A limit that names a parameter, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheParameterIsTheSameAsTheReference()
    {
        List<string> port = RunPortWithParameter();
        List<string> java = RunJavaWithParameter();
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
        SessionLocal session = NewSession();
        List<string> lines = [];
        foreach (string sql in statements)
        {
            lines.AddRange(Run(session, sql));
        }

        return lines;
    }

    /// <summary>
    /// Runs the query with a parameter here and writes what it answered.
    /// </summary>
    /// <returns>the lines</returns>
    private static List<string> RunPortWithParameter()
    {
        SessionLocal session = NewSession();
        List<string> lines = [];
        lines.AddRange(Run(session, "CREATE TABLE T(ID INT)"));
        lines.AddRange(Run(session, "INSERT INTO T VALUES (1), (2), (3), (4), (5)"));
        lines.AddRange(Run(session, "SELECT ID FROM T ORDER BY ID LIMIT ?", 2));
        return lines;
    }

    /// <summary>
    /// Creates a session of its own.
    /// </summary>
    /// <returns>the session</returns>
    private static SessionLocal NewSession()
    {
        return new SessionLocal(
            new Database(ConnectionInfo.Memory("limit_oracle")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
    }

    /// <summary>
    /// Runs one statement and writes what the question answered.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="sql">the statement</param>
    /// <param name="parameterValue">the value of the parameter, or null</param>
    /// <returns>the lines</returns>
    private static List<string> Run(SessionLocal session, string sql, int? parameterValue = null)
    {
        List<string> lines = [];
        try
        {
            CommandContainer container =
                Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
            if (parameterValue is not null)
            {
                List<Parameter>? parameters = container.GetPreparedCommand().GetParameters();
                Assert.NotNull(parameters);
                parameters![0].SetValue(ValueInteger.Get(parameterValue.Value));
            }

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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:limit_oracle");
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

    /// <summary>
    /// Runs the query with a parameter on the reference and writes what it answered.
    /// </summary>
    /// <returns>the lines</returns>
    private static List<string> RunJavaWithParameter()
        => OracleRunner.Run(
            [],
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:limit_oracle");
            java.sql.Statement stat = conn.createStatement();
            stat.execute("CREATE TABLE T(ID INT)");
            stat.execute("INSERT INTO T VALUES (1), (2), (3), (4), (5)");
            java.sql.PreparedStatement ps = conn.prepareStatement("SELECT ID FROM T ORDER BY ID LIMIT ?");
            ps.setInt(1, 2);
            java.sql.ResultSet rs = ps.executeQuery();
            while (rs.next()) {
              System.out.println("=" + rs.getString(1));
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}
