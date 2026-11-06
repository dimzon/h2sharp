/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Small statements that the earlier coverage sprints skipped, checked against the reference
 * running the same text: EXECUTE IMMEDIATE, constants, ALTER DOMAIN DROP CONSTRAINT, the
 * boolean tests (IS [NOT] TRUE/FALSE/UNKNOWN) over an indexed nullable column, field
 * references of row values and JSON members, and the BACKUP TO refusal on an in-memory
 * database.
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
public sealed class SmallStatementsOracleTests
{
    [JavaOracleFact]
    public void ExecuteImmediateRunsTheReferencedStatement()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT PRIMARY KEY)",
            "EXECUTE IMMEDIATE 'INSERT INTO T VALUES (1)'",
            "EXECUTE IMMEDIATE 'INSERT INTO T VALUES (' || 2 || ')'",
            "SELECT COUNT(*) FROM T",
            // The referenced text may be DDL too.
            "EXECUTE IMMEDIATE 'CREATE TABLE U(ID INT)'",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'U'",
            // A query refuses: the statement is an update, not a query.
            "EXECUTE IMMEDIATE 'SELECT 1'",
            // So does unparsable text, and a null statement.
            "EXECUTE IMMEDIATE 'THIS IS NOT SQL'",
            "EXECUTE IMMEDIATE NULL",
        ];
        RunBoth(statements);
    }

    [JavaOracleFact]
    public void ConstantsAnswerLikeTheReference()
    {
        string[] statements =
        [
            "CREATE CONSTANT C1 VALUE 42",
            "SELECT C1 + 1",
            "CREATE TABLE T(ID INT)",
            "INSERT INTO T VALUES (C1)",
            "SELECT COUNT(*) FROM T",
            // A duplicate refuses; IF NOT EXISTS keeps the first one.
            "CREATE CONSTANT C1 VALUE 1",
            "CREATE CONSTANT IF NOT EXISTS C1 VALUE 1",
            "SELECT C1",
            "DROP CONSTANT C1",
            "DROP CONSTANT C1",
            "DROP CONSTANT IF EXISTS C1",
            "DROP CONSTANT IF EXISTS C2",
            "CREATE CONSTANT C2 VALUE 'x'",
            "SELECT C2",
            "DROP TABLE T",
        ];
        RunBoth(statements);
    }

    [JavaOracleFact]
    public void DomainConstraintDropAnswerTheReference()
    {
        string[] statements =
        [
            "CREATE DOMAIN D1 AS INT",
            "ALTER DOMAIN D1 ADD CONSTRAINT DC1 CHECK (VALUE > 0)",
            "ALTER DOMAIN D1 DROP CONSTRAINT DC1",
            // Already dropped, then the silent form.
            "ALTER DOMAIN D1 DROP CONSTRAINT DC1",
            "ALTER DOMAIN D1 DROP CONSTRAINT IF EXISTS DC1",
            "ALTER DOMAIN D1 DROP CONSTRAINT DC2",
            "ALTER DOMAIN D1 DROP CONSTRAINT IF EXISTS DC2",
            // A table constraint of the same name is not the domain's own.
            "CREATE TABLE T(C INT CONSTRAINT CK CHECK (C > 0))",
            "ALTER DOMAIN D1 DROP CONSTRAINT CK",
            "ALTER DOMAIN D1 DROP CONSTRAINT IF EXISTS CK",
            "DROP TABLE T",
            "DROP DOMAIN D1",
        ];
        RunBoth(statements);
    }

    [JavaOracleFact]
    public void BooleanTestsAnswerTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT PRIMARY KEY, B BOOLEAN) AS VALUES (1, TRUE), (2, FALSE), (3, NULL)",
            "SELECT ID FROM T WHERE B IS TRUE ORDER BY ID",
            "SELECT ID FROM T WHERE B IS FALSE ORDER BY ID",
            "SELECT ID FROM T WHERE B IS UNKNOWN ORDER BY ID",
            "SELECT ID FROM T WHERE B IS NOT TRUE ORDER BY ID",
            "SELECT ID FROM T WHERE B IS NOT FALSE ORDER BY ID",
            "SELECT ID FROM T WHERE B IS NOT UNKNOWN ORDER BY ID",
            // The when-operand form of a simple case.
            "SELECT CASE B WHEN TRUE THEN 1 WHEN FALSE THEN 2 ELSE 3 END FROM T ORDER BY ID",
            "SELECT CASE WHEN B IS TRUE THEN 1 ELSE 2 END FROM T ORDER BY ID",
            // The optimizer may push the negation into the test itself.
            "SELECT ID FROM T WHERE NOT (B IS TRUE) ORDER BY ID",
            // With an index the test becomes an index condition.
            "CREATE INDEX T_B ON T(B)",
            "SELECT ID FROM T WHERE B IS TRUE ORDER BY ID",
            "SELECT ID FROM T WHERE B IS NOT UNKNOWN ORDER BY ID",
            "SELECT ID FROM T WHERE B IS UNKNOWN ORDER BY ID",
            "DROP TABLE T",
        ];
        RunBoth(statements);
    }

    [JavaOracleFact]
    public void FieldReferencesAnswerTheReference()
    {
        string[] statements =
        [
            // Unnamed row fields are C1, C2, ...; a named type keeps its names.
            "SELECT ROW(1, 'a').C1",
            "SELECT ROW(1, 'a').C2",
            "SELECT ROW(1, NULL).C2",
            // A null row value answers null.
            "SELECT CAST(NULL AS ROW(A INT)).A",
            // A JSON object answers its member, a missing member or a non-object answers null.
            "SELECT CAST('{\"a\": 1}' AS JSON).a",
            "SELECT CAST('{\"a\": 1}' AS JSON).b",
            "SELECT CAST('{}' AS JSON).a",
            "SELECT CAST('[1]' AS JSON).a",
            "SELECT CAST('null' AS JSON).a",
        ];
        RunBoth(statements);
    }

    [JavaOracleFact]
    public void BackupOnMemoryDatabaseRefusesOnBothSides()
    {
        RunBoth(["BACKUP TO 'x.zip'"]);
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
            new Database(ConnectionInfo.Memory("small_oracle")), "sa")
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:small_oracle");
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
