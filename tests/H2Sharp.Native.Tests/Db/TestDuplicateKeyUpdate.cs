// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestDuplicateKeyUpdate.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; setString/setInt/setNString are
// parameter values. PreparedStatement.addBatch/executeBatch is the loop of executions an
// ADO.NET caller writes: one execution per parameter set, in order. The reference's
// main() entry is the runner; xUnit runs Test() directly.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests for the ON DUPLICATE KEY UPDATE in the Insert class.
/// </summary>
public sealed class TestDuplicateKeyUpdate : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("duplicateKeyUpdate");
        using DbConnection conn = GetConnection("duplicateKeyUpdate;MODE=MySQL");
        TestDuplicateOnPrimary(conn);
        TestDuplicateOnUnique(conn);
        TestDuplicateCache(conn);
        TestDuplicateExpression(conn);
        TestOnDuplicateKeyInsertBatch(conn);
        TestOnDuplicateKeyInsertMultiValue(conn);
        TestPrimaryKeyAndUniqueKey(conn);
        TestUpdateCountAndQualifiedNames(conn);
        TestEnum(conn);
        conn.Close();
        DeleteDb("duplicateKeyUpdate");
    }

    private void TestDuplicateOnPrimary(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "CREATE TABLE table_test (\n"
            + "  id bigint(20) NOT NULL ,\n"
            + "  a_text varchar(254) NOT NULL,\n"
            + "  some_text varchar(254) NULL,\n"
            + "  PRIMARY KEY (id)\n"
            + ") ;");

        Execute(stat, "INSERT INTO table_test ( id, a_text, some_text ) VALUES "
            + "(1, 'aaaaaaaaaa', 'aaaaaaaaaa'), "
            + "(2, 'bbbbbbbbbb', 'bbbbbbbbbb'), "
            + "(3, 'cccccccccc', 'cccccccccc'), "
            + "(4, 'dddddddddd', 'dddddddddd'), "
            + "(5, 'eeeeeeeeee', 'eeeeeeeeee')");

        Execute(stat, "INSERT INTO table_test ( id , a_text, some_text ) "
            + "VALUES (1, 'zzzzzzzzzz', 'abcdefghij') "
            + "ON DUPLICATE KEY UPDATE some_text='UPDATE'");

        stat.CommandText = "SELECT some_text FROM table_test where id = 1";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("UPDATE", rs.GetString(0));
        }

        Execute(stat, "INSERT INTO table_test ( id , a_text, some_text ) "
            + "VALUES (3, 'zzzzzzzzzz', 'SOME TEXT') "
            + "ON DUPLICATE KEY UPDATE some_text=values(some_text)");
        stat.CommandText = "SELECT some_text FROM table_test where id = 3";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("SOME TEXT", rs.GetString(0));
        }
    }

    private void TestDuplicateOnUnique(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "CREATE TABLE table_test2 (\n"
            + "  id bigint(20) NOT NULL AUTO_INCREMENT,\n"
            + "  a_text varchar(254) NOT NULL,\n"
            + "  some_text varchar(254) NOT NULL,\n"
            + "  updatable_text varchar(254) NULL,\n"
            + "  PRIMARY KEY (id)\n"
            + ") ;");

        Execute(stat, "CREATE UNIQUE INDEX index_name "
            + "ON table_test2 (a_text, some_text);");

        Execute(stat, "INSERT INTO table_test2 "
            + "( a_text, some_text, updatable_text ) VALUES ('a', 'a', '1')");
        Execute(stat, "INSERT INTO table_test2 "
            + "( a_text, some_text, updatable_text ) VALUES ('b', 'b', '2')");
        Execute(stat, "INSERT INTO table_test2 "
            + "( a_text, some_text, updatable_text ) VALUES ('c', 'c', '3')");
        Execute(stat, "INSERT INTO table_test2 "
            + "( a_text, some_text, updatable_text ) VALUES ('d', 'd', '4')");
        Execute(stat, "INSERT INTO table_test2 "
            + "( a_text, some_text, updatable_text ) VALUES ('e', 'e', '5')");

        Execute(stat, "INSERT INTO table_test2 ( a_text, some_text ) "
            + "VALUES ('e', 'e') ON DUPLICATE KEY UPDATE updatable_text='UPDATE'");

        stat.CommandText = "SELECT updatable_text "
            + "FROM table_test2 where a_text = 'e'";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("UPDATE", rs.GetString(0));
        }

        Execute(stat, "INSERT INTO table_test2 (a_text, some_text, updatable_text ) "
            + "VALUES ('b', 'b', 'test'), ('c', 'c', 'test2') "
            + "ON DUPLICATE KEY UPDATE updatable_text=values(updatable_text)");
        stat.CommandText = "SELECT updatable_text "
            + "FROM table_test2 where a_text in ('b', 'c') order by a_text";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("test", rs.GetString(0));
            AssertTrue(rs.Read());
            AssertEquals("test2", rs.GetString(0));
        }
    }

    private void TestDuplicateCache(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "CREATE TABLE table_test3 (\n"
            + "  id bigint(20) NOT NULL ,\n"
            + "  a_text varchar(254) NOT NULL,\n"
            + "  some_text varchar(254) NULL,\n"
            + "  PRIMARY KEY (id)\n"
            + ") ;");

        Execute(stat, "INSERT INTO table_test3 ( id, a_text, some_text ) "
            + "VALUES (1, 'aaaaaaaaaa', 'aaaaaaaaaa')");

        Execute(stat, "INSERT INTO table_test3 ( id , a_text, some_text ) "
            + "VALUES (1, 'zzzzzzzzzz', 'SOME TEXT') "
            + "ON DUPLICATE KEY UPDATE some_text=values(some_text)");
        stat.CommandText = "SELECT some_text FROM table_test3 where id = 1";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("SOME TEXT", rs.GetString(0));
        }

        // Execute twice the same query to use the one from cache without
        // parsing, caused the values parameter to be seen as ambiguous
        Execute(stat, "INSERT INTO table_test3 ( id , a_text, some_text ) "
            + "VALUES (1, 'zzzzzzzzzz', 'SOME TEXT') "
            + "ON DUPLICATE KEY UPDATE some_text=values(some_text)");
        stat.CommandText = "SELECT some_text FROM table_test3 where id = 1";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("SOME TEXT", rs.GetString(0));
        }
    }

    private void TestDuplicateExpression(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "CREATE TABLE table_test4 (\n"
            + "  id bigint(20) NOT NULL ,\n"
            + "  a_text varchar(254) NOT NULL,\n"
            + "  some_value int(10) NULL,\n"
            + "  PRIMARY KEY (id)\n"
            + ") ;");

        Execute(stat, "INSERT INTO table_test4 ( id, a_text, some_value ) "
            + "VALUES (1, 'aaaaaaaaaa', 5)");
        Execute(stat, "INSERT INTO table_test4 ( id, a_text, some_value ) "
            + "VALUES (2, 'aaaaaaaaaa', 5)");

        Execute(stat, "INSERT INTO table_test4 ( id , a_text, some_value ) "
            + "VALUES (1, 'b', 1) "
            + "ON DUPLICATE KEY UPDATE some_value=some_value + values(some_value)");
        Execute(stat, "INSERT INTO table_test4 ( id , a_text, some_value ) "
            + "VALUES (1, 'b', 1) "
            + "ON DUPLICATE KEY UPDATE some_value=some_value + 100");
        Execute(stat, "INSERT INTO table_test4 ( id , a_text, some_value ) "
            + "VALUES (2, 'b', 1) "
            + "ON DUPLICATE KEY UPDATE some_value=values(some_value) + 1");
        stat.CommandText = "SELECT some_value FROM table_test4 where id = 1";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(106, rs.GetInt32(0));
        }

        stat.CommandText = "SELECT some_value FROM table_test4 where id = 2";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
        }
    }

    private void TestOnDuplicateKeyInsertBatch(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test "
            + "(id varchar(1) primary key, count int not null)");

        // Insert multiple values as a batch
        for (int i = 0; i <= 2; ++i)
        {
            using DbCommand prep = conn.CreateCommand();
            prep.CommandText = "insert into test(id, count) values(?, ?) "
                + "on duplicate key update count = count + 1";
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters[0].Value = "a";
            prep.Parameters[1].Value = 1;
            _ = prep.ExecuteNonQuery();
            prep.Parameters[0].Value = "b";
            prep.Parameters[1].Value = 1;
            _ = prep.ExecuteNonQuery();
            prep.Parameters[0].Value = "b";
            prep.Parameters[1].Value = 1;
            _ = prep.ExecuteNonQuery();
        }

        // Check result
        stat.CommandText = "select count from test where id = 'a'";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(3, rs.GetInt32(0));
        }

        Execute(stat, "drop table test");
    }

    private void TestOnDuplicateKeyInsertMultiValue(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test"
            + "(id varchar(1) primary key, count int not null)");

        // Insert multiple values in single insert operation
        for (int i = 0; i <= 2; ++i)
        {
            using DbCommand prep = conn.CreateCommand();
            prep.CommandText = "insert into test(id, count) values(?, ?), (?, ?), (?, ?) "
                + "on duplicate key update count = count + 1";
            prep.Parameters.Add(new H2Parameter { Value = "a" });
            prep.Parameters.Add(new H2Parameter { Value = 1 });
            prep.Parameters.Add(new H2Parameter { Value = "b" });
            prep.Parameters.Add(new H2Parameter { Value = 1 });
            prep.Parameters.Add(new H2Parameter { Value = "b" });
            prep.Parameters.Add(new H2Parameter { Value = 1 });
            _ = prep.ExecuteNonQuery();
        }

        // Check result
        stat.CommandText = "select count from test where id = 'a'";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(3, rs.GetInt32(0));
        }

        Execute(stat, "drop table test");
    }

    private void TestPrimaryKeyAndUniqueKey(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE test (id INT, dup INT, "
            + "counter INT, PRIMARY KEY(id), UNIQUE(dup))");
        Execute(stat, "INSERT INTO test (id, dup, counter) VALUES (1, 1, 1)");
        Execute(stat, "INSERT INTO test (id, dup, counter) VALUES (2, 1, 1) "
            + "ON DUPLICATE KEY UPDATE counter = counter + VALUES(counter)");

        // Check result
        stat.CommandText = "SELECT counter FROM test ORDER BY id";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        Execute(stat, "drop table test");
    }

    private void TestUpdateCountAndQualifiedNames(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "set mode mysql");
        Execute(stat, "create schema s2");
        Execute(stat, "create table s2.test(id int primary key, name varchar(255))");
        Execute(stat, "insert into s2.test(id, name) values(1, 'a')");
        stat.CommandText = "insert into s2.test(id, name) values(1, 'b') "
            + "on duplicate key update name = values(name)";
        AssertEquals(2, stat.ExecuteNonQuery());
        stat.CommandText = "insert into s2.test(id, name) values(1, 'b') "
            + "on duplicate key update name = values(name)";
        AssertEquals(0, stat.ExecuteNonQuery());
        stat.CommandText = "insert into s2.test(id, name) values(2, 'c') "
            + "on duplicate key update name = values(name)";
        AssertEquals(1, stat.ExecuteNonQuery());
        stat.CommandText = "select id, name from s2.test order by id";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertEquals("b", rs.GetString(1));
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertEquals("c", rs.GetString(1));
            AssertFalse(rs.Read());
        }

        // Check qualified names in ON UPDATE case
        stat.CommandText = "insert into s2.test(id, name) values(2, 'd') "
            + "on duplicate key update test.name = values(name)";
        AssertEquals(2, stat.ExecuteNonQuery());
        AssertThrows(ErrorCode.TableOrViewNotFound1, stat,
            "insert into s2.test(id, name) values(2, 'd') "
            + "on duplicate key update test2.name = values(name)");
        stat.CommandText = "insert into s2.test(id, name) values(2, 'e') "
            + "on duplicate key update s2.test.name = values(name)";
        AssertEquals(2, stat.ExecuteNonQuery());
        AssertThrows(ErrorCode.SchemaNameMustMatch, stat,
            "insert into s2.test(id, name) values(2, 'd') "
            + "on duplicate key update s3.test.name = values(name)");
        Execute(stat, "drop schema s2 cascade");
    }

    private void TestEnum(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(e enum('a', 'b') unique)");
        using DbCommand ps = conn.CreateCommand();
        ps.CommandText = "insert into test(e) values (?) on duplicate key update e = e";
        ps.Parameters.Add(new H2Parameter { Value = "a" });
        AssertEquals(1, ps.ExecuteNonQuery());
        AssertEquals(0, ps.ExecuteNonQuery());
        Execute(stat, "drop table test");
    }
}
