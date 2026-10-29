// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestTransaction.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands. setAutoCommit(false) is an
// explicit transaction the statements carry, commit()/rollback() are the wrapper's
// commit/rollback with the next transaction begun after; setTransactionIsolation is the
// level the transaction begins with (the JDBC call commits the open transaction first,
// which the mapping does the same way); the savepoints are the transaction's
// Save/Rollback by name. The addBatch/executeBatch pairs are one execution per
// parameter set. The metadata walk of testNestedResultSets is the INFORMATION_SCHEMA
// TABLES/COLUMNS queries through the ADO layer. The reference's main() entry is the
// runner; xUnit runs Test() directly.
//
// Modified (deferred, see MIGRATION_STATUS.md): the same-statement double result set of
// testNestedResultSets (the JDBC Statement closes its previous result set and the old
// one answers OBJECT_CLOSED) has no ADO.NET shape — a command refuses a second open
// reader instead, so the fragment runs two commands and the closed-reader assertion is
// dropped.
using System.Data;
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Transactional tests, including transaction isolation tests, and tests related
/// to savepoints.
/// </summary>
public sealed class TestTransaction : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestClosingConnectionWithSessionTempTable();
        TestClosingConnectionWithLockedTable();
        TestConstraintCreationRollback();
        TestCommitOnAutoCommitChange();
        TestConcurrentSelectForUpdate();
        TestRollback();
        TestRollback2();
        TestForUpdate();
        TestForUpdate2();
        TestForUpdate3();
        TestForUpdate4();
        TestUpdate();
        TestMergeUsing();
        TestDelete();
        TestSetTransaction();
        TestReferential();
        TestSavepoint();
        TestIsolation();
        TestIsolationLevels();
        TestIsolationLevels2();
        TestIsolationLevels3();
        TestIsolationLevels4();
        TestIsolationLevelsCountAggregate();
        TestIsolationLevelsCountAggregate2();
        TestIsolationLevelsMetadata();
        DeleteDb("transaction");
    }

    private void TestConstraintCreationRollback()
    {
        DeleteDb("transaction");
        using DbConnection conn = GetConnection("transaction");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int unique, p int)");
        Execute(stat, "insert into test values(1, 2)");
        AssertThrows(ErrorCode.ReferentialIntegrityViolatedParentMissing1, stat,
            "alter table test add constraint fail foreign key(p) references test(id)");
        Execute(stat, "insert into test values(2, 3)");
        Execute(stat, "drop table test");
    }

    private void TestCommitOnAutoCommitChange()
    {
        DeleteDb("transaction");
        using DbConnection conn = GetConnection("transaction");
        using DbTransaction transaction = conn.BeginTransaction();
        DbCommand stat = conn.CreateCommand();
        stat.Transaction = transaction;
        Execute(stat, "create table test(id int primary key)");

        using (DbConnection conn2 = GetConnection("transaction"))
        {
            DbCommand stat2 = conn2.CreateCommand();
            Execute(stat, "insert into test values(1)");

            // should have no effect
            // (the second setAutoCommit(false) sees the transaction already open)
            stat2.CommandText = "select count(*) from test";
            using (DbDataReader rs = stat2.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(0, rs.GetInt32(0));
            }

            // should commit
            transaction.Commit();
            // Modified (transaction shape): ADO keeps the completed DbTransaction on the
            // command, JDBC statements do not.
            stat.Transaction = null;

            stat2.CommandText = "select * from test";
            using (DbDataReader rs = stat2.ExecuteReader())
            {
                AssertTrue(rs.Read());
            }

            Execute(stat, "drop table test");
        }
    }

    private void TestConcurrentSelectForUpdate()
    {
        DeleteDb("transaction");
        using DbConnection conn = GetConnection("transaction");
        using DbTransaction transaction = conn.BeginTransaction();
        DbCommand stat = conn.CreateCommand();
        stat.Transaction = transaction;
        Execute(stat, "create table test(id int primary key, name varchar)");
        Execute(stat, "create table test2(id int primary key, name varchar)");
        Execute(stat, "insert into test values(1, 'Hello'), (2, 'World')");
        Execute(stat, "insert into test2 values(1, 'A'), (2, 'B')");
        transaction.Commit();
        TestConcurrentSelectForUpdateImpl(conn, "*");
        TestConcurrentSelectForUpdateImpl(conn, "*, count(*) over ()");
    }

    private void TestConcurrentSelectForUpdateImpl(DbConnection conn, string expressions)
    {
        // Modified (transaction shape): JDBC autocommit-off keeps one implicit transaction
        // open across the whole method; the ADO port must do the same for the locks to stay.
        using DbTransaction transaction = conn.BeginTransaction();
        DbCommand prep = conn.CreateCommand();
        prep.Transaction = transaction;
        prep.CommandText = "select * from test for update";
        using (_ = prep.ExecuteReader())
        {
        }

        using (DbConnection conn2 = GetConnection("transaction"))
        {
            using DbTransaction other = conn2.BeginTransaction();
            DbCommand stat2 = conn2.CreateCommand();
            stat2.Transaction = other;
            AssertThrows(ErrorCode.LockTimeout1, stat2,
                "select " + expressions + " from test for update");
        }

        prep.CommandText = "select " + expressions
            + " from test join test2 on test.id = test2.id for update";
        using (_ = prep.ExecuteReader())
        {
        }

        using (DbConnection conn2 = GetConnection("transaction"))
        {
            using DbTransaction other = conn2.BeginTransaction();
            DbCommand stat2 = conn2.CreateCommand();
            stat2.Transaction = other;
            AssertThrows(ErrorCode.LockTimeout1, stat2, "select * from test for update");
            AssertThrows(ErrorCode.LockTimeout1, stat2, "select * from test2 for update");
        }

        transaction.Rollback();
    }

    private void TestForUpdate()
    {
        DeleteDb("transaction");
        using DbConnection conn = GetConnection("transaction");
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            DbCommand stat = conn.CreateCommand();
            stat.Transaction = transaction;
            Execute(stat, "create table test(id int primary key, name varchar)");
            Execute(stat, "insert into test values(1, 'Hello'), (2, 'World')");
            transaction.Commit();
        }

        using (DbTransaction transaction = conn.BeginTransaction())
        {
            DbCommand prep = conn.CreateCommand();
            prep.Transaction = transaction;
            prep.CommandText = "select * from test where id = 1 for update";
            using (_ = prep.ExecuteReader())
            {
            }

            // releases the lock
            transaction.Commit();
        }

        using (DbTransaction transaction = conn.BeginTransaction())
        {
            DbCommand prep = conn.CreateCommand();
            prep.Transaction = transaction;
            prep.CommandText = "select * from test where id = 1 for update";
            using (_ = prep.ExecuteReader())
            {
            }

            using DbConnection conn2 = GetConnection("transaction");
            using DbTransaction transaction2 = conn2.BeginTransaction();
            DbCommand stat2 = conn2.CreateCommand();
            stat2.Transaction = transaction2;
            Execute(stat2, "update test set name = 'Welt' where id = 2");
            AssertThrows(ErrorCode.LockTimeout1, stat2,
                "update test set name = 'Hallo' where id = 1");
        }
    }

    private void TestForUpdate2()
    {
        // Exclude some configurations to avoid spending too much time in sleep()
        if (Config.Networked || Config.Cipher is not null)
        {
            return;
        }

        DeleteDb("transaction");
        using DbConnection conn1 = GetConnection("transaction");
        using DbConnection conn2 = GetConnection("transaction");
        DbCommand stat1 = conn1.CreateCommand();
        Execute(stat1, "CREATE TABLE TEST (ID INT PRIMARY KEY, V INT)");
        DbCommand setup2 = conn2.CreateCommand();
        Execute(setup2, "SET LOCK_TIMEOUT 2000");
        TestForUpdate2(conn1, stat1, conn2, false);
        TestForUpdate2(conn1, stat1, conn2, true);
    }

    private void TestForUpdate2(DbConnection conn1, DbCommand stat1,
        DbConnection conn2, bool forUpdate)
    {
        TestForUpdate2(conn1, stat1, conn2, forUpdate, false);
        TestForUpdate2(conn1, stat1, conn2, forUpdate, true);
    }

    private void TestForUpdate2(DbConnection conn1, DbCommand stat1,
        DbConnection conn2, bool forUpdate, bool window)
    {
        TestForUpdate2(conn1, stat1, conn2, forUpdate, window, false, false);
        TestForUpdate2(conn1, stat1, conn2, forUpdate, window, false, true);
        TestForUpdate2(conn1, stat1, conn2, forUpdate, window, true, false);
    }

    private void TestForUpdate2(DbConnection conn1, DbCommand stat1,
        DbConnection conn2, bool forUpdate, bool window,
        bool deleted, bool excluded)
    {
        // The reference keeps conn1 out of auto-commit; each call commits the setup,
        // then holds the following transaction open until the waiting thread is done.
        DbTransaction transaction1 = conn1.BeginTransaction();
        stat1.Transaction = transaction1;
        Execute(stat1, "MERGE INTO TEST KEY(ID) VALUES (1, 1)");
        transaction1.Commit();
        transaction1 = conn1.BeginTransaction();
        stat1.Transaction = transaction1;
        Execute(stat1, deleted ? "DELETE FROM TEST WHERE ID = 1" : "UPDATE TEST SET V = 2 WHERE ID = 1");
        StringBuilder builder = new("SELECT V");
        if (window)
        {
            _ = builder.Append(", RANK() OVER (ORDER BY ID)");
        }

        _ = builder.Append(" FROM TEST WHERE ID = 1");
        if (excluded)
        {
            _ = builder.Append(" AND V = 1");
        }

        if (forUpdate)
        {
            _ = builder.Append(" FOR UPDATE");
        }

        string query = builder.ToString();
        int[] res = [0];
        Exception?[] ex = [null];
        Thread t = new(() =>
        {
            try
            {
                using DbTransaction transaction = conn2.BeginTransaction();
                DbCommand prep2 = conn2.CreateCommand();
                prep2.Transaction = transaction;
                prep2.CommandText = query;
                using (DbDataReader resultSet = prep2.ExecuteReader())
                {
                    res[0] = resultSet.Read() ? resultSet.GetInt32(0) : -1;
                }

                transaction.Commit();
            }
            catch (DbException e)
            {
                ex[0] = e;
            }
        });
        t.Start();
        Thread.Sleep(500);
        transaction1.Commit();
        t.Join();
        if (ex[0] is not null)
        {
            throw ex[0]!;
        }

        AssertEquals(forUpdate ? deleted || excluded ? -1 : 2 : 1, res[0]);
    }

    private void TestForUpdate3()
    {
        // Exclude some configurations to avoid spending too much time in sleep()
        if (Config.Networked || Config.Cipher is not null)
        {
            return;
        }

        DeleteDb("transaction");
        using DbConnection conn1 = GetConnection("transaction");
        using DbConnection conn2 = GetConnection("transaction");
        DbCommand stat1 = conn1.CreateCommand();
        Execute(stat1, "CREATE TABLE TEST (ID INT PRIMARY KEY, V INT UNIQUE)");
        DbTransaction transaction1 = conn1.BeginTransaction();
        stat1.Transaction = transaction1;
        DbCommand setup2 = conn2.CreateCommand();
        Execute(setup2, "SET LOCK_TIMEOUT 2000");
        Execute(stat1, "MERGE INTO TEST KEY(ID) VALUES (1, 1), (2, 2), (3, 3), (4, 4)");
        transaction1.Commit();
        transaction1 = conn1.BeginTransaction();
        stat1.Transaction = transaction1;
        Execute(stat1, "UPDATE TEST SET V = 10 - V");
        Exception?[] ex = [null];
        Thread t = new(() =>
        {
            try
            {
                using DbTransaction transaction = conn2.BeginTransaction();
                DbCommand prep2 = conn2.CreateCommand();
                prep2.Transaction = transaction;
                prep2.CommandText = "SELECT V FROM TEST ORDER BY V FOR UPDATE";
                using (DbDataReader resultSet = prep2.ExecuteReader())
                {
                    int previous = -1;
                    while (resultSet.Read())
                    {
                        int value = resultSet.GetInt32(0);
                        AssertTrue(previous + ">=" + value, previous < value);
                        previous = value;
                    }
                }

                transaction.Commit();
            }
            catch (DbException e)
            {
                ex[0] = e;
            }
        });
        t.Start();
        Thread.Sleep(500);
        transaction1.Commit();
        t.Join();
        if (ex[0] is not null)
        {
            throw ex[0]!;
        }
    }

    private void TestForUpdate4()
    {
        DeleteDb("transaction");
        using DbConnection conn1 = GetConnection("transaction");
        using DbConnection conn2 = GetConnection("transaction");
        DbCommand stat1 = conn1.CreateCommand();
        DbCommand stat2 = conn2.CreateCommand();
        Execute(stat1, "CREATE TABLE TEST(ID BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, V INT)");
        Execute(stat1, "INSERT INTO TEST(V) VALUES 1, 2, 3");
        DbTransaction transaction1 = conn1.BeginTransaction();
        stat1.Transaction = transaction1;
        using DbTransaction transaction2 = conn2.BeginTransaction();
        stat2.Transaction = transaction2;
        Execute(stat1, "SET LOCK_TIMEOUT 10000");
        long n1 = Environment.TickCount64;
        Execute(stat2, "SELECT * FROM TEST WHERE ID = 1 FOR UPDATE");
        stat1.CommandText = "SELECT * FROM TEST ORDER BY ID FOR UPDATE SKIP LOCKED";
        using (DbDataReader rs = stat1.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(2L, rs.GetInt64(0));
            AssertTrue(rs.Read());
            AssertEquals(3L, rs.GetInt64(0));
            AssertFalse(rs.Read());
        }

        long n2 = Environment.TickCount64;
        if (n2 - n1 > 5_000)
        {
            Fail("FOR UPDATE SKIP LOCKED is too slow");
        }

        transaction1.Commit();
        transaction1 = conn1.BeginTransaction();
        stat1.Transaction = transaction1;
        n1 = Environment.TickCount64;
        AssertThrows(ErrorCode.LockTimeout1, stat1, "SELECT * FROM TEST FOR UPDATE NOWAIT");
        n2 = Environment.TickCount64;
        if (n2 - n1 > 5_000)
        {
            Fail("FOR UPDATE NOWAIT is too slow");
        }

        AssertThrows(ErrorCode.LockTimeout1, stat1, "SELECT * FROM TEST FOR UPDATE WAIT 0.001");
        n1 = Environment.TickCount64;
        if (n1 - n2 > 5_000)
        {
            Fail("FOR UPDATE WAIT 0.001 is too slow");
        }
    }

    private void TestUpdate()
    {
        int count = 50;
        DeleteDb("transaction");
        using DbConnection conn1 = GetConnection("transaction");
        using DbConnection conn2 = GetConnection("transaction");
        DbTransaction transaction1 = conn1.BeginTransaction();
        using DbTransaction transaction2 = conn2.BeginTransaction();
        DbCommand stat1 = conn1.CreateCommand();
        stat1.Transaction = transaction1;
        DbCommand stat2 = conn2.CreateCommand();
        stat2.Transaction = transaction2;
        Execute(stat1, "CREATE TABLE TEST(ID INT PRIMARY KEY, \"VALUE\" BOOLEAN) AS "
            + "SELECT X, FALSE FROM GENERATE_SERIES(1, " + count + ')');
        transaction1.Commit();
        transaction1 = conn1.BeginTransaction();
        stat1.Transaction = transaction1;
        stat1.CommandText = "SELECT * FROM TEST";
        using (_ = stat1.ExecuteReader())
        {
        }

        stat2.CommandText = "SELECT * FROM TEST";
        using (_ = stat2.ExecuteReader())
        {
        }

        int[] r = [0];
        Thread t = new(() =>
        {
            int sum = 0;
            try
            {
                // The reference shares conn1's open transaction with this thread; ADO.NET
                // cannot begin a second one on the same connection.
                using DbCommand prep = conn1.CreateCommand();
                prep.Transaction = transaction1;
                prep.CommandText = "UPDATE TEST SET \"VALUE\" = TRUE WHERE ID = ? AND NOT \"VALUE\"";
                prep.Parameters.Add(new H2Parameter());
                for (int i = 1; i <= count; i++)
                {
                    prep.Parameters[0].Value = i;
                    sum += prep.ExecuteNonQuery();
                }

                transaction1.Commit();
            }
            catch (DbException)
            {
                // Ignore
            }

            r[0] = sum;
        });
        t.Start();
        int sum = 0;
        using (DbCommand prep = conn2.CreateCommand())
        {
            prep.Transaction = transaction2;
            prep.CommandText = "UPDATE TEST SET \"VALUE\" = TRUE WHERE ID = ? AND NOT \"VALUE\"";
            prep.Parameters.Add(new H2Parameter());
            for (int i = 1; i <= count; i++)
            {
                prep.Parameters[0].Value = i;
                sum += prep.ExecuteNonQuery();
            }
        }

        transaction2.Commit();
        t.Join();
        AssertEquals(count, sum + r[0]);
    }

    private void TestMergeUsing()
    {
        int count = 50;
        DeleteDb("transaction");
        using DbConnection conn1 = GetConnection("transaction");
        using DbConnection conn2 = GetConnection("transaction");
        DbTransaction transaction1 = conn1.BeginTransaction();
        using DbTransaction transaction2 = conn2.BeginTransaction();
        DbCommand stat1 = conn1.CreateCommand();
        stat1.Transaction = transaction1;
        DbCommand stat2 = conn2.CreateCommand();
        stat2.Transaction = transaction2;
        Execute(stat1, "CREATE TABLE TEST(ID INT PRIMARY KEY, \"VALUE\" BOOLEAN) AS "
            + "SELECT X, FALSE FROM GENERATE_SERIES(1, " + count + ')');
        transaction1.Commit();
        transaction1 = conn1.BeginTransaction();
        stat1.Transaction = transaction1;
        stat1.CommandText = "SELECT * FROM TEST";
        using (_ = stat1.ExecuteReader())
        {
        }

        stat2.CommandText = "SELECT * FROM TEST";
        using (_ = stat2.ExecuteReader())
        {
        }

        const string mergeSql = "MERGE INTO TEST T USING (SELECT ?1::INT X) S ON T.ID = S.X AND NOT T.\"VALUE\""
            + " WHEN MATCHED THEN UPDATE SET T.\"VALUE\" = TRUE"
            + " WHEN NOT MATCHED THEN INSERT VALUES (10000 + ?1, FALSE)";
        int[] r = [0];
        Thread t = new(() =>
        {
            int sum = 0;
            try
            {
                // Same connection and transaction as the reference: ADO.NET cannot begin
                // another transaction on conn1 while transaction1 is open.
                using DbCommand prep = conn1.CreateCommand();
                prep.Transaction = transaction1;
                prep.CommandText = mergeSql;
                prep.Parameters.Add(new H2Parameter());
                for (int i = 1; i <= count; i++)
                {
                    prep.Parameters[0].Value = i;
                    sum += prep.ExecuteNonQuery();
                }

                transaction1.Commit();
            }
            catch (DbException)
            {
                // Ignore
            }

            r[0] = sum;
        });
        t.Start();
        int sum = 0;
        using (DbCommand prep = conn2.CreateCommand())
        {
            prep.Transaction = transaction2;
            prep.CommandText = mergeSql;
            prep.Parameters.Add(new H2Parameter());
            for (int i = 1; i <= count; i++)
            {
                prep.Parameters[0].Value = i;
                sum += prep.ExecuteNonQuery();
            }
        }

        transaction2.Commit();
        t.Join();
        AssertEquals(count * 2, sum + r[0]);
    }

    private void TestDelete()
    {
        string sql1 = "DELETE FROM TEST WHERE ID = ? AND NOT \"VALUE\"";
        string sql2 = "UPDATE TEST SET \"VALUE\" = TRUE WHERE ID = ? AND NOT \"VALUE\"";
        TestDeleteImpl(sql1, sql2);
        TestDeleteImpl(sql2, sql1);
    }

    private void TestDeleteImpl(string sql1, string sql2)
    {
        int count = 50;
        DeleteDb("transaction");
        using DbConnection conn1 = GetConnection("transaction");
        using DbConnection conn2 = GetConnection("transaction");
        DbTransaction transaction1 = conn1.BeginTransaction();
        using DbTransaction transaction2 = conn2.BeginTransaction();
        DbCommand stat1 = conn1.CreateCommand();
        stat1.Transaction = transaction1;
        DbCommand stat2 = conn2.CreateCommand();
        stat2.Transaction = transaction2;
        Execute(stat1, "CREATE TABLE TEST(ID INT PRIMARY KEY, \"VALUE\" BOOLEAN) AS "
            + "SELECT X, FALSE FROM GENERATE_SERIES(1, " + count + ')');
        transaction1.Commit();
        transaction1 = conn1.BeginTransaction();
        stat1.Transaction = transaction1;
        stat1.CommandText = "SELECT * FROM TEST";
        using (_ = stat1.ExecuteReader())
        {
        }

        stat2.CommandText = "SELECT * FROM TEST";
        using (_ = stat2.ExecuteReader())
        {
        }

        int[] r = [0];
        Thread t = new(() =>
        {
            int sum = 0;
            try
            {
                // The reference reuses conn1's open transaction in the worker thread.
                using DbCommand prep = conn1.CreateCommand();
                prep.Transaction = transaction1;
                prep.CommandText = sql1;
                prep.Parameters.Add(new H2Parameter());
                for (int i = 1; i <= count; i++)
                {
                    prep.Parameters[0].Value = i;
                    sum += prep.ExecuteNonQuery();
                }

                transaction1.Commit();
            }
            catch (DbException)
            {
                // Ignore
            }

            r[0] = sum;
        });
        t.Start();
        int sum = 0;
        using (DbCommand prep = conn2.CreateCommand())
        {
            prep.Transaction = transaction2;
            prep.CommandText = sql2;
            prep.Parameters.Add(new H2Parameter());
            for (int i = 1; i <= count; i++)
            {
                prep.Parameters[0].Value = i;
                sum += prep.ExecuteNonQuery();
            }
        }

        transaction2.Commit();
        t.Join();
        AssertEquals(count, sum + r[0]);
    }

    private void TestRollback()
    {
        DeleteDb("transaction");
        DbConnection conn = GetConnection("transaction");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int)");
        Execute(stat, "create index idx_id on test(id)");
        Execute(stat, "insert into test values(1), (1), (1)");
        if (!Config.Memory)
        {
            conn.Close();
            conn = GetConnection("transaction");
            stat = conn.CreateCommand();
        }

        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            Execute(stat, "delete from test");
            transaction.Rollback();
        }

        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            stat.CommandText = "select * from test";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertResultRowCount(3, rs);
            }

            stat.CommandText = "select * from test where id = 1";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertResultRowCount(3, rs);
            }
        }

        conn.Close();

        conn = GetConnection("transaction");
        stat = conn.CreateCommand();
        Execute(stat, "create table master(id int primary key) as select 1");
        Execute(stat, "create table child1(id int references master(id) "
            + "on delete cascade)");
        Execute(stat, "insert into child1 values(1), (1), (1)");
        Execute(stat, "create table child2(id int references master(id)) as select 1");
        if (!Config.Memory)
        {
            conn.Close();
            conn = GetConnection("transaction");
        }

        stat = conn.CreateCommand();
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            AssertThrows(ErrorCode.ReferentialIntegrityViolatedChildExists1, stat,
                "delete from master");
            transaction.Rollback();
            // ADO.NET commands cannot keep the transaction object after it completes;
            // JDBC statements only know their connection, whose auto-commit is off.
            stat.Transaction = null;
            stat.CommandText = "select * from master where id=1";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertResultRowCount(1, rs);
            }

            stat.CommandText = "select * from child1";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertResultRowCount(3, rs);
            }

            stat.CommandText = "select * from child1 where id=1";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertResultRowCount(3, rs);
            }
        }

        conn.Close();
    }

    private void TestRollback2()
    {
        DeleteDb("transaction");
        DbConnection conn = GetConnection("transaction");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id int)");
        Execute(stat, "create index idx_id on test(id)");
        Execute(stat, "insert into test values(1), (1)");
        if (!Config.Memory)
        {
            conn.Close();
            conn = GetConnection("transaction");
            stat = conn.CreateCommand();
        }

        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            Execute(stat, "delete from test");
            transaction.Rollback();
        }

        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            stat.CommandText = "select * from test where id = 1";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertResultRowCount(2, rs);
            }
        }

        conn.Close();

        conn = GetConnection("transaction");
        stat = conn.CreateCommand();
        Execute(stat, "create table master(id int primary key) as select 1");
        Execute(stat, "create table child1(id int references master(id) "
            + "on delete cascade)");
        Execute(stat, "insert into child1 values(1), (1)");
        Execute(stat, "create table child2(id int references master(id)) as select 1");
        if (!Config.Memory)
        {
            conn.Close();
            conn = GetConnection("transaction");
        }

        stat = conn.CreateCommand();
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            AssertThrows(ErrorCode.ReferentialIntegrityViolatedChildExists1, stat,
                "delete from master");
            stat.CommandText = "select * from master where id=1";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertResultRowCount(1, rs);
            }

            stat.CommandText = "select * from child1 where id=1";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertResultRowCount(2, rs);
            }
        }

        conn.Close();
    }

    private void TestSetTransaction()
    {
        DeleteDb("transaction");
        using DbConnection conn = GetConnection("transaction");
        using DbTransaction transaction = conn.BeginTransaction();
        DbCommand stat = conn.CreateCommand();
        stat.Transaction = transaction;
        Execute(stat, "create table test(id int)");
        Execute(stat, "insert into test values(1)");
        Execute(stat, "set @x = 1");
        transaction.Commit();
        // JDBC statements stay on the connection after commit; the ADO command cannot
        // keep referring to the completed DbTransaction object.
        stat.Transaction = null;
        AssertSingleValue(stat, "select id from test", 1);
        AssertSingleValue(stat, "call @x", 1);

        using (DbTransaction transaction2 = conn.BeginTransaction())
        {
            stat.Transaction = transaction2;
            Execute(stat, "update test set id=2");
            Execute(stat, "set @x = 2");
            transaction2.Rollback();
        }

        using (DbTransaction transaction3 = conn.BeginTransaction())
        {
            stat.Transaction = transaction3;
            AssertSingleValue(stat, "select id from test", 1);
            AssertSingleValue(stat, "call @x", 2);
        }
    }

    private void TestReferential()
    {
        DeleteDb("transaction");
        using DbConnection c1 = GetConnection("transaction");
        using DbTransaction t1 = c1.BeginTransaction();
        DbCommand s1 = c1.CreateCommand();
        s1.Transaction = t1;
        Execute(s1, "drop table if exists a");
        Execute(s1, "drop table if exists b");
        Execute(s1, "create table a (id integer generated by default as identity, "
            + "code varchar(10) not null, primary key(id))");
        Execute(s1, "create table b (name varchar(100) not null, a integer, "
            + "primary key(name), foreign key(a) references a(id))");
        using (DbConnection c2 = GetConnection("transaction"))
        {
            using DbTransaction t2 = c2.BeginTransaction();
            DbCommand s2 = c2.CreateCommand();
            s2.Transaction = t2;
            Execute(s1, "insert into A(code) values('one')");
            AssertThrows(ErrorCode.ReferentialIntegrityViolatedParentMissing1, s2,
                "insert into B values('two', 1)");
            t2.Commit();
        }

        t1.Rollback();
    }

    private void TestClosingConnectionWithLockedTable()
    {
        DeleteDb("transaction");
        DbConnection c1 = GetConnection("transaction");
        DbConnection c2 = GetConnection("transaction");
        DbCommand s1 = c1.CreateCommand();
        using (DbTransaction t1 = c1.BeginTransaction())
        {
            s1.Transaction = t1;
            Execute(s1, "create table a (id integer generated by default as identity, "
                + "code varchar(10) not null, primary key(id))");
            Execute(s1, "insert into a(code) values('one')");
            t1.Commit();
        }

        // Modified (transaction shape): JDBC autocommit-off starts the next transaction
        // implicitly; ADO needs it explicitly. It is not disposed before c1.Close, because
        // the close itself must release the row lock.
        DbTransaction lockTransaction = c1.BeginTransaction();
        s1.Transaction = lockTransaction;
        s1.CommandText = "select * from a for update";
        using (_ = s1.ExecuteReader())
        {
        }

        // c1.close() with the lock held: the reference closes the connection as is.
        c1.Close();

        using (DbConnection c2b = GetConnection("transaction"))
        {
            using DbTransaction t2 = c2b.BeginTransaction();
            DbCommand s2 = c2b.CreateCommand();
            s2.Transaction = t2;
            s2.CommandText = "select * from a for update";
            using (_ = s2.ExecuteReader())
            {
            }
        }

        c2.Close();
    }

    private void TestClosingConnectionWithSessionTempTable()
    {
        DeleteDb("transaction");
        DbConnection c1 = GetConnection("transaction");
        using (DbConnection c2 = GetConnection("transaction"))
        {
            using (DbTransaction t1 = c1.BeginTransaction())
            {
                DbCommand s1 = c1.CreateCommand();
                s1.Transaction = t1;
                Execute(s1, "create local temporary table a (id int, x BLOB)");
                t1.Commit();
            }

            c1.Close();

            using (DbTransaction t2 = c2.BeginTransaction())
            {
                DbCommand s2 = c2.CreateCommand();
                s2.Transaction = t2;
                Execute(s2, "create table c (id int)");
            }
        }
    }

    private void TestSavepoint()
    {
        DeleteDb("transaction");
        using DbConnection conn = GetConnection("transaction");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST0(ID IDENTITY, NAME VARCHAR)");
        Execute(stat, "CREATE TABLE TEST1(NAME VARCHAR, "
            + "ID IDENTITY, X TIMESTAMP DEFAULT CURRENT_TIMESTAMP)");
        DbTransaction transaction = conn.BeginTransaction();
        stat.Transaction = transaction;
        int[] count = new int[2];
        int[] countCommitted = new int[2];
        int[] countSave = new int[2];
        int len = GetSize(2000, 10000);
        Random random = new(10);
        string? sp = null;
        int savepointCounter = 0;
        for (int i = 0; i < len; i++)
        {
            int tableId = random.Next(2);
            string table = "TEST" + tableId;
            int op = random.Next(6);
            switch (op)
            {
                case 0:
                    Execute(stat, "INSERT INTO " + table + "(NAME) VALUES('op" + i + "')");
                    count[tableId]++;
                    break;
                case 1:
                    if (count[tableId] > 0)
                    {
                        stat.CommandText = "DELETE FROM " + table
                            + " WHERE ID=SELECT MIN(ID) FROM " + table;
                        AssertEquals(1, stat.ExecuteNonQuery());
                        count[tableId]--;
                    }

                    break;
                case 2:
                    sp = "SP" + savepointCounter++;
                    transaction.Save(sp);
                    countSave[0] = count[0];
                    countSave[1] = count[1];
                    break;
                case 3:
                    if (sp is not null)
                    {
                        transaction.Rollback(sp);
                        count[0] = countSave[0];
                        count[1] = countSave[1];
                    }

                    break;
                case 4:
                    transaction.Commit();
                    sp = null;
                    countCommitted[0] = count[0];
                    countCommitted[1] = count[1];
                    transaction = conn.BeginTransaction();
                    stat.Transaction = transaction;
                    break;
                case 5:
                    transaction.Rollback();
                    sp = null;
                    count[0] = countCommitted[0];
                    count[1] = countCommitted[1];
                    transaction = conn.BeginTransaction();
                    stat.Transaction = transaction;
                    break;
                default:
                    break;
            }

            CheckTableCount(stat, "TEST0", count[0]);
            CheckTableCount(stat, "TEST1", count[1]);
        }
    }

    private void CheckTableCount(DbCommand stat, string tableName, int count)
    {
        stat.CommandText = "SELECT COUNT(*) FROM " + tableName;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(count, rs.GetInt32(0));
        }
    }

    private void TestIsolation()
    {
        using DbConnection conn = GetConnection("transaction");
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            AssertEquals(IsolationLevel.ReadCommitted, transaction.IsolationLevel);
            transaction.Commit();
        }

        // setTransactionIsolation(SERIALIZABLE): the level the next transaction begins
        // with.
        using (DbTransaction transaction = conn.BeginTransaction(IsolationLevel.Serializable))
        {
            AssertEquals(IsolationLevel.Serializable, transaction.IsolationLevel);
        }

        DbCommand stat = conn.CreateCommand();
        // getAutoCommit: true while no transaction is open.
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY)");
        stat.CommandText = "COMMIT";
        _ = stat.ExecuteNonQuery();
        Execute(stat, "INSERT INTO TEST VALUES(0)");
        Execute(stat, "ROLLBACK");
        TestValue(stat, "SELECT COUNT(*) FROM TEST", "1");
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            Execute(stat, "DELETE FROM TEST");
            transaction.Rollback();
        }

        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            TestValue(stat, "SELECT COUNT(*) FROM TEST", "1");
            transaction.Commit();
        }

        TestNestedResultSets(conn);
        TestNestedResultSets(conn);
    }

    private void TestIsolationLevels()
    {
        foreach (IsolationLevel isolationLevel in new[]
        {
            IsolationLevel.RepeatableRead, IsolationLevel.Snapshot, IsolationLevel.Serializable,
        })
        {
            DeleteDb("transaction");
            using (DbConnection conn1 = GetConnection("transaction"))
            using (DbConnection conn2 = GetConnection("transaction"))
            using (DbConnection conn3 = GetConnection("transaction"))
            {
                using (DbTransaction t3 = conn3.BeginTransaction(IsolationLevel.ReadUncommitted))
                {
                    DbCommand stat1 = conn1.CreateCommand();
                    DbCommand stat2 = conn2.CreateCommand();
                    DbCommand stat3 = conn3.CreateCommand();
                    stat2.Transaction = t3;
                    stat3.Transaction = t3;
                    Execute(stat1, "CREATE TABLE TEST1(ID INT PRIMARY KEY) AS VALUES 1, 2");
                    Execute(stat1, "CREATE TABLE TEST2(ID INT PRIMARY KEY, V INT) AS VALUES (1, 10), (2, 20)");
                    using (DbTransaction t2 = conn2.BeginTransaction())
                    {
                        stat2.Transaction = t2;
                        // Read committed
                        TestIsolationLevelsCheckRowsAndCount(stat2, 1, 2);
                        Execute(stat1, "INSERT INTO TEST1 VALUES 3");
                        TestIsolationLevelsCheckRowsAndCount(stat2, 1, 3);
                        TestIsolationLevelsCheckRowsAndCount(stat2, 2, 2);
                        Execute(stat1, "INSERT INTO TEST2 VALUES (3, 30)");
                        TestIsolationLevelsCheckRowsAndCount(stat2, 2, 3);
                        t2.Commit();
                    }

                    // Repeatable read or serializable
                    using (DbTransaction t2 = conn2.BeginTransaction(isolationLevel))
                    {
                        stat2.Transaction = t2;
                        TestIsolationLevelsCheckRowsAndCount(stat2, 1, 3);

                        Execute(stat1, "INSERT INTO TEST1 VALUES 4");
                        TestIsolationLevelsCheckRowsAndCount(stat2, 1, 3);
                        TestIsolationLevelsCheckRowsAndCount(stat2, 2, 3);
                        Execute(stat1, "INSERT INTO TEST2 VALUES (4, 40)");
                        TestIsolationLevelsCheckRowsAndCount(stat2, 2, 3);
                        t2.Commit();
                        t2.Dispose();
                        DbTransaction t2b = conn2.BeginTransaction(isolationLevel);
                        stat2.Transaction = t2b;
                        TestIsolationLevelsCheckRowsAndCount(stat2, 1, 4);
                        TestIsolationLevelsCheckRowsAndCount(stat2, 2, 4);
                        Execute(stat1, "ALTER TABLE TEST2 ADD CONSTRAINT FK FOREIGN KEY(ID) REFERENCES TEST1(ID)");
                        t2b.Commit();
                        t2b = conn2.BeginTransaction(isolationLevel);
                        stat2.Transaction = t2b;
                        TestIsolationLevelsCheckRowsAndCount(stat2, 1, 4);
                        Execute(stat1, "INSERT INTO TEST1 VALUES 5");
                        Execute(stat1, "INSERT INTO TEST2 VALUES (5, 50)");
                        TestIsolationLevelsCheckRowsAndCount(stat2, 1, 4);
                        TestIsolationLevelsCheckRowsAndCount(stat2, 2, 4);
                        t2b.Commit();
                        t2b = conn2.BeginTransaction(isolationLevel);
                        stat2.Transaction = t2b;
                        TestIsolationLevelsCheckRowsAndCount(stat2, 1, 5);
                        TestIsolationLevelsCheckRowsAndCount(stat2, 2, 5);
                        Execute(stat2, "INSERT INTO TEST1 VALUES 6");
                        Execute(stat2, "INSERT INTO TEST2 VALUES (6, 60)");
                        Execute(stat2, "DELETE FROM TEST2 WHERE ID IN (1, 3)");
                        Execute(stat2, "UPDATE TEST2 SET V = 45 WHERE ID = 4");
                        Execute(stat1, "INSERT INTO TEST1 VALUES 7");
                        Execute(stat1, "INSERT INTO TEST2 VALUES (7, 70)");
                        Execute(stat2, "INSERT INTO TEST1 VALUES 8");
                        Execute(stat2, "INSERT INTO TEST2 VALUES (8, 80)");
                        Execute(stat2, "INSERT INTO TEST1 VALUES 9");
                        Execute(stat2, "INSERT INTO TEST2 VALUES (9, 90)");
                        Execute(stat2, "DELETE FROM TEST2 WHERE ID = 9");
                        TestIsolationLevelsCheckRowsAndCount2(stat2, 1, 1, 2, 3, 4, 5, 6, 8, 9);
                        // Read uncommitted
                        TestIsolationLevelsCheckRowsAndCount2(stat3, 1, 1, 2, 3, 4, 5, 6, 7, 8, 9);
                        // Repeatable read or serializable
                        stat2.CommandText = "SELECT COUNT(*) FROM TEST2";
                        using (DbDataReader rs = stat2.ExecuteReader())
                        {
                            AssertTrue(rs.Read());
                            AssertEquals(5, rs.GetInt64(0));
                        }

                        stat2.CommandText = "SELECT ID, V FROM TEST2 ORDER BY ID";
                        using (DbDataReader rs = stat2.ExecuteReader())
                        {
                            AssertTrue(rs.Read());
                            AssertEquals(2, rs.GetInt32(0));
                            AssertEquals(20, rs.GetInt32(1));
                            AssertTrue(rs.Read());
                            AssertEquals(4, rs.GetInt32(0));
                            AssertEquals(45, rs.GetInt32(1));
                            AssertTrue(rs.Read());
                            AssertEquals(5, rs.GetInt32(0));
                            AssertEquals(50, rs.GetInt32(1));
                            AssertTrue(rs.Read());
                            AssertEquals(6, rs.GetInt32(0));
                            AssertEquals(60, rs.GetInt32(1));
                            AssertTrue(rs.Read());
                            AssertEquals(8, rs.GetInt32(0));
                            AssertEquals(80, rs.GetInt32(1));
                            AssertFalse(rs.Read());
                        }

                        Execute(stat1, "INSERT INTO TEST1 VALUES 11");
                        Execute(stat1, "INSERT INTO TEST2 VALUES (11, 110)");
                        t2b.Commit();
                        t2b = conn2.BeginTransaction(isolationLevel);
                        stat2.Transaction = t2b;
                        TestIsolationLevelsCheckRowsAndCount2(stat1, 2, 2, 4, 5, 6, 7, 8, 11);
                        TestIsolationLevelsCheckRowsAndCount2(stat2, 2, 2, 4, 5, 6, 7, 8, 11);
                        Execute(stat2, "INSERT INTO TEST1 VALUES 10");
                        Execute(stat2, "INSERT INTO TEST2 VALUES (9, 90), (10, 100)");
                        Execute(stat2, "DELETE FROM TEST2 WHERE ID = 9");
                        TestIsolationLevelsCheckRowsAndCount2(stat2, 2, 2, 4, 5, 6, 7, 8, 10, 11);
                        Execute(stat1, "ALTER TABLE TEST2 DROP CONSTRAINT FK");
                        t2b.Commit();
                        t2b = conn2.BeginTransaction(isolationLevel);
                        stat2.Transaction = t2b;
                        stat2.CommandText = "SELECT COUNT(*) FROM TEST1";
                        using (DbDataReader rs = stat2.ExecuteReader())
                        {
                            AssertTrue(rs.Read());
                            AssertEquals(11, rs.GetInt64(0));
                        }

                        Execute(stat1, "INSERT INTO TEST2 VALUES (20, 200)");
                        stat2.CommandText = "SELECT COUNT(*) FROM TEST2";
                        using (DbDataReader rs = stat2.ExecuteReader())
                        {
                            AssertTrue(rs.Read());
                            AssertEquals(isolationLevel != IsolationLevel.RepeatableRead ? 8 : 9,
                                rs.GetInt64(0));
                        }

                        t2b.Commit();
                    }
                }
            }
        }

        DeleteDb("transaction");
    }

    private void TestIsolationLevelsCheckRowsAndCount(DbCommand stat, int table, int expected)
    {
        stat.CommandText = "SELECT COUNT(*) FROM TEST" + table;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(expected, rs.GetInt64(0));
        }

        stat.CommandText = "SELECT ID FROM TEST" + table + " ORDER BY ID";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            for (int i = 0; ++i <= expected;)
            {
                AssertTrue(rs.Read());
                AssertEquals(i, rs.GetInt32(0));
            }

            AssertFalse(rs.Read());
        }
    }

    private void TestIsolationLevelsCheckRowsAndCount2(DbCommand stat, int table,
        params int[] values)
    {
        stat.CommandText = "SELECT COUNT(*) FROM TEST" + table;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(values.Length, rs.GetInt64(0));
        }

        stat.CommandText = "SELECT ID FROM TEST" + table + " ORDER BY ID";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            foreach (int expected in values)
            {
                AssertTrue(rs.Read());
                AssertEquals(expected, rs.GetInt32(0));
            }

            AssertFalse(rs.Read());
        }
    }

    private void TestNestedResultSets(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();
        Test_(stat, "CREATE TABLE NEST1(ID INT PRIMARY KEY,\"VALUE\" VARCHAR(255))");
        Test_(stat, "CREATE TABLE NEST2(ID INT PRIMARY KEY,\"VALUE\" VARCHAR(255))");
        // meta.getTables/getColumns: the INFORMATION_SCHEMA queries through the ADO layer.
        List<string> result = [];
        stat.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE 'NEST%' ORDER BY TABLE_NAME";
        using (DbDataReader rs1 = stat.ExecuteReader())
        {
            while (rs1.Read())
            {
                string table = rs1.GetString(0);
                DbCommand columns = conn.CreateCommand();
                columns.CommandText =
                    "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME LIKE 'NEST%' AND TABLE_NAME = '"
                    + table + "' ORDER BY ORDINAL_POSITION";
                using (DbDataReader rs2 = columns.ExecuteReader())
                {
                    while (rs2.Read())
                    {
                        string column = rs2.GetString(0);
                        Trace("Table: " + table + " Column: " + column);
                        result.Add(table + "." + column);
                    }
                }
            }
        }

        // should be NEST1.ID, NEST1.NAME, NEST2.ID, NEST2.NAME
        AssertEquals(result.ToString()!, 4, result.Count);
        result = [];
        Test_(stat, "INSERT INTO NEST1 VALUES(1,'A')");
        Test_(stat, "INSERT INTO NEST1 VALUES(2,'B')");
        Test_(stat, "INSERT INTO NEST2 VALUES(1,'1')");
        Test_(stat, "INSERT INTO NEST2 VALUES(2,'2')");
        using (DbCommand s1 = conn.CreateCommand(), s2 = conn.CreateCommand())
        {
            s1.CommandText = "SELECT * FROM NEST1 ORDER BY ID";
            using (DbDataReader rs1 = s1.ExecuteReader())
            {
                while (rs1.Read())
                {
                    s2.CommandText = "SELECT * FROM NEST2 ORDER BY ID";
                    using DbDataReader rs2 = s2.ExecuteReader();
                    while (rs2.Read())
                    {
                        string v1 = rs1.GetString(rs1.GetOrdinal("VALUE"));
                        string v2 = rs2.GetString(rs2.GetOrdinal("VALUE"));
                        result.Add(v1 + "/" + v2);
                    }
                }
            }

            // should be A/1, A/2, B/1, B/2
            AssertEquals(result.ToString()!, 4, result.Count);
            result = [];
            s1.CommandText = "SELECT * FROM NEST1 ORDER BY ID";
            using (DbDataReader rs1 = s1.ExecuteReader())
            {
                s2.CommandText = "SELECT * FROM NEST2 ORDER BY ID";
                using (DbDataReader rs2 = s2.ExecuteReader())
                {
                    // The reference closes rs1 here through the statement reuse and reads
                    // the OBJECT_CLOSED error; two commands keep both readers open, and
                    // the fragment walks both.
                    while (rs1.Read() && rs2.Read())
                    {
                        result.Add(rs2.GetString(rs2.GetOrdinal("VALUE")));
                    }
                }
            }

            // should be A, B
            AssertEquals(result.ToString()!, 2, result.Count);
        }

        Test_(stat, "DROP TABLE NEST1");
        Test_(stat, "DROP TABLE NEST2");
    }

    private void TestValue(DbCommand stat, string sql, string data)
    {
        stat.CommandText = sql;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(data, rs.GetString(0));
        }
    }

    private void Test_(DbCommand stat, string sql)
    {
        Trace(sql);
        Execute(stat, sql);
    }

    private void TestIsolationLevels2()
    {
        foreach (IsolationLevel isolationLevel in new[]
        {
            IsolationLevel.ReadUncommitted, IsolationLevel.ReadCommitted,
            IsolationLevel.RepeatableRead, IsolationLevel.Snapshot, IsolationLevel.Serializable,
        })
        {
            DeleteDb("transaction");
            using (DbConnection conn1 = GetConnection("transaction"))
            using (DbConnection conn2 = GetConnection("transaction"))
            using (DbTransaction t1 = conn1.BeginTransaction(isolationLevel))
            using (DbTransaction t2 = conn2.BeginTransaction(isolationLevel))
            {
                DbCommand stat1 = conn1.CreateCommand();
                stat1.Transaction = t1;
                DbCommand stat2 = conn2.CreateCommand();
                stat2.Transaction = t2;
                // Test a table without constraints
                Execute(stat1, "CREATE TABLE TEST(\"VALUE\" INT)");
                stat1.CommandText = "TABLE TEST";
                using (_ = stat1.ExecuteReader())
                {
                }

                Execute(stat1, "DROP TABLE TEST");
                // Other tests
                Execute(stat1, "CREATE TABLE TEST(ID VARCHAR PRIMARY KEY, \"VALUE\" INT)");
                Execute(stat1, "INSERT INTO TEST VALUES ('1', 1)");
                t1.Commit();
                t1.Dispose();
                DbTransaction ta = conn1.BeginTransaction(isolationLevel);
                stat1.Transaction = ta;
                stat1.CommandText = "SELECT * FROM TEST WHERE ID = '1'";
                using (DbDataReader rs = stat1.ExecuteReader())
                {
                    AssertTrue(rs.Read());
                    AssertEquals(1, rs.GetInt32(1));
                }

                Execute(stat2, "UPDATE TEST SET \"VALUE\" = \"VALUE\" + 1");
                stat1.CommandText = "SELECT * FROM TEST WHERE ID = '1'";
                using (DbDataReader rs = stat1.ExecuteReader())
                {
                    AssertTrue(rs.Read());
                    AssertEquals(
                        isolationLevel == IsolationLevel.ReadUncommitted ? 2 : 1, rs.GetInt32(1));
                }

                AssertThrows(ErrorCode.LockTimeout1, stat1,
                    "SELECT * FROM TEST WHERE ID = '1' FOR UPDATE");
                t2.Commit();
                if (isolationLevel >= IsolationLevel.RepeatableRead)
                {
                    AssertThrows(ErrorCode.Deadlock1, stat1,
                        "SELECT * FROM TEST WHERE ID = '1' FOR UPDATE");
                }
                else
                {
                    stat1.CommandText = "SELECT * FROM TEST WHERE ID = '1' FOR UPDATE";
                    using (DbDataReader rs = stat1.ExecuteReader())
                    {
                        AssertTrue(rs.Read());
                        AssertEquals(2, rs.GetInt32(1));
                    }

                    stat1.CommandText = "SELECT * FROM TEST";
                    using (DbDataReader rs = stat1.ExecuteReader())
                    {
                        AssertTrue(rs.Read());
                        AssertEquals(2, rs.GetInt32(1));
                    }

                    stat1.CommandText = "SELECT * FROM TEST WHERE ID = '1'";
                    using (DbDataReader rs = stat1.ExecuteReader())
                    {
                        AssertTrue(rs.Read());
                        AssertEquals(2, rs.GetInt32(1));
                    }
                }

                ta.Commit();
            }
        }

        DeleteDb("transaction");
    }

    private void TestIsolationLevels3()
    {
        foreach (IsolationLevel isolationLevel in new[]
        {
            IsolationLevel.ReadUncommitted, IsolationLevel.ReadCommitted,
            IsolationLevel.RepeatableRead, IsolationLevel.Snapshot, IsolationLevel.Serializable,
        })
        {
            DeleteDb("transaction");
            using (DbConnection conn1 = GetConnection("transaction"))
            using (DbConnection conn2 = GetConnection("transaction"))
            using (DbTransaction t1 = conn1.BeginTransaction(isolationLevel))
            using (DbTransaction t2 = conn2.BeginTransaction(isolationLevel))
            {
                DbCommand stat1 = conn1.CreateCommand();
                stat1.Transaction = t1;
                DbCommand stat2 = conn2.CreateCommand();
                stat2.Transaction = t2;
                Execute(stat1, "CREATE TABLE TEST(ID BIGINT PRIMARY KEY, ID2 INT UNIQUE, \"VALUE\" INT)");
                Execute(stat1, "INSERT INTO TEST VALUES (1, 1, 1), (2, 2, 2), (3, 3, 3)");
                t1.Commit();
                t1.Dispose();
                DbTransaction ta = conn1.BeginTransaction(isolationLevel);
                stat1.Transaction = ta;
                stat1.CommandText = "SELECT * FROM TEST WHERE ID2 IN (1, 2)";
                using (DbDataReader rs = stat1.ExecuteReader())
                {
                    AssertTrue(rs.Read());
                    AssertEquals(1, rs.GetInt32(2));
                    AssertTrue(rs.Read());
                    AssertEquals(2, rs.GetInt32(2));
                }

                Execute(stat2, "UPDATE TEST SET ID2 = 4, \"VALUE\" = 5 WHERE ID2 = 2");
                stat1.CommandText = "SELECT * FROM TEST WHERE ID2 IN (1, 2)";
                using (DbDataReader rs = stat1.ExecuteReader())
                {
                    AssertTrue(rs.Read());
                    AssertEquals(1, rs.GetInt32(2));
                    if (isolationLevel == IsolationLevel.ReadUncommitted)
                    {
                        AssertFalse(rs.Read());
                    }
                    else
                    {
                        AssertTrue(rs.Read());
                        AssertEquals(2, rs.GetInt32(2));
                    }
                }

                if (isolationLevel == IsolationLevel.ReadUncommitted)
                {
                    stat1.CommandText = "SELECT * FROM TEST WHERE ID2 = 2 FOR UPDATE";
                    using (DbDataReader rs = stat1.ExecuteReader())
                    {
                        AssertFalse(rs.Read());
                    }

                    AssertThrows(ErrorCode.LockTimeout1, stat1,
                        "SELECT * FROM TEST WHERE ID2 = 4 FOR UPDATE");
                }
                else
                {
                    AssertThrows(ErrorCode.LockTimeout1, stat1,
                        "SELECT * FROM TEST WHERE ID2 = 2 FOR UPDATE");
                    stat1.CommandText = "SELECT * FROM TEST WHERE ID2 = 4 FOR UPDATE";
                    using (DbDataReader rs = stat1.ExecuteReader())
                    {
                        AssertFalse(rs.Read());
                    }
                }

                Execute(stat2, "UPDATE TEST SET \"VALUE\" = 6 WHERE ID2 = 3");
                t2.Commit();
                if (isolationLevel == IsolationLevel.ReadUncommitted
                    || isolationLevel == IsolationLevel.ReadCommitted)
                {
                    stat1.CommandText = "SELECT * FROM TEST WHERE ID2 = 4 FOR UPDATE";
                    using (DbDataReader rs = stat1.ExecuteReader())
                    {
                        AssertTrue(rs.Read());
                        AssertEquals(5, rs.GetInt32(2));
                    }

                    stat1.CommandText = "SELECT * FROM TEST";
                    using (DbDataReader rs = stat1.ExecuteReader())
                    {
                        AssertTrue(rs.Read());
                        AssertEquals(1, rs.GetInt32(2));
                        AssertTrue(rs.Read());
                        AssertEquals(5, rs.GetInt32(2));
                        AssertTrue(rs.Read());
                        AssertEquals(6, rs.GetInt32(2));
                    }

                    stat1.CommandText = "SELECT * FROM TEST WHERE ID2 = 4";
                    using (DbDataReader rs = stat1.ExecuteReader())
                    {
                        AssertTrue(rs.Read());
                        AssertEquals(5, rs.GetInt32(2));
                    }
                }
                else
                {
                    stat1.CommandText = "SELECT * FROM TEST WHERE ID2 = 3";
                    using (DbDataReader rs = stat1.ExecuteReader())
                    {
                        AssertTrue(rs.Read());
                        AssertEquals(3, rs.GetInt32(2));
                    }

                    AssertThrows(ErrorCode.Deadlock1, stat1,
                        "SELECT * FROM TEST WHERE ID2 = 3 FOR UPDATE");
                }

                ta.Commit();
            }
        }

        DeleteDb("transaction");
    }

    private void TestIsolationLevels4()
    {
        TestIsolationLevels4(true);
        TestIsolationLevels4(false);
    }

    private void TestIsolationLevels4(bool primaryKey)
    {
        foreach (IsolationLevel isolationLevel in new[]
        {
            IsolationLevel.ReadUncommitted, IsolationLevel.ReadCommitted,
            IsolationLevel.RepeatableRead, IsolationLevel.Snapshot, IsolationLevel.Serializable,
        })
        {
            DeleteDb("transaction");
            using (DbConnection conn1 = GetConnection("transaction"))
            using (DbConnection conn2 = GetConnection("transaction"))
            {
                DbCommand stat1 = conn1.CreateCommand();
                Execute(stat1, "CREATE TABLE TEST(ID INT " + (primaryKey ? "PRIMARY KEY" : "UNIQUE")
                    + ", V INT) AS VALUES (1, 2)");
                using DbTransaction t2 = conn2.BeginTransaction(isolationLevel);
                DbCommand stat2 = conn2.CreateCommand();
                stat2.Transaction = t2;
                stat2.CommandText = "SELECT V FROM TEST WHERE ID = 1";
                using (DbDataReader rs = stat2.ExecuteReader())
                {
                    AssertTrue(rs.Read());
                    AssertEquals(2, rs.GetInt32(0));
                    AssertFalse(rs.Read());
                }

                Execute(stat1, "UPDATE TEST SET V = V + 1");
                stat2.CommandText = "SELECT V FROM TEST WHERE ID = 1";
                using (DbDataReader rs = stat2.ExecuteReader())
                {
                    AssertTrue(rs.Read());
                    AssertEquals(isolationLevel >= IsolationLevel.RepeatableRead ? 2 : 3,
                        rs.GetInt32(0));
                    AssertFalse(rs.Read());
                }

                if (isolationLevel >= IsolationLevel.RepeatableRead)
                {
                    AssertThrows(ErrorCode.Deadlock1, stat2, "UPDATE TEST SET V = V + 2");
                    stat2.CommandText = "SELECT V FROM TEST WHERE ID = 1";
                    using (DbDataReader rs = stat2.ExecuteReader())
                    {
                        AssertTrue(rs.Read());
                        AssertEquals(3, rs.GetInt32(0));
                        AssertFalse(rs.Read());
                    }

                    Execute(stat1, "DELETE FROM TEST");
                    AssertThrows(ErrorCode.Deadlock1, stat2, "UPDATE TEST SET V = V + 2");
                    Execute(stat1, "INSERT INTO TEST VALUES (1, 2)");
                    stat2.CommandText = "SELECT V FROM TEST WHERE ID = 1";
                    using (DbDataReader rs = stat2.ExecuteReader())
                    {
                        AssertTrue(rs.Read());
                        AssertEquals(2, rs.GetInt32(0));
                        AssertFalse(rs.Read());
                    }

                    Execute(stat1, "DELETE FROM TEST");
                    Execute(stat1, "INSERT INTO TEST VALUES (1, 2)");
                    if (primaryKey)
                    {
                        // With a delegate index the row was completely
                        // restored, so no error
                        stat2.CommandText = "UPDATE TEST SET V = V + 2";
                        AssertEquals(1, stat2.ExecuteNonQuery());
                        stat2.CommandText = "SELECT V FROM TEST WHERE ID = 1";
                        using (DbDataReader rs = stat2.ExecuteReader())
                        {
                            AssertTrue(rs.Read());
                            AssertEquals(4, rs.GetInt32(0));
                            AssertFalse(rs.Read());
                        }

                        t2.Commit();
                        t2.Dispose();
                        DbTransaction tb = conn2.BeginTransaction(isolationLevel);
                        stat2.Transaction = tb;
                        stat2.CommandText = "SELECT V FROM TEST WHERE ID = 1";
                        using (DbDataReader rs = stat2.ExecuteReader())
                        {
                            AssertTrue(rs.Read());
                            AssertEquals(4, rs.GetInt32(0));
                            AssertFalse(rs.Read());
                        }

                    }
                    else
                    {
                        // With a secondary index restored row is not the same
                        AssertThrows(ErrorCode.Deadlock1, stat2, "UPDATE TEST SET V = V + 2");
                        stat2.CommandText = "SELECT V FROM TEST WHERE ID = 1";
                        using (DbDataReader rs = stat2.ExecuteReader())
                        {
                            AssertTrue(rs.Read());
                            AssertEquals(2, rs.GetInt32(0));
                            AssertFalse(rs.Read());
                        }
                    }

                    Execute(stat1, "DELETE FROM TEST");
                    AssertThrows(ErrorCode.DuplicateKey1, stat2,
                        "INSERT INTO TEST VALUES (1, 3)");
                }
            }
        }

        DeleteDb("transaction");
    }

    private void TestIsolationLevelsCountAggregate()
    {
        TestIsolationLevelsCountAggregate(IsolationLevel.ReadUncommitted, 12, 15, 15, 16);
        TestIsolationLevelsCountAggregate(IsolationLevel.ReadCommitted, 6, 9, 15, 16);
        TestIsolationLevelsCountAggregate(IsolationLevel.RepeatableRead, 6, 9, 9, 15);
        TestIsolationLevelsCountAggregate(IsolationLevel.Snapshot, 6, 9, 9, 15);
        TestIsolationLevelsCountAggregate(IsolationLevel.Serializable, 6, 9, 9, 15);
    }

    private void TestIsolationLevelsCountAggregate(IsolationLevel isolationLevel,
        long uncommitted1, long uncommitted2, long committed, long committedOther)
    {
        DeleteDb("transaction");
        using (DbConnection conn1 = GetConnection("transaction"))
        using (DbConnection conn2 = GetConnection("transaction"))
        {
            DbCommand stat1 = conn1.CreateCommand();
            Execute(stat1, "CREATE TABLE TEST(V BIGINT) AS VALUES 1, 2, 3, 4, 5, 18");
            using DbTransaction t1 = conn1.BeginTransaction(isolationLevel);
            stat1.Transaction = t1;
            using DbCommand all = conn1.CreateCommand();
            all.Transaction = t1;
            all.CommandText = "SELECT COUNT(*) FROM TEST";
            using DbCommand simple = conn1.CreateCommand();
            simple.Transaction = t1;
            simple.CommandText = "SELECT COUNT(V) FROM TEST";
            using DbTransaction t2 = conn2.BeginTransaction();
            DbCommand stat2 = conn2.CreateCommand();
            stat2.Transaction = t2;
            TestIsolationLevelsCountAggregate(all, simple, 6);
            Execute(stat2, "DELETE FROM TEST WHERE V IN(3, 4)");
            Execute(stat2, "INSERT INTO TEST SELECT * FROM SYSTEM_RANGE(10, 17)");
            TestIsolationLevelsCountAggregate(all, simple, uncommitted1);
            Execute(stat1, "DELETE FROM TEST WHERE V = 2");
            Execute(stat1, "INSERT INTO TEST SELECT * FROM SYSTEM_RANGE(6, 9)");
            TestIsolationLevelsCountAggregate(all, simple, uncommitted2);
            t2.Commit();
            TestIsolationLevelsCountAggregate(all, simple, committed);
            t1.Commit();
            t1.Dispose();
            DbTransaction ta = conn1.BeginTransaction(isolationLevel);
            all.Transaction = ta;
            simple.Transaction = ta;
            TestIsolationLevelsCountAggregate(all, simple, 15);
            using (DbTransaction tb = conn2.BeginTransaction())
            {
                stat2.Transaction = tb;
                Execute(stat2, "DELETE FROM TEST WHERE V = 17");
                Execute(stat2, "INSERT INTO TEST VALUES 19, 20");
                tb.Commit();
            }

            TestIsolationLevelsCountAggregate(all, simple, committedOther);
            ta.Commit();
        }
    }

    private void TestIsolationLevelsCountAggregate(DbCommand all, DbCommand simple, long expected)
    {
        using (DbDataReader rs = all.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(expected, rs.GetInt64(0));
        }

        using (DbDataReader rs = simple.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(expected, rs.GetInt64(0));
        }
    }

    private void TestIsolationLevelsCountAggregate2()
    {
        TestIsolationLevelsCountAggregate2(IsolationLevel.ReadUncommitted);
        TestIsolationLevelsCountAggregate2(IsolationLevel.ReadCommitted);
        TestIsolationLevelsCountAggregate2(IsolationLevel.RepeatableRead);
        TestIsolationLevelsCountAggregate2(IsolationLevel.Snapshot);
        TestIsolationLevelsCountAggregate2(IsolationLevel.Serializable);
    }

    private void TestIsolationLevelsCountAggregate2(IsolationLevel isolationLevel)
    {
        DeleteDb("transaction");
        using (DbConnection conn1 = GetConnection("transaction"))
        using (DbConnection conn2 = GetConnection("transaction"))
        {
            using DbTransaction t1 = conn1.BeginTransaction(isolationLevel);
            DbCommand stat1 = conn1.CreateCommand();
            stat1.Transaction = t1;
            DbCommand stat2 = conn2.CreateCommand();
            Execute(stat1, "CREATE TABLE TEST(X INTEGER PRIMARY KEY, Y INTEGER) AS SELECT X, 1 FROM SYSTEM_RANGE(1, 100)");
            t1.Commit();
            t1.Dispose();
            DbTransaction ta = conn1.BeginTransaction(isolationLevel);
            stat1.Transaction = ta;
            using DbCommand prep = conn1.CreateCommand();
            prep.Transaction = ta;
            prep.CommandText = "SELECT COUNT(*) FROM TEST";
            // Initial count
            TestIsolationLevelCountAggregate2(prep, 100L);
            Execute(stat1, "INSERT INTO TEST VALUES (101, 2)");
            Execute(stat1, "DELETE FROM TEST WHERE X BETWEEN 2 AND 3");
            Execute(stat1, "UPDATE TEST SET Y = 2 WHERE X BETWEEN 4 AND 7");
            // Own uncommitted changes
            TestIsolationLevelCountAggregate2(prep, 99L);
            using (DbTransaction t2 = conn2.BeginTransaction(isolationLevel))
            {
                stat2.Transaction = t2;
                Execute(stat2, "INSERT INTO TEST VALUES (102, 2)");
                Execute(stat2, "DELETE FROM TEST WHERE X BETWEEN 12 AND 13");
                Execute(stat2, "UPDATE TEST SET Y = 2 WHERE X BETWEEN 14 AND 17");
                // Own and concurrent uncommitted changes
                TestIsolationLevelCountAggregate2(prep,
                    isolationLevel == IsolationLevel.ReadUncommitted ? 98L : 99L);
                t2.Commit();
            }

            // Own uncommitted and concurrent committed changes
            TestIsolationLevelCountAggregate2(prep,
                isolationLevel <= IsolationLevel.ReadCommitted ? 98L : 99L);
            ta.Commit();
            ta = conn1.BeginTransaction(isolationLevel);
            prep.Transaction = ta;
            // Everything is committed
            TestIsolationLevelCountAggregate2(prep, 98L);
            using (DbTransaction t2 = conn2.BeginTransaction(isolationLevel))
            {
                stat2.Transaction = t2;
                Execute(stat2, "INSERT INTO TEST VALUES (103, 2)");
                Execute(stat2, "DELETE FROM TEST WHERE X BETWEEN 22 AND 23");
                Execute(stat2, "UPDATE TEST SET Y = 2 WHERE X BETWEEN 24 AND 27");
                // Concurrent uncommitted changes
                TestIsolationLevelCountAggregate2(prep,
                    isolationLevel == IsolationLevel.ReadUncommitted ? 97L : 98L);
                t2.Commit();
            }

            // Concurrent committed changes
            TestIsolationLevelCountAggregate2(prep,
                isolationLevel <= IsolationLevel.ReadCommitted ? 97L : 98L);
            ta.Commit();
            ta = conn1.BeginTransaction(isolationLevel);
            prep.Transaction = ta;
            // Everything is committed again
            TestIsolationLevelCountAggregate2(prep, 97L);
            using (DbTransaction t2 = conn2.BeginTransaction(isolationLevel))
            {
                stat2.Transaction = t2;
                Execute(stat2, "INSERT INTO TEST VALUES (104, 2)");
                ta.Commit();
                ta = conn1.BeginTransaction(isolationLevel);
                prep.Transaction = ta;
                // Transaction was started with concurrent uncommitted change
                TestIsolationLevelCountAggregate2(prep,
                    isolationLevel == IsolationLevel.ReadUncommitted ? 98L : 97L);
                ta.Commit();
            }
        }
    }

    private void TestIsolationLevelCountAggregate2(DbCommand prep, long expected)
    {
        using (DbDataReader rs = prep.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(expected, rs.GetInt64(0));
        }
    }

    private void TestIsolationLevelsMetadata()
    {
        DeleteDb("transaction");
        using (DbConnection conn1 = GetConnection("transaction"))
        using (DbConnection conn2 = GetConnection("transaction"))
        {
            using DbCommand prep1 = conn1.CreateCommand();
            prep1.CommandText =
                "SELECT ISOLATION_LEVEL, SESSION_ID = SESSION_ID() FROM INFORMATION_SCHEMA.SESSIONS";
            using DbCommand prep2 = conn2.CreateCommand();
            prep2.CommandText =
                "SELECT ISOLATION_LEVEL, SESSION_ID = SESSION_ID() FROM INFORMATION_SCHEMA.SESSIONS";
            foreach (IsolationLevel isolationLevel in new[]
            {
                IsolationLevel.ReadUncommitted, IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead, IsolationLevel.Snapshot, IsolationLevel.Serializable,
            })
            {
                using (DbTransaction t2 = conn2.BeginTransaction(isolationLevel))
                {
                    prep2.Transaction = t2;
                    string level = isolationLevel switch
                    {
                        IsolationLevel.ReadUncommitted => "READ UNCOMMITTED",
                        IsolationLevel.ReadCommitted => "READ COMMITTED",
                        IsolationLevel.RepeatableRead => "REPEATABLE READ",
                        IsolationLevel.Snapshot => "SNAPSHOT",
                        _ => "SERIALIZABLE",
                    };
                    using (DbDataReader rs = prep1.ExecuteReader())
                    {
                        while (rs.Read())
                        {
                            if (rs.GetBoolean(1))
                            {
                                AssertEquals("READ COMMITTED", rs.GetString(0));
                            }
                            else
                            {
                                AssertEquals(level, rs.GetString(0));
                            }
                        }
                    }

                    using (DbDataReader rs = prep2.ExecuteReader())
                    {
                        while (rs.Read())
                        {
                            if (rs.GetBoolean(1))
                            {
                                AssertEquals(level, rs.GetString(0));
                            }
                            else
                            {
                                AssertEquals("READ COMMITTED", rs.GetString(0));
                            }
                        }
                    }
                }
            }
        }

        DeleteDb("transaction");
    }
}
