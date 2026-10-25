// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from the portable half of org.h2.test.jdbc.TestResultSet.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the reference's typed-getter tests (testInt, testSmallInt, testBigInt,
// testVarchar, testDecimal, testDoubleFloat), testFindColumn, and the column-schema
// checks are ported against DbDataReader. The scrollable and updatable halves (rows,
// beforeFirst, absolute, setFetchSize, the result-set metadata property set) have no
// ADO.NET shape and stay on the discussion list, as decided. The MySQL-qualified
// column names ("TEST.ID") rely on a provider-surface behavior (qualified GetOrdinal)
// that is not ported yet; plain and case-insensitive names are asserted.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Jdbc;

/// <summary>
/// The typed-getter half of the reference's TestResultSet.
/// </summary>
public sealed class TestResultSetGetters : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestInt();
        TestSmallInt();
        TestBigInt();
        TestVarchar();
        TestDecimal();
        TestDoubleFloat();
        TestFindColumn();
        TestColumnLength();
        DeleteDb("resultSetGetters");
    }

    private void TestInt()
    {
        Trace("Test INT");
        using DbConnection conn = GetConnection("resultSetGetters");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY,\"VALUE\" INT)");
        Execute(stat, "INSERT INTO TEST VALUES(1,-1)");
        Execute(stat, "INSERT INTO TEST VALUES(2,0)");
        Execute(stat, "INSERT INTO TEST VALUES(3,1)");
        Execute(stat, "INSERT INTO TEST VALUES(4," + int.MaxValue + ")");
        Execute(stat, "INSERT INTO TEST VALUES(5," + int.MinValue + ")");
        Execute(stat, "INSERT INTO TEST VALUES(6,NULL)");
        Execute(stat, "INSERT INTO TEST VALUES(7,NULL)");

        stat.CommandText = "SELECT *, NULL AS N FROM TEST ORDER BY ID";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            // MySQL-qualified column names ("TEST.ID") are a provider-surface decision;
            // see the file header. Plain names are asserted below.
            AssertEquals(3, rs.FieldCount);
            AssertEquals("ID", rs.GetName(0));
            AssertEquals("VALUE", rs.GetName(1));
            AssertEquals("N", rs.GetName(2));
            AssertEquals(typeof(int), rs.GetFieldType(0));
            AssertEquals(typeof(int), rs.GetFieldType(1));
            AssertEquals(typeof(string), rs.GetFieldType(2));

            AssertTrue(rs.Read());
            AssertEquals(-1, rs.GetInt32(1));
            AssertFalse(rs.IsDBNull(1));
            AssertEquals(-1, rs.GetInt32(rs.GetOrdinal("VALUE")));
            AssertEquals(-1, rs.GetInt32(rs.GetOrdinal("value")));
            AssertEquals(-1, rs.GetInt32(rs.GetOrdinal("Value")));
            AssertEquals("-1", rs.GetString(rs.GetOrdinal("Value")));
            AssertEquals(-1, (int)rs.GetValue(1));
            AssertEquals(-1, rs.GetFieldValue<int>(1));
            AssertTrue(rs.GetBoolean(1));
            AssertEquals((sbyte)-1, rs.GetByte(1));
            AssertEquals((short)-1, rs.GetInt16(1));
            AssertEquals(-1L, rs.GetInt64(1));
            AssertEquals(-1.0f, rs.GetFloat(1));
            AssertEquals(-1.0, rs.GetDouble(1));
            AssertEquals(1, rs.GetInt32(0));

            AssertTrue(rs.Read());
            AssertEquals(0, rs.GetInt32(1));
            AssertFalse(rs.GetBoolean(1));
            AssertEquals((byte)0, rs.GetByte(1));
            AssertEquals((short)0, rs.GetInt16(1));
            AssertEquals(0L, rs.GetInt64(1));
            AssertEquals(0.0f, rs.GetFloat(1));
            AssertEquals(0.0, rs.GetDouble(1));
            AssertEquals("0", rs.GetString(1));
            AssertEquals(2, rs.GetInt32(0));

            AssertTrue(rs.Read());
            AssertEquals(3, rs.GetInt32(0));
            AssertEquals(1, rs.GetInt32(rs.GetOrdinal("VALUE")));

            AssertTrue(rs.Read());
            AssertEquals(4, rs.GetInt32(0));
            AssertEquals(int.MaxValue, rs.GetInt32(1));

            AssertTrue(rs.Read());
            AssertEquals(5, rs.GetInt32(0));
            AssertEquals(int.MinValue, rs.GetInt32(1));
            AssertEquals("5", rs.GetString(0));

            AssertTrue(rs.Read());
            AssertEquals(6, rs.GetInt32(0));
            AssertEquals(0, rs.GetInt32(1));
            AssertTrue(rs.IsDBNull(1));
            AssertEquals("6", rs.GetString(0));
            AssertTrue(rs.IsDBNull(1));
            AssertNull(rs.GetValue(1) is DBNull ? null : rs.GetValue(1));
            AssertTrue(rs.IsDBNull(1));

            AssertFalse(rs.Read());
        }

        Execute(stat, "DROP TABLE TEST");
    }

    private void TestSmallInt()
    {
        Trace("Test SMALLINT");
        using DbConnection conn = GetConnection("resultSetGetters");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY,\"VALUE\" SMALLINT)");
        Execute(stat, "INSERT INTO TEST VALUES(1,-1)");
        Execute(stat, "INSERT INTO TEST VALUES(2,0)");
        Execute(stat, "INSERT INTO TEST VALUES(3,1)");
        Execute(stat, "INSERT INTO TEST VALUES(4," + short.MaxValue + ")");
        Execute(stat, "INSERT INTO TEST VALUES(5," + short.MinValue + ")");
        Execute(stat, "INSERT INTO TEST VALUES(6,NULL)");

        stat.CommandText = "SELECT *, NULL AS N FROM TEST ORDER BY ID";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertEquals(3, rs.FieldCount);
            AssertEquals(typeof(int), rs.GetFieldType(0));
            AssertEquals(typeof(int), rs.GetFieldType(1));
            AssertEquals(typeof(string), rs.GetFieldType(2));

            AssertTrue(rs.Read());
            AssertEquals((short)-1, rs.GetInt16(1));
            AssertEquals((short)-1, rs.GetInt16(rs.GetOrdinal("VALUE")));
            AssertEquals((short)-1, rs.GetInt16(rs.GetOrdinal("value")));
            AssertEquals((short)-1, rs.GetInt16(rs.GetOrdinal("Value")));
            AssertEquals("-1", rs.GetString(rs.GetOrdinal("Value")));
            AssertEquals(-1, (int)rs.GetValue(1));
            AssertEquals((short)-1, rs.GetFieldValue<short>(1));
            AssertTrue(rs.GetBoolean(1));
            AssertEquals((sbyte)-1, rs.GetByte(1));
            AssertEquals(-1, rs.GetInt32(1));
            AssertEquals(-1L, rs.GetInt64(1));
            AssertEquals(-1.0f, rs.GetFloat(1));
            AssertEquals(-1.0, rs.GetDouble(1));
            AssertEquals((short)1, rs.GetInt16(0));

            AssertTrue(rs.Read());
            AssertEquals((short)0, rs.GetInt16(1));
            AssertEquals((short)1, rs.GetInt16(rs.GetOrdinal("VALUE")));

            AssertTrue(rs.Read());
            AssertEquals(short.MaxValue, rs.GetInt16(rs.GetOrdinal("ID")));

            AssertTrue(rs.Read());
            AssertEquals(short.MinValue, rs.GetInt16(1));

            AssertTrue(rs.Read());
            AssertEquals(0, rs.GetInt16(1));
            AssertTrue(rs.IsDBNull(1));

            AssertFalse(rs.Read());
        }

        Execute(stat, "DROP TABLE TEST");
    }

    private void TestBigInt()
    {
        Trace("Test BIGINT");
        using DbConnection conn = GetConnection("resultSetGetters");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY,\"VALUE\" BIGINT)");
        Execute(stat, "INSERT INTO TEST VALUES(1,-1)");
        Execute(stat, "INSERT INTO TEST VALUES(2,0)");
        Execute(stat, "INSERT INTO TEST VALUES(3,1)");
        Execute(stat, "INSERT INTO TEST VALUES(4," + long.MaxValue + ")");
        Execute(stat, "INSERT INTO TEST VALUES(5," + long.MinValue + ")");
        Execute(stat, "INSERT INTO TEST VALUES(6,NULL)");

        stat.CommandText = "SELECT *, NULL AS N FROM TEST ORDER BY ID";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertEquals(typeof(long), rs.GetFieldType(1));
            AssertEquals(typeof(string), rs.GetFieldType(2));

            AssertTrue(rs.Read());
            AssertEquals(-1L, rs.GetInt64(1));
            AssertEquals(-1L, rs.GetInt64(rs.GetOrdinal("VALUE")));
            AssertEquals(-1L, rs.GetInt64(rs.GetOrdinal("value")));
            AssertEquals(-1L, rs.GetInt64(rs.GetOrdinal("Value")));
            AssertEquals("-1", rs.GetString(rs.GetOrdinal("Value")));
            AssertEquals(-1L, (long)rs.GetValue(1));
            AssertEquals(-1L, rs.GetFieldValue<long>(1));
            AssertTrue(rs.GetBoolean(1));
            AssertEquals((sbyte)-1, rs.GetByte(1));
            AssertEquals((short)-1, rs.GetInt16(1));
            AssertEquals(-1, rs.GetInt32(1));
            AssertEquals(-1.0f, rs.GetFloat(1));
            AssertEquals(-1.0, rs.GetDouble(1));
            AssertEquals(1L, rs.GetInt64(0));

            AssertTrue(rs.Read());
            AssertEquals(0L, rs.GetInt64(1));

            AssertTrue(rs.Read());
            AssertEquals(1L, rs.GetInt64(1));

            AssertTrue(rs.Read());
            AssertEquals(long.MaxValue, rs.GetInt64(1));

            AssertTrue(rs.Read());
            AssertEquals(long.MinValue, rs.GetInt64(1));
            AssertEquals("5", rs.GetString(0));

            AssertTrue(rs.Read());
            AssertEquals(0L, rs.GetInt64(1));
            AssertTrue(rs.IsDBNull(1));

            AssertFalse(rs.Read());
        }

        Execute(stat, "DROP TABLE TEST");
    }

    private void TestVarchar()
    {
        Trace("Test VARCHAR");
        using DbConnection conn = GetConnection("resultSetGetters");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY,\"VALUE\" VARCHAR(255))");
        Execute(stat, "INSERT INTO TEST VALUES(1,'')");
        Execute(stat, "INSERT INTO TEST VALUES(2,' ')");
        Execute(stat, "INSERT INTO TEST VALUES(3,'  ')");
        Execute(stat, "INSERT INTO TEST VALUES(4,NULL)");
        Execute(stat, "INSERT INTO TEST VALUES(5,'Hi')");
        Execute(stat, "INSERT INTO TEST VALUES(6,' Hi ')");
        Execute(stat, "INSERT INTO TEST VALUES(7,'Joe''s')");
        Execute(stat, "INSERT INTO TEST VALUES(8,'{escape}')");
        Execute(stat, "INSERT INTO TEST VALUES(9,'\\n')");
        Execute(stat, "INSERT INTO TEST VALUES(10,'\\''')");
        Execute(stat, "INSERT INTO TEST VALUES(11,'\\%')");

        stat.CommandText = "SELECT * FROM TEST ORDER BY ID";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertEquals(2, rs.FieldCount);
            AssertEquals(typeof(string), rs.GetFieldType(1));

            AssertTrue(rs.Read());
            AssertEquals("", rs.GetString(1));
            AssertFalse(rs.IsDBNull(1));
            AssertEquals(1, rs.GetInt32(0));

            AssertTrue(rs.Read());
            AssertEquals(" ", rs.GetString(1));

            AssertTrue(rs.Read());
            AssertEquals("  ", rs.GetString(1));

            AssertTrue(rs.Read());
            AssertTrue(rs.IsDBNull(1));
            AssertEquals(4, rs.GetInt32(0));

            AssertTrue(rs.Read());
            AssertEquals(5, rs.GetInt32(0));
            AssertEquals("Hi", rs.GetString(1));
            AssertEquals("Hi", (string)rs.GetValue(1));

            AssertTrue(rs.Read());
            AssertEquals(" Hi ", rs.GetString(1));

            AssertTrue(rs.Read());
            AssertEquals("Joe's", rs.GetString(1));

            AssertTrue(rs.Read());
            AssertEquals("{escape}", rs.GetString(1));

            AssertTrue(rs.Read());
            AssertEquals("\\n", rs.GetString(1));

            AssertTrue(rs.Read());
            AssertEquals("\\'", rs.GetString(1));

            AssertTrue(rs.Read());
            AssertEquals("\\%", rs.GetString(1));

            AssertFalse(rs.Read());
        }

        Execute(stat, "DROP TABLE TEST");
    }

    private void TestDecimal()
    {
        Trace("Test DECIMAL");
        using DbConnection conn = GetConnection("resultSetGetters");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY,\"VALUE\" DECIMAL(10,2))");
        Execute(stat, "INSERT INTO TEST VALUES(1,-1.5)");
        Execute(stat, "INSERT INTO TEST VALUES(2,0)");
        Execute(stat, "INSERT INTO TEST VALUES(3,1.5)");
        Execute(stat, "INSERT INTO TEST VALUES(4,99999999.99)");
        Execute(stat, "INSERT INTO TEST VALUES(5,-99999999.99)");
        Execute(stat, "INSERT INTO TEST VALUES(6,NULL)");

        stat.CommandText = "SELECT * FROM TEST ORDER BY ID";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertEquals(typeof(decimal), rs.GetFieldType(1));

            AssertTrue(rs.Read());
            AssertEquals(-1.5m, rs.GetDecimal(1));
            AssertEquals("-1.50", rs.GetString(1));
            AssertEquals(-1.5m, (decimal)rs.GetValue(1));
            AssertEquals(-1.5m, rs.GetFieldValue<decimal>(1));

            AssertTrue(rs.Read());
            AssertEquals(0m, rs.GetDecimal(1));

            AssertTrue(rs.Read());
            AssertEquals(1.5m, rs.GetDecimal(1));

            AssertTrue(rs.Read());
            AssertEquals(99999999.99m, rs.GetDecimal(1));

            AssertTrue(rs.Read());
            AssertEquals(-99999999.99m, rs.GetDecimal(1));

            AssertTrue(rs.Read());
            AssertEquals(0m, rs.GetDecimal(1));
            AssertTrue(rs.IsDBNull(1));

            AssertFalse(rs.Read());
        }

        Execute(stat, "DROP TABLE TEST");
    }

    private void TestDoubleFloat()
    {
        Trace("Test DOUBLE/FLOAT");
        using DbConnection conn = GetConnection("resultSetGetters");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY,\"VALUE\" DOUBLE)");
        Execute(stat, "INSERT INTO TEST VALUES(1,-1.5)");
        Execute(stat, "INSERT INTO TEST VALUES(2,0)");
        Execute(stat, "INSERT INTO TEST VALUES(3,1.5)");
        Execute(stat, "INSERT INTO TEST VALUES(4,1.0e20)");
        Execute(stat, "INSERT INTO TEST VALUES(5,1.0e-20)");
        Execute(stat, "INSERT INTO TEST VALUES(6,NULL)");

        stat.CommandText = "SELECT * FROM TEST ORDER BY ID";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertEquals(typeof(double), rs.GetFieldType(1));

            AssertTrue(rs.Read());
            AssertEquals(-1.5, rs.GetDouble(1));
            AssertEquals("-1.5", rs.GetString(1));
            AssertEquals(-1.5, (double)rs.GetValue(1));
            AssertEquals(-1.5, rs.GetFieldValue<double>(1));

            AssertTrue(rs.Read());
            AssertEquals(0.0, rs.GetDouble(1));

            AssertTrue(rs.Read());
            AssertEquals(1.5, rs.GetDouble(1));

            AssertTrue(rs.Read());
            AssertEquals(1.0e20, rs.GetDouble(1));

            AssertTrue(rs.Read());
            AssertEquals(1.0e-20, rs.GetDouble(1));

            AssertTrue(rs.Read());
            AssertEquals(0.0, rs.GetDouble(1));
            AssertTrue(rs.IsDBNull(1));

            AssertFalse(rs.Read());
        }

        Execute(stat, "DROP TABLE TEST");
    }

    private void TestFindColumn()
    {
        Trace("testFindColumn");
        using DbConnection conn = GetConnection("resultSetGetters");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT, NAME VARCHAR)");
        stat.CommandText = "SELECT * FROM TEST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertEquals(1, rs.GetOrdinal("ID"));
            AssertEquals(2, rs.GetOrdinal("NAME"));
            AssertEquals(1, rs.GetOrdinal("id"));
            AssertEquals(2, rs.GetOrdinal("name"));
            AssertEquals(1, rs.GetOrdinal("Id"));
            AssertEquals(2, rs.GetOrdinal("Name"));
            // The table-qualified forms ("TEST.ID") are a provider-surface decision; see
            // the file header.
        }

        Execute(stat, "DROP TABLE TEST");

        Execute(stat, "CREATE TABLE TEST(ID INT, NAME VARCHAR, DATA VARCHAR)");
        stat.CommandText = "SELECT * FROM TEST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertEquals(3, rs.GetOrdinal("DATA"));
            AssertEquals(3, rs.GetOrdinal("data"));
            AssertEquals(3, rs.GetOrdinal("Data"));
        }

        Execute(stat, "DROP TABLE TEST");
    }

    private void TestColumnLength()
    {
        Trace("testColumnDisplayLength");
        using DbConnection conn = GetConnection("resultSetGetters");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE one (ID INT, NAME VARCHAR(255))");
        stat.CommandText = "select * from one";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertEquals("ID", rs.GetName(0));
            AssertEquals("NAME", rs.GetName(1));
            AssertEquals(255, rs.GetColumnSchema()[1].ColumnSize.GetValueOrDefault());
        }

        Execute(stat, "DROP TABLE one");

        stat.CommandText = "select 1, 'Hello' union select 2, 'Hello World!'";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertEquals(12, rs.GetColumnSchema()[1].ColumnSize.GetValueOrDefault());
        }

        stat.CommandText = "explain select * from dual";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertEquals(H2Sharp.Engine.Constants.MaxStringLength,
                rs.GetColumnSchema()[0].ColumnSize.GetValueOrDefault());
        }
    }
}
