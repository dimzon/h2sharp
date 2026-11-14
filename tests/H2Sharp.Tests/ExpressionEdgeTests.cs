/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 205: the edges of a column expression and of an arithmetic operation — NOT over
 * every numeric type, `+` as concatenation and the decimal scale rules in SQL Server mode,
 * the interval arithmetic refusals, and the guards of a column that was never mapped.
 */
using System;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ExpressionEdgeTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void NotOverTypedColumnsBecomesAComparison()
    {
        AssertSameAsReference("expr_edge_not",
            "CREATE TABLE T(B BOOLEAN, TI TINYINT, SI SMALLINT, I INT, BI BIGINT, "
                + "N NUMERIC(10, 2), R REAL, D DOUBLE, DF DECFLOAT, S VARCHAR)",
            "INSERT INTO T VALUES (TRUE, 0, 2, 0, 4, 0, 6.5, 0, 8.5, 'x')",
            "SELECT NOT B, NOT TI, NOT SI, NOT I, NOT BI FROM T",
            "SELECT NOT N, NOT R, NOT D, NOT DF FROM T",
            "SELECT NOT S FROM T",
            "SELECT CAST(NULL AS INT) + 1",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void PlusConcatAndNumericScaleEdgesInSqlServerMode()
    {
        AssertSameAsReferenceInMode("expr_edge_plus", "MSSQLServer",
            "CREATE TABLE T(A VARCHAR(10), B VARCHAR(10))",
            "INSERT INTO T VALUES ('a', 'b')",
            "SELECT 'x' + 'y'",
            "SELECT A + B FROM T",
            "SELECT ? + ?",
            "SELECT CAST(CAST(1 AS NUMERIC(100000, 100000)) / CAST(1 AS NUMERIC(1, 0)) AS INT)",
            "SELECT CAST(CAST(1 AS NUMERIC(1, 0)) / CAST(1 AS NUMERIC(1, 50000)) AS INT)",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void IntervalArithmeticEdges()
    {
        // The arithmetic runs the same way; the answer is read as text because the ADO.NET
        // reader hands intervals and timestamps over as CLR TimeSpan/DateTime.
        AssertSameAsReference("expr_edge_interval",
            "SELECT CAST(INTERVAL '1' DAY + INTERVAL '2' DAY AS VARCHAR)",
            "SELECT INTERVAL '1' DAY + 1",
            "SELECT 1 + INTERVAL '1' DAY",
            "SELECT CAST(TIMESTAMP '2020-01-01 00:00:00' + INTERVAL '1' DAY AS VARCHAR)",
            "SELECT CAST(INTERVAL '1' DAY * 2 AS VARCHAR)",
            "SELECT CAST(2 * INTERVAL '1' DAY AS VARCHAR)",
            "SELECT INTERVAL '1' DAY + 'x'",
            "SELECT INTERVAL '1' DAY * INTERVAL '1' DAY",
            "SELECT INTERVAL '1' DAY - TIMESTAMP '2020-01-01 00:00:00'");
    }

    /// <summary>
    /// A column that was never mapped refuses by name: the star keeps its unknown type, and
    /// taking a value needs the resolver a query sets.
    /// </summary>
    [Fact]
    public void AnUnmappedColumnRefuses()
    {
        Database database = new(ConnectionInfo.Memory("expr_edge_" + Guid.NewGuid().ToString("N")));
        SessionLocal session = new(database, "sa");

        ExpressionColumn star = new("*", null, 0);
        Assert.Equal(TypeInfo.TypeUnknown, star.GetTypeInfo());
        Assert.Null(star.GetTableAlias());
        Assert.Throws<TODOException>(() => star.GetValue(null!));

        ExpressionColumn named = new("X", "T", 0);
        Assert.Equal("T", named.GetTableAlias());
        Assert.Throws<TODOException>(() => named.GetValue(session));

        database.Close();
    }
}
