// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — CREATE AGGREGATE, CREATE ALIAS and CREATE CONSTANT,
// one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class CreateRoutineCommandTests
{
    /// <summary>docs/commands.html:1612 — CREATE AGGREGATE [ IF NOT EXISTS ] [schemaName.]aggregateName FOR classNameString</summary>
    [Fact]
    public void CreateAggregate_RegistersTheAggregate() // CMD-1612
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE AGGREGATE SIMPLE_MEDIAN FOR 'com.acme.db.Median'");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_NAME = 'SIMPLE_MEDIAN'"));
    }

    /// <summary>docs/commands.html:1631 — CREATE ALIAS [ IF NOT EXISTS ] [schemaName.]functionAliasName</summary>
    [Fact]
    public void CreateAlias_ForClassMethod_CanBeCalled() // CMD-1631
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE ALIAS MY_SQRT FOR 'java.lang.Math.sqrt'");

        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT MY_SQRT(4)"));
    }

    /// <summary>docs/commands.html:1632 — [ DETERMINISTIC ]</summary>
    [Fact]
    public void CreateAlias_Deterministic_CanBeCalled() // CMD-1632
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE ALIAS MY_ROUND DETERMINISTIC FOR 'java.lang.Math.round(double)'");

        Assert.Equal(3L, connection.QuerySingle<long>("SELECT MY_ROUND(2.6)"));
    }

    /// <summary>docs/commands.html:1633 — { FOR classAndMethodString | AS sourceCodeString }</summary>
    [Fact]
    public void CreateAlias_AsSourceCode_CanBeCalled() // CMD-1633
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE ALIAS ADD1 AS 'int add1(int x) { return x + 1; }'");

        Assert.Equal(2, connection.QuerySingle<int>("SELECT ADD1(1)"));
    }
}

public sealed class CreateConstantCommandTests
{
    /// <summary>docs/commands.html:1654 — CREATE CONSTANT [ IF NOT EXISTS ] [schemaName.]constantName</summary>
    [Fact]
    public void CreateConstant_IfNotExists_RegistersItOnce() // CMD-1654
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE CONSTANT IF NOT EXISTS ONE VALUE 1");
        _ = connection.Execute("CREATE CONSTANT IF NOT EXISTS ONE VALUE 1");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.CONSTANTS WHERE CONSTANT_NAME = 'ONE'"));
    }

    /// <summary>docs/commands.html:1655 — VALUE expression</summary>
    [Fact]
    public void CreateConstant_Value_IsReadableAsAnExpression() // CMD-1655
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE CONSTANT TWO VALUE 1 + 1");

        Assert.Equal(2, connection.QuerySingle<int>("SELECT TWO"));
    }
}
