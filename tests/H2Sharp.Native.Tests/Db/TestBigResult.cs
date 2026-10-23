// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestBigResult.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; setAutoCommit(false) is an
// explicit transaction. ResultSet.wasNull is IsDBNull on the same column, and the Blob
// and Clob handles the reference holds across a closed result set are the values the
// reader hands out — the port's reader materializes, so the captured byte[]/string is
// the same object the handle would have wrapped. The FileLister walk of
// testCloseConnectionDelete is the directory walk of TestDb.DeleteDb.
//
// Modified (adaptations, named): Statement.setMaxRows has no ADO.NET counterpart; the
// calls are guards the assertions never reach (they bound at len+1 rows where the test
// reads len), so they are dropped rather than faked. The scrollable fragment of
// testOrderGroup (rs.last() on a TYPE_SCROLL_INSENSITIVE statement) has no ADO.NET
// shape either and waits on the discussion list. The reference's main() entry is the
// runner; xUnit runs Test() directly.
using System.Collections;
using H2Sharp.Engine;
using H2Sharp.Store.FileSystem;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test for big result sets.
/// </summary>
public sealed class TestBigResult : TestDb
{
    /// <summary>
    /// Whether this test is enabled in the current configuration.
    /// </summary>
    /// <returns><c>true</c> when the test runs</returns>
    public override bool IsEnabled()
    {
        if (Config.Memory)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestLargeSubquery();
        TestSortingAndDistinct();
        TestLOB();
        TestLargeUpdateDelete();
        TestCloseConnectionDelete();
        TestOrderGroup();
        TestLimitBufferedResult();
        DeleteDb("bigResult");
    }

    private void TestLargeSubquery()
    {
        DeleteDb("bigResult");
        using DbConnection conn = GetConnection("bigResult");
        DbCommand stat = conn.CreateCommand();
        int len = GetSize(1000, 4000);
        Execute(stat, "SET MAX_MEMORY_ROWS " + (len / 10));
        Execute(stat, "CREATE TABLE RECOVERY(TRANSACTION_ID INT, SQL_STMT VARCHAR)");
        Execute(stat, "INSERT INTO RECOVERY " + "SELECT X, CASE MOD(X, 2) WHEN 0 THEN 'commit' ELSE 'begin' END "
            + "FROM SYSTEM_RANGE(1, " + len + ")");
        stat.CommandText = "SELECT * FROM RECOVERY " + "WHERE SQL_STMT LIKE 'begin%' AND "
            + "TRANSACTION_ID NOT IN(SELECT TRANSACTION_ID FROM RECOVERY "
            + "WHERE SQL_STMT='commit' OR SQL_STMT='rollback')";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            int count = 0, last = 1;
            while (rs.Read())
            {
                AssertEquals(last, rs.GetInt32(0));
                last += 2;
                count++;
            }

            AssertEquals(len / 2, count);
        }
    }

    private void TestSortingAndDistinct()
    {
        DeleteDb("bigResult");
        using DbConnection conn = GetConnection("bigResult");
        DbCommand stat = conn.CreateCommand();
        int count = GetSize(1000, 4000);
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, V INT NOT NULL)");
        using (DbCommand ps = conn.CreateCommand())
        {
            ps.CommandText = "INSERT INTO TEST VALUES (?, ?)";
            ps.Parameters.Add(new H2Parameter());
            ps.Parameters.Add(new H2Parameter());
            for (int i = 0; i < count; i++)
            {
                ps.Parameters[0].Value = i;
                ps.Parameters[1].Value = count - i;
                _ = ps.ExecuteNonQuery();
            }
        }

        // local result
        TestSortingAndDistinct1(stat, count, count);
        // external result
        TestSortingAndDistinct1(stat, 10, count);
        Execute(stat, "DROP TABLE TEST");
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, VALUE1 INT NOT NULL, VALUE2 INT NOT NULL)");
        using (DbCommand ps = conn.CreateCommand())
        {
            ps.CommandText = "INSERT INTO TEST VALUES (?, ?, ?)";
            ps.Parameters.Add(new H2Parameter());
            ps.Parameters.Add(new H2Parameter());
            ps.Parameters.Add(new H2Parameter());
            int partCount = count / 10;
            for (int i = 0; i < count; i++)
            {
                ps.Parameters[0].Value = i;
                int a = i / 10;
                int b = i % 10;
                ps.Parameters[1].Value = partCount - a;
                ps.Parameters[2].Value = 10 - b;
                _ = ps.ExecuteNonQuery();
            }
        }

        string sql;
        /*
         * Sorting only
         */
        sql = "SELECT VALUE2, VALUE1 FROM (SELECT ID, VALUE2, VALUE1 FROM TEST ORDER BY VALUE2)";
        // local result
        TestSortingAndDistinct2(stat, sql, count, count / 10);
        // external result
        TestSortingAndDistinct2(stat, sql, 10, count / 10);
        /*
         * Distinct only
         */
        sql = "SELECT VALUE2, VALUE1 FROM (SELECT DISTINCT ID, VALUE2, VALUE1 FROM TEST)";
        // local result
        TestSortingAndDistinct2DistinctOnly(stat, sql, count, count / 10);
        // external result
        TestSortingAndDistinct2DistinctOnly(stat, sql, 10, count / 10);
        /*
         * Sorting and distinct
         */
        sql = "SELECT VALUE2, VALUE1 FROM (SELECT DISTINCT ID, VALUE2, VALUE1 FROM TEST ORDER BY VALUE2)";
        // local result
        TestSortingAndDistinct2(stat, sql, count, count / 10);
        // external result
        TestSortingAndDistinct2(stat, sql, 10, count / 10);
        /*
         * One more distinct only
         */
        sql = "SELECT VALUE1 FROM (SELECT DISTINCT VALUE1 FROM TEST)";
        // local result
        TestSortingAndDistinct3DistinctOnly(stat, sql, count, count / 10);
        // external result
        TestSortingAndDistinct3DistinctOnly(stat, sql, 1, count / 10);
        /*
         * One more sorting and distinct
         */
        sql = "SELECT VALUE1 FROM (SELECT DISTINCT VALUE1 FROM TEST ORDER BY VALUE1)";
        // local result
        TestSortingAndDistinct3(stat, sql, count, count / 10);
        // external result
        TestSortingAndDistinct3(stat, sql, 1, count / 10);
        Execute(stat, "DROP TABLE TEST");
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, V INT)");
        using (DbCommand ps = conn.CreateCommand())
        {
            ps.CommandText = "INSERT INTO TEST VALUES (?, ?)";
            ps.Parameters.Add(new H2Parameter());
            ps.Parameters.Add(new H2Parameter());
            for (int i = 0; i < count; i++)
            {
                ps.Parameters[0].Value = i;
                int j = i / 10;
                ps.Parameters[1].Value = j == 0 ? DBNull.Value : j;
                _ = ps.ExecuteNonQuery();
            }
        }

        /*
         * Sorting and distinct
         */
        sql = "SELECT DISTINCT V FROM TEST ORDER BY V";
        // local result
        TestSortingAndDistinct4(stat, sql, count, count / 10);
        // external result
        TestSortingAndDistinct4(stat, sql, 1, count / 10);
        /*
         * Distinct only
         */
        sql = "SELECT DISTINCT V FROM TEST";
        // local result
        TestSortingAndDistinct4DistinctOnly(stat, sql, count, count / 10);
        // external result
        TestSortingAndDistinct4DistinctOnly(stat, sql, 1, count / 10);
        /*
         * Sorting only
         */
        sql = "SELECT V FROM TEST ORDER BY V";
        // local result
        TestSortingAndDistinct4SortingOnly(stat, sql, count, count / 10);
        // external result
        TestSortingAndDistinct4SortingOnly(stat, sql, 1, count / 10);
    }

    private void TestSortingAndDistinct1(DbCommand stat, int maxRows, int count)
    {
        Execute(stat, "SET MAX_MEMORY_ROWS " + maxRows);
        stat.CommandText = "SELECT V FROM (SELECT DISTINCT ID, V FROM TEST ORDER BY V)";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            for (int i = 1; i <= count; i++)
            {
                AssertTrue(rs.Read());
                AssertEquals(rs.GetInt32(0), i);
            }

            AssertFalse(rs.Read());
        }
    }

    private void TestSortingAndDistinct2(DbCommand stat, string sql, int maxRows, int partCount)
    {
        Execute(stat, "SET MAX_MEMORY_ROWS " + maxRows);
        stat.CommandText = sql;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            BitArray set = new(partCount + 1);
            for (int i = 1; i <= 10; i++)
            {
                set.SetAll(false);
                for (int j = 1; j <= partCount; j++)
                {
                    AssertTrue(rs.Read());
                    AssertEquals(i, rs.GetInt32(0));
                    set[rs.GetInt32(1)] = true;
                }

                AssertEquals(partCount + 1, NextClearBit(set, 1));
            }

            AssertFalse(rs.Read());
        }
    }

    private void TestSortingAndDistinct2DistinctOnly(DbCommand stat, string sql, int maxRows,
        int partCount)
    {
        Execute(stat, "SET MAX_MEMORY_ROWS " + maxRows);
        stat.CommandText = sql;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            BitArray set = new((partCount * 10) + 1);
            for (int i = 1; i <= 10; i++)
            {
                for (int j = 1; j <= partCount; j++)
                {
                    AssertTrue(rs.Read());
                    set[(rs.GetInt32(0) * partCount) + rs.GetInt32(1)] = true;
                }
            }

            AssertEquals((partCount * 11) + 1, NextClearBit(set, partCount + 1));
            AssertFalse(rs.Read());
        }
    }

    private void TestSortingAndDistinct3(DbCommand stat, string sql, int maxRows, int partCount)
    {
        Execute(stat, "SET MAX_MEMORY_ROWS " + maxRows);
        stat.CommandText = sql;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            for (int i = 1; i <= partCount; i++)
            {
                AssertTrue(rs.Read());
                AssertEquals(i, rs.GetInt32(0));
            }

            AssertFalse(rs.Read());
        }
    }

    private void TestSortingAndDistinct3DistinctOnly(DbCommand stat, string sql, int maxRows,
        int partCount)
    {
        Execute(stat, "SET MAX_MEMORY_ROWS " + maxRows);
        stat.CommandText = sql;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            BitArray set = new(partCount + 1);
            for (int i = 1; i <= partCount; i++)
            {
                AssertTrue(rs.Read());
                set[rs.GetInt32(0)] = true;
            }

            AssertEquals(partCount + 1, NextClearBit(set, 1));
            AssertFalse(rs.Read());
        }
    }

    private void TestSortingAndDistinct4(DbCommand stat, string sql, int maxRows, int count)
    {
        Execute(stat, "SET MAX_MEMORY_ROWS " + maxRows);
        stat.CommandText = sql;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            for (int i = 0; i < count; i++)
            {
                AssertTrue(rs.Read());
                AssertEquals(i, rs.GetInt32(0));
                if (i == 0)
                {
                    AssertTrue(rs.IsDBNull(0));
                }
            }

            AssertFalse(rs.Read());
        }
    }

    private void TestSortingAndDistinct4DistinctOnly(DbCommand stat, string sql, int maxRows,
        int count)
    {
        Execute(stat, "SET MAX_MEMORY_ROWS " + maxRows);
        stat.CommandText = sql;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            BitArray set = new(count + 1);
            for (int i = 0; i < count; i++)
            {
                AssertTrue(rs.Read());
                int v = rs.GetInt32(0);
                if (v == 0)
                {
                    AssertTrue(rs.IsDBNull(0));
                }

                AssertFalse(set[v]);
                set[v] = true;
            }

            AssertFalse(rs.Read());
            AssertEquals(count, NextClearBit(set, 0));
        }
    }

    private void TestSortingAndDistinct4SortingOnly(DbCommand stat, string sql, int maxRows,
        int count)
    {
        Execute(stat, "SET MAX_MEMORY_ROWS " + maxRows);
        stat.CommandText = sql;
        using (DbDataReader rs = stat.ExecuteReader())
        {
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < 10; j++)
                {
                    AssertTrue(rs.Read());
                    AssertEquals(i, rs.GetInt32(0));
                    if (i == 0)
                    {
                        AssertTrue(rs.IsDBNull(0));
                    }
                }
            }

            AssertFalse(rs.Read());
        }
    }

    /// <summary>
    /// The reference's BitSet.nextClearBit.
    /// </summary>
    /// <param name="set">the set</param>
    /// <param name="from">the index to start at</param>
    /// <returns>the first clear bit at or after <paramref name="from"/></returns>
    private static int NextClearBit(BitArray set, int from)
    {
        for (int i = from; i < set.Length; i++)
        {
            if (!set[i])
            {
                return i;
            }
        }

        return set.Length;
    }

    private void TestLOB()
    {
        if (Config.TraceLevelFile == 4)
        {
            // Trace system on this level can throw OOME with such large
            // arguments as used in this test.
            return;
        }

        DeleteDb("bigResult");
        using DbConnection conn = GetConnection("bigResult");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "SET MAX_MEMORY_ROWS " + 1);
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, V BLOB NOT NULL)");
        using (DbCommand ps = conn.CreateCommand())
        {
            ps.CommandText = "INSERT INTO TEST VALUES (?, ?)";
            ps.Parameters.Add(new H2Parameter());
            ps.Parameters.Add(new H2Parameter());
            int length = 1_000_000;
            byte[] data = new byte[length];
            for (int i = 1; i <= 10; i++)
            {
                ps.Parameters[0].Value = i;
                Array.Fill(data, (byte)i);
                ps.Parameters[1].Value = data;
                _ = ps.ExecuteNonQuery();
            }

            byte[]?[] blobs = new byte[10][];
            stat.CommandText = "SELECT * FROM TEST";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                for (int i = 1; i <= 10; i++)
                {
                    AssertTrue(rs.Read());
                    AssertEquals(i, rs.GetInt32(0));
                    blobs[i - 1] = (byte[])rs.GetValue(1);
                }

                AssertFalse(rs.Read());
            }

            for (int i = 1; i <= 10; i++)
            {
                byte[] bytes = blobs[i - 1]!;
                Array.Fill(data, (byte)i);
                AssertEquals(data, bytes);
            }
        }

        Execute(stat, "DROP TABLE TEST");
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, V CLOB NOT NULL)");
        using (DbCommand ps = conn.CreateCommand())
        {
            ps.CommandText = "INSERT INTO TEST VALUES (?, ?)";
            ps.Parameters.Add(new H2Parameter());
            ps.Parameters.Add(new H2Parameter());
            int length = 1_000_000;
            char[] cdata = new char[length];
            for (int i = 1; i <= 10; i++)
            {
                ps.Parameters[0].Value = i;
                Array.Fill(cdata, (char)i);
                ps.Parameters[1].Value = new string(cdata);
                _ = ps.ExecuteNonQuery();
            }

            string?[] clobs = new string[10];
            stat.CommandText = "SELECT * FROM TEST";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                for (int i = 1; i <= 10; i++)
                {
                    AssertTrue(rs.Read());
                    AssertEquals(i, rs.GetInt32(0));
                    clobs[i - 1] = rs.GetString(1);
                }

                AssertFalse(rs.Read());
            }

            for (int i = 1; i <= 10; i++)
            {
                string s = clobs[i - 1]!;
                Array.Fill(cdata, (char)i);
                AssertEquals(new string(cdata), s);
            }
        }
    }

    private void TestLargeUpdateDelete()
    {
        DeleteDb("bigResult");
        using DbConnection conn = GetConnection("bigResult");
        DbCommand stat = conn.CreateCommand();
        int len = GetSize(10000, 100000);
        Execute(stat, "SET MAX_OPERATION_MEMORY 4096");
        Execute(stat, "CREATE TABLE TEST AS SELECT * FROM SYSTEM_RANGE(1, " + len + ")");
        Execute(stat, "UPDATE TEST SET X=X+1");
        Execute(stat, "DELETE FROM TEST");
    }

    private void TestCloseConnectionDelete()
    {
        DeleteDb("bigResult");
        DbConnection conn = GetConnection("bigResult");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "SET MAX_MEMORY_ROWS 2");
        stat.CommandText = "SELECT * FROM SYSTEM_RANGE(1, 100)";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            while (rs.Read())
            {
                // ignore
            }
        }

        // rs.close();
        conn.Close();
        DeleteDb("bigResult");
        foreach (FilePath path in FilePath.Get(GetBaseDir()).NewDirectoryStream())
        {
            string f = path.ToString();
            if (f.EndsWith(Constants.SuffixMvFile, StringComparison.Ordinal)
                && path.GetName().StartsWith("bigResult.", StringComparison.Ordinal))
            {
                Fail("file not deleted: " + f);
            }
        }
    }

    private void TestLimitBufferedResult()
    {
        DeleteDb("bigResult");
        using DbConnection conn = GetConnection("bigResult");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "DROP TABLE IF EXISTS TEST");
        Execute(stat, "CREATE TABLE TEST(ID INT)");
        for (int i = 0; i < 200; i++)
        {
            Execute(stat, "INSERT INTO TEST(ID) VALUES(" + i + ")");
        }

        Execute(stat, "SET MAX_MEMORY_ROWS 100");
        stat.CommandText = "select id from test order by id limit 10 offset 85";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            for (int i = 85; rs.Read(); i++)
            {
                AssertEquals(i, rs.GetInt32(0));
            }
        }

        stat.CommandText = "select id from test order by id limit 10 offset 95";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            for (int i = 95; rs.Read(); i++)
            {
                AssertEquals(i, rs.GetInt32(0));
            }
        }

        stat.CommandText = "select id from test order by id limit 10 offset 105";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            for (int i = 105; rs.Read(); i++)
            {
                AssertEquals(i, rs.GetInt32(0));
            }
        }
    }

    private void TestOrderGroup()
    {
        DeleteDb("bigResult");
        using DbConnection conn = GetConnection("bigResult");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "DROP TABLE IF EXISTS TEST");
        Execute(stat, "CREATE TABLE TEST(" + "ID INT PRIMARY KEY, " + "Name VARCHAR(255), "
            + "FirstName VARCHAR(255), "
            + "Points INT," + "LicenseID INT)");
        int len = GetSize(10, 5000);
        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "INSERT INTO TEST VALUES(?, ?, ?, ?, ?)";
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters.Add(new H2Parameter());
            for (int i = 0; i < len; i++)
            {
                prep.Parameters[0].Value = i;
                prep.Parameters[1].Value = "Name " + i;
                prep.Parameters[2].Value = "First Name " + i;
                prep.Parameters[3].Value = i * 10;
                prep.Parameters[4].Value = i * i;
                _ = prep.ExecuteNonQuery();
            }
        }

        conn.Close();
        using (DbConnection reopened = GetConnection("bigResult"))
        {
            stat = reopened.CreateCommand();
            // Statement.setMaxRows has no ADO.NET counterpart; the reference's calls are
            // guards at len+1 rows where the test reads len, and are not observable.
            stat.CommandText = "SELECT * FROM TEST ORDER BY ID";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                for (int i = 0; i < len; i++)
                {
                    AssertTrue(rs.Read());
                    AssertEquals(i, rs.GetInt32(0));
                    AssertEquals("Name " + i, rs.GetString(1));
                    AssertEquals("First Name " + i, rs.GetString(2));
                    AssertEquals(i * 10, rs.GetInt32(3));
                    AssertEquals(i * i, rs.GetInt32(4));
                }
            }

            stat.CommandText = "SELECT * FROM TEST WHERE ID >= 1000 ORDER BY ID";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                for (int i = 1000; i < len; i++)
                {
                    AssertTrue(rs.Read());
                    AssertEquals(i, rs.GetInt32(0));
                    AssertEquals("Name " + i, rs.GetString(1));
                    AssertEquals("First Name " + i, rs.GetString(2));
                    AssertEquals(i * 10, rs.GetInt32(3));
                    AssertEquals(i * i, rs.GetInt32(4));
                }
            }

            Execute(stat, "SET MAX_MEMORY_ROWS 2");
            stat.CommandText = "SELECT Name, SUM(ID) FROM TEST GROUP BY NAME";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                while (rs.Read())
                {
                    _ = rs.GetString(0);
                    _ = rs.GetInt32(1);
                }
            }

            using (DbTransaction transaction = reopened.BeginTransaction())
            {
                stat.Transaction = transaction;
                Execute(stat, "SET MAX_MEMORY_ROWS 0");
                Execute(stat, "CREATE TABLE DATA(ID INT, NAME VARCHAR_IGNORECASE(255))");
                using (DbCommand prep = reopened.CreateCommand())
                {
                    prep.Transaction = transaction;
                    prep.CommandText = "INSERT INTO DATA VALUES(?, ?)";
                    prep.Parameters.Add(new H2Parameter());
                    prep.Parameters.Add(new H2Parameter());
                    for (int i = 0; i < len; i++)
                    {
                        prep.Parameters[0].Value = i;
                        prep.Parameters[1].Value = "" + i / 200;
                        _ = prep.ExecuteNonQuery();
                    }
                }

                // The reference scrolls a TYPE_SCROLL_INSENSITIVE reader to its last row;
                // the forward-only reader of ADO.NET has no such move, and the fragment
                // waits on the discussion list. The same statement runs to the end here.
                stat.CommandText = "SELECT NAME FROM DATA";
                using (DbDataReader rs = stat.ExecuteReader())
                {
                    while (rs.Read())
                    {
                        // do nothing
                    }
                }

                transaction.Commit();
            }

            stat.CommandText = "SELECT NAME FROM DATA ORDER BY ID";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                while (rs.Read())
                {
                    // do nothing
                }
            }
        }
    }
}
