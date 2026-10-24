// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestRights.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands, and the proxy form of
// assertThrows becomes the lambda form. The metadata walks (getTables) are the
// INFORMATION_SCHEMA.TABLES queries through the ADO layer. The executeSuccess helper
// touches the column schema the provider answers instead of the JDBC ResultSetMetaData
// property set. The Java class names in the alias/trigger/aggregrate SQL stay the
// reference's, as written. The reference's main() entry is the runner; xUnit runs
// Test() directly.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Access rights tests.
/// </summary>
public sealed class TestRights : TestDb
{
    private DbCommand? stat;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestNullPassword();
        TestLinkedTableMeta();
        TestGrantMore();
        TestGrantSchema();
        TestRevokeSchema();
        TestOpenNonAdminWithMode();
        TestDisallowedTables();
        TestDropOwnUser();
        TestGetTables();
        TestDropTempTables();
        // testLowerCaseUser();
        TestSchemaRenameUser();
        TestAccessRights();
        TestSchemaAdminRole();
        TestTableRename();
        TestSchemaRename();
        TestSchemaDrop();
        TestDropTable();
        TestSchemaOwner();
        DeleteDb("rights");
    }

    private void TestNullPassword()
    {
        DeleteDb("rights");
        using DbConnection conn = GetConnection("rights");
        stat = conn.CreateCommand();
        Execute(stat, "create user test password null");
        Execute(stat, "alter user test set password null");
        Execute(stat, "create user test2 salt null hash null");
        Execute(stat, "alter user test set salt null hash null");
    }

    private void TestLinkedTableMeta()
    {
        DeleteDb("rights");
        using (DbConnection conn = GetConnection("rights;OLD_INFORMATION_SCHEMA=TRUE"))
        {
            stat = conn.CreateCommand();
            Execute(stat, "create user test password 'test'");
            Execute(stat, "create linked table test"
                + "(null, 'jdbc:h2:mem:', 'sa', 'sa', 'DUAL')");
            // password is invisible to non-admin
            using (DbConnection conn2 = GetConnection("rights", "test", GetPassword("test")))
            {
                DbCommand stat2 = conn2.CreateCommand();
                stat2.CommandText = "select * from information_schema.tables "
                    + "where table_name = 'TEST'";
                using (DbDataReader rs = stat2.ExecuteReader())
                {
                    AssertTrue(rs.Read());
                    for (int i = 0; i < rs.FieldCount; i++)
                    {
                        string s = rs.IsDBNull(i) ? "" : rs.GetString(i);
                        AssertFalse(s is not null && s.Contains("'sa'", StringComparison.Ordinal));
                    }
                }
            }

            // password is visible to admin
            stat!.CommandText = "select * from information_schema.tables "
                + "where table_name = 'TEST'";
            bool foundPassword = false;
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                for (int i = 0; i < rs.FieldCount; i++)
                {
                    string s = rs.IsDBNull(i) ? "" : rs.GetString(i);
                    if (s is not null && s.Contains("'sa'", StringComparison.Ordinal))
                    {
                        foundPassword = true;
                    }
                }
            }

            AssertTrue(foundPassword);

            Execute(stat, "drop table test");
        }
    }

    private void TestGrantMore()
    {
        DeleteDb("rights");
        using DbConnection conn = GetConnection("rights");
        stat = conn.CreateCommand();
        Execute(stat, "create role new_role");
        Execute(stat, "create table test(id int)");
        Execute(stat, "grant select on test to new_role");
        stat.CommandText = "select * from information_schema.table_privileges";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertFalse(rs.Read());
        }

        Execute(stat, "grant insert on test to new_role");
        stat.CommandText = "select * from information_schema.table_privileges";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertTrue(rs.Read());
            AssertFalse(rs.Read());
        }

        Execute(stat, "drop table test");
        Execute(stat, "drop role new_role");
    }

    private void TestGrantSchema()
    {
        DeleteDb("rights");
        using (DbConnection connAdmin = GetConnection("rights"))
        {
            // Test with user
            DbCommand statAdmin = connAdmin.CreateCommand();
            Execute(statAdmin, "create user test_user password 'test'");
            Execute(statAdmin, "create table test1(id int)");
            Execute(statAdmin, "create table test2(id int)");
            Execute(statAdmin, "create table test3(id int)");
            Execute(statAdmin, "grant insert on schema public to test_user");
            Execute(statAdmin, "create table test4(id int)");

            using (DbConnection conn = GetConnection("rights", "test_user", GetPassword("test")))
            {
                DbCommand statLocal = conn.CreateCommand();
                // Must proceed
                Execute(statLocal, "insert into test1 values (1)");
                Execute(statLocal, "insert into test2 values (1)");
                Execute(statLocal, "insert into test3 values (1)");
                Execute(statLocal, "insert into test4 values (1)");
                // Must not proceed
                AssertThrows(ErrorCode.NotEnoughRightsFor1, statLocal, "select * from test1");
                AssertThrows(ErrorCode.NotEnoughRightsFor1, statLocal, "select * from test2");
                AssertThrows(ErrorCode.NotEnoughRightsFor1, statLocal, "select * from test3");
                AssertThrows(ErrorCode.NotEnoughRightsFor1, statLocal, "select * from test4");

                // Test with role
                Execute(statAdmin, "create role test_role");
                Execute(statAdmin, "grant test_role to test_user");
                Execute(statAdmin, "grant select on schema public to test_role");
                // create the table after grant
                Execute(statAdmin, "create table test5(id int)");

                // Must proceed
                Execute(statLocal, "insert into test1 values (2)");
                Execute(statLocal, "insert into test2 values (2)");
                Execute(statLocal, "insert into test3 values (2)");
                Execute(statLocal, "insert into test4 values (2)");
                Execute(statLocal, "insert into test5 values (1)");
                Execute(statLocal, "select * from test1");
                Execute(statLocal, "select * from test2");
                Execute(statLocal, "select * from test3");
                Execute(statLocal, "select * from test4");
                Execute(statLocal, "select * from test5");
            }
        }

        DeleteDb("rights");
    }

    private void TestRevokeSchema()
    {
        DeleteDb("rights");
        using (DbConnection connAdmin = GetConnection("rights"))
        {
            DbCommand statAdmin = connAdmin.CreateCommand();

            // Test with user
            Execute(statAdmin, "create user test_user password 'test'");
            Execute(statAdmin, "create table test1(id int)");
            Execute(statAdmin, "create table test2(id int)");
            Execute(statAdmin, "create table test3(id int)");
            Execute(statAdmin, "grant insert on schema public to test_user");

            using (DbConnection conn = GetConnection("rights", "test_user", GetPassword("test")))
            {
                DbCommand statLocal = conn.CreateCommand();
                // Must proceed
                Execute(statLocal, "insert into test1 values (1)");
                Execute(statLocal, "insert into test2 values (1)");
                Execute(statLocal, "insert into test3 values (1)");

                Execute(statAdmin, "revoke insert on schema public from test_user");
                Execute(statAdmin, "create table test4(id int)");

                // Must not proceed
                AssertThrows(ErrorCode.NotEnoughRightsFor1, statLocal, "insert into test1 values (2)");
                AssertThrows(ErrorCode.NotEnoughRightsFor1, statLocal, "insert into test2 values (2)");
                AssertThrows(ErrorCode.NotEnoughRightsFor1, statLocal, "insert into test3 values (2)");
                AssertThrows(ErrorCode.NotEnoughRightsFor1, statLocal, "insert into test4 values (2)");

                // Test with role
                Execute(statAdmin, "create role test_role");
                Execute(statAdmin, "grant test_role to test_user");
                Execute(statAdmin, "grant select on schema public to test_role");

                // Must proceed
                Execute(statLocal, "select * from test1");
                Execute(statLocal, "select * from test2");
                Execute(statLocal, "select * from test3");
                Execute(statLocal, "select * from test4");

                Execute(statAdmin, "revoke select on schema public from test_role");
                Execute(statAdmin, "create table test5(id int)");

                // Must not proceed
                AssertThrows(ErrorCode.NotEnoughRightsFor1, statLocal, "select * from test1");
                AssertThrows(ErrorCode.NotEnoughRightsFor1, statLocal, "select * from test2");
                AssertThrows(ErrorCode.NotEnoughRightsFor1, statLocal, "select * from test3");
                AssertThrows(ErrorCode.NotEnoughRightsFor1, statLocal, "select * from test4");
                AssertThrows(ErrorCode.NotEnoughRightsFor1, statLocal, "select * from test5");
            }
        }

        DeleteDb("rights");
    }

    private void TestOpenNonAdminWithMode()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("rights");
        DbConnection conn = GetConnection("rights;MODE=MYSQL");
        stat = conn.CreateCommand();
        Execute(stat, "create user test password 'test'");
        using (DbConnection conn2 = GetConnection("rights;MODE=MYSQL", "test", GetPassword("test")))
        {
        }

        conn.Close();
        // if opening alone
        using (DbConnection conn2 = GetConnection("rights;MODE=MYSQL", "test", GetPassword("test")))
        {
        }

        // if opening as the second connection
        conn = GetConnection("rights;MODE=MYSQL");
        using (DbConnection conn2 = GetConnection("rights;MODE=MYSQL", "test", GetPassword("test")))
        {
        }

        stat = conn.CreateCommand();
        Execute(stat, "drop user test");
        conn.Close();
    }

    private void TestDisallowedTables()
    {
        DeleteDb("rights");
        using (DbConnection conn = GetConnection("rights"))
        {
            stat = conn.CreateCommand();

            Execute(stat, "CREATE USER IF NOT EXISTS TEST PASSWORD 'TEST'");
            Execute(stat, "CREATE ROLE TEST_ROLE");
            Execute(stat, "CREATE TABLE ADMIN_ONLY(ID INT)");
            Execute(stat, "CREATE TABLE TEST(ID INT)");
            Execute(stat, "GRANT ALL ON TEST TO TEST");
            using (DbConnection conn2 = GetConnection("rights", "TEST", GetPassword("TEST")))
            {
                DbCommand stat2 = conn2.CreateCommand();

                string sql = "select * from admin_only where 1=0";
                Execute(stat, sql);
                AssertThrows(ErrorCode.NotEnoughRightsFor1, stat2, sql);

                // meta.getTables(null, "PUBLIC", "%", ["TABLE", "VIEW", "SEQUENCE"])
                stat2.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES "
                    + "WHERE TABLE_SCHEMA = 'PUBLIC'";
                using (DbDataReader rs = stat2.ExecuteReader())
                {
                    AssertTrue(rs.Read());
                    AssertTrue(rs.Read());
                    AssertFalse(rs.Read());
                }

                foreach (string s in new[]
                {
                    "information_schema.settings where setting_name='property.java.runtime.version'",
                    "information_schema.users where user_name='SA'",
                    "information_schema.roles",
                    "information_schema.rights",
                    "information_schema.sessions where user_name='SA'",
                })
                {
                    stat2.CommandText = "select * from " + s;
                    using (DbDataReader rs = stat2.ExecuteReader())
                    {
                        AssertFalse(rs.Read());
                    }

                    stat.CommandText = "select * from " + s;
                    using (DbDataReader rs = stat.ExecuteReader())
                    {
                        AssertTrue(rs.Read());
                    }
                }
            }
        }
    }

    private void TestDropOwnUser()
    {
        DeleteDb("rights");
        string user = GetUser().ToUpperInvariant();
        using (DbConnection conn = GetConnection("rights"))
        {
            stat = conn.CreateCommand();
            AssertThrows(ErrorCode.CannotDropCurrentUser, stat, "DROP USER " + user);
            Execute(stat, "CREATE USER TEST PASSWORD 'TEST' ADMIN");
            Execute(stat, "DROP USER " + user);
        }

        if (!Config.Memory)
        {
            AssertThrows(ErrorCode.WrongUserOrPassword,
                () => _ = GetConnection("rights"));
        }
    }

    private void TestGetTables()
    {
        DeleteDb("rights");
        using DbConnection conn = GetConnection("rights");
        stat = conn.CreateCommand();

        Execute(stat, "CREATE USER IF NOT EXISTS TEST PASSWORD 'TEST'");
        Execute(stat, "CREATE TABLE TEST(ID INT)");
        Execute(stat, "GRANT ALL ON TABLE TEST TO TEST");
        using (DbConnection conn2 = GetConnection("rights", "TEST", GetPassword("TEST")))
        {
            // meta.getTables(null, null, "%", ["TABLE", "VIEW", "SEQUENCE"])
            DbCommand command = conn2.CreateCommand();
            command.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES";
            using (_ = command.ExecuteReader())
            {
            }
        }
    }

    private void TestDropTempTables()
    {
        DeleteDb("rights");
        using DbConnection conn = GetConnection("rights");
        stat = conn.CreateCommand();
        Execute(stat, "CREATE USER IF NOT EXISTS READER PASSWORD 'READER'");
        Execute(stat, "CREATE TABLE TEST(ID INT)");
        using (DbConnection conn2 = GetConnection("rights", "READER", GetPassword("READER")))
        {
            DbCommand stat2 = conn2.CreateCommand();
            AssertThrows(ErrorCode.NotEnoughRightsFor1, stat2, "SELECT * FROM TEST");
            Execute(stat2, "CREATE LOCAL TEMPORARY TABLE IF NOT EXISTS MY_TEST(ID INT)");
            Execute(stat2, "INSERT INTO MY_TEST VALUES(1)");
            Execute(stat2, "SELECT * FROM MY_TEST");
            Execute(stat2, "DROP TABLE MY_TEST");
        }
    }

    private void TestSchemaRenameUser()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("rights");
        DbConnection conn = GetConnection("rights");
        stat = conn.CreateCommand();
        Execute(stat, "create user test password ''");
        Execute(stat, "create schema b authorization test");
        Execute(stat, "create table b.test(id int)");
        Execute(stat, "alter user test rename to test1");
        conn.Close();
        conn = GetConnection("rights");
        stat = conn.CreateCommand();
        Execute(stat, "select * from b.test");
        AssertThrows(ErrorCode.CannotDrop2, stat, "drop user test1");
        Execute(stat, "drop schema b cascade");
        Execute(stat, "drop user test1");
        conn.Close();
    }

    private void TestSchemaAdminRole()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("rights");
        DbConnection conn = GetConnection("rights");
        stat = conn.CreateCommand();
        // default table type
        TestTableType(conn, "MEMORY");
        TestTableType(conn, "CACHED");

        /* make sure admin can still do it. */

        ExecuteSuccess("CREATE USER SCHEMA_CREATOR PASSWORD 'xyz'");

        ExecuteSuccess("CREATE SCHEMA SCHEMA_RIGHT_TEST");
        ExecuteSuccess("ALTER SCHEMA SCHEMA_RIGHT_TEST "
            + "RENAME TO SCHEMA_RIGHT_TEST_RENAMED");
        ExecuteSuccess("DROP SCHEMA SCHEMA_RIGHT_TEST_RENAMED");

        /* create this for tests below */
        ExecuteSuccess("CREATE SCHEMA SCHEMA_RIGHT_TEST_EXISTS");
        ExecuteSuccess("CREATE TABLE SCHEMA_RIGHT_TEST_EXISTS.TEST_EXISTS"
            + "(ID INT PRIMARY KEY, NAME VARCHAR)");
        conn.Close();

        string url = "rights";

        // try and fail (no rights yet)
        conn = GetConnection(url, "SCHEMA_CREATOR", GetPassword("xyz"));
        stat = conn.CreateCommand();
        AssertThrows(ErrorCode.AdminRightsRequired, stat, "CREATE SCHEMA SCHEMA_RIGHT_TEST_WILL_FAIL");
        AssertThrows(ErrorCode.AdminRightsRequired, stat,
            "ALTER SCHEMA SCHEMA_RIGHT_TEST_EXISTS RENAME TO SCHEMA_RIGHT_TEST_WILL_FAIL");
        AssertThrows(ErrorCode.NotEnoughRightsFor1, stat, "DROP SCHEMA SCHEMA_RIGHT_TEST_EXISTS");
        conn.Close();

        // grant the right
        conn = GetConnection("rights");
        stat = conn.CreateCommand();
        ExecuteSuccess("GRANT ALTER ANY SCHEMA TO SCHEMA_CREATOR");
        conn.Close();

        // try and succeed
        conn = GetConnection(url, "SCHEMA_CREATOR", GetPassword("xyz"));
        stat = conn.CreateCommand();

        // should be able to create a schema and manipulate tables on that
        // schema...
        ExecuteSuccess("CREATE SCHEMA SCHEMA_RIGHT_TEST");
        ExecuteSuccess("ALTER SCHEMA SCHEMA_RIGHT_TEST RENAME TO S");
        ExecuteSuccess("CREATE TABLE S.TEST(ID INT PRIMARY KEY, NAME VARCHAR)");
        ExecuteSuccess("ALTER TABLE S.TEST ADD COLUMN QUESTION VARCHAR");
        ExecuteSuccess("INSERT INTO  S.TEST (ID, NAME) VALUES (42, 'Adams')");
        ExecuteSuccess("UPDATE S.TEST Set NAME = 'Douglas'");
        ExecuteSuccess("DELETE FROM S.TEST");
        ExecuteSuccess("DROP SCHEMA S CASCADE");

        // ...and on other schemata
        ExecuteSuccess("CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR)");
        ExecuteSuccess("ALTER TABLE TEST ADD COLUMN QUESTION VARCHAR");
        ExecuteSuccess("INSERT INTO  TEST (ID, NAME) VALUES (42, 'Adams')");
        ExecuteSuccess("UPDATE TEST Set NAME = 'Douglas'");
        ExecuteSuccess("DELETE FROM TEST");

        conn.Close();

        // revoke the right
        conn = GetConnection("rights");
        stat = conn.CreateCommand();
        ExecuteSuccess("REVOKE ALTER ANY SCHEMA FROM SCHEMA_CREATOR");
        conn.Close();

        // try again and fail
        conn = GetConnection(url, "SCHEMA_CREATOR", GetPassword("xyz"));
        stat = conn.CreateCommand();
        AssertThrows(ErrorCode.AdminRightsRequired, stat, "CREATE SCHEMA SCHEMA_RIGHT_TEST");
        AssertThrows(ErrorCode.AdminRightsRequired, stat,
            "ALTER SCHEMA SCHEMA_RIGHT_TEST_EXISTS RENAME TO SCHEMA_RIGHT_TEST_RENAMED");
        AssertThrows(ErrorCode.NotEnoughRightsFor1, stat, "DROP SCHEMA SCHEMA_RIGHT_TEST_EXISTS");
        AssertThrows(ErrorCode.NotEnoughRightsFor1, stat,
            "CREATE TABLE SCHEMA_RIGHT_TEST_EXISTS.TEST(ID INT PRIMARY KEY, NAME VARCHAR)");
        AssertThrows(ErrorCode.NotEnoughRightsFor1, stat,
            "INSERT INTO  SCHEMA_RIGHT_TEST_EXISTS.TEST_EXISTS (ID, NAME) VALUES (42, 'Adams')");
        AssertThrows(ErrorCode.NotEnoughRightsFor1, stat,
            "UPDATE SCHEMA_RIGHT_TEST_EXISTS.TEST_EXISTS Set NAME = 'Douglas'");
        AssertThrows(ErrorCode.NotEnoughRightsFor1, stat,
            "DELETE FROM SCHEMA_RIGHT_TEST_EXISTS.TEST_EXISTS");
        conn.Close();
    }

    private void TestTableRename()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("rights");
        DbConnection conn = GetConnection("rights");
        stat = conn.CreateCommand();
        Execute(stat, "create user test password '' admin");
        Execute(stat, "create schema b");
        Execute(stat, "create table b.t1(id int)");
        Execute(stat, "grant select on b.t1 to test");
        Execute(stat, "alter table b.t1 rename to b.t2");
        conn.Close();
        conn = GetConnection("rights");
        stat = conn.CreateCommand();
        Execute(stat, "drop user test");
        conn.Close();
    }

    private void TestSchemaRename()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("rights");
        DbConnection conn = GetConnection("rights");
        stat = conn.CreateCommand();
        Execute(stat, "create user test password '' admin");
        Execute(stat, "create schema b");
        Execute(stat, "grant select on schema b to test");
        Execute(stat, "alter schema b rename to c");
        conn.Close();
        conn = GetConnection("rights");
        stat = conn.CreateCommand();
        Execute(stat, "drop user test");
        conn.Close();
    }

    private void TestSchemaDrop()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("rights");
        DbConnection conn = GetConnection("rights");
        stat = conn.CreateCommand();
        Execute(stat, "create user test password '' admin");
        Execute(stat, "create schema b");
        Execute(stat, "grant select on schema b to test");
        Execute(stat, "drop schema b cascade");
        conn.Close();
        conn = GetConnection("rights");
        stat = conn.CreateCommand();
        Execute(stat, "drop user test");
        conn.Close();
    }

    private void TestAccessRights()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("rights");
        DbConnection conn = GetConnection("rights");
        stat = conn.CreateCommand();
        // default table type
        TestTableType(conn, "MEMORY");
        TestTableType(conn, "CACHED");

        // rights on tables and views
        ExecuteSuccess("CREATE USER PASS_READER PASSWORD 'abc'");
        ExecuteSuccess("CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR)");
        ExecuteSuccess("CREATE TABLE PASS(ID INT PRIMARY KEY, "
            + "NAME VARCHAR, PASSWORD VARCHAR)");
        ExecuteSuccess("CREATE VIEW PASS_NAME AS SELECT ID, NAME FROM PASS");
        ExecuteSuccess("GRANT SELECT ON PASS_NAME TO PASS_READER");
        ExecuteSuccess("GRANT SELECT, INSERT, UPDATE ON TEST TO PASS_READER");
        conn.Close();

        string url = "rights";
        conn = GetConnection(url, "PASS_READER", GetPassword("abc"));
        stat = conn.CreateCommand();
        ExecuteSuccess("SELECT * FROM PASS_NAME");
        ExecuteSuccess("SELECT * FROM (SELECT * FROM PASS_NAME)");
        ExecuteSuccess("SELECT (SELECT NAME FROM PASS_NAME) P FROM PASS_NAME");
        ExecuteError("SELECT (SELECT PASSWORD FROM PASS) P FROM PASS_NAME");
        ExecuteError("SELECT * FROM PASS");
        ExecuteError("INSERT INTO TEST SELECT 1, PASSWORD FROM PASS");
        ExecuteError("INSERT INTO TEST VALUES(SELECT PASSWORD FROM PASS)");
        ExecuteError("UPDATE TEST SET NAME=(SELECT PASSWORD FROM PASS)");
        ExecuteError("DELETE FROM TEST WHERE NAME=(SELECT PASSWORD FROM PASS)");
        ExecuteError("SELECT * FROM (SELECT * FROM PASS)");
        AssertThrows(ErrorCode.TableOrViewNotFound1, stat,
            "CREATE VIEW X AS SELECT * FROM PASS_READER");
        AssertThrows(ErrorCode.NotEnoughRightsFor1, stat,
            "CREATE VIEW X AS SELECT * FROM PASS_NAME");
        conn.Close();

        conn = GetConnection("rights");
        stat = conn.CreateCommand();

        ExecuteSuccess("DROP TABLE TEST");
        ExecuteSuccess("CREATE USER TEST PASSWORD 'abc'");
        ExecuteSuccess("ALTER USER TEST ADMIN TRUE");
        ExecuteSuccess("CREATE TABLE TEST(ID INT)");
        ExecuteSuccess("CREATE SCHEMA SCHEMA_A AUTHORIZATION SA");
        ExecuteSuccess("CREATE TABLE SCHEMA_A.TABLE_B(ID INT)");
        ExecuteSuccess("GRANT ALL ON SCHEMA_A.TABLE_B TO TEST");
        ExecuteSuccess("CREATE TABLE HIDDEN(ID INT)");
        ExecuteSuccess("CREATE TABLE PUB_TABLE(ID INT)");
        ExecuteSuccess("CREATE TABLE ROLE_TABLE(ID INT)");
        ExecuteSuccess("CREATE ROLE TEST_ROLE");
        ExecuteSuccess("GRANT SELECT ON ROLE_TABLE TO TEST_ROLE");
        ExecuteSuccess("GRANT UPDATE ON ROLE_TABLE TO TEST_ROLE");
        ExecuteSuccess("REVOKE UPDATE ON ROLE_TABLE FROM TEST_ROLE");
        AssertThrows(ErrorCode.RolesAndRightCannotBeMixed, stat,
            "REVOKE SELECT, SUB1 ON ROLE_TABLE FROM TEST_ROLE");
        ExecuteSuccess("GRANT TEST_ROLE TO TEST");
        ExecuteSuccess("GRANT SELECT ON PUB_TABLE TO PUBLIC");
        ExecuteSuccess("GRANT SELECT ON TEST TO TEST");
        ExecuteSuccess("CREATE ROLE SUB1");
        ExecuteSuccess("CREATE ROLE SUB2");
        ExecuteSuccess("CREATE TABLE SUB_TABLE(ID INT)");
        ExecuteSuccess("GRANT ALL ON SUB_TABLE TO SUB2");
        ExecuteSuccess("REVOKE UPDATE, DELETE ON SUB_TABLE FROM SUB2");
        ExecuteSuccess("GRANT SUB2 TO SUB1");
        ExecuteSuccess("GRANT SUB1 TO TEST");

        ExecuteSuccess("ALTER USER TEST SET PASSWORD 'def'");
        ExecuteSuccess("CREATE USER TEST2 PASSWORD 'def' ADMIN");
        ExecuteSuccess("ALTER USER TEST ADMIN FALSE");
        ExecuteSuccess("SCRIPT TO '" + GetBaseDir()
            + "/rights.sql' CIPHER AES PASSWORD 'test'");
        conn.Close();

        try
        {
            _ = GetConnection("rights", "Test", GetPassword("abc"));
            Fail("mixed case user name");
        }
        catch (DbException e)
        {
            AssertKnownException(e);
        }

        try
        {
            _ = GetConnection("rights", "TEST", GetPassword("abc"));
            Fail("wrong password");
        }
        catch (DbException e)
        {
            AssertKnownException(e);
        }

        try
        {
            _ = GetConnection("rights", "TEST", GetPassword(""));
            Fail("wrong password");
        }
        catch (DbException e)
        {
            AssertKnownException(e);
        }

        conn = GetConnection(url, "TEST", GetPassword("def"));
        stat = conn.CreateCommand();

        AssertThrows(ErrorCode.AdminRightsRequired, stat, "SET DEFAULT_TABLE_TYPE MEMORY");

        ExecuteSuccess("SELECT * FROM TEST");
        ExecuteSuccess("SELECT * FROM SYSTEM_RANGE(1,2)");
        ExecuteSuccess("SELECT * FROM SCHEMA_A.TABLE_B");
        ExecuteSuccess("SELECT * FROM PUB_TABLE");
        ExecuteSuccess("SELECT * FROM ROLE_TABLE");
        ExecuteError("UPDATE ROLE_TABLE SET ID=0");
        ExecuteError("DELETE FROM ROLE_TABLE");
        ExecuteError("SELECT * FROM HIDDEN");
        ExecuteError("UPDATE TEST SET ID=0");
        ExecuteError("CALL SELECT MIN(PASSWORD) FROM PASS");
        ExecuteSuccess("SELECT * FROM SUB_TABLE");
        ExecuteSuccess("INSERT INTO SUB_TABLE VALUES(1)");
        ExecuteError("DELETE FROM SUB_TABLE");
        ExecuteError("UPDATE SUB_TABLE SET ID=0");

        AssertThrows(ErrorCode.AdminRightsRequired, stat, "CREATE USER TEST3 PASSWORD 'def'");
        AssertThrows(ErrorCode.AdminRightsRequired, stat, "ALTER USER TEST2 ADMIN FALSE");
        AssertThrows(ErrorCode.AdminRightsRequired, stat, "ALTER USER TEST2 SET PASSWORD 'ghi'");
        AssertThrows(ErrorCode.AdminRightsRequired, stat, "ALTER USER TEST2 RENAME TO TEST_X");
        AssertThrows(ErrorCode.AdminRightsRequired, stat, "ALTER USER TEST RENAME TO TEST_X");
        ExecuteSuccess("ALTER USER TEST SET PASSWORD 'ghi'");
        AssertThrows(ErrorCode.AdminRightsRequired, stat, "DROP USER TEST2");

        conn.Close();
        conn = GetConnection("rights");
        stat = conn.CreateCommand();
        ExecuteSuccess("DROP ROLE SUB1");
        ExecuteSuccess("DROP TABLE ROLE_TABLE");
        ExecuteSuccess("DROP USER TEST");

        conn.Close();
        conn = GetConnection("rights");
        stat = conn.CreateCommand();

        ExecuteSuccess("DROP TABLE IF EXISTS TEST");
        ExecuteSuccess("CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(255))");
        ExecuteSuccess("CREATE USER GUEST PASSWORD 'abc'");
        ExecuteSuccess("GRANT SELECT ON TEST TO GUEST");
        ExecuteSuccess("ALTER USER GUEST RENAME TO GAST");
        conn.Close();
        conn = GetConnection("rights");
        conn.Close();
        File.Delete(GetBaseDir() + "/rights.sql");
    }

    private void TestTableType(DbConnection conn, string type)
    {
        ExecuteSuccess("SET DEFAULT_TABLE_TYPE " + type);
        ExecuteSuccess("CREATE TABLE TEST(ID INT)");
        DbCommand command = conn.CreateCommand();
        command.CommandText =
            "SELECT STORAGE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='TEST'";
        using (DbDataReader rs = command.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(type, rs.GetString(0));
        }

        ExecuteSuccess("DROP TABLE TEST");
    }

    private void TestDropTable()
    {
        DeleteDb("rights");
        DbConnection conn = GetConnection("rights");
        stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT)");
        Execute(stat, "CREATE USER U PASSWORD '1'");
        Execute(stat, "GRANT ALL PRIVILEGES ON TEST TO U");
        DbConnection conn2 = GetConnection("rights", "U", GetPassword("1"));
        conn.Close();
        stat = conn2.CreateCommand();
        stat.CommandText = "INSERT INTO TEST VALUES 1";
        AssertEquals(1, stat.ExecuteNonQuery());
        stat.CommandText = "UPDATE TEST SET ID = 2 WHERE ID = 1";
        AssertEquals(1, stat.ExecuteNonQuery());
        stat.CommandText = "DELETE FROM TEST WHERE ID = 2";
        AssertEquals(1, stat.ExecuteNonQuery());
        ExecuteError("DROP TABLE TEST");
        conn2.Close();
    }

    private void TestSchemaOwner()
    {
        DeleteDb("rights");
        using (DbConnection connAdmin = GetConnection("rights"))
        {
            DbCommand statAdmin = connAdmin.CreateCommand();
            Execute(statAdmin, "CREATE USER SCHEMA_ADMIN PASSWORD '1'");
            Execute(statAdmin, "GRANT ALTER ANY SCHEMA TO SCHEMA_ADMIN");
            using (DbConnection connSchemaAdmin = GetConnection("rights", "SCHEMA_ADMIN", GetPassword("1")))
            {
                DbCommand statSchemaAdmin = connSchemaAdmin.CreateCommand();
                Execute(statAdmin, "CREATE USER SCHEMA_OWNER PASSWORD '1'");
                using (DbConnection connSchemaOwner = GetConnection("rights", "SCHEMA_OWNER", GetPassword("1")))
                {
                    DbCommand statSchemaOwner = connSchemaOwner.CreateCommand();
                    Execute(statAdmin, "CREATE USER OTHER PASSWORD '1'");
                    using (DbConnection connOther = GetConnection("rights", "OTHER", GetPassword("1")))
                    {
                        DbCommand statOther = connOther.CreateCommand();
                        TestSchemaOwner(statAdmin, statSchemaAdmin, statSchemaOwner, statOther,
                            "SCHEMA_OWNER");
                        Execute(statAdmin, "CREATE ROLE SCHEMA_OWNER_ROLE");
                        Execute(statAdmin, "GRANT SCHEMA_OWNER_ROLE TO SCHEMA_OWNER");
                        TestSchemaOwner(statAdmin, statSchemaAdmin, statSchemaOwner, statOther,
                            "SCHEMA_OWNER_ROLE");
                        TestAdminAndSchemaOwner(statAdmin, statSchemaAdmin);
                    }
                }
            }
        }
    }

    private void TestSchemaOwner(DbCommand statAdmin, DbCommand statSchemaAdmin,
        DbCommand statSchemaOwner, DbCommand statOther, string authorization)
    {
        ExecuteSuccessErrorAdmin(statSchemaAdmin, statSchemaOwner,
            "CREATE SCHEMA S AUTHORIZATION " + authorization);
        ExecuteSuccessError(statSchemaOwner, statOther, "CREATE DOMAIN S.D INT");
        ExecuteSuccessError(statSchemaOwner, statOther,
            "ALTER DOMAIN S.D ADD CONSTRAINT S.D_C CHECK (VALUE > 0)");
        ExecuteSuccessError(statSchemaOwner, statOther, "ALTER DOMAIN S.D DROP CONSTRAINT S.D_C");
        ExecuteSuccessError(statSchemaOwner, statOther, "ALTER DOMAIN S.D RENAME TO S.D2");
        ExecuteSuccessError(statSchemaOwner, statOther, "DROP DOMAIN S.D2");
        ExecuteSuccessError(statSchemaOwner, statOther, "CREATE CONSTANT S.C VALUE 1");
        ExecuteSuccessError(statSchemaOwner, statOther, "DROP CONSTANT S.C");
        ExecuteSuccessErrorAdmin(statAdmin, statSchemaAdmin,
            "CREATE ALIAS S.F FOR 'java.lang.Math.max(long,long)'");
        ExecuteSuccessError(statSchemaOwner, statOther, "DROP ALIAS S.F");
        ExecuteSuccessErrorAdmin(statAdmin, statSchemaAdmin,
            "CREATE AGGREGATE S.A FOR 'org.h2.test.db.TestFunctions$MedianStringType'");
        ExecuteSuccessError(statSchemaOwner, statOther, "DROP AGGREGATE S.A");
        ExecuteSuccessError(statSchemaOwner, statOther, "CREATE SEQUENCE S.S");
        ExecuteSuccessError(statSchemaOwner, statOther, "ALTER SEQUENCE S.S RESTART WITH 2");
        ExecuteSuccessError(statSchemaOwner, statOther, "DROP SEQUENCE S.S");
        ExecuteSuccessError(statSchemaOwner, statOther, "CREATE VIEW S.V AS SELECT 1");
        ExecuteSuccessError(statSchemaOwner, statOther, "ALTER VIEW S.V RECOMPILE");
        ExecuteSuccessError(statSchemaOwner, statOther, "ALTER VIEW S.V RENAME TO S.V2");
        ExecuteSuccessError(statSchemaOwner, statOther, "DROP VIEW S.V2");
        ExecuteSuccessError(statSchemaOwner, statOther, "CREATE TABLE S.T(ID INT)");
        ExecuteSuccessError(statSchemaOwner, statOther, "ALTER TABLE S.T ADD V INT");
        ExecuteSuccessError(statSchemaOwner, statOther,
            "ALTER TABLE S.T ADD CONSTRAINT S.T_C UNIQUE(V)");
        ExecuteSuccessError(statSchemaOwner, statOther, "ALTER TABLE S.T DROP CONSTRAINT S.T_C");
        ExecuteSuccessError(statSchemaOwner, statOther, "CREATE UNIQUE INDEX S.I ON S.T(V)");
        ExecuteSuccessError(statSchemaOwner, statOther, "ALTER INDEX S.I RENAME TO S.I2");
        ExecuteSuccessError(statSchemaOwner, statOther, "DROP INDEX S.I2");
        ExecuteSuccessErrorAdmin(statAdmin, statSchemaAdmin,
            "CREATE TRIGGER S.G BEFORE INSERT ON S.T FOR EACH ROW CALL 'org.h2.test.db.TestRights$TestTrigger'");
        ExecuteSuccessError(statSchemaOwner, statOther, "DROP TRIGGER S.G");
        ExecuteSuccessError(statSchemaOwner, statOther, "GRANT SELECT ON S.T TO OTHER");
        ExecuteSuccessError(statSchemaOwner, statOther, "REVOKE SELECT ON S.T FROM OTHER");
        ExecuteSuccessError(statSchemaOwner, statOther, "ALTER TABLE S.T RENAME TO S.T2");
        ExecuteSuccessError(statSchemaOwner, statOther, "DROP TABLE S.T2");
        ExecuteSuccessError(statSchemaOwner, statOther, "DROP SCHEMA S");
    }

    private void TestAdminAndSchemaOwner(DbCommand statAdmin, DbCommand statSchemaAdmin)
    {
        ExecuteSuccessErrorAdmin(statAdmin, statSchemaAdmin, "GRANT ALTER ANY SCHEMA TO OTHER");
        ExecuteSuccessErrorAdmin(statAdmin, statSchemaAdmin, "REVOKE ALTER ANY SCHEMA FROM OTHER");
        ExecuteSuccessErrorAdmin(statAdmin, statSchemaAdmin, "CREATE USER U PASSWORD '1'");
        ExecuteSuccessErrorAdmin(statAdmin, statSchemaAdmin, "CREATE ROLE R");
        ExecuteSuccessErrorAdmin(statAdmin, statSchemaAdmin, "GRANT R TO U");
        ExecuteSuccessErrorAdmin(statAdmin, statSchemaAdmin, "REVOKE R FROM U");
        ExecuteSuccessErrorAdmin(statAdmin, statSchemaAdmin, "DROP USER U");
        ExecuteSuccessErrorAdmin(statAdmin, statSchemaAdmin, "DROP ROLE R");
    }

    /// <summary>
    /// A trigger used by the schema owner test's SQL.
    /// </summary>
    public static class TestTrigger
    {
        // The reference's TestTrigger implements org.h2.api.Trigger with an empty fire;
        // the trigger API is not ported, and the SQL above names the Java class.
    }

    private void ExecuteSuccessErrorAdmin(DbCommand success, DbCommand error, string sql)
    {
        AssertThrows(ErrorCode.AdminRightsRequired, error, sql);
        Execute(success, sql);
    }

    private void ExecuteSuccessError(DbCommand success, DbCommand error, string sql)
    {
        AssertThrows(ErrorCode.NotEnoughRightsFor1, error, sql);
        Execute(success, sql);
    }

    private void ExecuteError(string sql)
    {
        AssertThrows(ErrorCode.NotEnoughRightsFor1, stat!, sql);
    }

    private void ExecuteSuccess(string sql)
    {
        stat!.CommandText = sql;
        using DbDataReader rs = stat.ExecuteReader();
        // this will check if the result set is usable, as the reference checks updatability
        foreach (DbColumn column in rs.GetColumnSchema())
        {
            _ = column.ColumnName;
            _ = column.DataType;
            _ = column.ColumnSize;
            _ = column.AllowDBNull;
        }

        while (rs.Read())
        {
            for (int i = 0; i < rs.FieldCount; i++)
            {
                _ = rs.GetValue(i);
            }
        }
    }
}
