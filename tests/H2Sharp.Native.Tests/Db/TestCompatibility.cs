// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestCompatibility.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements/result sets are ADO.NET commands/readers, java.math
// BigDecimal expectations are decimal values, StandardCharsets.UTF_8 is
// Encoding.UTF8, Locale.ROOT toUpperCase/toLowerCase are the invariant string methods,
// AtomicReference is a shared exception slot, Thread is a .NET task, and
// ResultSetMetaData.getColumnLabel/getColumnName are the reader's GetName.
//
// Skipped (port gaps, documented here instead of a passing assertion):
//  - testColumnAlias: it distinguishes the result-set label from the column name and
//    asserts the column's table and schema names; the reader exposes one name and no
//    table/schema per column.
//  - DatabaseMetaData.getTables in testCaseSensitiveIdentifiers: the ADO layer has no
//    metadata surface, so the same question is asked through INFORMATION_SCHEMA.TABLES.
//  - DatabaseMetaData.stores*Identifiers and the updatable result set fragment of
//    testMySQL (updateString/updateRow), and the getQueryTimeout assertions of
//    testPostgreSQL: JDBC-only surfaces (see the discussion document).
//
// Blocked by the port: the modes rely on grammar forms the parser does not read yet
// (CREATE TABLE ... AS SELECT, IDENTITY, ... — issue #61).
using System.Globalization;
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests the compatibility with other databases.
/// </summary>
public sealed class TestCompatibility : TestDb
{
    private DbConnection conn = null!;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        DeleteDb("compatibility");

        TestCaseSensitiveIdentifiers();

        conn = GetConnection("compatibility");
        TestDomain();
        // testColumnAlias: skipped, see the header.
        TestUniqueIndexSingleNull();
        TestUniqueIndexOracle();
        TestPostgreSQL();
        TestHsqlDb();
        TestMySQL();
        TestConcurrentAutoIncrement();
        TestDB2();
        TestDerby();
        TestSybaseAndMSSQLServer();

        TestUnknownSet();

        conn.Close();
        TestIdentifiers();
        TestIdentifiersCaseInResultSet();
        TestDatabaseToLowerParser();
        TestOldInformationSchema();
        DeleteDb("compatibility");

        TestUnknownURL();
    }

    private void TestCaseSensitiveIdentifiers()
    {
        using DbConnection c = GetConnection(
            "compatibility;DATABASE_TO_UPPER=FALSE;CASE_INSENSITIVE_IDENTIFIERS=TRUE");
        DbCommand stat = c.CreateCommand();
        Execute(stat, "create table test(id int primary key, name varchar) "
            + "as select 1, 'hello'");
        AssertThrows(ErrorCode.TableOrViewAlreadyExists1, stat,
            "create table test(id int primary key, name varchar)");
        AssertThrows(ErrorCode.DuplicateColumnName1, stat,
            "alter table test add column Name varchar");

        // the reference's DatabaseMetaData.getTables(null, null, "test", null)
        stat.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES "
            + "WHERE TABLE_NAME = 'test'";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("test", rs.GetString(0));
        }

        stat.CommandText = "select id, name from test";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertEquals("id", rs.GetName(0));
            AssertEquals("name", rs.GetName(1));
        }

        stat.CommandText = "select Id, Name from Test";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertEquals("id", rs.GetName(0));
            AssertEquals("name", rs.GetName(1));
        }

        stat.CommandText = "select ID, NAME from TEST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertEquals("id", rs.GetName(0));
            AssertEquals("name", rs.GetName(1));
        }

        Execute(stat, "select COUNT(*), count(*), Count(*), Sum(id) from test");
        Execute(stat, "select LENGTH(name), length(name), Length(name) from test");
        Execute(stat, "select t.id from test t group by t.id");
        Execute(stat, "select id from test t group by t.id");
        Execute(stat, "select id from test group by ID");
        Execute(stat, "select id as c from test group by c");
        Execute(stat, "select t.id from test t group by T.ID");
        Execute(stat, "select id from test t group by T.ID");

        Execute(stat, "drop table test");

        stat.CommandText = "select 1e10, 1000000000000000000000e10, 0xfAfBl";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1e10, rs.GetDouble(0));
            AssertEquals(1000000000000000000000e10, rs.GetDouble(1));
            AssertEquals(0xfafbL, rs.GetInt64(2));
            AssertFalse(rs.Read());
        }

        Execute(stat, "create table \"t 1\" (a int, b int)");
        Execute(stat, "create view v as select * from \"t 1\"");
        Execute(stat, "select * from v");
        Execute(stat, "drop view v");
        Execute(stat, "drop table \"t 1\"");
    }

    private void TestDomain()
    {
        if (Config.Memory)
        {
            return;
        }

        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int primary key) as select 1");
        AssertThrows(ErrorCode.DomainAlreadyExists1, stat,
            "create domain int as varchar");
        conn.Close();
        conn = GetConnection("compatibility");
        stat = conn.CreateCommand();
        Execute(stat, "insert into test values(2)");
        Execute(stat, "drop table test");
    }

    private void TestUniqueIndexSingleNull()
    {
        DbCommand stat = conn.CreateCommand();
        string[] modes =
        {
            "PostgreSQL", "MySQL", "HSQLDB", "MSSQLServer", "Derby", "Oracle", "Regular",
        };
        const string multiNull = "PostgreSQL,MySQL,HSQLDB,Oracle,Regular";
        foreach (string mode in modes)
        {
            Execute(stat, "SET MODE " + mode);
            Execute(stat, "CREATE TABLE TEST(ID INT)");
            Execute(stat, "CREATE UNIQUE INDEX IDX_ID_U ON TEST(ID)");
            try
            {
                Execute(stat, "INSERT INTO TEST VALUES(1), (2), (NULL), (NULL)");
                AssertTrue(mode + " mode should not support multiple NULL",
                    multiNull.Contains(mode, StringComparison.Ordinal));
            }
            catch (H2SharpException)
            {
                AssertTrue(mode + " mode should support multiple NULL",
                    !multiNull.Contains(mode, StringComparison.Ordinal));
            }

            Execute(stat, "DROP TABLE TEST");
        }
    }

    private void TestUniqueIndexOracle()
    {
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "SET MODE ORACLE");
        Execute(stat, "create table t2(c1 int, c2 int)");
        Execute(stat, "create unique index i2 on t2(c1, c2)");
        Execute(stat, "insert into t2 values (null, 1)");
        AssertThrows(ErrorCode.DuplicateKey1, stat,
            "insert into t2 values (null, 1)");
        Execute(stat, "insert into t2 values (null, null)");
        Execute(stat, "insert into t2 values (null, null)");
        Execute(stat, "insert into t2 values (1, null)");
        AssertThrows(ErrorCode.DuplicateKey1, stat,
            "insert into t2 values (1, null)");
        Execute(stat, "DROP TABLE T2");
    }

    private void TestHsqlDb()
    {
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "set mode hsqldb");
        TestLog(Math.Log(10), stat);

        Execute(stat, "DROP TABLE TEST IF EXISTS; "
            + "CREATE TABLE TEST(ID INT PRIMARY KEY); ");
        Execute(stat, "CALL CURRENT_TIME");
        Execute(stat, "CALL CURRENT_TIMESTAMP");
        Execute(stat, "CALL CURRENT_DATE");
        Execute(stat, "CALL SYSDATE");
        Execute(stat, "CALL TODAY");

        Execute(stat, "DROP TABLE TEST IF EXISTS");
    }

    private void TestLog(double expected, DbCommand stat)
    {
        Execute(stat, "create table log(id int)");
        Execute(stat, "insert into log values(1)");
        stat.CommandText = "select log(10) from log";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            rs.Read();
            AssertEquals((int)(expected * 100), (int)(rs.GetDouble(0) * 100));
        }

        stat.CommandText = "select ln(10) from log";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            rs.Read();
            AssertEquals((int)(Math.Log(10) * 100), (int)(rs.GetDouble(0) * 100));
        }

        Execute(stat, "drop table log");
    }

    private void TestPostgreSQL()
    {
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "SET MODE PostgreSQL");
        TestLog(Math.Log10(10), stat);

        AssertResult("ABC", stat, "SELECT SUBSTRING('ABCDEF' FOR 3)");
        AssertResult("ABCD", stat, "SELECT SUBSTRING('0ABCDEF' FROM 2 FOR 4)");

        /* --------- Behaviour of CHAR(N) --------- */

        /* Test right-padding of CHAR(N) at INSERT */
        Execute(stat, "CREATE TABLE TEST(CH CHAR(10))");
        Execute(stat, "INSERT INTO TEST (CH) VALUES ('Hello')");
        AssertResult("Hello     ", stat, "SELECT CH FROM TEST");

        /* Test that WHERE clauses accept unpadded values and will pad before comparison */
        AssertResult("Hello     ", stat, "SELECT CH FROM TEST WHERE CH = 'Hello'");

        /* Test CHAR which is identical to CHAR(1) */
        Execute(stat, "DROP TABLE IF EXISTS TEST");
        Execute(stat, "CREATE TABLE TEST(CH CHAR)");
        Execute(stat, "INSERT INTO TEST (CH) VALUES ('')");
        AssertResult(" ", stat, "SELECT CH FROM TEST");
        AssertResult(" ", stat, "SELECT CH FROM TEST WHERE CH = ''");

        /* Test that excessive spaces are trimmed */
        Execute(stat, "DELETE FROM TEST");
        Execute(stat, "INSERT INTO TEST (CH) VALUES ('1   ')");
        AssertResult("1", stat, "SELECT CH FROM TEST");
        AssertResult("1", stat, "SELECT CH FROM TEST WHERE CH = '1      '");

        /* Test that we do not trim too far */
        Execute(stat, "DROP TABLE IF EXISTS TEST");
        Execute(stat, "CREATE TABLE TEST(CH CHAR(2))");
        Execute(stat, "INSERT INTO TEST (CH) VALUES ('1   ')");
        AssertResult("1 ", stat, "SELECT CH FROM TEST");
        AssertResult("1 ", stat, "SELECT CH FROM TEST WHERE CH = '1      '");

        /* --------- Disallowed column types --------- */

        string[] disallowedTypes = { "NUMBER", "IDENTITY", "TINYINT", "BLOB" };
        foreach (string type in disallowedTypes)
        {
            Execute(stat, "DROP TABLE IF EXISTS TEST");
            AssertThrows(ErrorCode.UnknownDataType1, stat,
                "CREATE TABLE TEST(COL " + type + ")");
        }

        /* Test MONEY data type */
        Execute(stat, "DROP TABLE IF EXISTS TEST");
        Execute(stat, "CREATE TABLE TEST(M MONEY)");
        Execute(stat, "INSERT INTO TEST(M) VALUES (-92233720368547758.08)");
        Execute(stat, "INSERT INTO TEST(M) VALUES (0.11111)");
        Execute(stat, "INSERT INTO TEST(M) VALUES (92233720368547758.07)");
        stat.CommandText = "SELECT M FROM TEST ORDER BY M";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(-92233720368547758.08m, rs.GetDecimal(0));
            AssertTrue(rs.Read());
            AssertEquals(0.11m, rs.GetDecimal(0));
            AssertTrue(rs.Read());
            AssertEquals(92233720368547758.07m, rs.GetDecimal(0));
            AssertFalse(rs.Read());
        }

        /* Test SET STATEMENT_TIMEOUT: the getQueryTimeout assertions are JDBC-only
           (the ADO command timeout is not wired to the engine yet). */
        conn.Close();
        DeleteDb("compatibility");
        conn = GetConnection("compatibility;MODE=PostgreSQL");
        stat = conn.CreateCommand();
        Execute(stat, "SET STATEMENT_TIMEOUT TO 30000");
    }

    private void TestMySQL()
    {
        // need to reconnect to change DATABASE_TO_LOWER
        conn.Close();
        DeleteDb("compatibility");
        conn = GetConnection("compatibility;MODE=MYSQL;DATABASE_TO_LOWER=TRUE");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create schema test_schema");
        Execute(stat, "use test_schema");
        AssertResult("test_schema", stat, "select schema()");
        Execute(stat, "use public");
        AssertResult("public", stat, "select schema()");

        Execute(stat, "SELECT 1");
        Execute(stat, "DROP TABLE IF EXISTS TEST");
        Execute(stat, "CREATE TABLE `TEST`(ID INT PRIMARY KEY, NAME VARCHAR)");
        Execute(stat, "INSERT INTO TEST VALUES(1, 'Hello'), (2, 'World')");
        AssertResult(null, stat, "SELECT UNIX_TIMESTAMP(NULL)");
        AssertResult("0", stat, "SELECT UNIX_TIMESTAMP('1970-01-01 00:00:00Z')");
        AssertResult("1196418619", stat, "SELECT UNIX_TIMESTAMP('2007-11-30 10:30:19Z')");
        AssertResult("1196418619", stat, "SELECT UNIX_TIMESTAMP(FROM_UNIXTIME(1196418619))");
        AssertResult("2007 November", stat, "SELECT FROM_UNIXTIME(1196300000, '%Y %M')");
        AssertResult("2003-12-31", stat, "SELECT DATE('2003-12-31 11:02:03')");
        AssertResult(null, stat, "SELECT DATE('100')");
        // check the weird MySQL variant of DELETE
        Execute(stat, "DELETE TEST FROM TEST WHERE 1=2");

        // Check conversion between VARCHAR and VARBINARY
        const string text = "ABCD\u1234";
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        Execute(stat, "CREATE TABLE TEST2(C VARCHAR, B VARBINARY)");
        Execute(stat, "INSERT INTO TEST2(C) VALUES ('" + text + "')");
        AssertEquals(1, ExecuteUpdate(stat, "UPDATE TEST2 SET B = C"));
        stat.CommandText = "SELECT B FROM TEST2";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(bytes, rs.GetFieldValue<byte[]>(0));
            AssertEquals(bytes, rs.GetFieldValue<byte[]>(rs.GetOrdinal("B")));
        }

        AssertEquals(1, ExecuteUpdate(stat, "UPDATE TEST2 SET C = B"));
        TestMySqlBytesCheck(stat, text, bytes);
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "UPDATE TEST2 SET C = ?";
            prep.Parameters.Add(new H2Parameter { Value = bytes });
            AssertEquals(1, prep.ExecuteNonQuery());
        }

        TestMySqlBytesCheck(stat, text, bytes);
        Execute(stat, "DELETE FROM TEST2");
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "INSERT INTO TEST2(C) VALUES (?)";
            prep.Parameters.Add(new H2Parameter { Value = bytes });
            AssertEquals(1, prep.ExecuteNonQuery());
        }

        TestMySqlBytesCheck(stat, text, bytes);
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "SELECT C FROM TEST2 WHERE C = ?";
            prep.Parameters.Add(new H2Parameter { Value = bytes });
            using DbDataReader rs = prep.ExecuteReader();
            TestMySqlBytesCheck(rs, text, bytes);
        }

        Execute(stat, "CREATE INDEX TEST2_C ON TEST2(C)");
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "SELECT C FROM TEST2 WHERE C = ?";
            prep.Parameters.Add(new H2Parameter { Value = bytes });
            using DbDataReader rs = prep.ExecuteReader();
            TestMySqlBytesCheck(rs, text, bytes);
        }

        Execute(stat, "DROP TABLE TEST2");

        if (!Config.Memory)
        {
            // need to reconnect, because meta data tables may be initialized
            conn.Close();
            conn = GetConnection("compatibility;MODE=MYSQL;DATABASE_TO_LOWER=TRUE");
            stat = conn.CreateCommand();
            TestLog(Math.Log(10), stat);

            // DatabaseMetaData.stores*Identifiers and the updatable result set
            // fragment are JDBC-only, see the header.

            AssertResult("test", stat, "SHOW TABLES");
            Execute(stat, "SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME > 'aaaa'");
            Execute(stat, "SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME < 'aaaa'");

            Execute(stat, "CREATE TABLE TEST_1"
                + "(ID INT PRIMARY KEY) ENGINE=InnoDb");
            Execute(stat, "CREATE TABLE TEST_2"
                + "(ID INT PRIMARY KEY) ENGINE=MyISAM");
            Execute(stat, "CREATE TABLE TEST_3"
                + "(ID INT PRIMARY KEY) ENGINE=InnoDb charset=UTF8");
            Execute(stat, "CREATE TABLE TEST_4"
                + "(ID INT PRIMARY KEY) charset=UTF8");
            Execute(stat, "CREATE TABLE TEST_5"
                + "(ID INT AUTO_INCREMENT PRIMARY KEY) ENGINE=InnoDb auto_increment=3 default charset=UTF8");
            Execute(stat, "CREATE TABLE TEST_6"
                + "(ID INT AUTO_INCREMENT PRIMARY KEY) "
                + "ENGINE=MyISAM default character set UTF8MB4, auto_increment 3");
            Execute(stat, "CREATE TABLE TEST_7"
                + "(ID INT AUTO_INCREMENT PRIMARY KEY) ENGINE=InnoDb auto_increment=3 charset=UTF8 comment 'text'");
            Execute(stat, "CREATE TABLE TEST_8"
                + "(ID INT AUTO_INCREMENT PRIMARY KEY) ENGINE=InnoDb auto_increment=3 character set=UTF8");
            Execute(stat, "CREATE TABLE TEST_9"
                + "(ID INT, KEY TEST_7_IDX(ID) USING BTREE)");
            Execute(stat, "CREATE TABLE TEST_10"
                + "(ID INT, UNIQUE KEY TEST_10_IDX(ID) USING BTREE)");
            Execute(stat, "CREATE TABLE TEST_11(ID INT) COLLATE UTF8");
            Execute(stat, "CREATE TABLE TEST_12(ID INT) DEFAULT COLLATE UTF8");
            Execute(stat, "CREATE TABLE TEST_13(a VARCHAR(10) COLLATE UTF8MB4)");
            Execute(stat, "CREATE TABLE TEST_14(a VARCHAR(10) NULL CHARACTER SET UTF8MB4 COLLATE UTF8MB4_BIN)");
            Execute(stat, "ALTER TABLE TEST_14 CONVERT TO CHARACTER SET UTF8MB4 COLLATE UTF8MB4_UNICODE_CI");
            Execute(stat, "ALTER TABLE TEST_14 MODIFY a VARCHAR(10) NOT NULL CHARACTER SET UTF8MB4 COLLATE UTF8");
            AssertThrows(ErrorCode.SyntaxError1, stat, "CREATE TABLE TEST_99"
                + "(ID INT PRIMARY KEY) CHARSET UTF8,");
            AssertThrows(ErrorCode.ColumnNotFound1, stat, "CREATE TABLE TEST_99"
                + "(ID INT PRIMARY KEY) AUTO_INCREMENT 100");
            AssertThrows(ErrorCode.ColumnNotFound1, stat, "CREATE TABLE TEST_99"
                + "(ID INT) AUTO_INCREMENT 100");

            // this maps to SET REFERENTIAL_INTEGRITY TRUE/FALSE
            Execute(stat, "SET foreign_key_checks = 0");
            Execute(stat, "SET foreign_key_checks = 1");

            // Check if mysql comments are supported, ensure clean connection
            conn.Close();
            conn = GetConnection("compatibility;MODE=MYSQL;DATABASE_TO_LOWER=TRUE");
            stat = conn.CreateCommand();
            Execute(stat, "DROP TABLE IF EXISTS TEST_NO_COMMENT");
            Execute(stat, "CREATE table TEST_NO_COMMENT "
                + "(ID bigint not null auto_increment, "
                + "SOME_STR varchar(255), primary key (ID))");
            // now test creating a table with a comment
            Execute(stat, "DROP TABLE IF EXISTS TEST_COMMENT");
            Execute(stat, "create table TEST_COMMENT (ID bigint not null auto_increment, "
                + "SOME_STR varchar(255), primary key (ID)) comment='Some comment.'");
            // now test creating a table with a comment and engine
            // and other typical mysql stuff as generated by hibernate
            Execute(stat, "DROP TABLE IF EXISTS TEST_COMMENT_ENGINE");
            Execute(stat, "create table TEST_COMMENT_ENGINE "
                + "(ID bigint not null auto_increment, "
                + "ATTACHMENT_ID varchar(255), "
                + "SOME_ITEM_ID bigint not null, primary key (ID), "
                + "unique (ATTACHMENT_ID, SOME_ITEM_ID)) "
                + "comment='Comment Again' ENGINE=InnoDB");

            Execute(stat, "CREATE TABLE TEST2(ID INT) ROW_FORMAT=DYNAMIC");

            // check the MySQL index dropping syntax
            Execute(stat, "ALTER TABLE TEST_COMMENT_ENGINE ADD CONSTRAINT CommentUnique UNIQUE (SOME_ITEM_ID)");
            Execute(stat, "ALTER TABLE TEST_COMMENT_ENGINE DROP INDEX CommentUnique");
            Execute(stat, "CREATE INDEX IDX_ATTACHMENT_ID ON TEST_COMMENT_ENGINE (ATTACHMENT_ID)");
            Execute(stat, "DROP INDEX IDX_ATTACHMENT_ID ON TEST_COMMENT_ENGINE");

            Execute(stat, "DROP ALL OBJECTS");
        }

        conn.Close();
        DeleteDb("compatibility");
        conn = GetConnection("compatibility");
    }

    private void TestMySqlBytesCheck(DbCommand stat, string text, byte[] bytes)
    {
        stat.CommandText = "SELECT C FROM TEST2";
        using DbDataReader rs = stat.ExecuteReader();
        TestMySqlBytesCheck(rs, text, bytes);
    }

    private void TestMySqlBytesCheck(DbDataReader rs, string text, byte[] bytes)
    {
        AssertTrue(rs.Read());
        AssertEquals(text, rs.GetString(0));
        AssertEquals(bytes, rs.GetFieldValue<byte[]>(0));
        AssertEquals(bytes, rs.GetFieldValue<byte[]>(rs.GetOrdinal("C")));
    }

    private void TestConcurrentAutoIncrement()
    {
        const int nThreads = 50;
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "SET MODE MySQL;");
        Execute(stat, "CREATE TABLE TEST(ID INT AUTO_INCREMENT PRIMARY KEY, V INT)");
        Exception?[] errors = new Exception?[1];
        Task[] threads = new Task[nThreads];
        try
        {
            for (int i = 0; i < nThreads; i++)
            {
                threads[i] = Task.Run(() =>
                {
                    try
                    {
                        using DbConnection c = GetConnection("compatibility;MODE=MYSQL");
                        using DbCommand ps = c.CreateCommand();
                        ps.CommandText = "INSERT INTO TEST(V) VALUES (?)";
                        ps.Parameters.Add(new H2Parameter());
                        for (int j = 0; j < 1000 && Volatile.Read(ref errors[0]) is null; j++)
                        {
                            ps.Parameters[0].Value = j;
                            _ = ps.ExecuteNonQuery();
                        }
                    }
                    catch (Exception e)
                    {
                        Interlocked.CompareExchange(ref errors[0], e, null);
                    }
                });
            }

            Task.WaitAll(threads);
        }
        finally
        {
            Execute(stat, "DROP TABLE TEST");
            if (errors[0] is { } e)
            {
                throw e;
            }
        }
    }

    private void TestSybaseAndMSSQLServer()
    {
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "SET MODE MSSQLServer");
        Execute(stat, "DROP TABLE IF EXISTS TEST");
        Execute(stat, "CREATE TABLE TEST(NAME VARCHAR(50), SURNAME VARCHAR(50))");
        Execute(stat, "INSERT INTO TEST VALUES('John', 'Doe')");
        Execute(stat, "INSERT INTO TEST VALUES('Jack', 'Sullivan')");

        AssertResult("abcd123", stat, "SELECT 'abc' + 'd123'");

        AssertResult("Doe, John", stat,
            "SELECT surname + ', ' + name FROM test "
            + "WHERE SUBSTRING(NAME,1,1)+SUBSTRING(SURNAME,1,1) = 'JD'");

        Execute(stat, "ALTER TABLE TEST ADD COLUMN full_name VARCHAR(100)");
        Execute(stat, "UPDATE TEST SET full_name = name + ', ' + surname");
        AssertResult("John, Doe", stat, "SELECT full_name FROM TEST where name='John'");

        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "INSERT INTO TEST VALUES(?, ?, ? + ', ' + ?)";
            int ca = 0;
            foreach (string value in new[] { "Paul", "Frank", "Paul", "Frank" })
            {
                prep.Parameters.Add(new H2Parameter { Value = value });
                ca++;
            }

            AssertEquals(4, ca);
            _ = prep.ExecuteNonQuery();
        }

        AssertResult("Paul, Frank", stat, "SELECT full_name FROM test "
            + "WHERE name = 'Paul'");

        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "SELECT ? + ?";
            prep.Parameters.Add(new H2Parameter { Value = "abcd123" });
            prep.Parameters.Add(new H2Parameter { Value = "d123" });
            using DbDataReader rs = prep.ExecuteReader();
            while (rs.Read())
            {
                // just loop
            }
        }

        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "SELECT full_name FROM test "
                + "WHERE (SUBSTRING(name, 1, 1) + SUBSTRING(surname, 2, 3)) = ?";
            prep.Parameters.Add(new H2Parameter { Value = "Joe" });
            using DbDataReader rs = prep.ExecuteReader();
            AssertTrue("Result cannot be empty", rs.Read());
            AssertEquals("John, Doe", rs.GetString(0));
        }

        // CONVERT has it's parameters the other way around from the default mode
        stat.CommandText = "SELECT CONVERT(INT, '10')";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            rs.Read();
            AssertEquals(10, rs.GetInt32(0));
        }

        stat.CommandText = "SELECT X FROM (SELECT CONVERT(INT, '10') AS X)";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            rs.Read();
            AssertEquals(10, rs.GetInt32(0));
        }

        // make sure we're ignoring the index part of the statement
        Execute(stat, "select * from test (index table1_index)");

        // UNIQUEIDENTIFIER is MSSQL's equivalent of UUID
        Execute(stat, "create table test3 (id UNIQUEIDENTIFIER)");

        /* Test MONEY data type */
        Execute(stat, "DROP TABLE IF EXISTS TEST");
        Execute(stat, "CREATE TABLE TEST(M MONEY)");
        Execute(stat, "INSERT INTO TEST(M) VALUES (-922337203685477.5808)");
        Execute(stat, "INSERT INTO TEST(M) VALUES (0.11111)");
        Execute(stat, "INSERT INTO TEST(M) VALUES (922337203685477.5807)");
        stat.CommandText = "SELECT M FROM TEST ORDER BY M";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(-922337203685477.5808m, rs.GetDecimal(0));
            AssertTrue(rs.Read());
            AssertEquals(0.1111m, rs.GetDecimal(0));
            AssertTrue(rs.Read());
            AssertEquals(922337203685477.5807m, rs.GetDecimal(0));
            AssertFalse(rs.Read());
        }

        /* Test SMALLMONEY data type */
        Execute(stat, "DROP TABLE IF EXISTS TEST");
        Execute(stat, "CREATE TABLE TEST(M SMALLMONEY)");
        Execute(stat, "INSERT INTO TEST(M) VALUES (-214748.3648)");
        Execute(stat, "INSERT INTO TEST(M) VALUES (0.11111)");
        Execute(stat, "INSERT INTO TEST(M) VALUES (214748.3647)");
        stat.CommandText = "SELECT M FROM TEST ORDER BY M";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(-214748.3648m, rs.GetDecimal(0));
            AssertTrue(rs.Read());
            AssertEquals(0.1111m, rs.GetDecimal(0));
            AssertTrue(rs.Read());
            AssertEquals(214748.3647m, rs.GetDecimal(0));
            AssertFalse(rs.Read());
        }
    }

    private void TestDB2()
    {
        conn.Close();
        conn = GetConnection("compatibility;MODE=DB2");
        DbCommand stat = conn.CreateCommand();
        TestLog(Math.Log(10), stat);

        stat.CommandText = "SELECT 1 FROM sysibm.sysdummy1";
        using (DbDataReader res = stat.ExecuteReader())
        {
            res.Read();
            AssertEquals("1", res.GetString(0));
        }

        conn.Close();
        conn = GetConnection("compatibility;MODE=MySQL");
        AssertThrows(ErrorCode.SchemaNotFound1, conn.CreateCommand(),
            "SELECT 1 FROM sysibm.sysdummy1");
        conn.Close();
        conn = GetConnection("compatibility;MODE=DB2");
        stat = conn.CreateCommand();
        Execute(stat, "drop table test if exists");
        Execute(stat, "create table test(id varchar)");
        Execute(stat, "insert into test values ('3'),('1'),('2')");
        stat.CommandText = "select id from test order by id fetch next 2 rows only";
        using (DbDataReader res = stat.ExecuteReader())
        {
            res.Read();
            AssertEquals("1", res.GetString(0));
            res.Read();
            AssertEquals("2", res.GetString(0));
            AssertFalse(res.Read());
        }

        conn.Close();

        // test isolation-clause
        conn = GetConnection("compatibility;MODE=DB2");
        stat = conn.CreateCommand();
        Execute(stat, "drop table test if exists");
        Execute(stat, "create table test(id varchar)");
        Execute(stat, "select * from test with ur");
        Execute(stat, "insert into test values (1) with ur");
        Execute(stat, "select * from test where id = 1 with rr");
        Execute(stat, "select * from test order by id fetch next 2 rows only with rr");
        Execute(stat, "select * from test order by id fetch next 2 rows only with rs");
        Execute(stat, "select * from test order by id fetch next 2 rows only with cs");
        Execute(stat, "select * from test order by id fetch next 2 rows only with ur");
        // test isolation-clause with lock-request-clause
        Execute(stat, "select * from test order by id "
            + "fetch next 2 rows only with rr use and keep share locks");
        Execute(stat, "select * from test order by id "
            + "fetch next 2 rows only with rs use and keep update locks");
        Execute(stat, "select * from test order by id "
            + "fetch next 2 rows only with rr use and keep exclusive locks");

        // Test DB2 TIMESTAMP format with dash separating date and time
        Execute(stat, "drop table test if exists");
        Execute(stat, "create table test(date TIMESTAMP)");
        Execute(stat, "insert into test (date) values ('2014-04-05-09.48.28.020005')");
        AssertResult("2014-04-05 09:48:28.020005", stat,
            "select date from test"); // <- result is always H2 format timestamp!
        AssertResult("2014-04-05 09:48:28.020005", stat,
            "select date from test where date = '2014-04-05-09.48.28.020005'");
        AssertResult("2014-04-05 09:48:28.020005", stat,
            "select date from test where date = '2014-04-05 09:48:28.020005'");

        // Test limited support for DB2's special registers

        // Standard SQL functions like LOCALTIMESTAMP, CURRENT_TIMESTAMP and
        // others are used to compare values, their implementation in H2 is
        // compatible with standard, but may be not really compatible with DB2.
        AssertResult("TRUE", stat, "SELECT LOCALTIMESTAMP = CURRENT TIMESTAMP");
        AssertResult("TRUE", stat, "SELECT CAST(LOCALTIMESTAMP AS VARCHAR) = CAST(CURRENT TIMESTAMP AS VARCHAR)");
        AssertResult("TRUE", stat, "SELECT CURRENT_TIMESTAMP = CURRENT TIMESTAMP WITH TIME ZONE");
        AssertResult("TRUE", stat,
            "SELECT CAST(CURRENT_TIMESTAMP AS VARCHAR) = CAST(CURRENT TIMESTAMP WITH TIME ZONE AS VARCHAR)");
        AssertResult("TRUE", stat, "SELECT CURRENT_TIME = CURRENT TIME");
        AssertResult("TRUE", stat, "SELECT CURRENT_DATE = CURRENT DATE");
    }

    private void TestDerby()
    {
        conn.Close();
        conn = GetConnection("compatibility;MODE=Derby");
        DbCommand stat = conn.CreateCommand();
        TestLog(Math.Log(10), stat);

        stat.CommandText = "SELECT 1 FROM sysibm.sysdummy1 fetch next 1 row only";
        using (DbDataReader res = stat.ExecuteReader())
        {
            res.Read();
            AssertEquals("1", res.GetString(0));
        }

        conn.Close();
        conn = GetConnection("compatibility;MODE=PostgreSQL");
        AssertThrows(ErrorCode.SchemaNotFound1, conn.CreateCommand(),
            "SELECT 1 FROM sysibm.sysdummy1");
        conn.Close();
        conn = GetConnection("compatibility");
    }

    private void TestUnknownSet()
    {
        AssertThrows(ErrorCode.UnknownMode1, conn.CreateCommand(),
            "SET MODE UnknownMode");
    }

    private void TestIdentifiers()
    {
        DeleteDb("compatibility");
        TestIdentifiers(false, false, false);
        TestIdentifiers(false, false, true);
        TestIdentifiers(true, false, false);
        TestIdentifiers(true, false, true);
        TestIdentifiers(false, true, false);
        TestIdentifiers(false, true, true);
    }

    private void TestIdentifiers(bool upper, bool lower, bool caseInsensitiveIdentifiers)
    {
        try
        {
            using DbConnection c = GetConnection("compatibility;DATABASE_TO_UPPER=" + upper
                + ";DATABASE_TO_LOWER=" + lower
                + ";CASE_INSENSITIVE_IDENTIFIERS=" + caseInsensitiveIdentifiers);
            DbCommand stat = c.CreateCommand();
            Execute(stat, "CREATE TABLE Test(Id INT) AS VALUES 2");
            string schema = "PUBLIC", table = "Test", column = "Id";
            if (upper)
            {
                table = table.ToUpperInvariant();
                column = column.ToUpperInvariant();
            }
            else if (lower)
            {
                schema = schema.ToLowerInvariant();
                table = table.ToLowerInvariant();
                column = column.ToLowerInvariant();
            }

            stat.CommandText = "SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME"
                + " FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME ILIKE 'Test'";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(schema, rs.GetString(0));
                AssertEquals(table, rs.GetString(1));
                AssertEquals(column, rs.GetString(2));
            }

            TestIdentifiers(stat, "Test", "Id", true);
            TestIdentifiers(stat, "`Test`", "`Id`", true);
            bool ok = upper || lower || caseInsensitiveIdentifiers;
            TestIdentifiers(stat, "TEST", "ID", ok);
            TestIdentifiers(stat, "`TEST`", "`ID`", ok);
            TestIdentifiers(stat, "test", "id", ok);
            TestIdentifiers(stat, "`test`", "`id`", ok);
            TestIdentifiers(stat, '"' + table + '"', '"' + column + '"', true);
            TestIdentifiers(stat, "\"TeSt\"", "\"iD\"", caseInsensitiveIdentifiers);
            Execute(stat, "CREATE TABLE T2(\"`\" INT, `\"'\"` INT) AS VALUES (1, 2)");
            stat.CommandText = "SELECT ````, \"\"\"'\"\"\" FROM T2";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(1, rs.GetInt32(0));
                AssertEquals(2, rs.GetInt32(1));
            }
        }
        finally
        {
            DeleteDb("compatibility");
        }
    }

    private void TestIdentifiers(DbCommand stat, string table, string column, bool ok)
    {
        string query = "SELECT _ROWID_, " + column + " FROM " + table;
        if (ok)
        {
            stat.CommandText = query;
            using DbDataReader rs = stat.ExecuteReader();
            AssertTrue(rs.Read());
            AssertEquals(1L, rs.GetInt64(0));
            AssertEquals(2, rs.GetInt32(1));
        }
        else
        {
            AssertThrows(ErrorCode.TableOrViewNotFoundWithCandidates2, stat, query);
        }
    }

    private void TestUnknownURL()
    {
        AssertThrows(ErrorCode.UnknownMode1, () =>
        {
            GetConnection("compatibility;MODE=Unknown").Close();
            DeleteDb("compatibility");
        });
    }

    private void TestIdentifiersCaseInResultSet()
    {
        try
        {
            using DbConnection conn = GetConnection(
                "compatibility;DATABASE_TO_UPPER=FALSE;CASE_INSENSITIVE_IDENTIFIERS=TRUE");
            DbCommand stat = conn.CreateCommand();
            Execute(stat, "CREATE TABLE TEST(A INT)");
            stat.CommandText = "SELECT a from test";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertEquals("A", rs.GetName(0));
            }

            stat.CommandText = "SELECT a FROM (SELECT 1) t(A)";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertEquals("A", rs.GetName(0));
            }
        }
        finally
        {
            DeleteDb("compatibility");
        }
    }

    private void TestDatabaseToLowerParser()
    {
        try
        {
            using DbConnection conn = GetConnection("compatibility;DATABASE_TO_LOWER=TRUE");
            DbCommand stat = conn.CreateCommand();
            stat.CommandText = "SELECT 0x1234567890AbCdEf";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                rs.Read();
                AssertEquals(0x1234567890ABCDEFL, rs.GetInt64(0));
            }
        }
        finally
        {
            DeleteDb("compatibility");
        }
    }

    private void TestOldInformationSchema()
    {
        try
        {
            using DbConnection conn = GetConnection(
                "compatibility;OLD_INFORMATION_SCHEMA=TRUE");
            DbCommand stat = conn.CreateCommand();
            stat.CommandText = "TABLE INFORMATION_SCHEMA.TABLE_TYPES";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                rs.Read();
                AssertEquals("TABLE", rs.GetString(0));
            }
        }
        finally
        {
            DeleteDb("compatibility");
        }
    }

    private static int ExecuteUpdate(DbCommand command, string sql)
    {
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }
}
