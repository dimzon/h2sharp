// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestIndex.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements/result sets are ADO.NET commands/readers;
// DatabaseMetaData.getIndexInfo is the INFORMATION_SCHEMA.INDEXES/INDEX_COLUMNS query
// through the ADO layer (the ASC_OR_DESC "D" answer is ORDERING_SPECIFICATION "DESC");
// Statement.execute's boolean result plus getResultSet/getUpdateCount is either a
// reader or ExecuteNonQuery's count, chosen by the statement text; AtomicInteger is a
// shared int with Interlocked; Thread is a .NET task.
//
// Skipped (port gap): testFunctionIndex and testFunctionIndexFunction need
// org.h2.tools.SimpleResultSet and the org.h2.command.query.Select stack probe; the
// port has neither, so the method and its alias are absent.
//
// Blocked by the port: on a file database CREATE INDEX goes through the unported
// MVTable.rebuildIndexBlockMerge (issue #62), which the first case already hits.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Index tests.
/// </summary>
public sealed class TestIndex : TestDb
{
    private DbConnection conn = null!;

    private DbCommand stat = null!;

    private Random random = new();

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        DeleteDb("index");
        TestOrderIndex();
        TestIndexTypes();
        TestHashIndexOnMemoryTable();
        TestErrorMessage();
        TestDuplicateKeyException();
        int to = Config.LockTimeout;
        Config.LockTimeout = 50000;
        try
        {
            TestConcurrentUpdate();
        }
        finally
        {
            Config.LockTimeout = to;
        }

        TestNonUniqueHashIndex();
        TestRenamePrimaryKey();
        TestRandomized();
        TestDescIndex();
        TestHashIndex();
        TestCompoundIndex_4161();

        if (Config.Networked && Config.Big)
        {
            return;
        }

        random = new Random(100);

        DeleteDb("index");
        TestWideIndex(147);
        TestWideIndex(313);
        TestWideIndex(979);
        TestWideIndex(1200);
        TestWideIndex(2400);
        if (Config.Big)
        {
            Random r = new();
            for (int j = 0; j < 10; j++)
            {
                int i = r.Next(3000);
                if ((i % 100) == 0)
                {
                    Println("width: " + i);
                }

                TestWideIndex(i);
            }
        }

        TestLike();
        Reconnect();
        TestConstraint();
        TestLargeIndex();
        TestMultiColumnIndex();
        TestHashIndex(true, false);
        TestHashIndex(false, false);
        TestHashIndex(true, true);
        TestHashIndex(false, true);
        TestMultiColumnHashIndex();

        conn.Close();
        DeleteDb("index");

        // This test uses its own connection
        TestEnumIndex();
    }

    private void TestOrderIndex()
    {
        conn = GetConnection("index");
        stat = conn.CreateCommand();
        Execute(stat, "create table test(id int, name varchar)");
        Execute(stat, "insert into test values (2, 'a'), (1, 'a')");
        Execute(stat, "create index on test(name)");
        stat.CommandText = "select id from test where name = 'a' order by id";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        conn.Close();
        DeleteDb("index");
    }

    private void TestIndexTypes()
    {
        conn = GetConnection("index");
        stat = conn.CreateCommand();
        foreach (string type in new[] { "unique", "hash", "unique hash" })
        {
            Execute(stat, "create table test(id int)");
            Execute(stat, "create " + type + " index idx_name on test(id)");
            Execute(stat, "insert into test select x from system_range(1, 1000)");
            stat.CommandText = "select * from test where id=100";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertFalse(rs.Read());
            }

            Execute(stat, "delete from test where id=100");
            stat.CommandText = "select * from test where id=100";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertFalse(rs.Read());
            }

            stat.CommandText = "select min(id), max(id) from test";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(1, rs.GetInt32(0));
                AssertEquals(1000, rs.GetInt32(1));
            }

            Execute(stat, "drop table test");
        }

        conn.Close();
        DeleteDb("index");
    }

    private void TestErrorMessage()
    {
        Reconnect();
        Execute(stat, "create table test(id int primary key, name varchar)");
        TestErrorMessage("PRIMARY", "KEY", " ON PUBLIC.TEST(ID)");
        Execute(stat, "create table test(id int, name varchar primary key)");
        TestErrorMessage("PRIMARY_KEY_2 ON PUBLIC.TEST(NAME)");
        Execute(stat, "create table test(id int, name varchar, primary key(id, name))");
        TestErrorMessage("PRIMARY_KEY_2 ON PUBLIC.TEST(ID, NAME)");
        Execute(stat, "create table test(id int, name varchar, primary key(name, id))");
        TestErrorMessage("PRIMARY_KEY_2 ON PUBLIC.TEST(NAME, ID)");
        Execute(stat, "create table test(id int, name int primary key)");
        TestErrorMessage("PRIMARY", "KEY", " ON PUBLIC.TEST(NAME)");
        Execute(stat, "create table test(id int, name int, unique(name))");
        TestErrorMessage("CONSTRAINT_2 INDEX PUBLIC.CONSTRAINT_INDEX_2 ON PUBLIC.TEST(NAME NULLS FIRST)");
        Execute(stat, "create table test(id int, name int, "
            + "constraint abc unique(name, id))");
        TestErrorMessage("ABC INDEX PUBLIC.ABC_INDEX_2 ON PUBLIC.TEST(NAME NULLS FIRST, ID NULLS FIRST)");
    }

    private void TestErrorMessage(params string[] expected)
    {
        try
        {
            Execute(stat, "INSERT INTO TEST VALUES(1, 1)");
            Execute(stat, "INSERT INTO TEST VALUES(1, 1)");
            Fail();
        }
        catch (H2SharpException e)
        {
            string m = e.Message;
            int start = m.IndexOf('"');
            int end = m.LastIndexOf('"');
            string s = m.Substring(start + 1, end - start - 1);
            foreach (string t in expected)
            {
                AssertContains(s, t);
            }
        }

        Execute(stat, "drop table test");
    }

    private void TestDuplicateKeyException()
    {
        Reconnect();
        Execute(stat, "create table test(id int primary key, name varchar(255))");
        Execute(stat, "create unique index idx_test_name on test(name)");
        Execute(stat, "insert into TEST values(1, 'Hello')");
        try
        {
            Execute(stat, "insert into TEST values(2, 'Hello')");
            Fail();
        }
        catch (H2SharpException ex)
        {
            AssertEquals(ErrorCode.DuplicateKey1, ex.H2ErrorCode);
            string m = ex.Message;
            // The format of the VALUES clause varies a little depending on the
            // type of the index, so just test that we're getting useful info
            // back.
            AssertContains(m, "IDX_TEST_NAME ON PUBLIC.TEST(NAME NULLS FIRST)");
            AssertContains(m, "'Hello'");
        }

        Execute(stat, "drop table test");
    }

    private sealed class ConcurrentUpdateThread
    {
        private readonly int[] concurrentUpdateId;
        private readonly int[] concurrentUpdateValue;
        private readonly DbCommand psInsert;
        private readonly DbCommand psDelete;

        public bool HaveDuplicateKeyException;

        public ConcurrentUpdateThread(DbConnection c, int[] concurrentUpdateId,
            int[] concurrentUpdateValue)
        {
            this.concurrentUpdateId = concurrentUpdateId;
            this.concurrentUpdateValue = concurrentUpdateValue;
            psInsert = c.CreateCommand();
            psInsert.CommandText = "insert into test(id, v) values (?, ?)";
            psInsert.Parameters.Add(new H2Parameter());
            psInsert.Parameters.Add(new H2Parameter());
            psDelete = c.CreateCommand();
            psDelete.CommandText = "delete from test where v = ?";
            psDelete.Parameters.Add(new H2Parameter());
        }

        public void Run()
        {
            for (int i = 0; i < 10000; i++)
            {
                try
                {
                    if (Random.Shared.NextDouble() > 0.05)
                    {
                        psInsert.Parameters[0].Value =
                            Interlocked.Increment(ref concurrentUpdateId[0]);
                        psInsert.Parameters[1].Value = concurrentUpdateValue[0];
                        _ = psInsert.ExecuteNonQuery();
                    }
                    else
                    {
                        psDelete.Parameters[0].Value = concurrentUpdateValue[0];
                        _ = psDelete.ExecuteNonQuery();
                    }
                }
                catch (H2SharpException ex)
                {
                    switch (ex.H2ErrorCode)
                    {
                        case ErrorCode.DuplicateKey1:
                            HaveDuplicateKeyException = true;
                            break;
                        case ErrorCode.ConcurrentUpdate1:
                            // Unlikely but possible
                            break;
                        default:
                            LogError("concurrent update", ex);
                            break;
                    }
                }

                if (Random.Shared.NextDouble() > 0.95)
                {
                    _ = Interlocked.Increment(ref concurrentUpdateValue[0]);
                }
            }
        }
    }

    private void TestConcurrentUpdate()
    {
        DbConnection c = GetConnection("index");
        DbCommand stat = c.CreateCommand();
        Execute(stat, "create table test(id int primary key, v int)");
        Execute(stat, "create unique index idx_value_name on test(v)");
        DbCommand check = c.CreateCommand();
        check.CommandText = "select v from test";
        ConcurrentUpdateThread[] threads = new ConcurrentUpdateThread[4];
        int[] concurrentUpdateId = new int[1];
        int[] concurrentUpdateValue = new int[1];

        // The same connection
        for (int i = 0; i < threads.Length; i++)
        {
            threads[i] = new ConcurrentUpdateThread(c, concurrentUpdateId, concurrentUpdateValue);
        }

        TestConcurrentUpdateRun(threads, check);
        // Different connections
        DbConnection[] connections = new DbConnection[threads.Length];
        for (int i = 0; i < threads.Length; i++)
        {
            DbConnection c2 = GetConnection("index");
            connections[i] = c2;
            threads[i] = new ConcurrentUpdateThread(c2, concurrentUpdateId, concurrentUpdateValue);
        }

        TestConcurrentUpdateRun(threads, check);
        foreach (DbConnection c2 in connections)
        {
            c2.Close();
        }

        Execute(stat, "drop table test");
        c.Close();
    }

    private void TestConcurrentUpdateRun(ConcurrentUpdateThread[] threads, DbCommand check)
    {
        Task[] tasks = new Task[threads.Length];
        for (int i = 0; i < threads.Length; i++)
        {
            ConcurrentUpdateThread thread = threads[i];
            tasks[i] = Task.Run(thread.Run);
        }

        Task.WaitAll(tasks);
        bool haveDuplicateKeyException = false;
        foreach (ConcurrentUpdateThread thread in threads)
        {
            haveDuplicateKeyException |= thread.HaveDuplicateKeyException;
        }

        AssertTrue("haveDuplicateKeys", haveDuplicateKeyException);
        HashSet<int> set = [];
        using DbDataReader rs = check.ExecuteReader();
        while (rs.Read())
        {
            if (!set.Add(rs.GetInt32(0)))
            {
                Fail("unique index violation");
            }
        }
    }

    private void TestNonUniqueHashIndex()
    {
        Reconnect();
        Execute(stat, "create memory table test(id bigint, data bigint)");
        Execute(stat, "create hash index on test(id)");
        Random rand = new(1);
        using DbCommand prepInsert = conn.CreateCommand();
        prepInsert.CommandText = "insert into test values(?, ?)";
        prepInsert.Parameters.Add(new H2Parameter());
        prepInsert.Parameters.Add(new H2Parameter());
        using DbCommand prepDelete = conn.CreateCommand();
        prepDelete.CommandText = "delete from test where id=?";
        prepDelete.Parameters.Add(new H2Parameter());
        using DbCommand prepSelect = conn.CreateCommand();
        prepSelect.CommandText = "select count(*) from test where id=?";
        prepSelect.Parameters.Add(new H2Parameter());
        Dictionary<long, int> map = [];
        for (int i = 0; i < 1000; i++)
        {
            long key = rand.Next(10) * 1000000000L;
            _ = map.TryGetValue(key, out int result);
            if (rand.Next(2) == 0)
            {
                prepSelect.Parameters[0].Value = key;
                using DbDataReader rs = prepSelect.ExecuteReader();
                rs.Read();
                AssertEquals(result, rs.GetInt32(0));
            }
            else
            {
                if (rand.Next(2) == 1)
                {
                    prepInsert.Parameters[0].Value = key;
                    prepInsert.Parameters[1].Value = rand.Next();
                    _ = prepInsert.ExecuteNonQuery();
                    map[key] = result + 1;
                }
                else
                {
                    prepDelete.Parameters[0].Value = key;
                    _ = prepDelete.ExecuteNonQuery();
                    map[key] = 0;
                }
            }
        }

        Execute(stat, "drop table test");
        conn.Close();
    }

    private void TestRenamePrimaryKey()
    {
        if (Config.Memory)
        {
            return;
        }

        Reconnect();
        Execute(stat, "create table test(id int not null)");
        Execute(stat, "alter table test add constraint x primary key(id)");
        string old = FirstIndexName("TEST", true);
        Execute(stat, "alter index " + old + " rename to y");
        AssertEquals("Y", FirstIndexName("TEST", true));
        Reconnect();
        AssertEquals("Y", FirstIndexName("TEST", true));
        Execute(stat, "drop table test");
    }

    /// <summary>
    /// The reference's DatabaseMetaData.getIndexInfo(..., unique, false) first row's
    /// INDEX_NAME; asked through INFORMATION_SCHEMA.INDEXES instead.
    /// </summary>
    /// <param name="table">the table name</param>
    /// <param name="unique">whether only unique indexes are wanted</param>
    /// <returns>the first index name</returns>
    private string FirstIndexName(string table, bool unique)
    {
        string sql = "SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES "
            + "WHERE TABLE_NAME = '" + table + "'"
            + (unique ? " AND INDEX_TYPE_NAME LIKE 'PRIMARY KEY%'" : "")
            + " ORDER BY INDEX_NAME";
        stat.CommandText = sql;
        using DbDataReader rs = stat.ExecuteReader();
        AssertTrue(rs.Read());
        return rs.GetString(0);
    }

    private void TestRandomized()
    {
        bool reopen = !Config.Memory;
        Random rand = new(1);
        Reconnect();
        Execute(stat, "drop all objects");
        Execute(stat, "CREATE TABLE TEST(ID identity default on null)");
        int len = GetSize(100, 1000);
        for (int i = 0; i < len; i++)
        {
            switch (rand.Next(4))
            {
                case 0:
                    if (rand.Next(10) == 0)
                    {
                        if (reopen)
                        {
                            Trace("reconnect");
                            Reconnect();
                        }
                    }

                    break;
                case 1:
                    Trace("insert");
                    Execute(stat, "insert into test(id) values(null)");
                    break;
                case 2:
                    Trace("delete");
                    Execute(stat, "delete from test");
                    break;
                case 3:
                    Trace("insert 1-100");
                    Execute(stat, "insert into test select null from system_range(1, 100)");
                    break;
            }
        }

        Execute(stat, "drop table test");
    }

    private void TestHashIndex()
    {
        Reconnect();
        Execute(stat, "create table testA(id int primary key, name varchar)");
        Execute(stat, "create table testB(id int primary key hash, name varchar)");
        int len = GetSize(300, 3000);
        Execute(stat, "insert into testA select x, 'Hello' from "
            + "system_range(1, " + len + ")");
        Execute(stat, "insert into testB select x, 'Hello' from "
            + "system_range(1, " + len + ")");
        Random rand = new(1);
        for (int i = 0; i < len; i++)
        {
            int x = rand.Next(len);
            string sql;
            switch (rand.Next(3))
            {
                case 0:
                    sql = "delete from testA where id = " + x;
                    break;
                case 1:
                    sql = "update testA set name = " + rand.Next(100) + " where id = " + x;
                    break;
                case 2:
                    sql = "select name from testA where id = " + x;
                    break;
                default:
                    sql = string.Empty;
                    break;
            }

            if (sql.StartsWith("select", StringComparison.OrdinalIgnoreCase))
            {
                string? s1 = FirstValue(stat, sql);
                string? s2 = FirstValue(stat, sql.Replace('A', 'B'));
                AssertEquals(s1, s2);
            }
            else
            {
                int count1 = ExecuteUpdate(stat, sql);
                int count2 = ExecuteUpdate(stat, sql.Replace('A', 'B'));
                AssertEquals(count1, count2);
            }
        }

        Execute(stat, "drop table testA");
        Execute(stat, "drop table testB");
        conn.Close();
    }

    private void Reconnect()
    {
        if (conn is not null)
        {
            conn.Close();
            conn = null!;
        }

        conn = GetConnection("index");
        stat = conn.CreateCommand();
    }

    private void TestDescIndex()
    {
        if (Config.Memory)
        {
            return;
        }

        Reconnect();
        Execute(stat, "CREATE TABLE TEST(ID INT)");
        Execute(stat, "CREATE INDEX IDX_ND ON TEST(ID DESC)");
        AssertEquals("DESC", FirstOrdering("IDX_ND"));
        Execute(stat, "INSERT INTO TEST SELECT X FROM SYSTEM_RANGE(1, 30)");
        stat.CommandText = "SELECT COUNT(*) FROM TEST WHERE ID BETWEEN 10 AND 20";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            rs.Read();
            AssertEquals(11, rs.GetInt32(0));
        }

        Reconnect();
        AssertEquals("DESC", FirstOrdering("IDX_ND"));
        stat.CommandText = "SELECT COUNT(*) FROM TEST WHERE ID BETWEEN 10 AND 20";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            rs.Read();
            AssertEquals(11, rs.GetInt32(0));
        }

        Execute(stat, "DROP TABLE TEST");

        Execute(stat, "create table test(x int, y int)");
        Execute(stat, "insert into test values(1, 1), (1, 2)");
        Execute(stat, "create index test_x_y on test (x desc, y desc)");
        stat.CommandText = "select * from test where x=1 and y<2";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }

        conn.Close();
    }

    /// <summary>
    /// The reference's getIndexInfo(...).getString("ASC_OR_DESC") for the first column;
    /// the INFORMATION_SCHEMA.INDEX_COLUMNS ordering specification here ("DESC" where the
    /// JDBC answer is "D").
    /// </summary>
    /// <param name="indexName">the index name</param>
    /// <returns>the ordering specification</returns>
    private string FirstOrdering(string indexName)
    {
        stat.CommandText = "SELECT ORDERING_SPECIFICATION FROM INFORMATION_SCHEMA.INDEX_COLUMNS "
            + "WHERE INDEX_NAME = '" + indexName + "' ORDER BY ORDINAL_POSITION";
        using DbDataReader rs = stat.ExecuteReader();
        AssertTrue(rs.Read());
        return rs.GetString(0);
    }

    private string GetRandomString(int len)
    {
        StringBuilder buff = new();
        for (int i = 0; i < len; i++)
        {
            _ = buff.Append((char)('a' + random.Next(26)));
        }

        return buff.ToString();
    }

    private void TestWideIndex(int length)
    {
        Reconnect();
        Execute(stat, "drop all objects");
        Execute(stat, "CREATE TABLE TEST(ID INT, NAME VARCHAR)");
        Execute(stat, "CREATE INDEX IDXNAME ON TEST(NAME)");
        for (int i = 0; i < 100; i++)
        {
            Execute(stat, "INSERT INTO TEST VALUES(" + i
                + ", SPACE(" + length + ") || " + i + " )");
        }

        stat.CommandText = "SELECT * FROM TEST ORDER BY NAME";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            while (rs.Read())
            {
                int id = rs.GetInt32(rs.GetOrdinal("ID"));
                string name = rs.GetString(rs.GetOrdinal("NAME"));
                AssertEquals("" + id, name.Trim());
            }
        }

        if (!Config.Memory)
        {
            Reconnect();
            stat.CommandText = "SELECT * FROM TEST ORDER BY NAME";
            using DbDataReader rs = stat.ExecuteReader();
            while (rs.Read())
            {
                int id = rs.GetInt32(rs.GetOrdinal("ID"));
                string name = rs.GetString(rs.GetOrdinal("NAME"));
                AssertEquals("" + id, name.Trim());
            }
        }

        Execute(stat, "drop all objects");
    }

    private void TestLike()
    {
        Reconnect();
        Execute(stat, "CREATE TABLE ABC(ID INT, NAME VARCHAR)");
        Execute(stat, "INSERT INTO ABC VALUES(1, 'Hello')");
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "SELECT * FROM ABC WHERE NAME LIKE CAST(? AS VARCHAR)";
            prep.Parameters.Add(new H2Parameter { Value = "Hi%" });
            using DbDataReader rs = prep.ExecuteReader();
            while (rs.Read())
            {
                // just loop
            }
        }

        Execute(stat, "DROP TABLE ABC");
    }

    private void TestConstraint()
    {
        if (Config.Memory)
        {
            return;
        }

        Execute(stat, "CREATE TABLE PARENT(ID INT PRIMARY KEY)");
        Execute(stat, "CREATE TABLE CHILD(ID INT PRIMARY KEY, "
            + "PID INT, FOREIGN KEY(PID) REFERENCES PARENT(ID))");
        Reconnect();
        // the reference's single DROP TABLE PARENT, CHILD (issue #61)
        Execute(stat, "DROP TABLE CHILD");
        Execute(stat, "DROP TABLE PARENT");
    }

    private void TestLargeIndex()
    {
        random = new Random(10);
        for (int i = 1; i < 100; i += GetSize(1000, 7))
        {
            Execute(stat, "DROP TABLE IF EXISTS TEST");
            Execute(stat, "CREATE TABLE TEST(NAME VARCHAR(" + i + "))");
            Execute(stat, "CREATE INDEX IDXNAME ON TEST(NAME)");
            using (DbCommand prep = conn.CreateCommand())
            {
                prep.CommandText = "INSERT INTO TEST VALUES(?)";
                prep.Parameters.Add(new H2Parameter());
                for (int j = 0; j < GetSize(2, 5); j++)
                {
                    prep.Parameters[0].Value = GetRandomString(i);
                    _ = prep.ExecuteNonQuery();
                }
            }

            if (!Config.Memory)
            {
                conn.Close();
                conn = GetConnection("index");
                stat = conn.CreateCommand();
            }

            stat.CommandText = "SELECT COUNT(*) FROM TEST WHERE NAME > 'mdd'";
            using DbDataReader rs = stat.ExecuteReader();
            rs.Read();
            int count = rs.GetInt32(0);
            Trace(i + " count=" + count);
        }

        Execute(stat, "DROP TABLE IF EXISTS TEST");
    }

    private void TestHashIndex(bool primaryKey, bool hash)
    {
        if (Config.Memory)
        {
            return;
        }

        Reconnect();

        Execute(stat, "DROP TABLE IF EXISTS TEST");
        if (primaryKey)
        {
            Execute(stat, "CREATE TABLE TEST(A INT PRIMARY KEY "
                + (hash ? "HASH" : "") + ", B INT)");
        }
        else
        {
            Execute(stat, "CREATE TABLE TEST(A INT, B INT)");
            Execute(stat, "CREATE UNIQUE " + (hash ? "HASH" : "") + " INDEX ON TEST(A)");
        }

        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "INSERT INTO TEST VALUES(?, ?)";
        prep.Parameters.Add(new H2Parameter());
        prep.Parameters.Add(new H2Parameter());
        int len = GetSize(5, 1000);
        for (int a = 0; a < len; a++)
        {
            prep.Parameters[0].Value = a;
            prep.Parameters[1].Value = a;
            _ = prep.ExecuteNonQuery();
            AssertEquals(1, GetValue("SELECT COUNT(*) FROM TEST WHERE A=" + a));
            AssertEquals(0, GetValue("SELECT COUNT(*) FROM TEST WHERE A=-1-" + a));
        }

        Reconnect();

        prep.Transaction = null;
        using DbCommand prepared = conn.CreateCommand();
        prepared.CommandText = "DELETE FROM TEST WHERE A=?";
        prepared.Parameters.Add(new H2Parameter());
        for (int a = 0; a < len; a++)
        {
            if (GetValue("SELECT COUNT(*) FROM TEST WHERE A=" + a) != 1)
            {
                AssertEquals(1, GetValue("SELECT COUNT(*) FROM TEST WHERE A=" + a));
            }

            prepared.Parameters[0].Value = a;
            AssertEquals(1, prepared.ExecuteNonQuery());
        }

        AssertEquals(0, GetValue("SELECT COUNT(*) FROM TEST"));
    }

    private void TestMultiColumnIndex()
    {
        Execute(stat, "DROP TABLE IF EXISTS TEST");
        Execute(stat, "CREATE TABLE TEST(A INT, B INT)");
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "INSERT INTO TEST VALUES(?, ?)";
        prep.Parameters.Add(new H2Parameter());
        prep.Parameters.Add(new H2Parameter());
        int len = GetSize(3, 260);
        for (int a = 0; a < len; a++)
        {
            prep.Parameters[0].Value = a;
            prep.Parameters[1].Value = a;
            _ = prep.ExecuteNonQuery();
        }

        Execute(stat, "INSERT INTO TEST SELECT A, B FROM TEST");
        Execute(stat, "CREATE INDEX ON TEST(A, B)");
        using DbCommand delete = conn.CreateCommand();
        delete.CommandText = "DELETE FROM TEST WHERE A=?";
        delete.Parameters.Add(new H2Parameter());
        for (int a = 0; a < len; a++)
        {
            Log("SELECT * FROM TEST");
            AssertEquals(2, GetValue("SELECT COUNT(*) FROM TEST WHERE A=" + (len - a - 1)));
            AssertEquals((len - a) * 2, GetValue("SELECT COUNT(*) FROM TEST"));
            delete.Parameters[0].Value = len - a - 1;
            _ = delete.ExecuteNonQuery();
        }

        AssertEquals(0, GetValue("SELECT COUNT(*) FROM TEST"));
    }

    private void TestMultiColumnHashIndex()
    {
        if (Config.Memory)
        {
            return;
        }

        Execute(stat, "DROP TABLE IF EXISTS TEST");
        Execute(stat, "CREATE TABLE TEST(A INT, B INT, DATA VARCHAR(255))");
        Execute(stat, "CREATE UNIQUE HASH INDEX IDX_AB ON TEST(A, B)");
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "INSERT INTO TEST VALUES(?, ?, ?)";
        prep.Parameters.Add(new H2Parameter());
        prep.Parameters.Add(new H2Parameter());
        prep.Parameters.Add(new H2Parameter());
        // speed is quadratic (len*len)
        int len = GetSize(2, 14);
        for (int a = 0; a < len; a++)
        {
            for (int b = 0; b < len; b += 2)
            {
                prep.Parameters[0].Value = a;
                prep.Parameters[1].Value = b;
                prep.Parameters[2].Value = "i(" + a + "," + b + ")";
                _ = prep.ExecuteNonQuery();
            }
        }

        Reconnect();

        using (DbCommand update = conn.CreateCommand())
        {
            update.CommandText = "UPDATE TEST SET DATA=DATA||? WHERE A=? AND B=?";
            update.Parameters.Add(new H2Parameter());
            update.Parameters.Add(new H2Parameter());
            update.Parameters.Add(new H2Parameter());
            for (int a = 0; a < len; a++)
            {
                for (int b = 0; b < len; b += 2)
                {
                    update.Parameters[0].Value = "u(" + a + "," + b + ")";
                    update.Parameters[1].Value = a;
                    update.Parameters[2].Value = b;
                    _ = update.ExecuteNonQuery();
                }
            }
        }

        Reconnect();

        stat.CommandText = "SELECT * FROM TEST WHERE DATA <> 'i('||a||','||b||')u('||a||','||b||')'";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertFalse(rs.Read());
        }

        AssertEquals(len * (len / 2), GetValue("SELECT COUNT(*) FROM TEST"));
        Execute(stat, "DROP TABLE TEST");
    }

    private void TestHashIndexOnMemoryTable()
    {
        Reconnect();
        Execute(stat, "drop table if exists hash_index_test");
        Execute(stat, "create memory table hash_index_test as "
            + "select x as id, x % 10 as data from (select *  from system_range(1, 100))");
        Execute(stat, "create hash index idx2 on hash_index_test(data)");
        AssertEquals(10,
            GetValue("select count(*) from hash_index_test where data = 1"));

        Execute(stat, "drop index idx2");
        Execute(stat, "create unique hash index idx2 on hash_index_test(id)");
        AssertEquals(1,
            GetValue("select count(*) from hash_index_test where id = 1"));
    }

    private int GetValue(string sql)
    {
        stat.CommandText = sql;
        using DbDataReader rs = stat.ExecuteReader();
        rs.Read();
        return rs.GetInt32(0);
    }

    private void Log(string sql)
    {
        Trace(sql);
        stat.CommandText = sql;
        using DbDataReader rs = stat.ExecuteReader();
        while (rs.Read())
        {
            StringBuilder buff = new();
            for (int i = 0; i < rs.FieldCount; i++)
            {
                if (i > 0)
                {
                    _ = buff.Append(", ");
                }

                _ = buff.Append("[" + i + "]=" + rs.GetString(i));
            }

            Trace(buff.ToString());
        }

        Trace("---done---");
    }

    private void TestEnumIndex()
    {
        if (Config.Memory || Config.Networked)
        {
            return;
        }

        DeleteDb("index");
        string url = "jdbc:h2:" + GetBaseDir() + "/index;DB_CLOSE_DELAY=0"
            + ";USER=" + GetUser() + ";PASSWORD=" + GetPassword();
        using DbConnection conn = new H2Connection(url);
        conn.Open();
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "CREATE TABLE TEST(ID INT, V ENUM('A', 'B'), CONSTRAINT PK PRIMARY KEY(ID, V))");
        Execute(stat, "INSERT INTO TEST VALUES (1, 'A'), (2, 'B')");

        conn.Close();
        using DbConnection conn2 = new H2Connection(url);
        conn2.Open();
        stat = conn2.CreateCommand();

        Execute(stat, "DELETE FROM TEST WHERE V = 'A'");
        Execute(stat, "DROP TABLE TEST");

        conn2.Close();
        DeleteDb("index");
    }

    /// <summary>
    /// Pick the better index when there are two competing indexes that both cover the
    /// required columns, https://github.com/h2database/h2database/issues/4161
    /// </summary>
    private void TestCompoundIndex_4161()
    {
        DbConnection conn = GetConnection("index");
        stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE tbl ( c1 INTEGER, c2 INTEGER, c3 INTEGER, c4 INTEGER, c5 INTEGER, c6 INTEGER,"
            + " c7 INTEGER );");
        Execute(stat, "insert into tbl select x, 0, 0, 0, 0, 0, 0 from system_range(1, 1000)");

        Execute(stat, "CREATE INDEX idx1 ON tbl ( c1, c2, c3, c4, c5 )");
        stat.CommandText = "EXPLAIN ANALYZE UPDATE tbl SET c6=6 WHERE c1=1 AND c2=2 AND c3=3 AND c4=4 AND c5=5";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), "PUBLIC.IDX1: C1 = 1");
        }

        Execute(stat, "CREATE INDEX idx2 ON tbl ( c1, c7 )");
        stat.CommandText = "EXPLAIN ANALYZE UPDATE tbl SET c6=6 WHERE c1=1 AND c2=2 AND c3=3 AND c4=4 AND c5=5";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertContains(rs.GetString(0), "PUBLIC.IDX1: C1 = 1");
        }

        conn.Close();
        DeleteDb("index");
    }

    private static int ExecuteUpdate(DbCommand command, string sql)
    {
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    private static string? FirstValue(DbCommand command, string sql)
    {
        command.CommandText = sql;
        using DbDataReader rs = command.ExecuteReader();
        return rs.Read() ? rs.GetString(0) : null;
    }
}
