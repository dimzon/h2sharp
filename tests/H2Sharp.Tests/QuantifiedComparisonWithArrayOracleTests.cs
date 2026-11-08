/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The reference's predicates/quantified-comparison-with-array.sql, statement by statement:
 * quantified comparison of a scalar with an array, both with and without nulls.
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
public sealed class QuantifiedComparisonWithArrayOracleTests
{
    [JavaOracleFact]
    public void QuantifiedComparisonWithArrayAnswersTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T1 AS SELECT * FROM (VALUES 0, 1, 2, 3, NULL) T(V);",
            "CREATE TABLE T2 AS SELECT * FROM (VALUES NULL, ARRAY[], ARRAY[NULL], ARRAY[1], ARRAY[1, NULL], ARRAY[1, 2], ARRAY[1, 2, NULL]) T(A);",
            "SELECT V, A, V = ANY(A), NOT(V <> ALL(A)), V = ALL(A), NOT(V <> ANY(A)), V <> ANY(A), NOT(V = ALL(A)), V <> ALL(A), NOT(V = ANY(A)) FROM T1, T2;",
            "SELECT V, A, V IS NOT DISTINCT FROM ANY(A), NOT(V IS DISTINCT FROM ALL(A)), V IS NOT DISTINCT FROM ALL(A), NOT(V IS DISTINCT FROM ANY(A)), V IS DISTINCT FROM ANY(A), NOT(V IS NOT DISTINCT FROM ALL(A)), V IS DISTINCT FROM ALL(A), NOT(V IS NOT DISTINCT FROM ANY(A)) FROM T1, T2;",
            "SELECT V, A, V >= ANY(A), NOT(V < ALL(A)), V >= ALL(A), NOT(V < ANY(A)), V < ANY(A), NOT(V >= ALL(A)), V < ALL(A), NOT(V >= ANY(A)) FROM T1, T2;",
            "SELECT V, A, V <= ANY(A), NOT(V > ALL(A)), V <= ALL(A), NOT(V > ANY(A)), V > ANY(A), NOT(V <= ALL(A)), V > ALL(A), NOT(V <= ANY(A)) FROM T1, T2;",
            "SELECT * FROM T1 WHERE V = ANY(ARRAY[1, 2]);",
            "SELECT V, A FROM T1 JOIN T2 ON T1.V = ANY(T2.A);",
            "CREATE INDEX T1_V_IDX ON T1(V);",
            "SELECT * FROM T1 WHERE V = ANY(ARRAY[1, 3]);",
            "SELECT * FROM T1 WHERE V = ANY(ARRAY[]);",
            "SELECT V, A FROM T1 JOIN T2 ON T1.V = ANY(T2.A);",
            "SELECT * FROM T1 WHERE T1.V = ANY(CAST((SELECT ARRAY_AGG(S.V) FROM T1 S) AS INTEGER ARRAY));",
            "SELECT V, A, CASE V WHEN = ANY(A) THEN 1 WHEN > ANY(A) THEN 2 WHEN < ANY(A) THEN 3 ELSE 4 END C FROM T1 JOIN T2;",
            "CREATE TABLE T3(C0 INT) AS VALUES 1, 2, 3;",
            "SELECT COUNT(*) FROM T3 WHERE C0 = ANY(ARRAY[1.0, 0.5, 2.5]);",
            "DROP TABLE T3;"
        ];
        RunBoth(statements);
    }

    private static void RunBoth(IReadOnlyList<string> statements)
    {
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        port.RemoveAll(static line => line.StartsWith("=CREATE USER", StringComparison.Ordinal));
        java.RemoveAll(static line => line.StartsWith("=CREATE USER", StringComparison.Ordinal));
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java count=" + java.Count + " port count=" + port.Count);
    }

    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("qc_array_oracle")), "sa")
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
                if (sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                    || sql.StartsWith("TABLE", StringComparison.OrdinalIgnoreCase)
                    || sql.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)
                    || sql.StartsWith("VALUES", StringComparison.OrdinalIgnoreCase))
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

                        string rowText = string.Join(":", cells);
                        string[] parts = rowText.Split('\n');
                        for (int p = 0; p < parts.Length; p++)
                        {
                            lines.Add(p == 0 ? "=" + parts[p].TrimEnd('\r') : parts[p].TrimEnd('\r'));
                        }
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

    private static List<string> RunJava(IReadOnlyList<string> statements)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:qc_array_oracle", "sa", "");
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
            extraJavaArgs: ["-Duser.language=en"]);
}

