// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — WITH command (common table expressions), one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class WithCommandTests
{
    /// <summary>docs/commands.html:1070 — WITH [ RECURSIVE ] { name [( columnName [,...] )] AS ( query ) [,...] }</summary>
    [Fact]
    public void With_Recursive_CanReferenceItself() // CMD-1070
    {
        using IDbConnection connection = TestDatabase.Open();

        int sum = connection.QuerySingle<int>(
            "WITH RECURSIVE CTE(N) AS (SELECT 1 UNION ALL SELECT N + 1 FROM CTE WHERE N < 100) SELECT SUM(N) FROM CTE");

        Assert.Equal(5050, sum);
    }

    /// <summary>docs/commands.html:1071 — query</summary>
    [Fact]
    public void With_SecondCte_CanReadTheFirst() // CMD-1071
    {
        using IDbConnection connection = TestDatabase.Open();

        int value = connection.QuerySingle<int>(
            "WITH CTE1 AS (SELECT 1 AS A), CTE2 AS (SELECT A + 1 AS B FROM CTE1) SELECT B FROM CTE2");

        Assert.Equal(2, value);
    }
}
