/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * INSERT forms, checked against the reference running the same text: the plan of the VALUES
 * form, and INSERT IGNORE in the mode that has it — a duplicate key is skipped instead of
 * refused, and the row that lands is counted. D-029: a compatibility mode is part of the
 * database open (the connection string / JDBC URL), not SET MODE.
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
public sealed class InsertFormsOracleTests
{
    [JavaOracleFact]
    public void InsertFormsAnswerTheReference()
    {
        string[] statements =
        [
            // The plan of the values form prints the rows it was built from.
            "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(20))",
            "EXPLAIN INSERT INTO TEST VALUES (1, 'a'), (2, 'b')",
            "EXPLAIN INSERT INTO TEST(ID, NAME) VALUES (3, 'c')",
            "DROP TABLE TEST",
        ];
        RunBoth(statements);
    }

    /// <summary>
    /// INSERT IGNORE lives in the MySQL mode; both sides open their database with MODE=MySQL
    /// and the duplicate key is skipped instead of refused.
    /// </summary>
    [JavaOracleFact]
    public void InsertIgnoreAnswerTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(20))",
            "INSERT INTO TEST VALUES (1, 'a')",
            "INSERT IGNORE INTO TEST VALUES (1, 'b')",
            "SELECT NAME FROM TEST",
            "INSERT IGNORE INTO TEST VALUES (2, 'c')",
            "SELECT ID, NAME FROM TEST ORDER BY ID",
            "INSERT INTO TEST VALUES (1, 'x')",
            "DROP TABLE TEST",
        ];
        RunBoth(statements, Mode.GetInstance("MySQL"));
    }

    /// <summary>
    /// Runs the statements on both sides and asserts the same answers: the rows, or the code
    /// of the error.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <param name="mode">the compatibility mode both sides open with, or null for regular</param>
    private static void RunBoth(IReadOnlyList<string> statements, Mode? mode = null)
    {
        List<string> port = RunPort(statements, mode);
        List<string> java = RunJava(statements, mode);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered: the rows, or the code of
    /// the error.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <param name="mode">the compatibility mode of the opened database</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements, Mode? mode)
    {
        Database database = new(ConnectionInfo.Memory("insert_forms_oracle", mode));
        SessionLocal session = new(database, "sa")
        {
            CompareMode = CompareMode.GetInstance(null, 0),
        };

        // The provider must answer the database's mode (SessionCastDataProvider): the tests'
        // own TestCastDataProvider always answers Regular.
        session.CastDataProvider = new SessionCastDataProvider(session, DefaultCastDataProvider.Instance);
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
    /// <param name="mode">the compatibility mode of the opened database</param>
    /// <returns>the lines</returns>
    private static List<string> RunJava(IReadOnlyList<string> statements, Mode? mode)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:INSERT_FORMS_URL", "sa", "");
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
            """.Replace("INSERT_FORMS_URL", "jdbc:h2:mem:insert_forms_oracle" + (mode is null ? "" : ";MODE=" + mode)),
            extraJavaArgs: ["-Duser.language=en"]);
}
