// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestCollation.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands, and the proxy form of
// assertThrows becomes the lambda form. The reference's main() entry is the runner;
// xUnit runs Test() directly.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Test the ICU4J collator.
/// </summary>
public sealed class TestCollation : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("collation");
        using DbConnection conn = GetConnection("collation");
        DbCommand stat = conn.CreateCommand();
        AssertThrows(ErrorCode.InvalidValue2, stat, "set collation xyz");
        Execute(stat, "set collation en");
        Execute(stat, "set collation default_en");
        AssertThrows(ErrorCode.ClassNotFound1, stat, "set collation icu4j_en");

        Execute(stat, "set collation ge");
        Execute(stat, "create table test(id int)");
        // the same as the current - ok
        Execute(stat, "set collation ge");
        // not allowed to change now
        AssertThrows(ErrorCode.CollationChangeWithDataTable1, stat, "set collation en");
    }
}
