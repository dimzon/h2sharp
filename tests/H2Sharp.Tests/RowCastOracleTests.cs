/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The ROW-to-ROW cast, checked against the reference running the same text. The reference's
 * convertToRow walks the fields positionally, but once a field has changed the remaining ones
 * are converted against the FIRST CHANGED FIELD'S type (the iterator stops advancing) — a
 * quirk that makes CAST(ROW(1,'x') AS ROW(BIGINT, VARCHAR)) refuse 'x' as a BIGINT. The port
 * reproduces the quirk 1:1; these tests pin it against the reference.
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
public sealed class RowCastOracleTests
{
    [JavaOracleFact]
    public void RowCastsAnswerTheReference()
    {
        string[] statements =
        [
            // No field changes on the cast: the row passes through as it is.
            "SELECT CAST(ROW(1, 'x') AS ROW(A INT, B VARCHAR(10)))",
            // The first field changes: the rest are converted against BIGINT, so 'x' refuses.
            "SELECT CAST(ROW(1, 'x') AS ROW(A BIGINT, B VARCHAR(10)))",
            "SELECT CAST(ROW(1, 'x') AS ROW(A BIGINT, B INT))",
            // Degree mismatch refuses.
            "SELECT CAST(ROW(1, 'x') AS ROW(A INT))",
            // The changed first field is the value the row keeps.
            "SELECT CAST(ROW(1.5, 'x') AS ROW(A BIGINT, B VARCHAR(10)))",
            "DROP TABLE T",
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
            new Database(ConnectionInfo.Memory("row_cast_oracle")), "sa")
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
                    while (result.Next())
                    {
                        Value?[] row = result.CurrentRow();
                        Value? value = row[0];
                        lines.Add("=" + (value is null or ValueNull ? "-" : value.GetString() ?? "-"));
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:row_cast_oracle", "sa", "");
            java.sql.Statement stat = conn.createStatement();
            for (int i = 0; i < inputs.length; i++) {
              try {
                boolean isQuery = stat.execute(inputs[i]);
                if (isQuery) {
                  java.sql.ResultSet rs = stat.getResultSet();
                  while (rs.next()) {
                    String s = rs.getString(1);
                    System.out.println("=" + (rs.wasNull() ? "-" : s));
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
