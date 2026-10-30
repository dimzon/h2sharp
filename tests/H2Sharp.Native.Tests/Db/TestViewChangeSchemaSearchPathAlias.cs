// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from the function-alias part of org.h2.test.db.TestView.testChangeSchemaSearchPath.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// The change-schema-search-path scenario with a function alias, split out of TestView
// so the rest of the suite stays green on its own.
//
// Blocked by the port (BlockedByPort): CREATE ALIAS is not ported, and aliases are
// out of scope by design. The trait keeps this in the suite as the defect's
// checklist entry.
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test a view over a function alias under SCHEMA_SEARCH_PATH.
/// </summary>
public sealed class TestViewChangeSchemaSearchPathAlias : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        DeleteDb("view");
        using DbConnection conn = GetConnection("view");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE ALIAS X AS $$ int x() { return 1; } $$;");
        Execute(stat, "CREATE SCHEMA S");
        Execute(stat, "CREATE VIEW S.TEST AS SELECT X() FROM DUAL");
        Execute(stat, "SET SCHEMA=S");
        Execute(stat, "SET SCHEMA_SEARCH_PATH=S");
        Execute(stat, "SELECT * FROM TEST");
    }
}
