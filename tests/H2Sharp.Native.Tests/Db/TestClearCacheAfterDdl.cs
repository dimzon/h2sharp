// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestClearCacheAfterDdl.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands. The alias of testAliasDrop is
// created for the reference's Java class name, exactly as the reference writes it. The
// reference's main() entry is the runner; xUnit runs Test() directly.
using static H2Sharp.Api.ErrorCode;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests for clearing query ache after DDL execution: After executing DDL that
/// invalidates previously cached queries, re-executing the same SQL should fail.
/// When the referenced object no longer exists or was renamed. This asserts that
/// cached prepared statements were cleared.
/// </summary>
public sealed class TestClearCacheAfterDdl : TestDb
{
    /// <summary>
    /// The identity function the reference's alias is created for.
    /// </summary>
    public static string StaticIdentityFunction(string str)
    {
        return str;
    }

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestTableDrop();
        TestTableRename();
        TestColumnDrop();
        TestColumnRename();
        TestViewDrop();
        TestSynonymDrop();
        TestSequenceDrop();
        TestSchemaRenameQualified();
        TestSchemaDropCascade();
        TestAliasDrop();
    }

    private void ExpectErrorAfterDdl(DbConnection connection, string sqlToCache, string ddl,
        int errorCode)
    {
        DbCommand statement = connection.CreateCommand();
        Execute(statement, sqlToCache);
        Execute(statement, ddl);
        AssertThrows(errorCode, statement, sqlToCache);
    }

    private void TestTableDrop()
    {
        using DbConnection connection = GetConnection("clearCacheAfterDdl_tableDrop");
        DbCommand statement = connection.CreateCommand();
        Execute(statement, "CREATE TABLE T_DROP(ID INT PRIMARY KEY)");
        ExpectErrorAfterDdl(connection, "SELECT * FROM T_DROP", "DROP TABLE T_DROP",
            TableOrViewNotFoundDatabaseEmpty1);
    }

    private void TestTableRename()
    {
        using DbConnection connection = GetConnection("clearCacheAfterDdl_tableRename");
        DbCommand statement = connection.CreateCommand();
        Execute(statement, "CREATE TABLE T_RENAME(ID INT PRIMARY KEY)");
        ExpectErrorAfterDdl(connection, "SELECT * FROM T_RENAME", "ALTER TABLE T_RENAME RENAME TO T_RENAMED",
            TableOrViewNotFound1);
    }

    private void TestColumnDrop()
    {
        using DbConnection connection = GetConnection("clearCacheAfterDdl_colDrop");
        DbCommand statement = connection.CreateCommand();
        Execute(statement, "CREATE TABLE T_COL_DROP(A INT, B INT, PRIMARY KEY(A))");
        ExpectErrorAfterDdl(connection, "SELECT B FROM T_COL_DROP", "ALTER TABLE T_COL_DROP DROP COLUMN B",
            ColumnNotFound1);
    }

    private void TestColumnRename()
    {
        using DbConnection connection = GetConnection("clearCacheAfterDdl_colRename");
        DbCommand statement = connection.CreateCommand();
        Execute(statement, "CREATE TABLE T_COL_RENAME(A INT, B INT, PRIMARY KEY(A))");
        ExpectErrorAfterDdl(connection, "SELECT B FROM T_COL_RENAME",
            "ALTER TABLE T_COL_RENAME ALTER COLUMN B RENAME TO C", ColumnNotFound1);
    }

    private void TestViewDrop()
    {
        using DbConnection connection = GetConnection("clearCacheAfterDdl_viewDrop");
        DbCommand statement = connection.CreateCommand();
        Execute(statement, "CREATE TABLE V_BACK(ID INT PRIMARY KEY)");
        Execute(statement, "CREATE VIEW V1 AS SELECT * FROM V_BACK");
        ExpectErrorAfterDdl(connection, "SELECT * FROM V1", "DROP VIEW V1", TableOrViewNotFound1);
    }

    private void TestSynonymDrop()
    {
        using DbConnection connection = GetConnection("clearCacheAfterDdl_synonym");
        DbCommand statement = connection.CreateCommand();
        Execute(statement, "CREATE TABLE BACKINGTABLE(ID INT PRIMARY KEY)");
        Execute(statement, "CREATE OR REPLACE SYNONYM TESTSYNONYM FOR BACKINGTABLE");
        ExpectErrorAfterDdl(connection, "SELECT * FROM TESTSYNONYM", "DROP SYNONYM TESTSYNONYM",
            TableOrViewNotFound1);
    }

    private void TestSequenceDrop()
    {
        using DbConnection connection = GetConnection("clearCacheAfterDdl_sequence");
        DbCommand statement = connection.CreateCommand();
        Execute(statement, "CREATE SEQUENCE SEQ1");
        ExpectErrorAfterDdl(connection, "SELECT NEXT VALUE FOR SEQ1", "DROP SEQUENCE SEQ1",
            SequenceNotFound1);
    }

    private void TestSchemaRenameQualified()
    {
        using DbConnection connection = GetConnection("clearCacheAfterDdl_schemaRename");
        DbCommand statement = connection.CreateCommand();
        Execute(statement, "CREATE SCHEMA S1");
        Execute(statement, "CREATE TABLE S1.T(ID INT PRIMARY KEY)");
        ExpectErrorAfterDdl(connection, "SELECT * FROM S1.T", "ALTER SCHEMA S1 RENAME TO S2",
            SchemaNotFound1);
    }

    private void TestSchemaDropCascade()
    {
        using DbConnection connection = GetConnection("clearCacheAfterDdl_schemaDrop");
        DbCommand statement = connection.CreateCommand();
        Execute(statement, "CREATE SCHEMA SD");
        Execute(statement, "CREATE TABLE SD.T(ID INT PRIMARY KEY)");
        ExpectErrorAfterDdl(connection, "SELECT * FROM SD.T", "DROP SCHEMA SD CASCADE",
            SchemaNotFound1);
    }

    private void TestAliasDrop()
    {
        using DbConnection connection = GetConnection("clearCacheAfterDdl_alias");
        DbCommand statement = connection.CreateCommand();
        Execute(statement, "CREATE ALIAS F1 FOR \"org.h2.test.db.TestClearCacheAfterDdl.staticIdentityFunction\"");
        ExpectErrorAfterDdl(connection, "SELECT F1('something')", "DROP ALIAS F1", FunctionNotFound1);
    }
}
