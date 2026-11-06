/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * More small statements against the reference running the same text: the IS [NOT] OF type
 * predicate (the reference's own predicates/type.sql matrix), the TRIM family, FOR UPDATE
 * clauses, the simple general value specifications, and ALTER TYPE ADD VALUE.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp;
using H2Sharp.Data;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class TypeAndTrimOracleTests
{
    [JavaOracleFact]
    public void TypePredicateAnswerTheReference()
    {
        string[] statements =
        [
            "SELECT 1 IS OF (INT)",
            "SELECT 1 IS NOT OF (INT)",
            "SELECT NULL IS OF (INT)",
            "SELECT NULL IS NOT OF (INT)",
            "SELECT 1 IS OF (INT, BIGINT)",
            "SELECT TRUE IS OF (VARCHAR, TIME)",
            "SELECT TRUE IS NOT OF (VARCHAR, TIME)",
            "CREATE TABLE TEST(A INT NOT NULL, B INT)",
            "EXPLAIN SELECT 'Test' IS OF (VARCHAR), 10 IS OF (VARCHAR), NULL IS OF (VARCHAR)",
            "EXPLAIN SELECT A IS OF (INT), A IS NOT OF (INT) FROM TEST",
            "EXPLAIN SELECT A IS NOT OF(INT) OR B IS OF (INT) FROM TEST",
            "DROP TABLE TEST",
        ];
        RunBoth(statements);
    }

    [JavaOracleFact]
    public void TrimFamilyAnswerTheReference()
    {
        string[] statements =
        [
            "SELECT TRIM('  x  ')",
            "SELECT LTRIM('  x')",
            "SELECT RTRIM('x  ')",
            "SELECT BTRIM('  x  ')",
            "SELECT TRIM(BOTH 'x' FROM 'xxaxx')",
            "SELECT TRIM(LEADING 'x' FROM 'xxaxx')",
            "SELECT TRIM(TRAILING 'x' FROM 'xxaxx')",
            // The character set form: every leading/trailing char in the set goes.
            "SELECT TRIM(TRAILING 'xy' FROM 'xxyayx')",
            "SELECT TRIM(LEADING 'xy' FROM 'xyxaxy')",
            "SELECT TRIM(LEADING FROM '  x')",
            "SELECT TRIM('')",
            "SELECT TRIM(NULL)",
        ];
        RunBoth(statements);
    }

    [JavaOracleFact]
    public void ForUpdateClausesAnswerTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE F(ID INT PRIMARY KEY, V INT)",
            "INSERT INTO F VALUES (1, 10), (2, 20)",
            "SELECT ID FROM F ORDER BY ID FOR UPDATE",
            "SELECT ID FROM F ORDER BY ID FOR UPDATE NOWAIT",
            "SELECT ID FROM F ORDER BY ID FOR UPDATE SKIP LOCKED",
            "SELECT ID FROM F ORDER BY ID FOR UPDATE WAIT 1000",
            // The SQL of the clause survives the plan.
            "EXPLAIN SELECT ID FROM F FOR UPDATE SKIP LOCKED",
            // A set operation locks every side; a VALUES query refuses.
            "SELECT ID FROM F UNION ALL SELECT ID FROM F ORDER BY ID FOR UPDATE",
            "VALUES (1) FOR UPDATE",
            "DROP TABLE F",
        ];
        RunBoth(statements);
    }

    [JavaOracleFact]
    public void SimpleValueSpecificationsAnswerTheReference()
    {
        // CURRENT_USER, SESSION_USER and SYSTEM_USER are not compared: the port answers the
        // fixed D-004 principal ("sa"), the reference the connection's user (uppercased).
        string[] statements =
        [
            "SELECT CURRENT_SCHEMA",
            "SELECT CURRENT_ROLE",
            "SELECT CURRENT_PATH",
            // The search path is quoted, comma-separated, in its own order.
            "SET SCHEMA_SEARCH_PATH X, PUBLIC",
            "SELECT CURRENT_PATH",
            "SET SCHEMA_SEARCH_PATH",
            "SELECT CURRENT_PATH",
        ];
        RunBoth(statements);
    }

    [Fact]
    public void UserSpecificationsAnswerTheD004Principal()
    {
        // D-004: the session has one fixed principal, "sa", for all three user specifications.
        Assert.Equal("sa", SelectScalar("SELECT CURRENT_USER"));
        Assert.Equal("sa", SelectScalar("SELECT SESSION_USER"));
        Assert.Equal("sa", SelectScalar("SELECT SYSTEM_USER"));
    }

    [JavaOracleFact]
    public void AlterTypeAddValueAnswerTheReference()
    {
        string[] statements =
        [
            "CREATE DOMAIN E1 AS ENUM('a','b')",
            "CREATE TABLE ET(V E1)",
            "INSERT INTO ET VALUES ('a')",
            // The new value becomes valid and sorts by its ordinal.
            "ALTER TYPE E1 ADD VALUE 'c'",
            "INSERT INTO ET VALUES ('c')",
            "SELECT V FROM ET ORDER BY V",
            // The not-a-domain and missing-domain refusals, then the syntax of anything else.
            "CREATE DOMAIN I1 AS INT",
            "ALTER TYPE I1 ADD VALUE 'x'",
            "ALTER TYPE E2 ADD VALUE 'x'",
            "DROP TABLE ET",
            "DROP DOMAIN E1",
            "DROP DOMAIN I1",
        ];
        RunBoth(statements);
    }

    /// <summary>SELECT over an in-memory connection, one scalar per statement.</summary>
    private static object? SelectScalar(string sql)
    {
        using H2Connection connection = new("Data Source=scalar_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
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
    /// Runs the statements here and writes what the question answered: the rows (SELECT and
    /// EXPLAIN), or the code of the error.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("type_trim_oracle")), "sa")
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:type_trim_oracle", "sa", "");
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
