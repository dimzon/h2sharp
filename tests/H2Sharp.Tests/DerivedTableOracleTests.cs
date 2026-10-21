/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The derived table, checked against the reference running the same text.
 *
 * A derived table is a query in the FROM clause that stands for a table: the rows are the
 * rows of the query, and the name after it is the name the table is known by. The names in
 * parentheses after that name are the names the columns of the table are known by.
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
public sealed class DerivedTableOracleTests
{
    /// <summary>
    /// The forms of the derived table, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheDerivedTableIsTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT, NAME VARCHAR(20))",
            "INSERT INTO T VALUES (1, 'a'), (2, 'b'), (3, 'c')",
            "SELECT * FROM (SELECT ID FROM T) x",
            "SELECT x.ID FROM (SELECT ID FROM T) AS x ORDER BY x.ID",
            "SELECT * FROM (SELECT ID FROM T) AS x",
            "SELECT * FROM (SELECT ID FROM T) x(C) ORDER BY C",
            "SELECT * FROM (SELECT ID, NAME FROM T) x(C, D) ORDER BY C",
            "SELECT COUNT(*) FROM (SELECT ID FROM T) x",
            "SELECT * FROM (SELECT ID FROM T) x WHERE x.ID = 2",
            "SELECT SUM(ID) FROM (SELECT ID FROM T) x",
            "SELECT * FROM (SELECT ID FROM T ORDER BY ID DESC) x",
            "SELECT * FROM (SELECT ID FROM T WHERE ID > 1) x ORDER BY x.ID",
            "SELECT * FROM (SELECT ID FROM T) x JOIN T y ON x.ID = y.ID ORDER BY x.ID",
            "SELECT * FROM (SELECT * FROM T) x",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        AssertSame(java, port);
    }

    /// <summary>
    /// The names the derived table gives its columns, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheColumnNamesAreTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT)",
            "INSERT INTO T VALUES (1), (2)",
            "SELECT C FROM (SELECT ID FROM T) x(C) ORDER BY C",
            "SELECT x.C FROM (SELECT ID FROM T) x(C) ORDER BY x.C",
            "SELECT * FROM (SELECT ID FROM T) x(C, D)",
            "SELECT * FROM (SELECT ID FROM T) x(C, C)",
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
            new Database(new ConnectionInfo("jdbc:h2:mem:derived_oracle")), "sa")
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:derived_oracle");
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
