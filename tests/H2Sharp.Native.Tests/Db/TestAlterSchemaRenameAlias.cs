// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from the alias part of org.h2.test.db.TestAlterSchemaRename.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// The alias part of the reference test, split out of TestAlterSchemaRename so the
// schema-rename regression stays green on its own.
//
// Blocked by the port (BlockedByPort): CREATE ALIAS with a $$ Java source body is not
// ported, and aliases are out of scope by design. The reference's Java source is kept
// verbatim below, exactly as the reference writes it. The trait keeps this in the suite
// as the defect's checklist entry.
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test that aliases in a schema move with ALTER SCHEMA RENAME.
/// </summary>
public sealed class TestAlterSchemaRenameAlias : TestDb
{
    private DbConnection? conn;

    private DbCommand? stat;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        DeleteDb(GetTestName());
        conn = GetConnection(GetTestName());
        stat = conn.CreateCommand();
        TestAlias();
        conn.Close();
        DeleteDb(GetTestName());
    }

    /// <summary>
    /// Check that aliases in the schema got moved.
    /// </summary>
    private void TestAlias()
    {
        Execute(stat!, "create schema s1");
        Execute(stat!, "CREATE ALIAS S1.REVERSE AS $$ "
            + "String reverse(String s) {"
            + "   return new StringBuilder(s).reverse().toString();"
            + "} $$;");
        Execute(stat!, "alter schema s1 rename to s2");
        stat!.CommandText = "CALL S2.REVERSE('1234')";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("4321", rs.GetString(0));
        }

        if (!Config.Memory)
        {
            conn!.Close();
            conn = GetConnection(GetTestName());
            stat = conn.CreateCommand();
            stat.CommandText = "CALL S2.REVERSE('1234')";
            using DbDataReader rs = stat.ExecuteReader();
            while (rs.Read())
            {
                // just loop
            }
        }

        Execute(stat!, "drop schema s2 cascade");
    }
}
