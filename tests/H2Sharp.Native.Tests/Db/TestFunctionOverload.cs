// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestFunctionOverload.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands, and the proxy form of
// assertThrows becomes the lambda form. The alias names stay the reference's Java class
// name, exactly as the reference writes them.
//
// Modified (port gaps, named): the meta.getProcedures checks read the
// DatabaseMetaData procedure list, which the provider does not have yet; those
// assertions are skipped until the metadata surface is ported. The reference's main()
// entry is the runner; xUnit runs Test() directly.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests for overloaded user defined functions.
/// </summary>
public sealed class TestFunctionOverload : TestDb
{
    private const string Me = "org.h2.test.db.TestFunctionOverload";

    private DbConnection? conn;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("functionOverload");
        conn = GetConnection("functionOverload");
        TestControl();
        TestOverload();
        TestOverloadNamedArgs();
        TestOverloadWithConnection();
        TestOverloadError();
        conn.Close();
        DeleteDb("functionOverload");
    }

    private void TestOverloadError()
    {
        DbCommand stat = conn!.CreateCommand();
        AssertThrows(ErrorCode.MethodsMustHaveDifferentParameterCounts2, stat,
            "create alias overloadError for '" + Me + ".overloadError'");
    }

    private void TestControl()
    {
        DbCommand stat = conn!.CreateCommand();
        Execute(stat, "create alias overload0 for '" + Me + ".overload0'");
        stat.CommandText = "select overload0() from dual";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("0 args", 0, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        // meta.getProcedures(null, null, "OVERLOAD0"): the provider has no metadata
        // surface yet (see the header).
    }

    private void TestOverload()
    {
        DbCommand stat = conn!.CreateCommand();
        Execute(stat, "create alias overload1or2 for '" + Me + ".overload1or2'");
        stat.CommandText = "select overload1or2(1) from dual";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("1 arg", 1, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        stat.CommandText = "select overload1or2(1, 2) from dual";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("2 args", 3, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        // meta.getProcedures(null, null, "OVERLOAD1OR2") and the SPECIFIC_NAME rows:
        // the provider has no metadata surface yet (see the header).
    }

    private void TestOverloadNamedArgs()
    {
        DbCommand stat = conn!.CreateCommand();

        Execute(stat, "create alias overload1or2Named for '" + Me + ".overload1or2(int)'");

        stat.CommandText = "select overload1or2Named(1) from dual";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue("First Row", rs.Read());
            AssertEquals("1 arg", 1, rs.GetInt32(0));
            AssertFalse("Second Row", rs.Read());
        }

        AssertThrows(ErrorCode.MethodNotFound1, stat,
            "select overload1or2Named(1, 2) from dual");
    }

    private void TestOverloadWithConnection()
    {
        DbCommand stat = conn!.CreateCommand();

        Execute(stat, "create alias overload1or2WithConn for '" + Me + ".overload1or2WithConn'");

        stat.CommandText = "select overload1or2WithConn(1) from dual";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("1 arg", 1, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        stat.CommandText = "select overload1or2WithConn(1, 2) from dual";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("2 args", 3, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }
    }

    /// <summary>
    /// This method is called via reflection from the database.
    /// </summary>
    /// <returns>0</returns>
    public static int Overload0()
    {
        return 0;
    }

    /// <summary>
    /// This method is called via reflection from the database.
    /// </summary>
    /// <param name="one">the value</param>
    /// <returns>the value</returns>
    public static int Overload1Or2(int one)
    {
        return one;
    }

    /// <summary>
    /// This method is called via reflection from the database.
    /// </summary>
    /// <param name="one">the first value</param>
    /// <param name="two">the second value</param>
    /// <returns>the sum of both</returns>
    public static int Overload1Or2(int one, int two)
    {
        return one + two;
    }

    /// <summary>
    /// This method is called via reflection from the database.
    /// </summary>
    /// <param name="one">the first value</param>
    /// <param name="two">the second value</param>
    /// <returns>the sum of both</returns>
    public static int OverloadError(int one, int two)
    {
        return one + two;
    }

    /// <summary>
    /// This method is called via reflection from the database.
    /// </summary>
    /// <param name="one">the first value</param>
    /// <param name="two">the second value</param>
    /// <returns>the sum of both</returns>
    public static int OverloadError(double one, double two)
    {
        return (int)(one + two);
    }
}
