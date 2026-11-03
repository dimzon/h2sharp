/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * CAST across the type families: every value through every target type. What the cast answers, or
 * which error it refuses with, is compared with the reference running the same statement.
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
public sealed class CastMatrixOracleTests
{
    /// <summary>
    /// The values cast in the matrix, as SQL text.
    /// </summary>
    private static readonly string[] Values =
    [
        "NULL",
        "TRUE",
        "'12'",
        "' 12 '",
        "'12.9'",
        "'abc'",
        "'true'",
        "'false'",
        "12",
        "-12",
        "300",
        "70000",
        "5000000000",
        "12.9",
        "-12.5",
        "1E3",
        "DATE '2024-03-05'",
        "TIME '12:34:56.789'",
        "'12:34:56.789+02:00'",
        "TIMESTAMP '2024-03-05 12:34:56.789'",
        "TIMESTAMP WITH TIME ZONE '2024-03-05 12:34:56.789+02:00'",
        "X'01'",
        "X'0102'",
        "X'414243'",
        "'123e4567-e89b-12d3-a456-426614174000'",
        "INTERVAL '1-2' YEAR TO MONTH",
        "INTERVAL '1 02:03:04' DAY TO SECOND",
        "ARRAY[1, 2]",
        "ROW (1, 2)",
        "'{\"a\":1}'",
        // The source forms the first matrix did not write: each interval field on its
        // own, a decimal floating-point literal, geometry text, a nested cast to a
        // padded CHAR, and the numeric edges.
        "INTERVAL '5' YEAR",
        "INTERVAL '5' MONTH",
        "INTERVAL '5' DAY",
        "INTERVAL '5' HOUR",
        "INTERVAL '5' MINUTE",
        "INTERVAL '5' SECOND",
        "DECFLOAT '1.5'",
        "DECFLOAT '1E+300'",
        "'POINT(1 2)'",
        "CAST('ab' AS CHAR(3))",
        "9007199254740993",
        "-9223372036854775808",
        "1.7976931348623157E308",
        "2.2250738585072014E-308",
        "0.000000000000000000000000000001",
    ];

    /// <summary>
    /// The target types cast to.
    /// </summary>
    private static readonly string[] Types =
    [
        "TINYINT",
        "SMALLINT",
        "INTEGER",
        "BIGINT",
        "NUMERIC(5, 2)",
        "NUMERIC(30, 10)",
        "REAL",
        "DOUBLE",
        "DECFLOAT",
        "BOOLEAN",
        "CHAR(5)",
        "CHAR(2)",
        "VARCHAR(5)",
        "VARCHAR_IGNORECASE(5)",
        "BINARY(2)",
        "BINARY(4)",
        "VARBINARY(2)",
        "VARBINARY(4)",
        "BLOB",
        "CLOB",
        "DATE",
        "TIME(3)",
        "TIME(9)",
        "TIME WITH TIME ZONE(3)",
        "TIMESTAMP(3)",
        "TIMESTAMP(9)",
        "TIMESTAMP WITH TIME ZONE(3)",
        "UUID",
        "INTERVAL YEAR TO MONTH",
        "INTERVAL DAY TO SECOND",
        "INTEGER ARRAY",
        "JSON",
        // The remaining targets: the single interval fields, geometry (plain and with
        // its type and SRID), and the wide or narrow spellings a provider sends.
        "INTERVAL YEAR",
        "INTERVAL MONTH",
        "INTERVAL DAY",
        "INTERVAL HOUR",
        "INTERVAL MINUTE",
        "INTERVAL SECOND",
        "GEOMETRY",
        "GEOMETRY(POINT, 4326)",
        "CHAR(1)",
        "CHAR(300)",
        "BINARY(1)",
        "BINARY(8)",
        "NUMERIC(38, 0)",
        "DECFLOAT(10)",
        "VARCHAR(1)",
        "DOUBLE PRECISION",
    ];

    /// <summary>
    /// Every value through every type, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void CastsAnswerTheSameAsTheReference()
    {
        List<string> statements = ["SET TIME ZONE 'UTC'"];
        foreach (string value in Values)
        {
            foreach (string type in Types)
            {
                statements.Add("SELECT CAST(" + value + " AS " + type + ")");
            }
        }

        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.True(
            java.Count == port.Count,
            "lines: java=" + java.Count.ToString(CultureInfo.InvariantCulture)
            + " port=" + port.Count.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == port[i], $"line {i} ({statements[i]}):\n  java: {java[i]}\n  port: {port[i]}");
        }
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        // The session's own clock, as the reference's: TIME to TIMESTAMP takes today's date,
        // and a fixed test provider would put a different day on the port side.
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("cast_matrix_oracle")), "sa")
        {
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:cast_matrix_oracle");
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
                } else {
                  System.out.println("=" + stat.getUpdateCount());
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en", "-Duser.timezone=UTC"]);
}
