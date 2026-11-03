/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The index conditions a WHERE clause turns into: equality, ranges, IN lists, ANY over an
 * array, IN over a subquery, row-value IN lists against a composite index, the same on a
 * temporary table (where an IN combined with other conditions cannot use an index), and the
 * NULLs inside the lists. The answers are checked against the reference running the same
 * text; the plans are checked separately.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class IndexConditionOracleTests
{
    /// <summary>
    /// The statements, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheIndexConditionsAnswerTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(A INT PRIMARY KEY, B INT, C INT)",
            "CREATE TABLE U(ID INT PRIMARY KEY, V INT)",
            "CREATE TABLE U2(ID INT PRIMARY KEY, ARR INT ARRAY)",
            "CREATE TABLE T3(A INT PRIMARY KEY, B INT, C INT)",
            "CREATE TABLE TEST2(A INT, B INT, ID INT, NAME INT, PRIMARY KEY(ID, NAME))",
            "CREATE INDEX IDX_AB ON T(B, A)",
            "CREATE INDEX IDX_C ON T(C)",
            "CREATE UNIQUE INDEX C2 ON TEST2(B, A)",
            "CREATE INDEX IDX_AB3 ON T3(A, B)",
            "INSERT INTO T VALUES (1, 10, 100), (2, 20, 200), (3, 30, 300), (4, 40, NULL), (5, 50, 500)",
            "INSERT INTO U VALUES (1, 10), (2, 20), (3, 30)",
            "INSERT INTO U2 VALUES (1, ARRAY[10, 30])",
            "INSERT INTO T3 VALUES (1, 10, 100), (2, 20, 200), (3, 30, 300)",
            "INSERT INTO TEST2 VALUES (1, 10, 1, 1), (2, 20, 2, 2)",
            // Equality, ranges and their combinations on the indexed column.
            "SELECT A FROM T WHERE B = 20",
            "SELECT A FROM T WHERE B >= 20 AND B < 40 ORDER BY A",
            "SELECT A FROM T WHERE B > 10 AND B <= 30 ORDER BY A",
            "SELECT A FROM T WHERE B <> 20 ORDER BY A",
            // An IN list narrows the scan, with a NULL in it and without.
            "SELECT A FROM T WHERE B IN (10, 30) ORDER BY A",
            "SELECT A FROM T WHERE B IN (10, NULL) ORDER BY A",
            "SELECT A FROM T WHERE B NOT IN (10, 30) ORDER BY A",
            "SELECT A FROM T WHERE B NOT IN (10, NULL) ORDER BY A",
            // ANY over an array is the same set as an IN list.
            "SELECT A FROM T WHERE B = ANY(ARRAY[10, 30]) ORDER BY A",
            "SELECT A FROM T WHERE B = ANY(ARRAY[10, NULL]) ORDER BY A",
            // ANY over an array column of another table: the index condition holds the array
            // expression itself rather than a list of values.
            "SELECT A FROM T, U2 WHERE B = ANY(U2.ARR) ORDER BY A",
            "SELECT A FROM T, U2 WHERE B = ANY(U2.ARR) AND A >= 2 ORDER BY A",
            // IN over a subquery.
            "SELECT A FROM T WHERE B IN (SELECT V FROM U) ORDER BY A",
            "SELECT A FROM T WHERE B NOT IN (SELECT V FROM U) ORDER BY A",
            // The same over a subquery whose single column is a literal NULL, over a left value
            // that is NULL, over a row compared with a single-column subquery, and over a
            // column type the index cannot order with the subquery's.
            "SELECT A FROM T WHERE B IN (SELECT NULL FROM U) ORDER BY A",
            "SELECT A FROM T WHERE NULL IN (SELECT V FROM U) ORDER BY A",
            "SELECT A FROM T WHERE (B, A) IN (SELECT V FROM U) ORDER BY A",
            "SELECT A FROM T WHERE B IN (SELECT CAST(V AS DOUBLE) FROM U) ORDER BY A",
            "SELECT A FROM T WHERE B IN (SELECT V FROM U) AND A > 1 ORDER BY A",
            "SELECT MAX(A) FROM T WHERE B IN (SELECT V FROM U)",
            "SELECT A FROM T WHERE NOT (B <> ALL(SELECT V FROM U)) ORDER BY A",
            "SELECT A FROM T WHERE B = ANY(SELECT V FROM U) ORDER BY A",
            "SELECT A FROM T WHERE B <> ALL(SELECT V FROM U) ORDER BY A",
            // Row-value IN against the composite index, and with the columns written in the
            // other order (the condition is reordered to the index).
            "SELECT A FROM T WHERE (A, B) IN ((1, 10), (3, 30)) ORDER BY A",
            "SELECT A FROM T WHERE (B, A) IN ((10, 1), (30, 3)) ORDER BY A",
            "SELECT A FROM T WHERE (A, B) IN ((1, 10), (2, 99)) ORDER BY A",
            // The same against an index whose column order differs from the condition: the
            // condition is cloned with the index columns, reordering each row value.
            "SELECT A FROM T3 WHERE (B, A) IN ((10, 1), (30, 3)) ORDER BY A",
            "SELECT A FROM T3 WHERE (A, C) IN ((1, 100), (3, 300)) ORDER BY A",
            "SELECT A FROM T3 WHERE (B, A) IN ((B, A)) ORDER BY A",
            // An IN list that holds a column is not evaluatable; the index scan still answers
            // the same rows.
            "SELECT A FROM T WHERE B IN (A, 10) ORDER BY A",
            "SELECT A FROM T WHERE B IN (A, 10) AND A > 1 ORDER BY A",
            // A NULL-able column: IN with the NULL value and the missing column.
            "SELECT A FROM T WHERE C IN (100, 300) ORDER BY A",
            "SELECT A FROM T WHERE C = ANY(ARRAY[100, 300]) ORDER BY A",
            "SELECT A FROM T WHERE C IS NULL ORDER BY A",
            // An always-false condition folds the lookup away.
            "SELECT A FROM T WHERE 1 = 0 AND A = 1 ORDER BY A",
            "SELECT A FROM T WHERE A = 1 AND 1 = 0 ORDER BY A",
            "SELECT A FROM T WHERE FALSE ORDER BY A",
            "SELECT A FROM T WHERE 1 = 0 ORDER BY A",
            "SELECT A FROM T WHERE B IS NOT DISTINCT FROM 10 ORDER BY A",
            "SELECT A FROM T WHERE B IS NOT DISTINCT FROM NULL ORDER BY A",
            // A derived table: an IN combined with another condition cannot use its index,
            // so the mask of the IN condition is nothing and the scan answers the same rows.
            "SELECT * FROM (SELECT A, B FROM TEST2) WHERE A = 1 AND B IN (10, 20) ORDER BY A",
            "SELECT * FROM (SELECT A, B FROM TEST2) WHERE A = 1 AND (B, A) IN ((10, 1)) ORDER BY A",
            "SELECT * FROM (SELECT A, B FROM TEST2) WHERE B IN (10, 20) ORDER BY A",
            // The derived query's own index is fed the outer condition: equality, range and
            // both together, with the condition that cannot become one equality left outside.
            "SELECT * FROM (SELECT A, B FROM TEST2) WHERE A = 1 ORDER BY A",
            "SELECT * FROM (SELECT A, B FROM TEST2) WHERE A >= 1 ORDER BY A",
            "SELECT * FROM (SELECT A, B FROM TEST2) WHERE A <= 2 ORDER BY A",
            "SELECT * FROM (SELECT A, B FROM TEST2) WHERE A BETWEEN 1 AND 2 ORDER BY A",
            "SELECT * FROM (SELECT A, B FROM TEST2) WHERE A = 1 AND B = 10 ORDER BY A",
            "SELECT * FROM (SELECT A, B FROM TEST2) WHERE A = 1 OR B = 20 ORDER BY A",
            "SELECT * FROM (SELECT A, B FROM TEST2) WHERE A = 1 AND B > 5 ORDER BY A",
            "CREATE VIEW V3 AS SELECT A, B FROM TEST2",
            "SELECT * FROM V3 WHERE A = 1 AND B IN (10, 20) ORDER BY A",
            "SELECT * FROM V3 WHERE A BETWEEN 1 AND 2 ORDER BY A",
            // An expression the planner may fold away: the pushed parameter can disappear.
            "SELECT * FROM (SELECT NULL AS X) Y WHERE Y.X = 1",
            "SELECT * FROM (SELECT 1 AS X) Y WHERE Y.X = 1",
            // A view is not a regular table either: the same conditions answer the same rows.
            "CREATE VIEW V2 AS SELECT A, B FROM TEST2",
            "SELECT * FROM V2 WHERE A = 1 AND B IN (10, 20) ORDER BY A",
            "SELECT * FROM V2 WHERE A = 1 AND (B, A) IN ((10, 1)) ORDER BY A",
            // A temporary table: an IN list combined with another condition cannot use its
            // index, and the answer must be the same anyway.
            "CREATE LOCAL TEMPORARY TABLE TT(A INT PRIMARY KEY, B INT)",
            "CREATE INDEX IDX_TT_B ON TT(B)",
            "INSERT INTO TT VALUES (1, 10), (2, 20), (3, 30)",
            "SELECT A FROM TT WHERE A = 1 AND B IN (10, 20) ORDER BY A",
            "SELECT A FROM TT WHERE B IN (10, 20) ORDER BY A",
            // A correlated IN: the subquery cannot be prepared once, so each row runs it.
            "INSERT INTO U VALUES (5, NULL)",
            "SELECT A FROM T TT WHERE B IN (SELECT V FROM U WHERE U.ID = TT.A) ORDER BY A",
            "SELECT A FROM T TT WHERE B NOT IN (SELECT V FROM U WHERE U.ID = TT.A) ORDER BY A",
            "SELECT A FROM T TT WHERE B = ANY(SELECT V FROM U WHERE U.ID >= TT.A) ORDER BY A",
            "SELECT A FROM T TT WHERE B <> ALL(SELECT V FROM U WHERE U.ID <> TT.A) ORDER BY A",
            "SELECT A FROM T TT WHERE (A, B) IN (SELECT ID, V FROM U WHERE U.ID = TT.A) ORDER BY A",
            "SELECT A FROM T TT WHERE B NOT IN (SELECT V FROM U WHERE U.ID <> TT.A) ORDER BY A",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        int common = Math.Min(java.Count, port.Count);
        for (int i = 0; i < common; i++)
        {
            Assert.True(
                java[i] == port[i],
                $"line {i}:\n  java: {java[i]}\n  port: {port[i]}\n"
                + "java statement: " + StatementOf(java[i], statements) + "\n"
                + "port statement: " + StatementOf(port[i], statements));
        }

        Assert.True(
            java.Count == port.Count,
            "lines: java=" + java.Count.ToString(CultureInfo.InvariantCulture)
            + " port=" + port.Count.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The statement a result line came from, from the index the line carries.
    /// </summary>
    /// <param name="line">the result line</param>
    /// <param name="statements">the statements</param>
    /// <returns>the statement</returns>
    private static string StatementOf(string line, IReadOnlyList<string> statements)
    {
        int equals = line.IndexOf('=', StringComparison.Ordinal);
        if (equals < 0
            || !int.TryParse(
                line.AsSpan(0, equals), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            || index < 0 || index >= statements.Count)
        {
            return "(unknown)";
        }

        return statements[index];
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
            new Database(ConnectionInfo.Memory("index_condition_oracle")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        List<string> lines = [];
        for (int statementIndex = 0; statementIndex < statements.Count; statementIndex++)
        {
            string sql = statements[statementIndex];
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

                        lines.Add(statementIndex + "=" + string.Join(":", cells));
                    }
                }
                else
                {
                    lines.Add(statementIndex + "=" + container.ExecuteUpdate(false).GetUpdateCount());
                }
            }
            catch (Exception e)
            {
                lines.Add(e is H2SharpException h2
                    ? statementIndex + "=" + h2.H2ErrorCode.ToString(CultureInfo.InvariantCulture)
                    : statementIndex + "=" + e.GetType().Name + ":" + e.Message);
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:index_condition_oracle");
            java.sql.Statement stat = conn.createStatement();
            for (int i = 0; i < inputs.length; i++) {
              try {
                boolean isQuery = stat.execute(inputs[i]);
                if (isQuery) {
                  java.sql.ResultSet rs = stat.getResultSet();
                  int columns = rs.getMetaData().getColumnCount();
                  while (rs.next()) {
                    StringBuilder line = new StringBuilder().append(i).append("=");
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
                  System.out.println(i + "=" + stat.getUpdateCount());
                }
              } catch (java.sql.SQLException e) {
                System.out.println(i + "=" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}
