/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * An eight-table join: past seven filters the optimizer stops trying every order and fills the
 * rest greedily, then shuffles and keeps what is cheaper. Which order it settles on is not
 * compared — the answer is, against the reference running the same text.
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
public sealed class OptimizerOracleTests
{
    /// <summary>
    /// The statements, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void AnEightTableJoinAnswersTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE O1(ID INT PRIMARY KEY, V INT)",
            "CREATE TABLE O2(ID INT PRIMARY KEY, V INT)",
            "CREATE TABLE O3(ID INT PRIMARY KEY, V INT)",
            "CREATE TABLE O4(ID INT PRIMARY KEY, V INT)",
            "CREATE TABLE O5(ID INT PRIMARY KEY, V INT)",
            "CREATE TABLE O6(ID INT PRIMARY KEY, V INT)",
            "CREATE TABLE O7(ID INT PRIMARY KEY, V INT)",
            "CREATE TABLE O8(ID INT PRIMARY KEY, V INT)",
            "INSERT INTO O1 VALUES (1, 10), (2, 20), (3, 30)",
            "INSERT INTO O2 VALUES (1, 11), (2, 21), (3, 31)",
            "INSERT INTO O3 VALUES (1, 12), (2, 22), (3, 32)",
            "INSERT INTO O4 VALUES (1, 13), (2, 23), (3, 33)",
            "INSERT INTO O5 VALUES (1, 14), (2, 24), (3, 34)",
            "INSERT INTO O6 VALUES (1, 15), (2, 25), (3, 35)",
            "INSERT INTO O7 VALUES (1, 16), (2, 26), (3, 36)",
            "INSERT INTO O8 VALUES (1, 17), (2, 27), (3, 37)",
            // Eight filters: the order is found greedily plus shuffled.
            "SELECT COUNT(*) FROM O1, O2, O3, O4, O5, O6, O7, O8"
                + " WHERE O1.ID = O2.ID AND O2.ID = O3.ID AND O3.ID = O4.ID"
                + " AND O4.ID = O5.ID AND O5.ID = O6.ID AND O6.ID = O7.ID AND O7.ID = O8.ID",
            // A filter that keeps only some of the rows in the middle of the join.
            "SELECT O1.ID, O1.V + O8.V FROM O1, O2, O3, O4, O5, O6, O7, O8"
                + " WHERE O1.ID = O2.ID AND O2.ID = O3.ID AND O3.ID = O4.ID"
                + " AND O4.ID = O5.ID AND O5.ID = O6.ID AND O6.ID = O7.ID AND O7.ID = O8.ID"
                + " AND O4.V > 20 ORDER BY O1.ID",
            // An outer join between the extreme filters.
            "SELECT O1.ID, O8.V FROM O1 LEFT OUTER JOIN O8 ON O1.ID = O8.ID"
                + " LEFT OUTER JOIN O2 ON O2.ID = O1.ID"
                + " LEFT OUTER JOIN O3 ON O3.ID = O1.ID"
                + " LEFT OUTER JOIN O4 ON O4.ID = O1.ID"
                + " LEFT OUTER JOIN O5 ON O5.ID = O1.ID"
                + " LEFT OUTER JOIN O6 ON O6.ID = O1.ID"
                + " LEFT OUTER JOIN O7 ON O7.ID = O1.ID"
                + " ORDER BY O1.ID",
            // The same tables under a subquery keep the seven-filter exhaustive search.
            "SELECT COUNT(*) FROM (SELECT O1.ID FROM O1, O2, O3, O4, O5, O6, O7"
                + " WHERE O1.ID = O2.ID AND O2.ID = O3.ID AND O3.ID = O4.ID"
                + " AND O4.ID = O5.ID AND O5.ID = O6.ID AND O6.ID = O7.ID) X",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
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
            new Database(ConnectionInfo.Memory("optimizer_oracle")), "sa")
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:optimizer_oracle");
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
