// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from the updatable half of org.h2.test.db.TestCompatibilityOracle.testVarchar.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// New test, per the discussion decision (see docs/NATIVE_TESTS_DISCUSSION.md): the
// reference updates through a CONCUR_UPDATABLE result set (updateString/updateRow); the
// ADO.NET reader is read-only, so the same scenario ports as updates through a command —
// what an ADO.NET caller writes. Each update form of the reference (string, national
// string, object, by index, by name) becomes the same UPDATE statement, and the NULL
// result of an empty string under MODE=Oracle is asserted after each.
using Xunit;

namespace H2Sharp.Native.Tests.Jdbc;

/// <summary>
/// The update-through-command counterpart of the reference's updatable testVarchar.
/// </summary>
public sealed class TestUpdateThroughCommand : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("oracle");
        using DbConnection conn = GetConnection("oracle;MODE=Oracle");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, V VARCHAR) AS VALUES (1, 'a')");

        using (DbCommand prep = conn.CreateCommand())
        {
            // updateString(2, "")
            prep.CommandText = "UPDATE TEST SET V = ? WHERE ID = ?";
            prep.Parameters.Add(new H2Parameter { Value = "" });
            prep.Parameters.Add(new H2Parameter { Value = 1 });
            _ = prep.ExecuteNonQuery();
            AssertNullValue(stat);
        }

        using (DbCommand prep = conn.CreateCommand())
        {
            // updateNString(2, "")
            prep.CommandText = "UPDATE TEST SET V = ? WHERE ID = ?";
            prep.Parameters.Add(new H2Parameter { Value = "" });
            prep.Parameters.Add(new H2Parameter { Value = 1 });
            _ = prep.ExecuteNonQuery();
            AssertNullValue(stat);
        }

        using (DbCommand prep = conn.CreateCommand())
        {
            // updateObject(2, "")
            prep.CommandText = "UPDATE TEST SET V = ? WHERE ID = ?";
            prep.Parameters.Add(new H2Parameter { Value = "" });
            prep.Parameters.Add(new H2Parameter { Value = 1 });
            _ = prep.ExecuteNonQuery();
            AssertNullValue(stat);
        }

        using (DbCommand prep = conn.CreateCommand())
        {
            // updateObject("V", "")
            prep.CommandText = "UPDATE TEST SET V = '' WHERE ID = 1";
            _ = prep.ExecuteNonQuery();
            AssertNullValue(stat);
        }
    }

    /// <summary>
    /// The reference's assertNull(rs.getString(1)) after each update: the empty string is
    /// NULL under MODE=Oracle.
    /// </summary>
    /// <param name="stat">the statement</param>
    private void AssertNullValue(DbCommand stat)
    {
        stat.CommandText = "SELECT V FROM TEST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertNull(rs.IsDBNull(0) ? null : rs.GetString(0));
            AssertFalse(rs.Read());
        }
    }
}
