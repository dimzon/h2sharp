// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from the alias part of org.h2.test.db.TestCases.testDependencies.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// The alias part of the dependency scenario, split out of TestCases so the
// dependency regression stays green on its own.
//
// Blocked by the port (BlockedByPort): CREATE ALIAS is not ported, and aliases are
// out of scope by design. The reference's Java source is kept verbatim below, exactly
// as the reference writes it. The trait keeps this in the suite as the defect's
// checklist entry.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test that dropping an alias a check constraint depends on is refused.
/// </summary>
public sealed class TestDependenciesAlias : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        DeleteDb("cases");
        using DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();

        // ensure the dependency is detected
        Execute(stat, "create alias is_positive as "
            + "'boolean isPositive(int x) { return x > 0; }'");
        Execute(stat, "create table a(a integer, constraint test check is_positive(a))");
        AssertThrows(ErrorCode.CannotDrop2, stat, "drop alias is_positive");
        Execute(stat, "drop table a");
        Execute(stat, "drop alias is_positive");
    }
}
