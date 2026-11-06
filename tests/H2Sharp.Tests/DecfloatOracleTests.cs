/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * DECFLOAT, checked against the reference running the same text (the matrix of the reference's
 * datatypes/decfloat.sql): literals and precision, the four arithmetic operations, MOD, casts
 * that round, the type questions, and the special values.
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
public sealed class DecfloatOracleTests
{
    [JavaOracleFact]
    public void DecfloatBasicsAnswerTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE TEST(D1 DECFLOAT, D2 DECFLOAT(5), D3 DECFLOAT(10), X NUMBER)",
            "INSERT INTO TEST VALUES(1, 1, 9999999999, 1.23)",
            "SELECT D1, D2, D3, X FROM TEST",
            "SELECT D2 + D3 A, D2 - D3 S, D2 * D3 M, D2 / D3 D FROM TEST",
            "CREATE TABLE RESULT AS SELECT D2 + D3 A, D2 - D3 S, D2 * D3 M, D2 / D3 D FROM TEST",
            "SELECT A, S, M, D FROM RESULT",
            "DROP TABLE TEST, RESULT",
            "EXPLAIN VALUES (CAST(-9223372036854775808 AS DECFLOAT(19)), CAST(9223372036854775807 AS DECFLOAT(19)), 1.0, -9223372036854775809, 9223372036854775808)",
            "CREATE TABLE T(C DECFLOAT(0))",
            "SELECT CAST(11 AS DECFLOAT(1))",
            "SELECT 1E1 IS OF(DECFLOAT)",
            "SELECT (CAST(1 AS REAL) + CAST(1 AS SMALLINT)) IS OF(REAL)",
            "SELECT (CAST(1 AS REAL) + CAST(1 AS BIGINT)) IS OF(DECFLOAT)",
            "SELECT (CAST(1 AS REAL) + CAST(1 AS NUMERIC)) IS OF(DECFLOAT)",
            "SELECT MOD(CAST(5 AS DECFLOAT), CAST(2 AS DECFLOAT))",
            "EXPLAIN SELECT 1.1E0, 1E1",
        ];
        RunBoth(statements);
    }

    [JavaOracleFact]
    public void DecfloatSpecialValuesAnswerTheReference()
    {
        string[] statements =
        [
            "CREATE MEMORY TABLE TEST(D DECFLOAT(8)) AS VALUES '-Infinity', '-1', '0', '1', '1.5', 'Infinity', 'NaN'",
            "SELECT D, -D, SIGN(D) FROM TEST ORDER BY D",
            "SELECT A.D, B.D, A.D + B.D, A.D - B.D, A.D * B.D FROM TEST A JOIN TEST B ORDER BY A.D, B.D",
            "SELECT A.D, B.D, A.D / B.D, MOD(A.D, B.D) FROM TEST A JOIN TEST B WHERE B.D <> 0 ORDER BY A.D, B.D",
            "DROP TABLE TEST",
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
            new Database(ConnectionInfo.Memory("decfloat_oracle")), "sa")
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
                if (sql.StartsWith("SELECT", StringComparison.Ordinal)
                    || sql.StartsWith("TABLE", StringComparison.Ordinal)
                    || sql.StartsWith("EXPLAIN", StringComparison.Ordinal)
                    || sql.StartsWith("VALUES", StringComparison.Ordinal))
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
                                // EXPLAIN plans are multi-line: one line per row on both sides.
                                : (value.GetString() ?? "-").Replace("\n", "\\n"));
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:decfloat_oracle", "sa", "");
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
                      line.append(rs.wasNull() ? "-" : s.replace("\n", "\\n"));
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
