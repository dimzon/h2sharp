// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from the alias part of org.h2.test.db.TestClearCacheAfterDdl.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// The alias part of the reference test, split out of TestClearCacheAfterDdl so the
// query-cache regression stays green on its own. The database name carries a run-unique
// GUID suffix for the same reason as there: the xUnit runner does not wipe the data
// directory between runs.
//
// Blocked by the port (BlockedByPort): CREATE ALIAS is not ported, and aliases are
// out of scope by design. The reference's Java class name is kept verbatim below,
// exactly as the reference writes it. The trait keeps this in the suite as the
// defect's checklist entry.
using System;
using Xunit;
using static H2Sharp.Api.ErrorCode;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test that dropping an alias clears it from the query cache.
/// </summary>
public sealed class TestClearCacheAfterDdlAlias : TestDb
{
    private readonly string runId = Guid.NewGuid().ToString("N");

    /// <summary>
    /// The identity function the reference's alias is created for.
    /// </summary>
    public static string StaticIdentityFunction(string str)
    {
        return str;
    }

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        TestAliasDrop();
    }

    private string Db(string name) => name + "_" + runId;

    private void ExpectErrorAfterDdl(DbConnection connection, string sqlToCache, string ddl,
        int errorCode)
    {
        DbCommand statement = connection.CreateCommand();
        Execute(statement, sqlToCache);
        Execute(statement, ddl);
        AssertThrows(errorCode, statement, sqlToCache);
    }

    private void TestAliasDrop()
    {
        using DbConnection connection = GetConnection(Db("clearCacheAfterDdl_alias"));
        DbCommand statement = connection.CreateCommand();
        Execute(statement, "CREATE ALIAS F1 FOR \"org.h2.test.db.TestClearCacheAfterDdl.staticIdentityFunction\"");
        ExpectErrorAfterDdl(connection, "SELECT F1('something')", "DROP ALIAS F1", FunctionNotFound1);
    }
}
