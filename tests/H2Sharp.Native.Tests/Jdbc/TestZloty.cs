// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.jdbc.TestZloty.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; setBytes is a parameter value.
//
// Modified (deferred, see MIGRATION_STATUS.md): the reference's testZloty feeds the
// engine BigDecimal subclasses (one with an inverted compareTo, one with a foreign
// toString) and expects INVALID_CLASS_2, because H2 serializes a foreign
// java.math.BigDecimal class as JAVA_OBJECT and refuses it as a DECIMAL parameter. An
// ADO.NET parameter carries a value, not a class, so the question the test asks has no
// ADO.NET shape; the part is skipped and put on the discussion list. The byte-array
// half (testModifyBytes) is ported below.
using Xunit;

namespace H2Sharp.Native.Tests.Jdbc;

/// <summary>
/// Tests a custom BigDecimal implementation, as well
/// as direct modification of a byte in a byte array.
/// </summary>
public sealed class TestZloty : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        // testZloty: deferred, see the header and MIGRATION_STATUS.md.
        TestModifyBytes();
        DeleteDb("zloty");
    }

    private void TestModifyBytes()
    {
        DeleteDb("zloty");
        using DbConnection conn = GetConnection("zloty");
        DbCommand create = conn.CreateCommand();
        create.CommandText = "CREATE TABLE TEST(ID INT, DATA BINARY)";
        _ = create.ExecuteNonQuery();

        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "INSERT INTO TEST VALUES(?, ?)";
        byte[] shared = [0];
        prep.Parameters.Add(new H2Parameter { Value = 0 });
        prep.Parameters.Add(new H2Parameter { Value = shared });
        _ = prep.ExecuteNonQuery();
        shared[0] = 1;
        prep.Parameters[0].Value = 1;
        prep.Parameters[1].Value = shared;
        _ = prep.ExecuteNonQuery();

        DbCommand select = conn.CreateCommand();
        select.CommandText = "SELECT * FROM TEST ORDER BY ID";
        using DbDataReader rs = select.ExecuteReader();
        AssertTrue(rs.Read());
        AssertEquals(0, rs.GetInt32(0));
        AssertEquals(0, ((byte[])rs.GetValue(1))[0]);
        AssertTrue(rs.Read());
        AssertEquals(1, rs.GetInt32(0));
        AssertEquals(1, ((byte[])rs.GetValue(1))[0]);
        ((byte[])rs.GetValue(1))[0] = 2;
        AssertEquals(1, ((byte[])rs.GetValue(1))[0]);
        AssertFalse(rs.Read());
    }
}
