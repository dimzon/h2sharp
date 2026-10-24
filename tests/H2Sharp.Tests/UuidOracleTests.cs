/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The UUID literal and the conversions into UUID, checked against the reference running the
 * same text. A UUID literal is read by counting hexadecimal digits — dashes and case and
 * blanks are decoration — so the forms differ only in how many digits they carry, and the
 * refusal of a malformed one is compared by its error code, which is what a client sees.
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
public sealed class UuidOracleTests
{
    /// <summary>
    /// The statements both sides run. The first five are one hexadecimal digit short of a UUID
    /// (thirty-one, not thirty-two) and have to be refused by both sides with the same code;
    /// the rest are well-formed in every spelling the reference reads.
    /// </summary>
    private static readonly string[] Statements =
    [
        "SELECT UUID 'd3b07384-d9a4-4f5b-8f1a-2a1e7a1e7a1'",
        "SELECT UUID 'D3B07384-D9A4-4F5B-8F1A-2A1E7A1E7A1'",
        "SELECT UUID 'd3b07384d9a44f5b8f1a2a1e7a1e7a1'",
        "SELECT CAST('d3b07384-d9a4-4f5b-8f1a-2a1e7a1e7a1' AS UUID)",
        "SELECT CAST(X'd3b07384d9a44f5b8f1a2a1e7a1e7a1' AS UUID)",
        "SELECT UUID 'd3b07384-d9a4-4f5b-8f1a-2a1e7a1e7a11'",
        "SELECT UUID 'D3B07384-D9A4-4F5B-8F1A-2A1E7A1E7A11'",
        "SELECT UUID 'd3b07384d9a44f5b8f1a2a1e7a1e7a11'",
        "SELECT UUID ' D3B07384-D9A4-4F5B-8F1A-2A1E7A1E7A11 '",
        "SELECT CAST('d3b07384-d9a4-4f5b-8f1a-2a1e7a1e7a11' AS UUID)",
        "SELECT CAST('D3B07384D9A44F5B8F1A2A1E7A1E7A11' AS UUID)",
        "SELECT CAST(X'd3b07384d9a44f5b8f1a2a1e7a1e7a11' AS UUID)",
        "SELECT UUID X'd3b07384d9a44f5b8f1a2a1e7a1e7a11'",
        "SELECT CAST(UUID 'd3b07384-d9a4-4f5b-8f1a-2a1e7a1e7a11' AS VARCHAR)",
    ];

    /// <summary>
    /// The statement the reference reads and the port does not: the PostgreSQL spelling of a
    /// cast, which the reference accepts for every type and the port has not ported.
    /// </summary>
    private static readonly string[] BlockedStatements =
    [
        "SELECT 'd3b07384-d9a4-4f5b-8f1a-2a1e7a1e7a11'::UUID",
    ];

    /// <summary>
    /// The same statements, run here and there.
    /// </summary>
    [JavaOracleFact]
    public void UuidLiteralsAnswerTheSameAsTheReference()
    {
        AssertStatementsMatch(Statements);
    }

    /// <summary>
    /// The PostgreSQL cast spelling, which the reference reads and the port refuses with a
    /// syntax error. Blocked by the port until the <c>::</c> cast is ported; the red run by
    /// this category is the list of such defects, not a broken test.
    /// </summary>
    [JavaOracleFact]
    [Trait("Category", "BlockedByPort")]
    public void PostgreSqlCastSyntaxIsNotPortedYet()
    {
        AssertStatementsMatch(BlockedStatements);
    }

    private static void AssertStatementsMatch(IReadOnlyList<string> statements)
    {
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.Equal(java.Count, port.Count);
        List<string> mismatches = [];
        for (int i = 0; i < java.Count; i++)
        {
            if (java[i] != port[i])
            {
                mismatches.Add($"{statements[i]}\n  java:   {java[i]}\n  csharp: {port[i]}");
            }
        }

        Assert.True(
            mismatches.Count == 0,
            string.Join(Environment.NewLine + Environment.NewLine, mismatches));
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
            new Database(new ConnectionInfo("jdbc:h2:mem:uuid_oracle")), "sa")
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:uuid_oracle");
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
