// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/grammar.html — GEOMETRY and JSON literals, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Grammar;

public sealed class TypeLiteralTests
{
    /// <summary>docs/grammar.html:1018 — GEOMETRY { bytes | string }</summary>
    [Fact]
    public void GeometryLiteral_AcceptsStringAndBytes() // GRM-1018
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT GEOMETRY 'POINT(1 1)' IS NOT NULL"));
        Assert.True(connection.QuerySingle<bool>(
            "SELECT GEOMETRY X'00000000013ff00000000000003ff0000000000000' IS NOT NULL"));
    }

    /// <summary>docs/grammar.html:1037 — JSON { bytes | string }</summary>
    [Fact]
    public void JsonLiteral_AcceptsStringAndBytes() // GRM-1037
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT JSON '{\"id\": 10}' IS NOT NULL"));
        Assert.True(connection.QuerySingle<bool>("SELECT JSON X'7472' '7565' IS NOT NULL"));
    }
}
