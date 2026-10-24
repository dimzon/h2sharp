// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestSynonymForTable.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands. conn.getMetaData().getTables
// and .getColumns are the INFORMATION_SCHEMA.TABLES / COLUMNS queries through the ADO
// layer — the tables the metadata calls walk; Constants.SCHEMA_MAIN is "PUBLIC". The
// reference's main() entry is the runner; xUnit runs Test() directly.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests for table synonyms.
/// </summary>
public sealed class TestSynonymForTable : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestSelectFromSynonym();
        TestInsertIntoSynonym();
        TestInsertWithColumnNameIntoSynonym();
        TestUpdate();
        TestDeleteFromSynonym();
        TestTruncateSynonym();
        TestExistingTableName();
        TestCreateForUnknownTable();
        TestMetaData();
        TestCreateOrReplace();
        TestCreateOrReplaceExistingTable();
        TestSynonymInDifferentSchema();
        TestReopenDatabase();
        TestDropSynonym();
        TestDropTable();
        TestDropSchema();
    }

    private void TestUpdate()
    {
        using DbConnection conn = GetConnection("synonym");
        CreateTableWithSynonym(conn);
        InsertIntoSynonym(conn, 25);

        DbCommand stmnt = conn.CreateCommand();
        stmnt.CommandText = "UPDATE testsynonym set id = 30 WHERE id = 25";
        AssertEquals(1, stmnt.ExecuteNonQuery());

        AssertSynonymContains(conn, 30);
    }

    private void TestDropSchema()
    {
        using DbConnection conn = GetConnection("synonym");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "CREATE SCHEMA IF NOT EXISTS s1");
        Execute(stat, "CREATE TABLE IF NOT EXISTS s1.backingtable(id INT PRIMARY KEY)");
        Execute(stat, "CREATE OR REPLACE SYNONYM testsynonym FOR s1.backingtable");
        Execute(stat, "DROP SCHEMA s1 CASCADE");

        AssertThrows(ErrorCode.TableOrViewNotFoundDatabaseEmpty1, stat,
            "SELECT id FROM testsynonym");
    }

    private void TestDropTable()
    {
        using DbConnection conn = GetConnection("synonym");
        CreateTableWithSynonym(conn);
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "DROP TABLE backingtable");

        // Backing table does not exist anymore.
        AssertThrows(ErrorCode.TableOrViewNotFoundDatabaseEmpty1, stat,
            "SELECT id FROM testsynonym");

        // Synonym should be dropped as well
        DbCommand synonyms = conn.CreateCommand();
        synonyms.CommandText = "SELECT * FROM INFORMATION_SCHEMA.SYNONYMS WHERE SYNONYM_NAME='TESTSYNONYM'";
        using (DbDataReader synonymRows = synonyms.ExecuteReader())
        {
            AssertFalse(synonymRows.Read());
        }

        // Reopening should work with dropped synonym
        using (DbConnection conn2 = GetConnection("synonym"))
        {
            AssertThrows(ErrorCode.ObjectClosed, conn2.CreateCommand(),
                "SELECT id FROM testsynonym");
        }
    }

    private void TestDropSynonym()
    {
        using DbConnection conn = GetConnection("synonym");
        CreateTableWithSynonym(conn);
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "DROP SYNONYM testsynonym");

        // Synonym does not exist anymore.
        AssertThrows(ErrorCode.TableOrViewNotFound1, stat, "SELECT id FROM testsynonym");

        // Dropping with "if exists" should succeed even if the synonym does not exist anymore.
        Execute(stat, "DROP SYNONYM IF EXISTS testsynonym");

        // Without "if exists" the command should fail if the synonym does not exist.
        AssertThrows(ErrorCode.TableOrViewNotFound1, stat, "DROP SYNONYM testsynonym");
    }

    private void TestSynonymInDifferentSchema()
    {
        using DbConnection conn = GetConnection("synonym");
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "CREATE SCHEMA IF NOT EXISTS s1");
        Execute(stat, "CREATE TABLE IF NOT EXISTS s1.backingtable(id INT PRIMARY KEY)");
        Execute(stat, "TRUNCATE TABLE s1.backingtable");
        Execute(stat, "CREATE OR REPLACE SYNONYM testsynonym FOR s1.backingtable");
        Execute(stat, "INSERT INTO s1.backingtable VALUES(15)");
        AssertSynonymContains(conn, 15);
    }

    private void TestCreateOrReplaceExistingTable()
    {
        using DbConnection conn = GetConnection("synonym");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE IF NOT EXISTS backingtable(id INT PRIMARY KEY)");

        AssertThrows(ErrorCode.TableOrViewAlreadyExists1, stat,
            "CREATE OR REPLACE SYNONYM backingtable FOR backingtable");
    }

    private void TestCreateOrReplace()
    {
        // start with a fresh db so the first create or replace has to actually create the synonym.
        DeleteDb("synonym");
        using DbConnection conn = GetConnection("synonym");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE IF NOT EXISTS backingtable(id INT PRIMARY KEY)");
        Execute(stat, "CREATE TABLE IF NOT EXISTS backingtable2(id INT PRIMARY KEY)");
        Execute(stat, "CREATE OR REPLACE SYNONYM testsynonym FOR backingtable");
        InsertIntoBackingTable(conn, 17);

        stat.CommandText = "SELECT id FROM testsynonym";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(17, rs.GetInt32(0));
        }

        Execute(stat, "CREATE OR REPLACE SYNONYM testsynonym FOR backingtable2");

        // Should not return a result, since backingtable2 is empty.
        stat.CommandText = "SELECT id FROM testsynonym";
        using (DbDataReader rs2 = stat.ExecuteReader())
        {
            AssertFalse(rs2.Read());
        }

        DeleteDb("synonym");
    }

    private void TestMetaData()
    {
        using DbConnection conn = GetConnection("synonym");
        CreateTableWithSynonym(conn);

        // getMetaData().getTables(null, SCHEMA_MAIN, null, ["SYNONYM"]): the
        // INFORMATION_SCHEMA.TABLES rows of the synonyms, through the ADO layer.
        DbCommand tables = conn.CreateCommand();
        tables.CommandText =
            "SELECT TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES "
            + "WHERE TABLE_SCHEMA = 'PUBLIC' AND TABLE_TYPE = 'SYNONYM'";
        using (DbDataReader tableRows = tables.ExecuteReader())
        {
            AssertTrue(tableRows.Read());
            AssertEquals("TESTSYNONYM", tableRows.GetString(tableRows.GetOrdinal("TABLE_NAME")));
            AssertEquals("SYNONYM", tableRows.GetString(tableRows.GetOrdinal("TABLE_TYPE")));
            AssertFalse(tableRows.Read());
        }

        // getMetaData().getColumns(null, SCHEMA_MAIN, "TESTSYNONYM", null)
        DbCommand columns = conn.CreateCommand();
        columns.CommandText =
            "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS "
            + "WHERE TABLE_SCHEMA = 'PUBLIC' AND TABLE_NAME = 'TESTSYNONYM'";
        using (DbDataReader columnRows = columns.ExecuteReader())
        {
            AssertTrue(columnRows.Read());
            AssertEquals("TESTSYNONYM", columnRows.GetString(columnRows.GetOrdinal("TABLE_NAME")));
            AssertEquals("ID", columnRows.GetString(columnRows.GetOrdinal("COLUMN_NAME")));
            AssertFalse(columnRows.Read());
        }

        DbCommand synonyms = conn.CreateCommand();
        synonyms.CommandText = "SELECT * FROM INFORMATION_SCHEMA.SYNONYMS";
        using (DbDataReader synonymRows = synonyms.ExecuteReader())
        {
            AssertTrue(synonymRows.Read());
            AssertEquals("SYNONYM", synonymRows.GetString(synonymRows.GetOrdinal("SYNONYM_CATALOG")));
            AssertEquals("PUBLIC", synonymRows.GetString(synonymRows.GetOrdinal("SYNONYM_SCHEMA")));
            AssertEquals("TESTSYNONYM", synonymRows.GetString(synonymRows.GetOrdinal("SYNONYM_NAME")));
            AssertEquals("BACKINGTABLE", synonymRows.GetString(synonymRows.GetOrdinal("SYNONYM_FOR")));
            AssertEquals("VALID", synonymRows.GetString(synonymRows.GetOrdinal("STATUS")));
            AssertNull(synonymRows.IsDBNull(synonymRows.GetOrdinal("REMARKS"))
                ? null : synonymRows.GetString(synonymRows.GetOrdinal("REMARKS")));
            AssertFalse(synonymRows.Read());
        }
    }

    private void TestCreateForUnknownTable()
    {
        using DbConnection conn = GetConnection("synonym");
        DbCommand stat = conn.CreateCommand();

        AssertThrows(ErrorCode.TableOrViewNotFound1, stat,
            "CREATE SYNONYM someSynonym FOR nonexistingTable");
    }

    private void TestExistingTableName()
    {
        using DbConnection conn = GetConnection("synonym");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE IF NOT EXISTS backingtable(id INT PRIMARY KEY)");

        AssertThrows(ErrorCode.TableOrViewAlreadyExists1, stat,
            "CREATE SYNONYM backingtable FOR backingtable");
    }

    /// <summary>
    /// Make sure, that the schema changes are persisted when reopening the database.
    /// </summary>
    private void TestReopenDatabase()
    {
        if (!Config.Memory)
        {
            DeleteDb("synonym");
            DbConnection conn = GetConnection("synonym");
            CreateTableWithSynonym(conn);
            InsertIntoBackingTable(conn, 9);
            conn.Close();
            using (DbConnection conn2 = GetConnection("synonym"))
            {
                AssertSynonymContains(conn2, 9);
            }
        }
    }

    private void TestTruncateSynonym()
    {
        using DbConnection conn = GetConnection("synonym");
        CreateTableWithSynonym(conn);

        InsertIntoBackingTable(conn, 7);
        AssertBackingTableContains(conn, 7);

        Execute(conn.CreateCommand(), "TRUNCATE TABLE testsynonym");

        AssertBackingTableIsEmpty(conn);
    }

    private void TestDeleteFromSynonym()
    {
        using DbConnection conn = GetConnection("synonym");
        CreateTableWithSynonym(conn);

        InsertIntoBackingTable(conn, 7);
        AssertBackingTableContains(conn, 7);
        DeleteFromSynonym(conn, 7);

        AssertBackingTableIsEmpty(conn);
    }

    private static void DeleteFromSynonym(DbConnection conn, int id)
    {
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "DELETE FROM testsynonym WHERE id = ?";
        prep.Parameters.Add(new H2Parameter { Value = id });
        _ = prep.ExecuteNonQuery();
    }

    private void AssertBackingTableIsEmpty(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "SELECT id FROM backingtable";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertFalse(rs.Read());
        }
    }

    private void TestInsertIntoSynonym()
    {
        using DbConnection conn = GetConnection("synonym");
        CreateTableWithSynonym(conn);

        InsertIntoSynonym(conn, 5);
        AssertBackingTableContains(conn, 5);
    }

    private void TestInsertWithColumnNameIntoSynonym()
    {
        using DbConnection conn = GetConnection("synonym");
        CreateTableWithSynonym(conn);

        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "INSERT INTO testsynonym (id) VALUES(?)";
        prep.Parameters.Add(new H2Parameter { Value = 55 });
        _ = prep.ExecuteNonQuery();
        AssertBackingTableContains(conn, 55);
    }

    private void AssertBackingTableContains(DbConnection conn, int testValue)
    {
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "SELECT id FROM backingtable";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(testValue, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }
    }

    private void TestSelectFromSynonym()
    {
        DeleteDb("synonym");
        using DbConnection conn = GetConnection("synonym");
        CreateTableWithSynonym(conn);
        InsertIntoBackingTable(conn, 1);
        AssertSynonymContains(conn, 1);
    }

    private void AssertSynonymContains(DbConnection conn, int id)
    {
        DbCommand stat = conn.CreateCommand();
        stat.CommandText = "SELECT id FROM testsynonym";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(id, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }
    }

    private static void InsertIntoSynonym(DbConnection conn, int id)
    {
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "INSERT INTO testsynonym VALUES(?)";
        prep.Parameters.Add(new H2Parameter { Value = id });
        _ = prep.ExecuteNonQuery();
    }

    private static void InsertIntoBackingTable(DbConnection conn, int id)
    {
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "INSERT INTO backingtable VALUES(?)";
        prep.Parameters.Add(new H2Parameter { Value = id });
        _ = prep.ExecuteNonQuery();
    }

    private void CreateTableWithSynonym(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE IF NOT EXISTS backingtable(id INT PRIMARY KEY)");
        Execute(stat, "CREATE OR REPLACE SYNONYM testsynonym FOR backingtable");
        Execute(stat, "TRUNCATE TABLE backingtable");
    }
}
