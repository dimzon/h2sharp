// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestGeneralCommonTableQueries.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; setInt is a parameter value,
// setAutoCommit(false) is an explicit transaction that the commands it runs carry, and
// the metadata questions (getColumnLabel, getColumnCount) are the reader's GetName and
// FieldCount. The reference's main() entry is the runner; xUnit runs Test() directly.
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test non-recursive queries using WITH, but more than one common table defined.
/// </summary>
public sealed class TestGeneralCommonTableQueries : AbstractBaseForCommonTableExpressions
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestSimpleSelect();
        TestImpliedColumnNames();
        TestChainedQuery();
        TestParameterizedQuery();
        TestNumberedParameterizedQuery();
        TestColumnNames();

        TestInsert();
        TestUpdate();
        TestDelete();
        TestMerge();
        TestCreateTable();
        TestNestedSQL();
        TestSimple4RowRecursiveQuery();
        TestSimple2By4RowRecursiveQuery();
        TestSimple3RowRecursiveQueryWithLazyEval();
        TestSimple3RowRecursiveQueryDropAllObjects();
    }

    private static void SetInt(DbCommand prep, int index, int value)
    {
        while (prep.Parameters.Count < index)
        {
            _ = prep.Parameters.Add(new H2Parameter());
        }

        prep.Parameters[index - 1].Value = value;
    }

    private void TestSimpleSelect()
    {
        DeleteDb("commonTableExpressionQueries");
        using DbConnection conn = GetConnection("commonTableExpressionQueries");
        DbCommand stat = conn.CreateCommand();
        string simpleTwoColumnQuery = "with "
            + "t1(n) as (select 1 as first) "
            + ",t2(n) as (select 2 as first) "
            + "select * from t1 union all select * from t2";
        stat.CommandText = simpleTwoColumnQuery;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = simpleTwoColumnQuery;
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(1, rs.GetInt32(0));
                AssertTrue(rs.Read());
                AssertEquals(2, rs.GetInt32(0));
                AssertFalse(rs.Read());
            }

            prep.CommandText = "with "
                + "t1(n) as (select 2 as first) "
                + ",t2(n) as (select 3 as first) "
                + "select * from t1 union all select * from t2 where n<>?";
            SetInt(prep, 1, 0);
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(2, rs.GetInt32(0));
                AssertTrue(rs.Read());
                AssertEquals(3, rs.GetInt32(0));
                AssertFalse(rs.Read());
            }

            prep.CommandText = "with "
                + "t1(n) as (select 2 as first) "
                + ",t2(n) as (select 3 as first) "
                + ",t3(n) as (select 4 as first) "
                + "select * from t1 union all select * from t2 union all select * from t3 where n<>?";
            SetInt(prep, 1, 4);
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(2, rs.GetInt32(0));
                AssertTrue(rs.Read());
                AssertEquals(3, rs.GetInt32(0));
                AssertFalse(rs.Read());
            }
        }
    }

    private void TestImpliedColumnNames()
    {
        DeleteDb("commonTableExpressionQueries");
        using DbConnection conn = GetConnection("commonTableExpressionQueries");
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "with "
            + "t1 as (select 2 as first_col) "
            + ",t2 as (select first_col+1 from t1) "
            + ",t3 as (select 4 as first_col) "
            + "select * from t1 union all select * from t2 union all select * from t3 where first_col<>?";
        SetInt(prep, 1, 4);
        using (DbDataReader rs = prep.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(3, rs.GetInt32(rs.GetOrdinal("FIRST_COL")));
            AssertFalse(rs.Read());
            AssertEquals(rs.FieldCount, 1);
            AssertEquals("FIRST_COL", rs.GetName(0));
        }
    }

    private void TestChainedQuery()
    {
        DeleteDb("commonTableExpressionQueries");
        using DbConnection conn = GetConnection("commonTableExpressionQueries");
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "    WITH t1 AS ("
            + "        SELECT 1 AS FIRST_COLUMN"
            + "),"
            + "     t2 AS ("
            + "        SELECT FIRST_COLUMN+1 AS FIRST_COLUMN FROM t1 "
            + ") "
            + "SELECT sum(FIRST_COLUMN) FROM t2";

        using (DbDataReader rs = prep.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }
    }

    private void TestParameterizedQuery()
    {
        DeleteDb("commonTableExpressionQueries");
        using DbConnection conn = GetConnection("commonTableExpressionQueries");
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "WITH t1 AS ("
            + "     SELECT X, 'T1' FROM SYSTEM_RANGE(?,?)"
            + "),"
            + "t2 AS ("
            + "     SELECT X, 'T2' FROM SYSTEM_RANGE(?,?)"
            + ") "
            + "SELECT * FROM t1 UNION ALL SELECT * FROM t2 "
            + "UNION ALL SELECT X, 'Q' FROM SYSTEM_RANGE(?,?)";
        SetInt(prep, 1, 1);
        SetInt(prep, 2, 2);
        SetInt(prep, 3, 3);
        SetInt(prep, 4, 4);
        SetInt(prep, 5, 5);
        SetInt(prep, 6, 6);
        using (DbDataReader rs = prep.ExecuteReader())
        {
            foreach (int n in new[] { 1, 2, 3, 4, 5, 6 })
            {
                AssertTrue(rs.Read());
                AssertEquals(n, rs.GetInt32(0));
            }

            AssertFalse(rs.Read());
        }

        // call it twice
        using (DbDataReader rs = prep.ExecuteReader())
        {
            foreach (int n in new[] { 1, 2, 3, 4, 5, 6 })
            {
                AssertTrue(rs.Read());
                AssertEquals(n, rs.GetInt32(0));
            }

            AssertFalse(rs.Read());
        }
    }

    private void TestNumberedParameterizedQuery()
    {
        DeleteDb("commonTableExpressionQueries");
        using DbConnection conn = GetConnection("commonTableExpressionQueries");
        using DbTransaction transaction = conn.BeginTransaction();

        using (DbCommand prep = conn.CreateCommand())
        {
            prep.Transaction = transaction;
            prep.CommandText = "WITH t1 AS ("
                + "     SELECT R.X, 'T1' FROM SYSTEM_RANGE(?1,?2) R"
                + "),"
                + "t2 AS ("
                + "     SELECT R.X, 'T2' FROM SYSTEM_RANGE(?3,?4) R"
                + ") "
                + "SELECT * FROM t1 UNION ALL SELECT * FROM t2 UNION ALL SELECT X, 'Q' FROM SYSTEM_RANGE(?5,?6)";
            SetInt(prep, 1, 1);
            SetInt(prep, 2, 2);
            SetInt(prep, 3, 3);
            SetInt(prep, 4, 4);
            SetInt(prep, 5, 5);
            SetInt(prep, 6, 6);
            using (DbDataReader rs = prep.ExecuteReader())
            {
                foreach (int n in new[] { 1, 2, 3, 4, 5, 6 })
                {
                    AssertTrue(rs.Read());
                    AssertEquals(n, rs.GetInt32(0));
                }

                AssertEquals("X", rs.GetName(0));
                AssertEquals("'T1'", rs.GetName(1));

                AssertFalse(rs.Read());
            }

            try
            {
                prep.CommandText = "SELECT * FROM t1 UNION ALL SELECT * FROM t2 "
                    + "UNION ALL SELECT X, 'Q' FROM SYSTEM_RANGE(5,6)";
                using DbDataReader rs = prep.ExecuteReader();
                Fail("Temp view T1 was accessible after previous WITH statement finished "
                    + "- but should not have been.");
            }
            catch (DbException e)
            {
                // ensure the T1 table has been removed even without auto commit
                AssertContains(e.Message ?? "", "Table \"T1\" not found (this database is empty);");
            }
        }
    }

    private void TestInsert()
    {
        DeleteDb("commonTableExpressionQueries");
        using DbConnection conn = GetConnection("commonTableExpressionQueries");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE T1 ( ID INT GENERATED BY DEFAULT AS IDENTITY,  X INT NULL, Y VARCHAR(100) NULL )");

        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "INSERT INTO T1 (X,Y) "
            + "WITH v1 AS ("
            + "     SELECT R.X, 'X1' AS Y FROM SYSTEM_RANGE(?1,?2) R"
            + ")"
            + "SELECT v1.X, v1.Y FROM v1";
        SetInt(prep, 1, 1);
        SetInt(prep, 2, 2);
        int rowCount = prep.ExecuteNonQuery();

        AssertEquals(2, rowCount);

        stat.CommandText = "SELECT ID, X,Y FROM T1";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            foreach (int n in new[] { 1, 2 })
            {
                AssertTrue(rs.Read());
                AssertTrue(rs.GetInt32(0) != 0);
                AssertEquals(n, rs.GetInt32(1));
                AssertEquals("X1", rs.GetString(2));
            }
        }
    }

    private void TestUpdate()
    {
        DeleteDb("commonTableExpressionQueries");
        using DbConnection conn = GetConnection("commonTableExpressionQueries");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE IF NOT EXISTS T1 AS SELECT R.X AS ID, R.X, 'X1' AS Y FROM SYSTEM_RANGE(1,2) R");

        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "UPDATE T1 SET Y = 'Y1' WHERE X IN ("
            + "WITH v1 AS ("
            + "     SELECT R.X, 'X1' AS Y FROM SYSTEM_RANGE(?1,?2) R"
            + ")"
            + "SELECT v1.X FROM v1)";
        SetInt(prep, 1, 1);
        SetInt(prep, 2, 2);
        int rowCount = prep.ExecuteNonQuery();

        AssertEquals(2, rowCount);

        stat.CommandText = "SELECT ID, X,Y FROM T1";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            foreach (int n in new[] { 1, 2 })
            {
                AssertTrue(rs.Read());
                AssertTrue(rs.GetInt32(0) != 0);
                AssertEquals(n, rs.GetInt32(1));
                AssertEquals("Y1", rs.GetString(2));
            }
        }
    }

    private void TestDelete()
    {
        DeleteDb("commonTableExpressionQueries");
        using DbConnection conn = GetConnection("commonTableExpressionQueries");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE IF NOT EXISTS T1 AS SELECT R.X AS ID, R.X, 'X1' AS Y FROM SYSTEM_RANGE(1,2) R");

        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "DELETE FROM T1 WHERE X IN ("
            + "WITH v1 AS ("
            + "     SELECT R.X, 'X1' AS Y FROM SYSTEM_RANGE(1,2) R"
            + ")"
            + "SELECT v1.X FROM v1 )";
        int rowCount = prep.ExecuteNonQuery();

        AssertEquals(2, rowCount);

        stat.CommandText = "SELECT ID, X,Y FROM T1";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertFalse(rs.Read());
        }
    }

    private void TestMerge()
    {
        DeleteDb("commonTableExpressionQueries");
        using DbConnection conn = GetConnection("commonTableExpressionQueries");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE IF NOT EXISTS T1 AS SELECT R.X AS ID, R.X, 'X1' AS Y FROM SYSTEM_RANGE(1,2) R");

        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "MERGE INTO T1 KEY(ID) "
            + "WITH v1 AS ("
            + "     SELECT R.X, 'X1' AS Y FROM SYSTEM_RANGE(1,3) R"
            + ")"
            + "SELECT v1.X AS ID, v1.X, v1.Y FROM v1";
        int rowCount = prep.ExecuteNonQuery();

        AssertEquals(3, rowCount);

        stat.CommandText = "SELECT ID, X,Y FROM T1";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            foreach (int n in new[] { 1, 2, 3 })
            {
                AssertTrue(rs.Read());
                AssertTrue(rs.GetInt32(0) != 0);
                AssertEquals(n, rs.GetInt32(1));
                AssertEquals("X1", rs.GetString(2));
            }
        }
    }

    private void TestCreateTable()
    {
        DeleteDb("commonTableExpressionQueries");
        using DbConnection conn = GetConnection("commonTableExpressionQueries");
        DbCommand stat = conn.CreateCommand();
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "CREATE TABLE IF NOT EXISTS T1 AS "
                + "WITH v1 AS ("
                + "     SELECT R.X, 'X1' AS Y FROM SYSTEM_RANGE(1,3) R"
                + ")"
                + "SELECT v1.X AS ID, v1.X, v1.Y FROM v1";
            // PreparedStatement.execute() answers whether a result set came back; a
            // reader over an update answers with its update count instead (-1 only for a
            // result set).
            bool success;
            using (DbDataReader reader = prep.ExecuteReader())
            {
                success = reader.RecordsAffected < 0;
            }

            AssertEquals(false, success);
        }

        stat.CommandText = "SELECT ID, X,Y FROM T1";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            foreach (int n in new[] { 1, 2, 3 })
            {
                AssertTrue(rs.Read());
                AssertTrue(rs.GetInt32(0) != 0);
                AssertEquals(n, rs.GetInt32(1));
                AssertEquals("X1", rs.GetString(2));
            }
        }
    }

    private void TestNestedSQL()
    {
        DeleteDb("commonTableExpressionQueries");
        using DbConnection conn = GetConnection("commonTableExpressionQueries");
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "WITH T1 AS (                        "
            + "        SELECT *                    "
            + "        FROM TABLE (                "
            + "            K VARCHAR = ('a', 'b'), "
            + "            V INTEGER = (1, 2)      "
            + "    )                               "
            + "),                                  "
            + "                                    "
            + "                                    "
            + "T2 AS (                             "
            + "        SELECT *                    "
            + "        FROM TABLE (                "
            + "            K VARCHAR = ('a', 'b'), "
            + "            V INTEGER = (3, 4)      "
            + "    )                               "
            + "),                                  "
            + "                                    "
            + "                                    "
            + "JOIN_CTE AS (                       "
            + "    SELECT T1.*                     "
            + "                                    "
            + "    FROM                            "
            + "        T1                          "
            + "        JOIN T2 ON (                "
            + "            T1.K = T2.K             "
            + "        )                           "
            + ")                                   "
            + "                                    "
            + "SELECT * FROM JOIN_CTE";

        using (DbDataReader rs = prep.ExecuteReader())
        {
            foreach (string keyLetter in new[] { "a", "b" })
            {
                AssertTrue(rs.Read());
                AssertContains("ab", rs.GetString(0));
                AssertEquals(rs.GetString(0), keyLetter);
                AssertTrue(rs.GetInt32(1) != 0);
            }
        }
    }

    private void TestColumnNames()
    {
        DeleteDb("commonTableExpressionQueries");
        using DbConnection conn = GetConnection("commonTableExpressionQueries");
        using DbTransaction transaction = conn.BeginTransaction();
        using DbCommand prep = conn.CreateCommand();
        prep.Transaction = transaction;
        prep.CommandText = "WITH t1 AS ("
            + "     SELECT 1 AS ONE, R.X AS TWO, 'T1' AS THREE, X FROM SYSTEM_RANGE(1,1) R"
            + ")"
            + "SELECT * FROM t1";
        using (DbDataReader rs = prep.ExecuteReader())
        {
            foreach (int n in new[] { 1 })
            {
                AssertTrue(rs.Read());
                AssertEquals(n, rs.GetInt32(0));
                AssertEquals(n, rs.GetInt32(3));
            }

            AssertEquals("ONE", rs.GetName(0));
            AssertEquals("TWO", rs.GetName(1));
            AssertEquals("THREE", rs.GetName(2));
            AssertEquals("X", rs.GetName(3));

            AssertFalse(rs.Read());
        }
    }

    private void TestSimple4RowRecursiveQuery()
    {
        string?[] expectedRowData = ["|1", "|2", "|3"];
        string[] expectedColumnTypes = ["INTEGER"];
        string[] expectedColumnNames = ["N"];

        string setupSQL = "-- do nothing";
        string withQuery = "with recursive r(n) as (\n"
            + "(select 1) union all (select n+1 from r where n < 3)\n"
            + ")\n"
            + "select n from r";

        int maxRetries = 3;
        int expectedNumberOfRows = expectedRowData.Length;

        TestRepeatedQueryWithSetup(maxRetries, expectedRowData, expectedColumnNames, expectedNumberOfRows,
            setupSQL, withQuery, maxRetries - 1, expectedColumnTypes, false);
    }

    private void TestSimple2By4RowRecursiveQuery()
    {
        string?[] expectedRowData = ["|0|1|10", "|1|2|11", "|2|3|12", "|3|4|13"];
        string[] expectedColumnTypes = ["INTEGER", "INTEGER", "INTEGER"];
        string[] expectedColumnNames = ["K", "N", "N2"];

        string setupSQL = "-- do nothing";
        string withQuery = "with recursive\n"
            + "r1(n,k) as ((select 1, 0) union all (select n+1,k+1 from r1 where n <= 3)),"
            + "r2(n,k) as ((select 10,0) union all (select n+1,k+1 from r2 where n <= 13))"
            + "select r1.k, r1.n, r2.n AS n2 from r1 inner join r2 ON r1.k= r2.k          ";

        int maxRetries = 3;
        int expectedNumberOfRows = expectedRowData.Length;

        TestRepeatedQueryWithSetup(maxRetries, expectedRowData, expectedColumnNames, expectedNumberOfRows,
            setupSQL, withQuery, maxRetries - 1, expectedColumnTypes, false);
    }

    private void TestSimple3RowRecursiveQueryWithLazyEval()
    {
        if (Config.Lazy && Config.Networked)
        {
            return;
        }

        string?[] expectedRowData = ["|6"];
        string[] expectedColumnTypes = ["BIGINT"];
        string[] expectedColumnNames = ["SUM(N)"];

        // back up the config - to restore it after this test
        TestAll backupConfig = Config;
        Config = new TestAll();

        try
        {
            // Test with settings: lazy mvStore memory multiThreaded
            // connection url is
            // mem:script;MV_STORE=true;LOG=1;LOCK_TIMEOUT=50;
            // LAZY_QUERY_EXECUTION=1
            Config.Lazy = true;
            Config.Memory = true;

            string setupSQL = "--no config set";
            string withQuery = "select sum(n) from (\n"
                + "    with recursive r(n) as (\n"
                + "        (select 1) union all (select n+1 from r where n < 3) \n"
                + "    )\n"
                + "    select n from r \n"
                + ")\n";

            int maxRetries = 10;
            int expectedNumberOfRows = expectedRowData.Length;

            TestRepeatedQueryWithSetup(maxRetries, expectedRowData, expectedColumnNames, expectedNumberOfRows,
                setupSQL, withQuery, maxRetries - 1, expectedColumnTypes, false);
        }
        finally
        {
            Config = backupConfig;
        }
    }

    private void TestSimple3RowRecursiveQueryDropAllObjects()
    {
        string?[] expectedRowData = ["|6"];
        string[] expectedColumnTypes = ["BIGINT"];
        string[] expectedColumnNames = ["SUM(N)"];

        string setupSQL = "DROP ALL OBJECTS;";
        string withQuery = "select sum(n) from ("
            + "    with recursive r(n) as ("
            + "        (select 1) union all (select n+1 from r where n < 3)"
            + "    ),"
            + "   dummyUnusedCte(n) as ("
            + "   select 1 "
            + "   )"
            + "    select n from r"
            + ")";

        int maxRetries = 10;
        int expectedNumberOfRows = expectedRowData.Length;

        TestRepeatedQueryWithSetup(maxRetries, expectedRowData, expectedColumnNames, expectedNumberOfRows,
            setupSQL, withQuery, maxRetries - 1, expectedColumnTypes, false);
    }
}
