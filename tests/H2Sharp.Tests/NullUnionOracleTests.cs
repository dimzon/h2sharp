/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What a NULL contributes to a set operation, checked against the reference running the same
 * text: the rows, and the type the union column comes out as — a NULL arm has no type of its
 * own, so the union's type is decided by the other arms, and that decision is what the reader
 * of the result sees.
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
public sealed class NullUnionOracleTests
{
    private static readonly string[] Statements =
    [
        "SELECT NULL AS A UNION ALL SELECT 1",
        "SELECT NULL AS A UNION ALL SELECT 1 AS A",
        "SELECT NULL UNION ALL SELECT 1",
        "SELECT NULL AS A UNION SELECT 1",
        "SELECT NULL AS A UNION ALL SELECT NULL",
        "SELECT NULL AS A UNION ALL SELECT 1 UNION ALL SELECT 'x'",
        "SELECT 1 UNION ALL SELECT NULL AS A",
    ];

    [JavaOracleFact]
    public void NullInSetOperationsAnswersTheSameAsTheReference()
    {
        List<string> port = RunPort(Statements);
        List<string> java = RunJava(Statements);
        Assert.Equal(java.Count, port.Count);
        List<string> mismatches = [];
        for (int i = 0; i < java.Count; i++)
        {
            if (java[i] != port[i])
            {
                mismatches.Add($"{Statements[i]}\n  java:   {java[i]}\n  csharp: {port[i]}");
            }
        }

        Assert.True(
            mismatches.Count == 0,
            string.Join(Environment.NewLine + Environment.NewLine, mismatches));
    }

    /// <summary>
    /// Runs the statements here and writes what each of them answered: the column's name and
    /// type, then the rows.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>one line per column description and one per row</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("null_union_oracle")), "sa")
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
                lines.Add("=column:" + result.GetColumnName(0) + ":" + Value.GetTypeName(
                    result.GetColumnType(0).GetValueType()));
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
    /// Runs the statements on the reference and writes what each of them answered: the column's
    /// name and type, then the rows.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>one line per column description and one per row</returns>
    private static List<string> RunJava(IReadOnlyList<string> statements)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:null_union_oracle");
            java.sql.Statement stat = conn.createStatement();
            for (int i = 0; i < inputs.length; i++) {
              try {
                java.sql.ResultSet rs = stat.executeQuery(inputs[i]);
                java.sql.ResultSetMetaData md = rs.getMetaData();
                System.out.println("=column:" + md.getColumnName(1) + ":" + md.getColumnTypeName(1));
                while (rs.next()) {
                  String s = rs.getString(1);
                  System.out.println("=" + (rs.wasNull() ? "-" : s));
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}
