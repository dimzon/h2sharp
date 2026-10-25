// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestSequence.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; setAutoCommit(false) is an
// explicit transaction the statements carry. The reference's main() entry is the
// runner; xUnit runs Test() directly.
// Java anonymous Task subclasses are named private CLR subclasses; stop/join and
// exception capture use the port of org.h2.util.Task, rather than an indexed closure.
//
// Modified (deferred, see MIGRATION_STATUS.md): testConcurrentCreate creates a trigger
// whose class implements org.h2.api.Trigger, which the port does not have; the part is
// skipped until the trigger API is ported.
// BlockedByPort: the sequence scenarios now reach testCreateSql, which needs SCRIPT NODATA.
using H2Sharp.Engine;
using Xunit;
using H2Task = H2Sharp.Util.H2Task;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests the sequence feature of this database.
/// </summary>
public sealed class TestSequence : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        // testConcurrentCreate: deferred, see the header (the Trigger API is not ported).
        TestConcurrentNextAndCurrentValue();
        TestSchemaSearchPath();
        TestAlterSequenceColumn();
        TestAlterSequence();
        TestCache();
        TestTwo();
        TestMetaTable();
        TestCreateWithMinValue();
        TestCreateWithMaxValue();
        TestCreationErrors();
        TestCreateSql();
        TestDefaultMinMax();
        DeleteDb("sequence");
    }

    private void TestConcurrentNextAndCurrentValue()
    {
        DeleteDb("sequence");
        string url = GetURL("sequence", true);
        DbConnection conn = GetConnection(url);
        H2Task[] tasks = new H2Task[2];
        try
        {
            DbCommand stat = conn.CreateCommand();
            Execute(stat, "CREATE SEQUENCE SEQ1");
            Execute(stat, "CREATE SEQUENCE SEQ2");
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = new ConcurrentNextAndCurrentValueTask(this, url).Execute();
            }
            Thread.Sleep(1000);
            foreach (H2Task task in tasks)
            {
                Exception? e = task.GetException();
                if (e is not null)
                {
                    throw new AssertionError(e.Message, e);
                }
            }
        }
        finally
        {
            foreach (H2Task? task in tasks) task?.Join();
            conn.Close();
        }
    }

    // Named CLR equivalent of the anonymous org.h2.util.Task in the reference method.
    private sealed class ConcurrentNextAndCurrentValueTask(TestSequence test, string url) : H2Task
    {
        public override void Call()
        {
            using DbConnection taskConn = test.GetConnection(url);
            using DbCommand next1 = taskConn.CreateCommand();
            next1.CommandText = "CALL NEXT VALUE FOR SEQ1";
            using DbCommand next2 = taskConn.CreateCommand();
            next2.CommandText = "CALL NEXT VALUE FOR SEQ2";
            using DbCommand current1 = taskConn.CreateCommand();
            current1.CommandText = "CALL CURRENT VALUE FOR SEQ1";
            using DbCommand current2 = taskConn.CreateCommand();
            current2.CommandText = "CALL CURRENT VALUE FOR SEQ2";
            while (!Stop)
            {
                long v1, v2;
                using (DbDataReader rs = next1.ExecuteReader())
                {
                    test.AssertTrue(rs.Read());
                    v1 = rs.GetInt64(0);
                }
                using (DbDataReader rs = next2.ExecuteReader())
                {
                    test.AssertTrue(rs.Read());
                    v2 = rs.GetInt64(0);
                }
                using (DbDataReader rs = current1.ExecuteReader())
                {
                    test.AssertTrue(rs.Read());
                    if (v1 != rs.GetInt64(0)) throw new Exception("Unexpected CURRENT VALUE FOR SEQ1");
                }
                using (DbDataReader rs = current2.ExecuteReader())
                {
                    test.AssertTrue(rs.Read());
                    if (v2 != rs.GetInt64(0)) throw new Exception("Unexpected CURRENT VALUE FOR SEQ2");
                }
            }
        }
    }

    private void TestSchemaSearchPath()
    {
        DeleteDb("sequence");
        using DbConnection conn = GetConnection("sequence");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE SCHEMA TEST");
        Execute(stat, "CREATE SEQUENCE TEST.TEST_SEQ");
        Execute(stat, "SET SCHEMA_SEARCH_PATH PUBLIC, TEST");
        Execute(stat, "CALL NEXT VALUE FOR TEST_SEQ");
        Execute(stat, "CALL CURRENT VALUE FOR TEST_SEQ");
    }

    private void TestAlterSequenceColumn()
    {
        DeleteDb("sequence");
        using DbConnection conn = GetConnection("sequence");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT , NAME VARCHAR(255))");
        Execute(stat, "INSERT INTO TEST VALUES(1, 'Hello')");
        Execute(stat, "ALTER TABLE TEST ALTER COLUMN ID INT GENERATED BY DEFAULT AS IDENTITY");
        Execute(stat, "ALTER TABLE test ALTER COLUMN ID RESTART WITH 3");
        Execute(stat, "INSERT INTO TEST (name) VALUES('Other World')");
    }

    private void TestAlterSequence()
    {
        Test_("create sequence s; alter sequence s restart with 2", null, 2, 3, 4);
        Test_("create sequence s; alter sequence s restart with 7", null, 7, 8, 9, 10);
        Test_("create sequence s; alter sequence s start with 3 restart with 11 minvalue 3 maxvalue 12 cycle",
            null, 11, 12, 3, 4);
        Test_("create sequence s; alter sequence s restart with 5 cache 2",
            null, 5, 6, 7, 8);
        Test_("create sequence s; alter sequence s restart with 9 "
            + "maxvalue 12 nocycle nocache",
            "Sequence \"S\" has run out of numbers", 9, 10, 11, 12);
    }

    private void TestCache()
    {
        if (Config.Memory)
        {
            return;
        }

        DeleteDb("sequence");
        DbConnection conn = GetConnection("sequence");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create sequence test_Sequence");
        Execute(stat, "create sequence test_Sequence3 cache 3");
        conn.Close();
        conn = GetConnection("sequence");
        stat = conn.CreateCommand();
        Execute(stat, "call next value for test_Sequence");
        Execute(stat, "call next value for test_Sequence3");
        stat.CommandText = "select * from "
            + "information_schema.sequences order by sequence_name";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("TEST_SEQUENCE", rs.GetString(rs.GetOrdinal("SEQUENCE_NAME")));
            AssertEquals("32", rs.GetString(rs.GetOrdinal("CACHE")));
            AssertTrue(rs.Read());
            AssertEquals("TEST_SEQUENCE3", rs.GetString(rs.GetOrdinal("SEQUENCE_NAME")));
            AssertEquals("3", rs.GetString(rs.GetOrdinal("CACHE")));
            AssertFalse(rs.Read());
        }

        conn.Close();
    }

    private void TestMetaTable()
    {
        DeleteDb("sequence");
        using DbConnection conn = GetConnection("sequence");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create sequence a");
        Execute(stat, "create sequence b start with 7 minvalue 5 "
            + "maxvalue 9 cycle increment by 2 nocache");
        Execute(stat, "create sequence c start with -4 minvalue -9 "
            + "maxvalue -3 no cycle increment by -2 cache 3");
        if (!Config.Memory)
        {
            conn.Close();
            conn.Open();
            stat = conn.CreateCommand();
        }

        stat.CommandText = "select * from "
            + "information_schema.sequences order by sequence_name";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("SEQUENCE", rs.GetString(rs.GetOrdinal("SEQUENCE_CATALOG")));
            AssertEquals("PUBLIC", rs.GetString(rs.GetOrdinal("SEQUENCE_SCHEMA")));
            AssertEquals("A", rs.GetString(rs.GetOrdinal("SEQUENCE_NAME")));
            AssertEquals(1, rs.GetInt64(rs.GetOrdinal("BASE_VALUE")));
            AssertEquals(1, rs.GetInt64(rs.GetOrdinal("INCREMENT")));
            AssertNull(rs.IsDBNull(rs.GetOrdinal("REMARKS"))
                ? null : rs.GetString(rs.GetOrdinal("REMARKS")));
            AssertEquals(32, rs.GetInt64(rs.GetOrdinal("CACHE")));
            AssertEquals(1, rs.GetInt64(rs.GetOrdinal("MINIMUM_VALUE")));
            AssertEquals(long.MaxValue, rs.GetInt64(rs.GetOrdinal("MAXIMUM_VALUE")));
            AssertEquals("NO", rs.GetString(rs.GetOrdinal("CYCLE_OPTION")));
            AssertTrue(rs.Read());
            AssertEquals("SEQUENCE", rs.GetString(rs.GetOrdinal("SEQUENCE_CATALOG")));
            AssertEquals("PUBLIC", rs.GetString(rs.GetOrdinal("SEQUENCE_SCHEMA")));
            AssertEquals("B", rs.GetString(rs.GetOrdinal("SEQUENCE_NAME")));
            AssertEquals(7, rs.GetInt64(rs.GetOrdinal("BASE_VALUE")));
            AssertEquals(2, rs.GetInt64(rs.GetOrdinal("INCREMENT")));
            AssertNull(rs.IsDBNull(rs.GetOrdinal("REMARKS"))
                ? null : rs.GetString(rs.GetOrdinal("REMARKS")));
            AssertEquals(1, rs.GetInt64(rs.GetOrdinal("CACHE")));
            AssertEquals(5, rs.GetInt64(rs.GetOrdinal("MINIMUM_VALUE")));
            AssertEquals(9, rs.GetInt64(rs.GetOrdinal("MAXIMUM_VALUE")));
            AssertEquals("YES", rs.GetString(rs.GetOrdinal("CYCLE_OPTION")));
            AssertTrue(rs.Read());
            AssertEquals("SEQUENCE", rs.GetString(rs.GetOrdinal("SEQUENCE_CATALOG")));
            AssertEquals("PUBLIC", rs.GetString(rs.GetOrdinal("SEQUENCE_SCHEMA")));
            AssertEquals("C", rs.GetString(rs.GetOrdinal("SEQUENCE_NAME")));
            AssertEquals(-4, rs.GetInt64(rs.GetOrdinal("BASE_VALUE")));
            AssertEquals(-2, rs.GetInt64(rs.GetOrdinal("INCREMENT")));
            AssertNull(rs.IsDBNull(rs.GetOrdinal("REMARKS"))
                ? null : rs.GetString(rs.GetOrdinal("REMARKS")));
            AssertEquals(3, rs.GetInt64(rs.GetOrdinal("CACHE")));
            AssertEquals(-9, rs.GetInt64(rs.GetOrdinal("MINIMUM_VALUE")));
            AssertEquals(-3, rs.GetInt64(rs.GetOrdinal("MAXIMUM_VALUE")));
            AssertEquals("NO", rs.GetString(rs.GetOrdinal("CYCLE_OPTION")));
            AssertFalse(rs.Read());
        }
    }

    private void TestCreateWithMinValue()
    {
        Test_("create sequence s minvalue 3", null, 3, 4, 5, 6);
        Test_("create sequence s minvalue -3 increment by -1 cycle",
            null, -1, -2, -3, -1);
        Test_("create sequence s minvalue -3 increment by -1",
            "Sequence \"S\" has run out of numbers", -1, -2, -3);
        Test_("create sequence s minvalue -3 increment by -1 nocycle",
            "Sequence \"S\" has run out of numbers", -1, -2, -3);
        Test_("create sequence s minvalue -3 increment by -1 no cycle",
            "Sequence \"S\" has run out of numbers", -1, -2, -3);
        Test_("create sequence s minvalue -3 increment by -1 nocache cycle",
            null, -1, -2, -3, -1);
        Test_("create sequence s minvalue -3 increment by -1 nocache",
            "Sequence \"S\" has run out of numbers", -1, -2, -3);
        Test_("create sequence s minvalue -3 increment by -1 nocache nocycle",
            "Sequence \"S\" has run out of numbers", -1, -2, -3);
        Test_("create sequence s minvalue -3 increment by -1 no cache no cycle",
            "Sequence \"S\" has run out of numbers", -1, -2, -3);
    }

    private void TestCreateWithMaxValue()
    {
        Test_("create sequence s maxvalue -3 increment by -1",
            null, -3, -4, -5, -6);
        Test_("create sequence s maxvalue 3 cycle", null, 1, 2, 3, 1);
        Test_("create sequence s maxvalue 3",
            "Sequence \"S\" has run out of numbers", 1, 2, 3);
        Test_("create sequence s maxvalue 3 nocycle",
            "Sequence \"S\" has run out of numbers", 1, 2, 3);
        Test_("create sequence s maxvalue 3 no cycle",
            "Sequence \"S\" has run out of numbers", 1, 2, 3);
        Test_("create sequence s maxvalue 3 nocache cycle",
            null, 1, 2, 3, 1);
        Test_("create sequence s maxvalue 3 nocache",
            "Sequence \"S\" has run out of numbers", 1, 2, 3);
        Test_("create sequence s maxvalue 3 nocache nocycle",
            "Sequence \"S\" has run out of numbers", 1, 2, 3);
        Test_("create sequence s maxvalue 3 no cache no cycle",
            "Sequence \"S\" has run out of numbers", 1, 2, 3);
    }

    private void TestCreationErrors()
    {
        DeleteDb("sequence");
        using DbConnection conn = GetConnection("sequence");
        DbCommand stat = conn.CreateCommand();
        ExpectError(
            stat,
            "create sequence a minvalue 5 start with 2",
            "Unable to create or alter sequence \"A\" because of "
            + "invalid attributes (base value \"2\", start value \"2\", "
            + "min value \"5\", max value \"" + long.MaxValue
            + "\", increment \"1\", cache size \"32\")");
        ExpectError(
            stat,
            "create sequence b maxvalue 5 start with 7",
            "Unable to create or alter sequence \"B\" because of "
            + "invalid attributes (base value \"7\", start value \"7\", "
            + "min value \"1\", max value \"5\", increment \"1\", cache size \"32\")");
        ExpectError(
            stat,
            "create sequence c minvalue 5 maxvalue 2",
            "Unable to create or alter sequence \"C\" because of "
            + "invalid attributes (base value \"5\", start value \"5\", "
            + "min value \"5\", max value \"2\", increment \"1\", cache size \"32\")");
        ExpectError(
            stat,
            "create sequence d increment by 0",
            "Unable to create or alter sequence \"D\" because of "
            + "invalid attributes (base value \"1\", start value \"1\", "
            + "min value \"1\", max value \""
            + long.MaxValue + "\", increment \"0\", cache size \"32\")");
        ExpectError(stat,
            "create sequence e minvalue 1 maxvalue 5 increment 99",
            "Unable to create or alter sequence \"E\" because of "
            + "invalid attributes (base value \"1\", start value \"1\", "
            + "min value \"1\", max value \"5\", increment \"99\", cache size \"32\")");
    }

    private void TestCreateSql()
    {
        DeleteDb("sequence");
        using DbConnection conn = GetConnection("sequence");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create sequence a");
        Execute(stat, "create sequence b start with 5 increment by 2 "
            + "minvalue 3 maxvalue 7 cycle nocache");
        Execute(stat, "create sequence c start with 3 increment by 1 "
            + "minvalue 2 maxvalue 9 nocycle cache 2");
        Execute(stat, "create sequence d nomaxvalue no minvalue no cache nocycle");
        Execute(stat, "create sequence e cache 1");
        List<string> script = [];
        stat.CommandText = "script nodata";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            while (rs.Read())
            {
                script.Add(rs.GetString(0));
            }
        }

        script.Sort(string.CompareOrdinal);
        AssertEquals("-- H2 " + Constants.Version + ";", script[0]);
        AssertEquals("CREATE SEQUENCE \"PUBLIC\".\"A\" START WITH 1;", script[1]);
        AssertEquals("CREATE SEQUENCE \"PUBLIC\".\"B\" START "
            + "WITH 5 INCREMENT BY 2 "
            + "MINVALUE 3 MAXVALUE 7 CYCLE NO CACHE;", script[2]);
        AssertEquals("CREATE SEQUENCE \"PUBLIC\".\"C\" START "
            + "WITH 3 MINVALUE 2 MAXVALUE 9 CACHE 2;",
            script[3]);
        AssertEquals("CREATE SEQUENCE \"PUBLIC\".\"D\" START "
            + "WITH 1 NO CACHE;", script[4]);
        AssertEquals("CREATE SEQUENCE \"PUBLIC\".\"E\" START "
            + "WITH 1 NO CACHE;", script[5]);
    }

    private void TestDefaultMinMax()
    {
        // test that we calculate default MIN and MAX values correctly
        DeleteDb("sequence");
        using DbConnection conn = GetConnection("sequence");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create sequence a START WITH -7320917853639540658");
        Execute(stat, "create sequence b START WITH 7320917853639540658 INCREMENT -1");
    }

    private void TestTwo()
    {
        DeleteDb("sequence");
        using DbConnection conn = GetConnection("sequence");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create sequence s");
        using DbTransaction transaction = conn.BeginTransaction();
        stat.Transaction = transaction;

        using (DbConnection conn2 = GetConnection("sequence"))
        {
            DbCommand stat2 = conn2.CreateCommand();
            using DbTransaction transaction2 = conn2.BeginTransaction();
            stat2.Transaction = transaction2;

            long last = 0;
            for (int i = 0; i < 100; i++)
            {
                long v1 = GetNext(stat);
                AssertTrue(v1 > last);
                last = v1;
                for (int j = 0; j < 100; j++)
                {
                    long v2 = GetNext(stat2);
                    AssertTrue(v2 > last);
                    last = v2;
                }
            }
        }
    }

    /// <summary>
    /// The reference's test(setupSql, finalError, values); the underscore keeps it apart
    /// from the xUnit entry.
    /// </summary>
    /// <param name="setupSql">the setup SQL</param>
    /// <param name="finalError">the error the next value must answer, or null</param>
    /// <param name="values">the values the sequence hands out</param>
    private void Test_(string setupSql, string? finalError, params long[] values)
    {
        DeleteDb("sequence");
        DbConnection conn = GetConnection("sequence");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, setupSql);
        if (!Config.Memory)
        {
            conn.Close();
            conn = GetConnection("sequence");
        }
        stat = conn.CreateCommand();
        foreach (long value in values)
        {
            AssertEquals(value, GetNext(stat));
        }

        if (finalError is not null)
        {
            try
            {
                _ = GetNext(stat);
                Fail("Expected error: " + finalError);
            }
            catch (H2SharpException e)
            {
                AssertContains(e.Message ?? "", finalError);
            }
        }

        conn.Close();
    }

    private void ExpectError(DbCommand stat, string sql, string error)
    {
        try
        {
            Execute(stat, sql);
            Fail("Expected error: " + error);
        }
        catch (H2SharpException e)
        {
            AssertContains(e.Message ?? "", error);
        }
    }

    private long GetNext(DbCommand stat)
    {
        stat.CommandText = "call next value for s";
        using DbDataReader rs = stat.ExecuteReader();
        AssertTrue(rs.Read());
        long value = rs.GetInt64(0);
        return value;
    }
}
