/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What a CAST answers, checked against the reference running the same text.
 *
 * A cast is two things at once: a reader of a type and a conversion of a value. The reader is
 * where the words are, so the same statement is asked of both sides: "NATIONAL CHARACTER(3)",
 * "CHARACTER VARYING(5)", "BINARY LARGE OBJECT", a length scaled by K, "INTERVAL YEAR(2) TO
 * MONTH", "FLOAT(24)", "DECIMAL(10, 2)". The conversion is where the numbers are, so the
 * values are compared cell by cell, and a refusal is compared by its error code: a cast that
 * refuses here must refuse there with the same code, because the code is what a client sees.
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
public sealed class CastOracleTests
{
    /// <summary>
    /// The statements both sides run. Every one of them is a statement over the dual table:
    /// there is no row to read, and the value is the answer.
    /// </summary>
    private static readonly string[] Statements =
    [
        "SELECT CAST(1 AS INTEGER)",
        "SELECT CAST(1 AS INT)",
        "SELECT CAST(1 AS BIGINT)",
        "SELECT CAST(1 AS SMALLINT)",
        "SELECT CAST(1 AS TINYINT)",
        "SELECT CAST(1.5 AS DOUBLE)",
        "SELECT CAST(1.5 AS DOUBLE PRECISION)",
        "SELECT CAST(1.5 AS FLOAT)",
        "SELECT CAST(1.5 AS FLOAT(24))",
        "SELECT CAST(1.5 AS REAL)",
        "SELECT CAST(1.5 AS DECIMAL(10, 2))",
        "SELECT CAST(1.5 AS DEC(10, 2))",
        "SELECT CAST(1.5 AS NUMERIC(10, 2))",
        "SELECT CAST(1.5 AS DECIMAL)",
        "SELECT CAST(1 AS BOOLEAN)",
        "SELECT CAST(123 AS VARCHAR)",
        "SELECT CAST(123 AS VARCHAR(10))",
        "SELECT CAST(123 AS CHAR(5))",
        "SELECT CAST('abc' AS VARCHAR(10))",
        "SELECT CAST('abc' AS CHARACTER VARYING(10))",
        "SELECT CAST('abc' AS NATIONAL CHARACTER(3))",
        "SELECT CAST('abc' AS VARCHAR_IGNORECASE)",
        "SELECT CAST(1.25 AS DECFLOAT)",
        "SELECT CAST('123' AS INTEGER)",
        "SELECT CAST('1.5' AS DOUBLE)",
        "SELECT CAST(1.9 AS INTEGER)",
        "SELECT CAST(-1.9 AS INTEGER)",
        "SELECT CAST(CAST('7' AS INTEGER) AS VARCHAR)",
        "SELECT CAST(NULL AS INTEGER)",
        "SELECT CAST(NULL AS INTEGER) IS NULL",
        "SELECT CAST(1 AS INTEGER) = 1",
        "SELECT CAST('1' AS INTEGER) = 1",
        "SELECT CAST(X'41' AS VARBINARY(5)) = X'41'",
        "SELECT CAST('a' AS INTEGER)",
        "SELECT CAST(1 AS NOSUCHTYPE)",
        "SELECT CAST(1 AS VARCHAR(0))",
        "SELECT CAST(1 AS DECIMAL(0, 0))",
        "SELECT CAST(1 AS DECIMAL(5, 6))",
        "SELECT 1 WHERE CAST('1' AS INTEGER) = 1",
        "SELECT CASE WHEN CAST('1' AS INTEGER) = 1 THEN 'y' ELSE 'n' END",
        "SELECT LENGTH(CAST(123 AS VARCHAR))",
        "SELECT CAST(1 AS \"VARCHAR\")",
        "SELECT CAST(1 AS A.B)",
        "SELECT CAST('ab' AS CHARACTER LARGE OBJECT) IS NOT NULL",
        "SELECT CAST(X'41' AS BINARY LARGE OBJECT) IS NOT NULL",
        "SELECT CAST(X'41' AS BINARY VARYING)",
        "SELECT CAST(1 AS FLOAT(53))",
        "SELECT CAST(1.5 AS DECFLOAT(10))",
        "SELECT CAST(1 AS TIMESTAMP(3))",
        "SELECT CAST(1 AS TIME(3))",
        "SELECT CAST(1 AS TIME WITH TIME ZONE)",
        "SELECT CAST(1 AS TIMESTAMP WITH TIME ZONE)",
        "SELECT CAST('5' AS INTERVAL YEAR)",
        "SELECT CAST('POINT(1 2)' AS GEOMETRY) IS NOT NULL",
        "SELECT CAST('2020-01-02' AS DATE FORMAT 'yyyy-MM-dd')",
        "SELECT CAST('2020-01-02 03:04:05' AS TIMESTAMP FORMAT 'yyyy-MM-dd HH:mm:ss')",
        "SELECT CAST(CAST('2020-01-02 03:04:05' AS TIMESTAMP) AS VARCHAR FORMAT 'yyyy-MM-dd HH:mm:ss')",
        "SELECT CAST('2020-01-02' AS DATE FORMAT 'dd.MM.yyyy')",
        "SELECT CAST(1 AS INTEGER FORMAT 'x')",
        "SELECT CAST('2020-01-02' AS DATE FORMAT 'yyyy-QQQQ')",
    ];

    /// <summary>
    /// The same statements, run here and there.
    /// </summary>
    [JavaOracleFact]
    public void CastsAnswerTheSameAsTheReference()
    {
        List<string> port = RunPort(Statements);
        List<string> java = RunJava(Statements);
        Assert.Equal(java.Count, port.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == port[i],
                $"{Statements[i]}\n  java:   {java[i]}\n  csharp: {port[i]}");
        }
    }

    /// <summary>
    /// Runs the statements here and writes what each of them answered: the value of the row, or
    /// the error code of the refusal.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>one line per statement</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("cast_oracle")), "sa")
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
            catch (H2SharpException e)
            {
                lines.Add("=" + e.H2ErrorCode.ToString(CultureInfo.InvariantCulture));
            }
        }

        return lines;
    }

    /// <summary>
    /// Runs the statements on the reference and writes what each of them answered: the value of
    /// the row, or the error code of the refusal.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>one line per statement</returns>
    private static List<string> RunJava(IReadOnlyList<string> statements)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:cast_oracle");
            java.sql.Statement stat = conn.createStatement();
            for (int i = 0; i < inputs.length; i++) {
              try {
                java.sql.ResultSet rs = stat.executeQuery(inputs[i]);
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
              } catch (java.sql.SQLException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}
