/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The spatial index, checked against the reference running the same text.
 *
 * A spatial index is an R-tree over the envelope of a geometry column, queried with the
 * intersects operator &&. Two points that are not the same point do not intersect — the
 * queries below pin that as much as the hits.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class SpatialIndexOracleTests
{
    /// <summary>
    /// A spatial index over a populated table: the intersects query answers the rows whose
    /// envelopes the box meets, a delete takes its row out, and a NULL geometry is stored but
    /// never answers.
    /// </summary>
    [JavaOracleFact]
    public void ASpatialIndexIsTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE G(ID INT PRIMARY KEY, THE_GEOM GEOMETRY)",
            "INSERT INTO G VALUES (1, 'POINT (1 1)'), (2, 'POINT (50 50)'), (3, 'POINT (2 2)')",
            "CREATE SPATIAL INDEX IDX_S ON G(THE_GEOM)",
            "SELECT ID FROM G WHERE THE_GEOM && 'POINT (1 1)' ORDER BY ID",
            "SELECT ID FROM G WHERE THE_GEOM && 'POINT (1.5 1.5)' ORDER BY ID",
            "DELETE FROM G WHERE ID = 1",
            "SELECT ID FROM G WHERE THE_GEOM && 'POINT (1 1)' ORDER BY ID",
            "SELECT ID FROM G WHERE THE_GEOM && 'POINT (50 50)' ORDER BY ID",
            "INSERT INTO G VALUES (4, NULL)",
            "SELECT ID FROM G WHERE THE_GEOM && 'POINT (50 50)' ORDER BY ID",
            "SELECT COUNT(*) FROM G",
        ];
        List<string> java = RunJava("spatial_index_oracle", statements);
        List<string> port = RunPort("spatial_index_oracle", statements);
        AssertTrue(java, port);
    }

    /// <summary>
    /// What INFORMATION_SCHEMA.INDEXES says a spatial index is, read back the way a client
    /// reads it.
    /// </summary>
    [JavaOracleFact]
    public void TheIndexTypeLabelIsTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE G(ID INT PRIMARY KEY, THE_GEOM GEOMETRY)",
            "CREATE SPATIAL INDEX IDX_S ON G(THE_GEOM)",
            "SELECT INDEX_NAME, INDEX_TYPE_NAME FROM INFORMATION_SCHEMA.INDEXES "
            + "WHERE TABLE_NAME = 'G' ORDER BY INDEX_NAME",
        ];
        List<string> java = RunJava("spatial_index_label_oracle", statements);
        List<string> port = RunPort("spatial_index_label_oracle", statements);
        AssertTrue(java, port);
    }

    /// <summary>
    /// The refusals: a spatial index over a non-geometry column, UNIQUE together with SPATIAL,
    /// and a hash or spatial spelling with USING.
    /// </summary>
    [JavaOracleFact]
    public void TheRefusalsAnswerTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE G(ID INT PRIMARY KEY, THE_GEOM GEOMETRY)",
            "CREATE TABLE N(X INT)",
            "CREATE SPATIAL INDEX IDX_N ON N(X)",
            "CREATE UNIQUE SPATIAL INDEX ON G(THE_GEOM)",
            "CREATE HASH INDEX IDX_H ON G(ID) USING RTREE (ID)",
            "CREATE SPATIAL INDEX IDX_S ON G(THE_GEOM) USING RTREE (THE_GEOM)",
        ];
        List<string> java = RunJava("spatial_index_refusals_oracle", statements);
        List<string> port = RunPort("spatial_index_refusals_oracle", statements);
        AssertTrue(java, port);
    }

    /// <summary>
    /// The PostgreSQL spelling: USING RTREE makes a plain index a spatial one.
    /// </summary>
    [JavaOracleFact]
    public void TheUsingRtreeSpellingIsTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE G(ID INT PRIMARY KEY, THE_GEOM GEOMETRY)",
            "INSERT INTO G VALUES (1, 'POINT (1 1)'), (2, 'POINT (9 9)')",
            "CREATE INDEX IDX_R ON G USING RTREE (THE_GEOM)",
            "SELECT ID FROM G WHERE THE_GEOM && 'POINT (9 9)' ORDER BY ID",
        ];
        List<string> java = RunJava("spatial_index_using_oracle", statements);
        List<string> port = RunPort("spatial_index_using_oracle", statements);
        AssertTrue(java, port);
    }

    /// <summary>
    /// The plan a spatial index produces: the annotation names the index and the condition,
    /// the same text on both sides once the layout whitespace is folded away.
    /// </summary>
    [JavaOracleFact]
    public void TheQueryPlanNamesTheSpatialIndex()
    {
        string[] statements =
        [
            "CREATE TABLE G(ID INT PRIMARY KEY, THE_GEOM GEOMETRY)",
            "CREATE SPATIAL INDEX IDX_S ON G(THE_GEOM)",
            "INSERT INTO G VALUES (1, 'POINT (1 1)')",
            "EXPLAIN SELECT ID FROM G WHERE THE_GEOM && 'POINT (1 1)'",
        ];
        List<string> java = RunJava("spatial_index_plan_oracle", statements);
        List<string> port = RunPort("spatial_index_plan_oracle", statements);
        AssertTrue(java, port);
    }

    /// <summary>
    /// Compares the answers of the two sides.
    /// </summary>
    /// <param name="java">the answers of the reference</param>
    /// <param name="port">the answers of the port</param>
    private static void AssertTrue(List<string> java, List<string> port)
    {
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered.
    /// </summary>
    /// <param name="name">the name of the database</param>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(string name, IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory(name)), "sa")
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
                if (sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
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
                            cells.Add(value is null or ValueNull
                                ? "-"
                                : Regex.Replace(value.GetString() ?? "-", "\\s+", " "));
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
    /// <param name="name">the name of the database</param>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunJava(string name, IReadOnlyList<string> statements)
        => OracleRunner.Run(
            statements,
            $$"""
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:{{name}}");
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
                      line.append(rs.wasNull() ? "-" : s.replaceAll("\\s+", " "));
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
