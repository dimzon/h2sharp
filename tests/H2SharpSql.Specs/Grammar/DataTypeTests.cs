// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/grammar.html — Data Type, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Grammar;

public sealed class DataTypeTests
{
    /// <summary>docs/grammar.html:2725 — predefinedType | arrayType | rowType</summary>
    [Fact]
    public void DataType_ArrayAndRowTypes_AreUsable() // GRM-2725
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT, A INT ARRAY, B ROW(X INT, Y VARCHAR))");

        _ = connection.Execute("INSERT INTO TEST(ID, A) VALUES (1, ARRAY[1, 2, 3])");

        Assert.Equal(3, connection.QuerySingle<int>("SELECT CARDINALITY(A) FROM TEST WHERE ID = 1"));
    }
}
