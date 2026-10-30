// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestAlter.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands, and the proxy form of
// assertThrows becomes the lambda form. getMetaData().getColumns is the
// INFORMATION_SCHEMA.COLUMNS query through the ADO layer; the sequence count of
// testAlterTableDropIdentityColumn is the schema's sequence registry, the same answer
// the reference reads from the session (identity sequences belong to their table and
// never appear in INFORMATION_SCHEMA.SEQUENCES). The reference's main() entry is the
// runner; xUnit runs Test() directly.
using H2Sharp.Api;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test ALTER statements.
/// </summary>
public sealed class TestAlter : TestDb
{
    private DbConnection? conn;

    private DbCommand? stat;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb(GetTestName());
        conn = GetConnection(GetTestName());
        stat = conn.CreateCommand();
        TestAlterTableRenameConstraint();
        TestAlterTableDropColumnWithReferences();
        TestAlterTableDropMultipleColumns();
        TestAlterTableAddColumnIdentity();
        TestAlterTableDropIdentityColumn();
        TestAlterTableAddColumnIfNotExists();
        TestAlterTableAddMultipleColumns();
        TestAlterTableAddColumnBefore();
        TestAlterTableAddColumnAfter();
        TestAlterTableAddMultipleColumnsBefore();
        TestAlterTableAddMultipleColumnsAfter();
        conn.Close();
        DeleteDb(GetTestName());
    }

    private void TestAlterTableDropColumnWithReferences()
    {
        Execute(stat!, "create table parent(id int primary key, b int)");
        Execute(stat!, "create table child(p int primary key)");
        Execute(stat!, "alter table child add foreign key(p) references parent(id)");
        Execute(stat!, "alter table parent drop column id");
        Execute(stat!, "drop table parent");
        Execute(stat!, "drop table child");

        Execute(stat!, "create table test(id int, name varchar(255))");
        Execute(stat!, "alter table test add constraint x check (id > name)");

        // the constraint references multiple columns
        AssertThrows(ErrorCode.ColumnIsReferenced1, stat!,
            "alter table test drop column id");

        Execute(stat!, "drop table test");

        Execute(stat!, "create table test(id int, name varchar(255))");
        Execute(stat!, "alter table test add constraint x unique(id, name)");

        // the constraint references multiple columns
        AssertThrows(ErrorCode.ColumnIsReferenced1, stat!,
            "alter table test drop column id");

        Execute(stat!, "drop table test");

        Execute(stat!, "create table test(id int, name varchar(255))");
        Execute(stat!, "alter table test add constraint x check (id > 1)");
        Execute(stat!, "alter table test drop column id");
        Execute(stat!, "drop table test");

        Execute(stat!, "create table test(id int, name varchar(255))");
        Execute(stat!, "alter table test add constraint x check (name > 'TEST.ID')");
        // previous versions of H2 used sql.indexOf(columnName)
        // to check if the column is referenced
        Execute(stat!, "alter table test drop column id");
        Execute(stat!, "drop table test");

        Execute(stat!, "create table test(id int, name varchar(255))");
        Execute(stat!, "alter table test add constraint x unique(id)");
        Execute(stat!, "alter table test drop column id");
        Execute(stat!, "drop table test");
    }

    private void TestAlterTableDropMultipleColumns()
    {
        Execute(stat!, "create table test(id int, b varchar, c int, d int)");
        Execute(stat!, "alter table test drop column b, c");
        Execute(stat!, "alter table test drop d");
        Execute(stat!, "drop table test");
        // Test-Case: Same as above but using brackets (Oracle style)
        Execute(stat!, "create table test(id int, b varchar, c int, d int)");
        Execute(stat!, "alter table test drop column (b, c)");
        AssertThrows(ErrorCode.ColumnNotFound1, stat!,
            "alter table test drop column b");
        Execute(stat!, "alter table test drop (d)");
        Execute(stat!, "drop table test");
        // Test-Case: Error if dropping all columns
        Execute(stat!, "create table test(id int, name varchar, name2 varchar)");
        AssertThrows(ErrorCode.CannotDropLastColumn, stat!,
            "alter table test drop column id, name, name2");
        Execute(stat!, "drop table test");
    }

    private void TestAlterTableRenameConstraint()
    {
        Execute(stat!, "create table test(id int, name varchar(255))");
        Execute(stat!, "alter table test add constraint x check (id > name)");
        Execute(stat!, "alter table test rename constraint x to x2");
        Execute(stat!, "drop table test");
    }

    private void TestAlterTableDropIdentityColumn()
    {
        Execute(stat!, "create table test(id int auto_increment, name varchar)");
        Execute(stat!, "alter table test drop column id");
        AssertEquals(0L, CountSequences());
        Execute(stat!, "drop table test");

        Execute(stat!, "create table test(id int auto_increment, name varchar)");
        Execute(stat!, "alter table test drop column name");
        AssertEquals(1L, CountSequences());
        Execute(stat!, "drop table test");
    }

    /// <summary>
    /// The reference's session-level sequence collection count, as the
    /// INFORMATION_SCHEMA.SEQUENCES row count.
    /// </summary>
    /// <returns>the number of sequences</returns>
    /// <summary>
    /// The sequence count of testAlterTableDropIdentityColumn, read the way the reference
    /// reads it: from the schema's sequence registry, not from INFORMATION_SCHEMA.
    /// </summary>
    /// <remarks>
    /// The reference test (TestAlter.java) counts
    /// <c>getDatabase().getMainSchema().getAllSequences()</c>. INFORMATION_SCHEMA.SEQUENCES
    /// would always answer 0 here, in both implementations: the reference's own
    /// <c>InformationSchemaTable.sequences()</c> skips every sequence with
    /// <c>belongsToTable</c> set, which identity sequences always have. Verified against
    /// the h2-2.5.259 jar: SELECT from INFORMATION_SCHEMA.SEQUENCES right after
    /// CREATE TABLE ... IDENTITY returns no rows there either.
    /// </remarks>
    /// <returns>the number of sequences</returns>
    private long CountSequences()
    {
        H2Connection connection = (H2Connection)conn!;
        return connection.CurrentDatabase!.GetMainSchema()!.GetAllSequences().Count;
    }

    private void TestAlterTableAddColumnIdentity()
    {
        Execute(stat!, "create table t(x varchar)");
        Execute(stat!, "alter table t add id bigint generated by default as identity(start with 5 increment by 5)"
            + " default on null");
        Execute(stat!, "insert into t values (null, null)");
        Execute(stat!, "insert into t values (null, null)");
        stat!.CommandText = "select id from t order by id";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(5, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(10, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        Execute(stat!, "drop table t");
    }

    private void TestAlterTableAddColumnIfNotExists()
    {
        Execute(stat!, "create table t(x varchar) as select 'x'");
        Execute(stat!, "alter table t add if not exists x int");
        Execute(stat!, "drop table t");
        Execute(stat!, "create table t(x varchar) as select 'x'");
        Execute(stat!, "alter table t add if not exists y int");
        Execute(stat!, "select x, y from t");
        Execute(stat!, "drop table t");
    }

    private void TestAlterTableAddMultipleColumns()
    {
        Execute(stat!, "create table t(x varchar) as select 'x'");
        Execute(stat!, "alter table t add (y int, z varchar)");
        Execute(stat!, "drop table t");
        Execute(stat!, "create table t(x varchar) as select 'x'");
        Execute(stat!, "alter table t add (y int)");
        Execute(stat!, "drop table t");
    }

    // column and field names must be upper-case due to getMetaData sensitivity
    private void TestAlterTableAddMultipleColumnsBefore()
    {
        Execute(stat!, "create table T(X varchar)");
        Execute(stat!, "alter table T add (Y int, Z int) before X");
        AssertColumnOrder(["Y", "Z", "X"]);
        Execute(stat!, "drop table T");
    }

    // column and field names must be upper-case due to getMetaData sensitivity
    private void TestAlterTableAddMultipleColumnsAfter()
    {
        Execute(stat!, "create table T(X varchar)");
        Execute(stat!, "alter table T add (Y int, Z int) after X");
        AssertColumnOrder(["X", "Y", "Z"]);
        Execute(stat!, "drop table T");
    }

    // column and field names must be upper-case due to getMetaData sensitivity
    private void TestAlterTableAddColumnBefore()
    {
        Execute(stat!, "create table T(X varchar)");
        Execute(stat!, "alter table T add Y int before X");
        AssertColumnOrder(["Y", "X"]);
        Execute(stat!, "drop table T");
    }

    // column and field names must be upper-case due to getMetaData sensitivity
    private void TestAlterTableAddColumnAfter()
    {
        Execute(stat!, "create table T(X varchar)");
        Execute(stat!, "alter table T add Y int after X");
        AssertColumnOrder(["X", "Y"]);
        Execute(stat!, "drop table T");
    }

    /// <summary>
    /// The reference's DatabaseMetaData.getColumns walk: the INFORMATION_SCHEMA.COLUMNS
    /// rows of the table, in ordinal order, through the ADO layer.
    /// </summary>
    /// <param name="expected">the expected column names</param>
    private void AssertColumnOrder(string[] expected)
    {
        DbCommand command = conn!.CreateCommand();
        command.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS "
            + "WHERE TABLE_NAME = 'T' ORDER BY ORDINAL_POSITION";
        using (DbDataReader rs = command.ExecuteReader())
        {
            foreach (string name in expected)
            {
                AssertTrue(rs.Read());
                AssertEquals(name, rs.GetString(0));
            }

            AssertFalse(rs.Read());
        }
    }
}
