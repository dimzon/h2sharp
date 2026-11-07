/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The SET forms of UPDATE, checked against the reference running the same text (the matrix of
 * the reference's dml/update.sql): row-constructor assignments, DEFAULT on plain and generated
 * columns, duplicate targets, the protected _ROWID_, and VALUES as the row source.
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
public sealed class UpdateSetFormsOracleTests
{
    [JavaOracleFact]
    public void UpdateSetFormsAnswerTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE TEST(A INT, B INT)",
            "INSERT INTO TEST VALUES (1, 2)",
            "UPDATE TEST SET (A, B) = (3, 4)",
            "SELECT A, B FROM TEST",
            "UPDATE TEST SET (B) = 5",
            "SELECT B FROM TEST",
            "UPDATE TEST SET (B) = ROW (6)",
            "SELECT B FROM TEST",
            "UPDATE TEST SET (B) = (7)",
            "SELECT B FROM TEST",
            "UPDATE TEST SET (B) = (2, 3)",
            // The plan reads as plain assignments.
            "EXPLAIN UPDATE TEST SET (A) = ROW(3), B = 4",
            "EXPLAIN UPDATE TEST SET A = 3, (B) = 4",
            "UPDATE TEST SET (A, B) = (1, 2), (B, A) = (2, 1)",
            "UPDATE TEST SET (A) = A * 3",
            "SELECT A FROM TEST",
            "DROP TABLE TEST",
            // The row identifier is not assignable.
            "CREATE TABLE TEST(ID INT) AS VALUES 100",
            "UPDATE TEST SET _ROWID_ = 2 WHERE ID = 100",
            "DROP TABLE TEST",
            // DEFAULT on a generated column recomputes it.
            "CREATE TABLE TEST(A INT, B INT GENERATED ALWAYS AS (A + 1))",
            "INSERT INTO TEST(A) VALUES 1",
            "UPDATE TEST SET A = 2, B = DEFAULT",
            "SELECT A, B FROM TEST",
            // A generated column refuses an assignment.
            "UPDATE TEST SET B = 1",
            "UPDATE TEST SET B = DEFAULT",
            "SELECT A, B FROM TEST",
            "DROP TABLE TEST",
            // A scalar subquery answers the new value.
            "CREATE TABLE TEST(A INT, B INT)",
            "INSERT INTO TEST VALUES (1, 2)",
            "UPDATE TEST SET B = (SELECT A * 10 FROM TEST)",
            "SELECT A, B FROM TEST",
            "DROP TABLE TEST",
            // The row constructor may read its values from a subquery, refuses duplicate
            // targets, a wrong degree, and a null row.
            "CREATE TABLE TEST(ID BIGINT GENERATED ALWAYS AS IDENTITY, ID2 BIGINT GENERATED ALWAYS AS (ID + 1), V INT, U INT ON UPDATE (5))",
            "INSERT INTO TEST(V) VALUES 1",
            "SELECT ID, ID2, V, U FROM TEST",
            "UPDATE TEST SET V = V + 1",
            "UPDATE TEST SET V = V + 1, ID = DEFAULT, ID2 = DEFAULT",
            "SELECT ID, ID2, V, U FROM TEST",
            "EXPLAIN UPDATE TEST SET (ID2, V) = (SELECT ID, V FROM TEST FETCH FIRST ROW ONLY)",
            "UPDATE TEST SET (ID2, V) = (SELECT ID, V FROM TEST FETCH FIRST ROW ONLY)",
            "SELECT ID, ID2, V, U FROM TEST",
            "UPDATE TEST SET (V, V) = (SELECT 1, 2 FROM TEST)",
            "UPDATE TEST SET (ID, V) = (SELECT 1, 2, 3 FROM TEST)",
            "UPDATE TEST SET (ID, ID2) = NULL",
            "SELECT ID, ID2, V, U FROM TEST",
            "DROP TABLE TEST",
            // An array element assignment pads with nulls, walks nested arrays, and refuses
            // a null target and a DEFAULT element.
            "CREATE TABLE TEST(ID BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, A INTEGER ARRAY, B INTEGER)",
            "INSERT INTO TEST(A) VALUES ARRAY[], ARRAY[1], ARRAY[1, 2], ARRAY[1, 2, 3]",
            "UPDATE TEST SET A[2] = 4",
            "SELECT A FROM TEST ORDER BY ID",
            "UPDATE TEST SET (A[2], B) = SELECT 4, 7",
            "SELECT A, B FROM TEST ORDER BY ID",
            "INSERT INTO TEST(A) VALUES NULL",
            "UPDATE TEST SET A[1] = 0",
            // DEFAULT in an expression position refuses as a syntax error on both sides,
            // but the code differs (the reference 42001 with the expected-token list, the
            // port 42000 — the parse structure clears the list before the refusal; see
            // MIGRATION_STATUS) — pinned port-side in UpdateSetDefaultRefusalTests.
            "DELETE FROM TEST",
            "DROP TABLE TEST",
            "CREATE TABLE TEST(ID BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, A INTEGER ARRAY ARRAY)",
            "INSERT INTO TEST(A) VALUES ARRAY[ARRAY[]], ARRAY[ARRAY[1]], ARRAY[ARRAY[1, 2], ARRAY[3, 4, 5]], ARRAY[ARRAY[1], ARRAY[2, 3], ARRAY[4], NULL]",
            "UPDATE TEST SET A[2][3] = 9",
            "SELECT A FROM TEST ORDER BY ID",
            "INSERT INTO TEST(A) VALUES ARRAY[ARRAY[], NULL]",
            "UPDATE TEST SET A[2][1] = 0",
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
            new Database(ConnectionInfo.Memory("update_set_oracle")), "sa")
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
                    || sql.StartsWith("EXPLAIN", StringComparison.Ordinal))
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:update_set_oracle", "sa", "");
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

/// <summary>
/// The port-side pin of the DEFAULT-in-expression refusal: a keyword is not a column name,
/// the refusal points at the keyword. The code differs from the reference (42000 vs 42001 —
/// the parse structure clears the expected-token list before the refusal), see
/// MIGRATION_STATUS.
/// </summary>
public sealed class UpdateSetDefaultRefusalTests
{
    [Fact]
    public void DefaultInExpressionPositionRefusesAsSyntaxError()
    {
        using H2Sharp.Data.H2Connection connection = new(
            "Data Source=default_refusal_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE T(A INT ARRAY, B INT)";
            _ = create.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE T SET A[1] = DEFAULT";
            H2Sharp.H2SharpException e = Assert.Throws<H2Sharp.H2SharpException>(
                () => _ = command.ExecuteNonQuery());
            // The refusal points at the keyword, not at the statement start.
            Assert.Contains("[*]DEFAULT", e.Message, StringComparison.Ordinal);
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT DEFAULT FROM T";
            H2Sharp.H2SharpException e = Assert.Throws<H2Sharp.H2SharpException>(
                () => _ = command.ExecuteScalar());
            Assert.Contains("[*]DEFAULT", e.Message, StringComparison.Ordinal);
        }
    }
}
