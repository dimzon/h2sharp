/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The joins, checked against the reference running the same text.
 *
 * A join reads two tables as one: the rows of the left table, and for each of them the rows
 * of the right table that the condition of the join accepts. An outer join keeps the rows of
 * the left table that nothing on the right matched, and a natural join takes its condition
 * from the columns the two tables have in common.
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
public sealed class JoinOracleTests
{
    /// <summary>
    /// The inner join, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheInnerJoinIsTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT, NAME VARCHAR(20))",
            "CREATE TABLE U(ID INT, NOTE VARCHAR(20))",
            "INSERT INTO T VALUES (1, 'a'), (2, 'b'), (3, 'c')",
            "INSERT INTO U VALUES (2, 'x'), (3, 'y')",
            "SELECT T.ID, T.NAME, U.NOTE FROM T JOIN U ON T.ID = U.ID ORDER BY T.ID",
            "SELECT T.ID, U.NOTE FROM T INNER JOIN U ON T.ID = U.ID ORDER BY T.ID",
            "SELECT ID, NOTE FROM T JOIN U USING (ID) ORDER BY ID",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        AssertSame(java, port);
    }

    /// <summary>
    /// The outer joins, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheOuterJoinIsTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT, NAME VARCHAR(20))",
            "CREATE TABLE U(ID INT, NOTE VARCHAR(20))",
            "INSERT INTO T VALUES (1, 'a'), (2, 'b'), (3, 'c')",
            "INSERT INTO U VALUES (2, 'x'), (3, 'y')",
            "SELECT T.ID, U.NOTE FROM T LEFT JOIN U ON T.ID = U.ID ORDER BY T.ID",
            "SELECT T.ID, U.NOTE FROM T LEFT OUTER JOIN U ON T.ID = U.ID ORDER BY T.ID",
            "SELECT T.ID, U.NOTE FROM T RIGHT JOIN U ON T.ID = U.ID ORDER BY T.ID",
            "SELECT T.ID, U.NOTE FROM T RIGHT OUTER JOIN U ON T.ID = U.ID ORDER BY T.ID",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        AssertSame(java, port);
    }

    /// <summary>
    /// The aliases of the joined tables, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheAliasesAreTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT, NAME VARCHAR(20))",
            "CREATE TABLE U(ID INT, NOTE VARCHAR(20))",
            "INSERT INTO T VALUES (1, 'a')",
            "INSERT INTO U VALUES (1, 'x')",
            "SELECT t.ID, u.NOTE FROM T AS t JOIN U AS u ON t.ID = u.ID",
            "SELECT t.ID, u.NOTE FROM T t JOIN U u ON t.ID = u.ID",
            "SELECT t.ID FROM T AS t LEFT JOIN U AS u ON t.ID = u.ID",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        AssertSame(java, port);
    }

    /// <summary>
    /// Three tables in a row, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheThreeTablesAreTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT, NAME VARCHAR(20))",
            "CREATE TABLE U(ID INT, TID INT, NOTE VARCHAR(20))",
            "CREATE TABLE V(ID INT, UID INT, TEXT VARCHAR(20))",
            "INSERT INTO T VALUES (1, 'a'), (2, 'b')",
            "INSERT INTO U VALUES (10, 1, 'x'), (20, 2, 'y')",
            "INSERT INTO V VALUES (100, 10, 'p')",
            "SELECT T.NAME, U.NOTE, V.TEXT FROM T "
                + "JOIN U ON T.ID = U.TID JOIN V ON U.ID = V.UID ORDER BY T.NAME",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        AssertSame(java, port);
    }

    /// <summary>
    /// The natural and the cross join, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheNaturalAndCrossJoinAreTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT, NAME VARCHAR(20))",
            "CREATE TABLE U(ID INT, NOTE VARCHAR(20))",
            "INSERT INTO T VALUES (1, 'a'), (2, 'b')",
            "INSERT INTO U VALUES (2, 'x')",
            "SELECT ID, NAME, NOTE FROM T NATURAL JOIN U ORDER BY ID",
            "SELECT T.ID, U.ID FROM T CROSS JOIN U ORDER BY T.ID, U.ID",
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
            new Database(ConnectionInfo.Memory("join_oracle")), "sa")
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:join_oracle");
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
