/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The filter layer under the statements: outer joins and their null rows, conditions on views
 * and derived tables, temporary tables, generated columns and a multi-column DROP. Answers and
 * refusals are compared with the reference running the same text.
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
public sealed class TableFilterOracleTests
{
    /// <summary>
    /// The statements, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheFilterLayerAnswersTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE P(ID INT PRIMARY KEY, V INT)",
            "CREATE TABLE C(ID INT PRIMARY KEY, PID INT, V INT)",
            "CREATE TABLE D(ID INT PRIMARY KEY, CID INT, V INT)",
            "INSERT INTO P VALUES (1, 10), (2, 20), (3, 30)",
            "INSERT INTO C VALUES (10, 1, 100), (11, 1, 110), (20, 2, 200)",
            "INSERT INTO D VALUES (100, 10, 1), (101, 20, 2)",
            // Inner, left, right and full outer joins, with and without a matching side.
            "SELECT P.ID, C.ID FROM P JOIN C ON C.PID = P.ID ORDER BY P.ID, C.ID",
            "SELECT P.ID, C.ID FROM P LEFT JOIN C ON C.PID = P.ID ORDER BY P.ID, C.ID",
            "SELECT P.ID, C.ID FROM P RIGHT JOIN C ON C.PID = P.ID ORDER BY P.ID, C.ID",
            "SELECT P.ID, C.ID FROM P FULL OUTER JOIN C ON C.PID = P.ID ORDER BY P.ID, C.ID",
            // The null row of an outer join is a row: IS NULL finds it, a condition on the
            // inner side drops it.
            "SELECT P.ID FROM P LEFT JOIN C ON C.PID = P.ID WHERE C.ID IS NULL ORDER BY P.ID",
            "SELECT P.ID FROM P LEFT JOIN C ON C.PID = P.ID WHERE C.V > 0 ORDER BY P.ID",
            // Three tables, each joined on the previous one, some of them outer.
            "SELECT P.ID, C.ID, D.ID FROM P LEFT JOIN C ON C.PID = P.ID"
                + " LEFT JOIN D ON D.CID = C.ID ORDER BY P.ID, C.ID, D.ID",
            "SELECT P.ID, C.ID, D.ID FROM P JOIN C ON C.PID = P.ID"
                + " RIGHT JOIN D ON D.CID = C.ID ORDER BY P.ID, C.ID, D.ID",
            // An index on the join column changes the plan, not the answer.
            "CREATE INDEX IDX_C_PID ON C(PID)",
            "CREATE INDEX IDX_D_CID ON D(CID)",
            "SELECT P.ID, C.ID FROM P LEFT JOIN C ON C.PID = P.ID ORDER BY P.ID, C.ID",
            "SELECT P.ID, COUNT(C.ID) FROM P LEFT JOIN C ON C.PID = P.ID GROUP BY P.ID ORDER BY P.ID",
            // A derived table: conditions outside and inside it.
            "SELECT X.ID, X.V FROM (SELECT ID, V FROM C WHERE V >= 100) X WHERE X.ID > 10 ORDER BY X.ID",
            "SELECT X.ID FROM (SELECT PID, COUNT(*) AS CNT FROM C GROUP BY PID) X WHERE X.CNT > 1 ORDER BY X.ID",
            "SELECT X.ID FROM (SELECT ID FROM C) X WHERE X.ID IN (SELECT CID FROM D) ORDER BY X.ID",
            // A view: conditions on the view and inside it; the answer is the same either way.
            "CREATE VIEW VW AS SELECT ID, V FROM C WHERE V > 0",
            "SELECT ID FROM VW WHERE ID > 10 ORDER BY ID",
            "SELECT COUNT(*) FROM VW WHERE V < 200",
            // Temporary tables go through the same filters.
            "CREATE LOCAL TEMPORARY TABLE TP(ID INT PRIMARY KEY, V INT)",
            "CREATE INDEX IDX_TP_V ON TP(V)",
            "INSERT INTO TP VALUES (1, 10), (2, 20)",
            "SELECT ID FROM TP WHERE V IN (10, 20) ORDER BY ID",
            "SELECT P.ID FROM P JOIN TP ON TP.ID = P.ID ORDER BY P.ID",
            // A multi-column DROP removes the indexes and constraints that only covered the
            // dropped columns, and refuses when a remaining object still needs one.
            "CREATE TABLE M(A INT, B INT, C INT, D INT, PRIMARY KEY(A, B))",
            "CREATE INDEX IDX_M_CD ON M(C, D)",
            "CREATE INDEX IDX_M_BC ON M(B, C)",
            "INSERT INTO M VALUES (1, 10, 100, 1000), (2, 20, 200, 2000)",
            "ALTER TABLE M DROP COLUMN (C, D)",
            "SELECT A, B FROM M ORDER BY A",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'M'",
            // A generated column may not be written to; leaving it out computes it.
            "CREATE TABLE G(ID INT PRIMARY KEY, A INT, B INT GENERATED ALWAYS AS (A * 2))",
            "INSERT INTO G(ID, A) VALUES (1, 21)",
            "SELECT ID, A, B FROM G",
            "INSERT INTO G(ID, A, B) VALUES (2, 5, 10)",
            "UPDATE G SET B = 7",
            "UPDATE G SET A = 22",
            "SELECT ID, A, B FROM G",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.True(
            java.Count == port.Count,
            "lines: java=" + java.Count.ToString(CultureInfo.InvariantCulture)
            + " port=" + port.Count.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < java.Count; i++)
        {
            string statement = i < statements.Length ? statements[i] : "(a later row)";
            Assert.True(java[i] == port[i], $"line {i} ({statement}):\n  java: {java[i]}\n  port: {port[i]}");
        }
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("table_filter_oracle")), "sa")
        {
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:table_filter_oracle");
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
            extraJavaArgs: ["-Duser.language=en", "-Duser.timezone=UTC"]);
}
