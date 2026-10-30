// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from the function-alias part of org.h2.test.db.TestCases.testCheckConstraintWithFunction.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// The check-constraint-with-function scenario, split out of TestCases so the rest of
// the suite stays green on its own.
//
// Blocked by the port (BlockedByPort): CREATE ALIAS is not ported, and aliases are
// out of scope by design. The reference's Java source is kept verbatim below, exactly
// as the reference writes it. The trait keeps this in the suite as the defect's
// checklist entry.
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test a check constraint that calls a function alias.
/// </summary>
public sealed class TestCheckConstraintWithFunctionAlias : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        DeleteDb("cases");
        DbConnection conn = GetConnection("cases");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create alias is_email as "
            + "'boolean isEmail(String x) { return x != null && x.indexOf(''@'') > 0; }'");
        Execute(stat, "create domain email as varchar check is_email(value)");
        Execute(stat, "create table test(e email)");
        conn.Close();
        conn = GetConnection("cases");
        conn.Close();
    }
}
