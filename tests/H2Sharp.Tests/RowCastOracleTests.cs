/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The ROW-to-ROW cast. D-048 (approved deviation): the reference's convertToRow stops
 * advancing the field iterator after the first changed field, so its tail converts against
 * that field's type and CAST(ROW(1,'x') AS ROW(BIGINT, VARCHAR)) refuses 'x' as a BIGINT.
 * The port converts every field against its own type. The cases where both sides agree are
 * checked against the reference; the fixed behavior is pinned by port-side tests.
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
    /// <summary>
    /// The casts where the port and the reference agree: no field changes on the cast, and
    /// the degree mismatch refusal.
    /// </summary>
    [JavaOracleFact]
    public void AgreeingRowCastsAnswerTheReference()
    {
        string[] statements =
        [
            // No field changes on the cast: the row passes through as it is.
            "SELECT CAST(ROW(1, 'x') AS ROW(A INT, B VARCHAR(10)))",
            "SELECT CAST(ROW(2, 'y') AS ROW(A INT, B VARCHAR(10)))",
            // Degree mismatch refuses.
            "SELECT CAST(ROW(1, 'x') AS ROW(A INT))",
            "DROP TABLE T",
        ];
        RunBoth(statements);
    }

    /// <summary>
    /// D-048: every field converts against its own declared type, so a changed first field
    /// does not force the tail into it.
    /// </summary>
    [Fact]
    public void EachFieldConvertsAgainstItsOwnType()
    {
        Assert.Equal("ROW (1, x)", RowCast("SELECT CAST(ROW(1, 'x') AS ROW(A BIGINT, B VARCHAR(10)))"));

        // The tail still converts against its own type: a real conversion error stays one.
        H2SharpException e = Assert.Throws<H2SharpException>(
            () => RowCast("SELECT CAST(ROW(1, 'x') AS ROW(A BIGINT, B INT))"));
        Assert.Equal(H2Sharp.Api.ErrorCode.DataConversionError1, e.H2ErrorCode);

        // A changed field later in the row converts against its own type too.
        Assert.Equal("ROW (1, 2)", RowCast("SELECT CAST(ROW(1, '2') AS ROW(A INT, B INT))"));
    }

    /// <summary>
    /// Runs one row-valued SELECT on an in-memory database and answers the row's SQL, or
    /// rethrows.
    /// </summary>
    /// <param name="sql">the SELECT</param>
    /// <returns>the row's SQL text</returns>
    private static string RowCast(string sql)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("row_cast_fixed_" + Guid.NewGuid().ToString("N"))), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        try
        {
            CommandContainer container =
                Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
            ResultInterface result = container.Query(-1);
            Assert.True(result.Next());
            Value? value = result.CurrentRow()[0];
            return value?.GetString() ?? "-";
        }
        finally
        {
            session.Close();
        }
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
