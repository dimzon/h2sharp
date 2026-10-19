// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/datatypes.html — binary data types, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Datatypes;

public sealed class BinaryTypeTests
{
    /// <summary>docs/datatypes.html:349 — BINARY [ ( lengthInt ) ]</summary>
    [Fact]
    public void Binary_WithAndWithoutLength() // TYP-0349
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT CAST(X'41' AS BINARY) = X'41'"));
        Assert.True(connection.QuerySingle<bool>("SELECT CAST(X'41' AS BINARY(2)) IS NOT NULL"));
    }

    /// <summary>docs/datatypes.html:367 — { BINARY VARYING | VARBINARY }</summary>
    [Fact]
    public void BinaryVarying_SupportsBothSpellings() // TYP-0367
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT CAST(X'41' AS BINARY VARYING) = X'41'"));
        Assert.True(connection.QuerySingle<bool>("SELECT CAST(X'41' AS VARBINARY) = X'41'"));
    }

    /// <summary>docs/datatypes.html:368 — [ ( lengthInt ) ]</summary>
    [Fact]
    public void BinaryVarying_WithLength() // TYP-0368
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT CAST(X'41' AS VARBINARY(5)) = X'41'"));
    }

    /// <summary>docs/datatypes.html:387 — { BINARY LARGE OBJECT | BLOB }</summary>
    [Fact]
    public void BinaryLargeObject_SupportsBothSpellings() // TYP-0387
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT CAST(X'41' AS BINARY LARGE OBJECT) = X'41'"));
        Assert.True(connection.QuerySingle<bool>("SELECT CAST(X'41' AS BLOB) = X'41'"));
    }

    /// <summary>docs/datatypes.html:388 — [ ( lengthLong [K|M|G|T|P]) ]</summary>
    [Fact]
    public void BinaryLargeObject_WithScaledLength() // TYP-0388
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT CAST(X'41' AS BLOB(1 K)) = X'41'"));
    }
}
