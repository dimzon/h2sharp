/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The reference's dml/delete.sql, statement by statement: DELETE with ROWNUM, the FETCH
 * forms, the EXPLAIN forms, the subquery form of LIMIT, and the parameter form. The
 * DELETE TOP 10 form belongs to MSSQLServer mode and runs in its own fact, because the
 * mode is fixed at the open (D-029).
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
public sealed class DeleteScriptOracleTests
{
    [JavaOracleFact]
    public void DeleteScriptAnswersTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE TEST(ID INT)",
            "INSERT INTO TEST VALUES (1), (2), (3)",
            "DELETE FROM TEST WHERE EXISTS (SELECT X FROM SYSTEM_RANGE(1, 3) WHERE X = ID) AND ROWNUM() = 1",
            "SELECT ID FROM TEST",
            "DROP TABLE TEST",
            "CREATE TABLE TEST(ID INT PRIMARY KEY) AS SELECT * FROM SYSTEM_RANGE(1, 13)",
            "DELETE FROM TEST WHERE ID <= 12 FETCH FIRST ROW ONLY",
            "DELETE FROM TEST WHERE ID <= 12 FETCH FIRST ROWS ONLY",
            "DELETE FROM TEST WHERE ID <= 12 FETCH NEXT ROW ONLY",
            "DELETE FROM TEST WHERE ID <= 12 FETCH NEXT ROWS ONLY",
            "DELETE FROM TEST WHERE ID <= 12 FETCH FIRST 2 ROW ONLY",
            "DELETE FROM TEST WHERE ID <= 12 FETCH FIRST 2 ROWS ONLY",
            "DELETE FROM TEST WHERE ID <= 12 FETCH NEXT 2 ROW ONLY",
            "DELETE FROM TEST WHERE ID <= 12 FETCH NEXT 2 ROWS ONLY",
            "EXPLAIN DELETE FROM TEST WHERE ID <= 12 FETCH FIRST 2 ROWS ONLY",
            "EXPLAIN DELETE FROM TEST FETCH FIRST 1 ROW ONLY",
            "EXPLAIN DELETE FROM TEST",
            "TABLE TEST",
            "DROP TABLE TEST",
            "CREATE TABLE TEST(id int) AS SELECT x FROM system_range(1, 100)",
            "DELETE FROM TEST LIMIT ((SELECT COUNT(*) FROM TEST) / 10)",
            "SELECT COUNT(*) FROM TEST",
            "EXPLAIN DELETE FROM TEST LIMIT ((SELECT COUNT(*) FROM TEST) / 10)",
            "DELETE FROM TEST LIMIT ?",
            "SELECT COUNT(*) FROM TEST",
            "DROP TABLE TEST",
        ];
        RunBoth(statements, null);
    }

    [JavaOracleFact]
    public void DeleteTopMatchesJdbcInMsSqlMode()
    {
        string[] statements =
        [
            "CREATE TABLE TEST(id int) AS SELECT x FROM system_range(1, 100)",
            "DELETE TOP 10 FROM TEST",
            "SELECT COUNT(*) FROM TEST",
            "DROP TABLE TEST",
        ];
        RunBoth(statements, Mode.GetInstance("MSSQLServer"));
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
        Database database = new(ConnectionInfo.Memory("delete_script_oracle", mode));
        SessionLocal session = new(database, "sa")
        {
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        session.CastDataProvider = new SessionCastDataProvider(session, DefaultCastDataProvider.Instance);
        List<string> lines = [];
        foreach (string sql in statements)
        {
            try
            {
                CommandContainer container =
                    Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
                if (sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                    || sql.StartsWith("TABLE", StringComparison.OrdinalIgnoreCase)
                    || sql.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase))
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:delete_script_oracleMODE", "sa", "");
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
                "delete_script_oracleMODE",
                "delete_script_oracle" + (mode is null ? "" : ";MODE=" + mode)),
            extraJavaArgs: ["-Duser.language=en"]);
}
