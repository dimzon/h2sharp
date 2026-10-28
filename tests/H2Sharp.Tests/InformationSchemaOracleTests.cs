/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What the INFORMATION_SCHEMA answers, checked against the reference running the same text.
 *
 * The catalogue is a view of the metadata of the database, and a view that answers is a view
 * that has to agree with the reference on every column it shows: the name of a table, the
 * type and the position of a column, the constraint on it, the index over it. The same
 * statements are asked of both sides, over the same table, and the rows are compared cell by
 * cell.
 *
 * The SYSTEM_RANGE table is asked of both sides too: it is a table function, and the numbers
 * it makes are the numbers the reference makes.
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
public sealed class InformationSchemaOracleTests
{
    /// <summary>
    /// The table both sides look at: a primary key, a unique column, a decimal and a string.
    /// </summary>
    private static readonly string[] Setup =
    [
        "CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(50) NOT NULL, AMOUNT DECIMAL(10, 2), BIG BIGINT)",
        "CREATE INDEX IDX ON T(NAME)",
    ];

    /// <summary>
    /// The questions both sides are asked.
    /// </summary>
    private static readonly string[] Queries =
    [
        "SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA ORDER BY SCHEMA_NAME",
        "SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE, IS_INSERTABLE_INTO FROM INFORMATION_SCHEMA.TABLES "
            + "ORDER BY TABLE_SCHEMA, TABLE_NAME",
        "SELECT TABLE_SCHEMA, TABLE_NAME, VIEW_DEFINITION, CHECK_OPTION, IS_UPDATABLE, STATUS "
            + "FROM INFORMATION_SCHEMA.VIEWS ORDER BY TABLE_SCHEMA, TABLE_NAME",
        "SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, ORDINAL_POSITION, DATA_TYPE, IS_NULLABLE, "
            + "NUMERIC_PRECISION, NUMERIC_SCALE, CHARACTER_MAXIMUM_LENGTH, DATETIME_PRECISION "
            + "FROM INFORMATION_SCHEMA.COLUMNS ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION",
        "SELECT CONSTRAINT_NAME, CONSTRAINT_TYPE, TABLE_NAME FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS "
            + "WHERE TABLE_NAME = 'T' ORDER BY CONSTRAINT_NAME",
        "SELECT CONSTRAINT_NAME, COLUMN_NAME, ORDINAL_POSITION FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE "
            + "WHERE TABLE_NAME = 'T' ORDER BY CONSTRAINT_NAME, ORDINAL_POSITION",
        "SELECT CONSTRAINT_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE "
            + "WHERE TABLE_NAME = 'T' ORDER BY CONSTRAINT_NAME, COLUMN_NAME",
        "SELECT INDEX_NAME, TABLE_NAME, INDEX_TYPE_NAME, IS_GENERATED FROM INFORMATION_SCHEMA.INDEXES "
            + "WHERE TABLE_NAME = 'T' ORDER BY INDEX_NAME",
        "SELECT INDEX_NAME, COLUMN_NAME, ORDINAL_POSITION, ORDERING_SPECIFICATION, IS_UNIQUE "
            + "FROM INFORMATION_SCHEMA.INDEX_COLUMNS WHERE TABLE_NAME = 'T' ORDER BY INDEX_NAME, ORDINAL_POSITION",
        "SELECT COLLATION_NAME, PAD_ATTRIBUTE FROM INFORMATION_SCHEMA.COLLATIONS "
            + "WHERE COLLATION_NAME = 'OFF' ORDER BY COLLATION_NAME",
        "SELECT X FROM SYSTEM_RANGE(1, 5)",
        "SELECT COUNT(*) FROM SYSTEM_RANGE(1, 5)",
        "SELECT X FROM SYSTEM_RANGE(10, 2, -2)",
    ];

    /// <summary>
    /// The whole COLUMNS view, every column of it, in the order the reference declares them.
    /// </summary>
    private static readonly string[] AllColumns =
    [
        "TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "COLUMN_NAME", "ORDINAL_POSITION",
        "COLUMN_DEFAULT", "IS_NULLABLE", "DATA_TYPE", "CHARACTER_MAXIMUM_LENGTH",
        "CHARACTER_OCTET_LENGTH", "NUMERIC_PRECISION", "NUMERIC_PRECISION_RADIX", "NUMERIC_SCALE",
        "DATETIME_PRECISION", "INTERVAL_TYPE", "INTERVAL_PRECISION", "CHARACTER_SET_CATALOG",
        "CHARACTER_SET_SCHEMA", "CHARACTER_SET_NAME", "COLLATION_CATALOG", "COLLATION_SCHEMA",
        "COLLATION_NAME", "DOMAIN_CATALOG", "DOMAIN_SCHEMA", "DOMAIN_NAME", "MAXIMUM_CARDINALITY",
        "DTD_IDENTIFIER", "IS_IDENTITY", "IDENTITY_GENERATION", "IDENTITY_START", "IDENTITY_INCREMENT",
        "IDENTITY_MAXIMUM", "IDENTITY_MINIMUM", "IDENTITY_CYCLE", "IS_GENERATED",
        "GENERATION_EXPRESSION", "DECLARED_DATA_TYPE", "DECLARED_NUMERIC_PRECISION",
        "DECLARED_NUMERIC_SCALE", "GEOMETRY_TYPE", "GEOMETRY_SRID", "IDENTITY_BASE", "IDENTITY_CACHE",
        "COLUMN_ON_UPDATE", "IS_VISIBLE", "DEFAULT_ON_NULL", "SELECTIVITY", "REMARKS",
    ];

    /// <summary>
    /// The same questions, asked of both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheCatalogueAnswersTheSameAsTheReference()
    {
        List<string> port = RunPort(Setup, Queries);
        List<string> java = RunJava(Setup, Queries);
        Assert.Equal(java.Count, port.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == port[i],
                $"line {i}\n  java:   {java[i]}\n  csharp: {port[i]}");
        }
    }

    /// <summary>
    /// The COLUMNS view with every column of it, asked of both sides.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     A row of a view is read by the order of the columns the view declares, and the reader
    ///     walks the row by that order. A row that is shorter leaves it outside the array, and a
    ///     row whose values are in the wrong order answers with the wrong cell. Asking for a part
    ///     of the view hides both: the cells that are not asked for are never read, and the ones
    ///     that are read may be the right ones with the wrong meaning.
    /// </para>
    /// </remarks>
    [JavaOracleFact]
    public void EveryColumnOfTheColumnsViewAgreesWithTheReference()
    {
        string[] queries =
        [
            "SELECT " + string.Join(", ", AllColumns) + " FROM INFORMATION_SCHEMA.COLUMNS "
                + "ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION",
        ];

        List<string> port = RunPort(Setup, queries);
        List<string> java = RunJava(Setup, queries);
        Assert.Equal(java.Count, port.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == port[i],
                $"line {i}\n  java:   {java[i]}\n  csharp: {port[i]}");
        }
    }

    /// <summary>
    /// Runs the statements here and writes what each of them answered.
    /// </summary>
    /// <param name="setup">the statements that prepare the database</param>
    /// <param name="queries">the statements that are asked</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> setup, IReadOnlyList<string> queries)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("info_schema_oracle")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };

        foreach (string sql in setup)
        {
            CommandContainer container =
                Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
            _ = container.ExecuteUpdate(false);
        }

        List<string> lines = [];
        foreach (string sql in queries)
        {
            CommandContainer container =
                Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
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

        return lines;
    }

    /// <summary>
    /// Runs the statements on the reference and writes what each of them answered.
    /// </summary>
    /// <param name="setup">the statements that prepare the database</param>
    /// <param name="queries">the statements that are asked</param>
    /// <returns>the lines</returns>
    private static List<string> RunJava(IReadOnlyList<string> setup, IReadOnlyList<string> queries)
    {
        List<string> inputs = [.. setup, .. queries];
        return OracleRunner.Run(
            inputs,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:info_schema_oracle");
            java.sql.Statement stat = conn.createStatement();
            int setupCount = 2;
            for (int i = 0; i < inputs.length; i++) {
              if (i < setupCount) {
                stat.execute(inputs[i]);
                continue;
              }
              java.sql.ResultSet rs = stat.executeQuery(inputs[i]);
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
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
    }
}
