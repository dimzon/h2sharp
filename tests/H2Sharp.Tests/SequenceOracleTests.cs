/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What a sequence answers, checked against the reference running the same text.
 *
 * A sequence is a counter: it hands out 1, 2, 3, and the options move it — a start value,
 * an increment, bounds, a cycle. The current value is the one it handed out last in this
 * session, and the catalogue says what the sequence is.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class SequenceOracleTests
{
    /// <summary>
    /// The values of a sequence, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheValuesAreTheSameAsTheReference()
    {
        List<string> port = RunPort(
        [
            "CREATE SEQUENCE SEQ_ID",
            "SELECT NEXT VALUE FOR SEQ_ID",
            "SELECT NEXT VALUE FOR SEQ_ID",
            "SELECT CURRENT VALUE FOR SEQ_ID",
        ]);
        List<string> java = RunJava(
        [
            "CREATE SEQUENCE SEQ_ID",
            "SELECT NEXT VALUE FOR SEQ_ID",
            "SELECT NEXT VALUE FOR SEQ_ID",
            "SELECT CURRENT VALUE FOR SEQ_ID",
        ]);
        AssertSame(java, port);
    }

    /// <summary>
    /// The options of a sequence, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheOptionsAreTheSameAsTheReference()
    {
        List<string> port = RunPort(
        [
            "CREATE SEQUENCE SEQ_A START WITH 50",
            "SELECT NEXT VALUE FOR SEQ_A",
            "CREATE SEQUENCE SEQ_B INCREMENT BY 10",
            "SELECT NEXT VALUE FOR SEQ_B",
            "SELECT NEXT VALUE FOR SEQ_B",
            "CREATE SEQUENCE SEQ_C MINVALUE 5 START WITH 5",
            "SELECT NEXT VALUE FOR SEQ_C",
            "CREATE SEQUENCE SEQ_D MAXVALUE 100 START WITH 99",
            "SELECT NEXT VALUE FOR SEQ_D",
            "CREATE SEQUENCE SEQ_E MAXVALUE 1 START WITH 1 CYCLE",
            "SELECT NEXT VALUE FOR SEQ_E",
            "SELECT NEXT VALUE FOR SEQ_E",
        ]);
        List<string> java = RunJava(
        [
            "CREATE SEQUENCE SEQ_A START WITH 50",
            "SELECT NEXT VALUE FOR SEQ_A",
            "CREATE SEQUENCE SEQ_B INCREMENT BY 10",
            "SELECT NEXT VALUE FOR SEQ_B",
            "SELECT NEXT VALUE FOR SEQ_B",
            "CREATE SEQUENCE SEQ_C MINVALUE 5 START WITH 5",
            "SELECT NEXT VALUE FOR SEQ_C",
            "CREATE SEQUENCE SEQ_D MAXVALUE 100 START WITH 99",
            "SELECT NEXT VALUE FOR SEQ_D",
            "CREATE SEQUENCE SEQ_E MAXVALUE 1 START WITH 1 CYCLE",
            "SELECT NEXT VALUE FOR SEQ_E",
            "SELECT NEXT VALUE FOR SEQ_E",
        ]);
        AssertSame(java, port);
    }

    /// <summary>
    /// The changes of a sequence, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheChangesAreTheSameAsTheReference()
    {
        List<string> port = RunPort(
        [
            "CREATE SEQUENCE SEQ_ID",
            "SELECT NEXT VALUE FOR SEQ_ID",
            "ALTER SEQUENCE SEQ_ID RESTART WITH 100",
            "SELECT NEXT VALUE FOR SEQ_ID",
            "ALTER SEQUENCE SEQ_ID INCREMENT BY 5",
            "SELECT NEXT VALUE FOR SEQ_ID",
            "DROP SEQUENCE SEQ_ID",
            "SELECT NEXT VALUE FOR SEQ_ID",
        ]);
        List<string> java = RunJava(
        [
            "CREATE SEQUENCE SEQ_ID",
            "SELECT NEXT VALUE FOR SEQ_ID",
            "ALTER SEQUENCE SEQ_ID RESTART WITH 100",
            "SELECT NEXT VALUE FOR SEQ_ID",
            "ALTER SEQUENCE SEQ_ID INCREMENT BY 5",
            "SELECT NEXT VALUE FOR SEQ_ID",
            "DROP SEQUENCE SEQ_ID",
            "SELECT NEXT VALUE FOR SEQ_ID",
        ]);
        AssertSame(java, port);
    }

    /// <summary>
    /// The rows of the catalogue about a sequence, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheCatalogueSaysTheSameAsTheReference()
    {
        string query = "SELECT SEQUENCE_NAME, DATA_TYPE, NUMERIC_PRECISION, NUMERIC_SCALE, START_VALUE, "
            + "MINIMUM_VALUE, MAXIMUM_VALUE, INCREMENT, CYCLE_OPTION, BASE_VALUE, CACHE "
            + "FROM INFORMATION_SCHEMA.SEQUENCES ORDER BY SEQUENCE_NAME";
        List<string> port = RunPort(
        [
            "CREATE SEQUENCE SEQ_A START WITH 50",
            "CREATE SEQUENCE SEQ_B INCREMENT BY 10",
            query,
        ]);
        List<string> java = RunJava(
        [
            "CREATE SEQUENCE SEQ_A START WITH 50",
            "CREATE SEQUENCE SEQ_B INCREMENT BY 10",
            query,
        ]);
        AssertSame(java, port);
    }

    /// <summary>
    /// The changes of the identity column, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheChangesOfTheIdentityAreTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT GENERATED BY DEFAULT AS IDENTITY, NAME VARCHAR(50))",
            "ALTER TABLE T ALTER COLUMN ID RESTART WITH 100",
            "INSERT INTO T(NAME) VALUES('a')",
            "ALTER TABLE T ALTER COLUMN ID SET INCREMENT BY 5",
            "INSERT INTO T(NAME) VALUES('b')",
            "ALTER TABLE T ALTER COLUMN ID SET GENERATED ALWAYS",
            "SELECT IDENTITY_GENERATION FROM INFORMATION_SCHEMA.COLUMNS "
                + "WHERE TABLE_NAME = 'T' AND COLUMN_NAME = 'ID'",
            "ALTER TABLE T ALTER COLUMN ID DROP IDENTITY",
            "SELECT IS_IDENTITY FROM INFORMATION_SCHEMA.COLUMNS "
                + "WHERE TABLE_NAME = 'T' AND COLUMN_NAME = 'ID'",
            "INSERT INTO T(ID, NAME) VALUES(7, 'c')",
            "SELECT ID, NAME FROM T ORDER BY ID",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        AssertSame(java, port);
    }

    /// <summary>
    /// The truncation of a table, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheTruncationIsTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE T(ID INT GENERATED BY DEFAULT AS IDENTITY, NAME VARCHAR(50))",
            "INSERT INTO T(NAME) VALUES('a')",
            "TRUNCATE TABLE T RESTART IDENTITY",
            "INSERT INTO T(NAME) VALUES('b')",
            "SELECT ID, NAME FROM T ORDER BY ID",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        AssertSame(java, port);
    }

    /// <summary>
    /// The compatibility spellings of the sequence value, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheCompatibilityFunctionsAreTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE SEQUENCE SEQ_ID",
            "SELECT NEXTVAL('SEQ_ID')",
            "SELECT CURRVAL('SEQ_ID')",
            "SELECT NEXTVAL('PUBLIC', 'SEQ_ID')",
            "SELECT NEXTVAL('NOSUCH')",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        AssertSame(java, port);
    }

    /// <summary>
    /// The spelling of the ownership option, the same on both sides.
    /// </summary>
    /// <remarks>
    /// The statements are interleaved with queries that print a marker, because the two
    /// spellings fail on different statements and a bare list of errors cannot say which is
    /// which.
    /// </remarks>
    [JavaOracleFact]
    public void TheOwnershipOptionIsTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE SEQUENCE SEQ_A BELONGS TO TABLE",
            "SELECT 'A'",
            "CREATE SEQUENCE SEQ_B BELONGS_TO_TABLE",
            "SELECT 'B'",
            "DROP SEQUENCE SEQ_A",
            "DROP SEQUENCE SEQ_B",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        AssertSame(java, port);
    }

    /// <summary>
    /// The name of a sequence with a schema in front of it, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheQualifiedNameIsTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE SEQUENCE PUBLIC.SEQ_ID",
            "SELECT NEXT VALUE FOR PUBLIC.SEQ_ID",
            "SELECT SEQUENCE_SCHEMA, SEQUENCE_NAME FROM INFORMATION_SCHEMA.SEQUENCES "
                + "ORDER BY SEQUENCE_NAME",
            "DROP SEQUENCE PUBLIC.SEQ_ID",
            "SELECT NEXT VALUE FOR PUBLIC.SEQ_ID",
            "CREATE SEQUENCE NOSUCH.SEQ_ID",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        AssertSame(java, port);
    }

    /// <summary>
    /// Compares the answers of the two sides.
    /// </summary>
    /// <param name="java">the answers of the reference</param>
    /// <param name="port">the answers of the port</param>
    private static void AssertSame(List<string> java, List<string> port)
    {
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("sequence_oracle")), "sa")
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
                    _ = container.ExecuteUpdate(false);
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:sequence_oracle");
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
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}
