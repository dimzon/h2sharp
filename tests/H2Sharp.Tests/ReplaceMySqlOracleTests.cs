/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * REPLACE INTO, the reference's dml/replace.sql: the MySQL-mode statement is MERGE with
 * isReplace — a row whose key exists is deleted and inserted again, so the update count is
 * two. The mode comes from the open (D-029); in Regular mode the word is a syntax error.
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
public sealed class ReplaceMySqlOracleTests
{
    [JavaOracleFact]
    public void ReplaceIntoMySqlModeMatchesJdbc()
    {
        string[] statements =
        [
            "CREATE TABLE TABLE_WORD (WORD_ID INT NOT NULL AUTO_INCREMENT, WORD VARCHAR(128) NOT NULL, PRIMARY KEY (WORD_ID))",
            "REPLACE INTO TABLE_WORD(WORD) VALUES ('aaaaaaaaaa')",
            "REPLACE INTO TABLE_WORD(WORD) VALUES ('bbbbbbbbbb')",
            "REPLACE INTO TABLE_WORD(WORD_ID, WORD) VALUES (3, 'cccccccccc')",
            "SELECT WORD FROM TABLE_WORD WHERE WORD_ID = 1",
            "REPLACE INTO TABLE_WORD(WORD_ID, WORD) VALUES (1, 'REPLACED')",
            "SELECT WORD FROM TABLE_WORD WHERE WORD_ID = 1",
            "REPLACE INTO TABLE_WORD(WORD) SELECT 'dddddddddd'",
            "SELECT WORD FROM TABLE_WORD WHERE WORD_ID = 4",
            "REPLACE INTO TABLE_WORD(WORD_ID, WORD) SELECT 1, 'REPLACED2'",
            "SELECT WORD FROM TABLE_WORD WHERE WORD_ID = 1",
            "DROP TABLE TABLE_WORD",
        ];
        RunBoth(statements, Mode.GetInstance("MySQL"));
    }

    [JavaOracleFact]
    public void ReplaceIntoRefusesOutsideMySqlMode()
    {
        string[] statements =
        [
            "CREATE TABLE TABLE_WORD (WORD_ID INT PRIMARY KEY, WORD VARCHAR(128) NOT NULL)",
            "REPLACE INTO TABLE_WORD(WORD) VALUES ('aaaaaaaaaa')",
            "DROP TABLE TABLE_WORD",
        ];
        RunBoth(statements, null);
    }

    /// <summary>
    /// Runs the statements on both sides and asserts the same answers.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <param name="mode">the mode both sides open with, or null</param>
    private static void RunBoth(IReadOnlyList<string> statements, Mode? mode)
    {
        List<string> port = RunPort(statements, mode);
        List<string> java = RunJava(statements, mode);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <param name="mode">the mode the database opens with, or null</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements, Mode? mode)
    {
        Database database = new(ConnectionInfo.Memory("replace_oracle", mode));
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
    /// <param name="mode">the mode the database opens with, or null</param>
    /// <returns>the lines</returns>
    private static List<string> RunJava(IReadOnlyList<string> statements, Mode? mode)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:replace_oracleMODE", "sa", "");
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
            """.Replace(
                "replace_oracleMODE",
                "replace_oracle" + (mode is null ? "" : ";MODE=" + mode)),
            extraJavaArgs: ["-Duser.language=en"]);
}
