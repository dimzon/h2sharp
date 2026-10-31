/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The hash index, checked against the reference running the same text.
 *
 * In H2 2.5 a hash index is not a structure of its own: the flag rides on IndexType and the
 * storage is the same ordered map as any other secondary index — what the flag changes is the
 * label (HASH INDEX in INFORMATION_SCHEMA, the SQL of the index), the two optimizations it
 * disqualifies, and nothing else. The reference's own TestIndex exercises the shapes these
 * tests repeat: plain, multi-column, unnamed unique, the PRIMARY KEY HASH constraint and the
 * PostgreSQL USING spelling.
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
public sealed class HashIndexOracleTests
{
    /// <summary>
    /// The reference's testIndexTypes shapes: a plain hash index over a thousand rows, a point
    /// lookup, a delete, the same lookup and the min/max over the column.
    /// </summary>
    [JavaOracleFact]
    public void AHashIndexIsTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE TEST(ID INT)",
            "CREATE HASH INDEX IDX_NAME ON TEST(ID)",
            "INSERT INTO TEST SELECT X FROM SYSTEM_RANGE(1, 1000)",
            "SELECT COUNT(*) FROM TEST WHERE ID = 100",
            "DELETE FROM TEST WHERE ID = 100",
            "SELECT COUNT(*) FROM TEST WHERE ID = 100",
            "SELECT MIN(ID), MAX(ID) FROM TEST",
            "CREATE HASH INDEX IDX_MULTI ON TEST(ID, ID)",
            "SELECT COUNT(*) FROM TEST WHERE ID = 3",
        ];
        List<string> java = RunJava("hash_index_oracle", statements);
        List<string> port = RunPort("hash_index_oracle", statements);
        AssertTrue(java, port);
    }

    /// <summary>
    /// The reference's testHashIndex unique shape: CREATE UNIQUE HASH INDEX without a name, a
    /// duplicate refused with 23505, and two NULLs both kept.
    /// </summary>
    [JavaOracleFact]
    public void AUniqueHashIndexIsTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE U(A INT, B INT)",
            "CREATE UNIQUE HASH INDEX ON U(A)",
            "INSERT INTO U VALUES (1, 10)",
            "INSERT INTO U VALUES (1, 11)",
            "INSERT INTO U VALUES (NULL, 12)",
            "INSERT INTO U VALUES (NULL, 13)",
            "SELECT A, B FROM U ORDER BY B",
        ];
        List<string> java = RunJava("unique_hash_index_oracle", statements);
        List<string> port = RunPort("unique_hash_index_oracle", statements);
        AssertTrue(java, port);
    }

    /// <summary>
    /// The hash spelling of a primary key, as the table constraint the reference's
    /// testHashIndex uses: duplicates refused, lookups answered.
    /// </summary>
    [JavaOracleFact]
    public void APrimaryKeyHashConstraintIsTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE P(ID INT, NAME VARCHAR, PRIMARY KEY HASH(ID))",
            "INSERT INTO P VALUES (1, 'a')",
            "INSERT INTO P VALUES (1, 'b')",
            "INSERT INTO P VALUES (2, 'b')",
            "SELECT NAME FROM P WHERE ID = 2",
        ];
        List<string> java = RunJava("primary_key_hash_oracle", statements);
        List<string> port = RunPort("primary_key_hash_oracle", statements);
        AssertTrue(java, port);
    }

    /// <summary>
    /// What INFORMATION_SCHEMA.INDEXES says a hash index is: the generated name and the
    /// INDEX_TYPE_NAME label, read back the way a client reads them.
    /// </summary>
    [JavaOracleFact]
    public void TheIndexTypeLabelIsTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR)",
            "CREATE HASH INDEX IDX_H ON TEST(NAME)",
            "SELECT INDEX_NAME, INDEX_TYPE_NAME FROM INFORMATION_SCHEMA.INDEXES "
            + "WHERE TABLE_NAME = 'TEST' ORDER BY INDEX_NAME",
        ];
        List<string> java = RunJava("hash_index_label_oracle", statements);
        List<string> port = RunPort("hash_index_label_oracle", statements);
        AssertTrue(java, port);
    }

    /// <summary>
    /// The PostgreSQL USING spelling: USING HASH makes a plain index a hash one, and a hash or
    /// spatial index refuses a USING spelling of its own with the same syntax error.
    /// </summary>
    [JavaOracleFact]
    public void TheUsingSpellingsAnswerTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE TEST(ID INT, NAME VARCHAR)",
            "CREATE INDEX IDX_USING ON TEST USING HASH (NAME)",
            "INSERT INTO TEST VALUES (1, 'a'), (2, 'b')",
            "SELECT COUNT(*) FROM TEST WHERE NAME = 'a'",
            "CREATE HASH INDEX IDX_H2 ON TEST USING HASH (ID)",
            "CREATE INDEX IDX_BOGUS ON TEST USING BOGUS (NAME)",
        ];
        List<string> java = RunJava("hash_index_using_oracle", statements);
        List<string> port = RunPort("hash_index_using_oracle", statements);
        AssertTrue(java, port);
    }

    /// <summary>
    /// The plan a hash index produces: the annotation names the index and the condition, the
    /// same text on both sides once the layout whitespace is folded away.
    /// </summary>
    [JavaOracleFact]
    public void TheQueryPlanNamesTheHashIndex()
    {
        string[] statements =
        [
            "CREATE TABLE TEST(ID INT, NAME VARCHAR)",
            "CREATE HASH INDEX IDX_H ON TEST(NAME)",
            "INSERT INTO TEST VALUES (1, 'a'), (2, 'b')",
            "EXPLAIN SELECT * FROM TEST WHERE NAME = 'a'",
        ];
        List<string> java = RunJava("hash_index_plan_oracle", statements);
        List<string> port = RunPort("hash_index_plan_oracle", statements);
        AssertTrue(java, port);
    }

    /// <summary>
    /// Folds runs of whitespace into one space: the plan text is laid out with different
    /// indentation on the two sides, and the folded line is what the question is about.
    /// </summary>
    /// <param name="text">the cell text</param>
    /// <returns>the folded text</returns>
    private static string Fold(string text)
        => Regex.Replace(text, "\\s+", " ");

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
                                : Fold(value.GetString() ?? "-"));
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
