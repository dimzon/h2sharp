/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Domain constraints, checked against the reference running the same text: ALTER DOMAIN ADD
 * CONSTRAINT over existing data (the walk over every table with a column of the domain,
 * including domains built on the domain), the NOCHECK escape, insert-time checks with TRUE
 * and NULL passing, renames, and the dropped-domain fallout.
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
public sealed class DomainConstraintOracleTests
{
    /// <summary>
    /// The statements, the same on both sides. The reference's alterDomain.sql supplies the
    /// matrix: a domain over a domain, existing data, the same CHECK added over three layers,
    /// NOCHECK, and the insert-time refusals.
    /// </summary>
    [JavaOracleFact]
    public void TheDomainConstraintsAnswerTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE DOMAIN D1 AS INT",
            "CREATE DOMAIN D2 AS D1",
            "CREATE TABLE TEST(A INT, C D2) AS VALUES (1, 1)",
            // Existing data violates: the walk refuses on every layer.
            "ALTER TABLE TEST ADD CHECK (C > 1)",
            "ALTER DOMAIN D2 ADD CHECK (VALUE > 1)",
            "ALTER DOMAIN D1 ADD CHECK (VALUE > 1)",
            // A scan index on the column does not change the walk.
            "CREATE INDEX TEST_C_IDX ON TEST(C)",
            "ALTER DOMAIN D1 ADD CHECK (VALUE > 1)",
            // NOCHECK skips the data walk.
            "ALTER DOMAIN D1 ADD CONSTRAINT DC1 CHECK (VALUE > 1) NOCHECK",
            // NULL passes, FALSE refuses, TRUE passes.
            "INSERT INTO TEST VALUES (2, NULL)",
            "INSERT INTO TEST VALUES (3, 0)",
            "INSERT INTO TEST VALUES (4, 5)",
            "SELECT A, C FROM TEST ORDER BY A",
            // The rename, and the not-found refusals.
            "ALTER DOMAIN D1 RENAME CONSTRAINT DC1 TO DC1R",
            "ALTER DOMAIN D3 RENAME CONSTRAINT DC1R TO DC1X",
            "ALTER DOMAIN IF EXISTS D3 RENAME CONSTRAINT DC1R TO DC1X",
            "ALTER DOMAIN D2 RENAME CONSTRAINT DC1R TO DC1X",
            "SELECT CONSTRAINT_NAME, DOMAIN_NAME FROM INFORMATION_SCHEMA.DOMAIN_CONSTRAINTS",
            // Dropping the base domain refuses while a dependent one exists, then succeeds
            // in order.
            "DROP DOMAIN D1",
            "DROP DOMAIN D2",
            "DROP DOMAIN D1",
            // CASCADE copies the domain's constraint onto the dependent column and the
            // dependent domain, and the parent domain's default onto both.
            "DROP TABLE TEST",
            "CREATE DOMAIN E1 AS INT DEFAULT 5",
            "CREATE DOMAIN E2 AS E1",
            // C has the dependent domain, D the dropped one directly: the copy runs for both
            // a column and a domain.
            "CREATE TABLE E(A INT, C E2, D E1)",
            "ALTER DOMAIN E1 ADD CONSTRAINT EC1 CHECK (VALUE > 1) NOCHECK",
            "DROP DOMAIN E1 CASCADE",
            "INSERT INTO E(A) VALUES (1)",
            "SELECT A, C, D FROM E",
            "INSERT INTO E(A, C, D) VALUES (2, 0, 0)",
            "INSERT INTO E(A, C, D) VALUES (3, 7, 9)",
            "SELECT A, C, D FROM E ORDER BY A",
            "DROP TABLE E",
        ];
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
            new Database(ConnectionInfo.Memory("domain_oracle")), "sa")
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:domain_oracle");
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
