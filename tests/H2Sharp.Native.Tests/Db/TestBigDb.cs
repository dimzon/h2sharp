// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestBigDb.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands. The reference's main() entry is
// the runner; xUnit runs Test() directly.
using System.Diagnostics;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test for big databases.
/// </summary>
public sealed class TestBigDb : TestDb
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

        if (Config.Networked && Config.Big)
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
        TestLargeTable();
        TestInsert();
        TestLeftSummary();
        DeleteDb("bigDb");
    }

    private void TestLargeTable()
    {
        DeleteDb("bigDb");
        using DbConnection conn = GetConnection("bigDb");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE CACHED TABLE TEST("
            + "M_CODE CHAR(1) DEFAULT CAST(RAND()*9 AS INT),"
            + "PRD_CODE CHAR(20) DEFAULT SECURE_RAND(10),"
            + "ORG_CODE_SUPPLIER CHAR(13) DEFAULT SECURE_RAND(6),"
            + "PRD_CODE_1 CHAR(14) DEFAULT SECURE_RAND(7),"
            + "PRD_CODE_2 CHAR(20)  DEFAULT SECURE_RAND(10),"
            + "ORG_CODE CHAR(13)  DEFAULT SECURE_RAND(6),"
            + "SUBSTITUTED_BY CHAR(20) DEFAULT SECURE_RAND(10),"
            + "SUBSTITUTED_BY_2 CHAR(14) DEFAULT SECURE_RAND(7),"
            + "SUBSTITUTION_FOR CHAR(20) DEFAULT SECURE_RAND(10),"
            + "SUBSTITUTION_FOR_2 CHAR(14) DEFAULT SECURE_RAND(7),"
            + "TEST CHAR(2) DEFAULT SECURE_RAND(1),"
            + "TEST_2 CHAR(2) DEFAULT SECURE_RAND(1),"
            + "TEST_3 DECIMAL(7,2) DEFAULT RAND(),"
            + "PRIMARY_UNIT_CODE CHAR(3) DEFAULT SECURE_RAND(1),"
            + "RATE_PRICE_ORDER_UNIT DECIMAL(9,3) DEFAULT RAND(),"
            + "ORDER_UNIT_CODE CHAR(3) DEFAULT SECURE_RAND(1),"
            + "ORDER_QTY_MIN DECIMAL(6,1) DEFAULT RAND(),"
            + "ORDER_QTY_LOT_SIZE DECIMAL(6,1) DEFAULT RAND(),"
            + "ORDER_UNIT_CODE_2 CHAR(3) DEFAULT SECURE_RAND(1),"
            + "PRICE_GROUP CHAR(20) DEFAULT SECURE_RAND(10),"
            + "LEAD_TIME INTEGER DEFAULT RAND(),"
            + "LEAD_TIME_UNIT_CODE CHAR(3) DEFAULT SECURE_RAND(1),"
            + "PRD_GROUP CHAR(10) DEFAULT SECURE_RAND(5),"
            + "WEIGHT_GROSS DECIMAL(7,3) DEFAULT RAND(),"
            + "WEIGHT_UNIT_CODE CHAR(3) DEFAULT SECURE_RAND(1),"
            + "PACK_UNIT_CODE CHAR(3) DEFAULT SECURE_RAND(1),"
            + "PACK_LENGTH DECIMAL(7,3) DEFAULT RAND(),"
            + "PACK_WIDTH DECIMAL(7,3) DEFAULT RAND(),"
            + "PACK_HEIGHT DECIMAL(7,3) DEFAULT RAND(),"
            + "SIZE_UNIT_CODE CHAR(3) DEFAULT SECURE_RAND(1),"
            + "STATUS_CODE CHAR(3) DEFAULT SECURE_RAND(1),"
            + "INTRA_STAT_CODE CHAR(12) DEFAULT SECURE_RAND(6),"
            + "PRD_TITLE CHAR(50) DEFAULT SECURE_RAND(25),"
            + "VALID_FROM DATE DEFAULT CURRENT_DATE,"
            + "MOD_DATUM DATE DEFAULT CURRENT_DATE)");
        int len = GetSize(10, 50000);
        try
        {
            using DbCommand prep = conn.CreateCommand();
            prep.CommandText = "INSERT INTO TEST(PRD_CODE) VALUES('abc' || ?)";
            prep.Parameters.Add(new H2Parameter());
            long time = Stopwatch.GetTimestamp();
            for (int i = 0; i < len; i++)
            {
                if ((i % 1000) == 0)
                {
                    long t = Stopwatch.GetTimestamp();
                    if (t - time > Stopwatch.Frequency)
                    {
                        time = t;
                        long free = Util.Utils.GetMemoryUsed();
                        Println("i: " + i + " used: " + free);
                    }
                }

                prep.Parameters[0].Value = i;
                _ = prep.ExecuteNonQuery();
            }

            Execute(stat, "CREATE INDEX IDX_TEST_PRD_CODE ON TEST(PRD_CODE)");
            stat.CommandText = "SELECT * FROM TEST";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                int columns = rs.FieldCount;
                while (rs.Read())
                {
                    for (int i = 0; i < columns; i++)
                    {
                        _ = rs.IsDBNull(i) ? null : rs.GetString(i);
                    }
                }
            }
        }
        catch (OutOfMemoryException e)
        {
            LogError("memory", e);
            conn.Close();
            throw;
        }
    }

    private void TestLeftSummary()
    {
        DeleteDb("bigDb");
        using DbConnection conn = GetConnection("bigDb");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT, NEG INT AS -ID, "
            + "NAME VARCHAR, PRIMARY KEY(ID, NAME))");
        Execute(stat, "CREATE INDEX IDX_NEG ON TEST(NEG, NAME)");
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "INSERT INTO TEST(ID, NAME) VALUES(?, '1234567890')";
        prep.Parameters.Add(new H2Parameter());
        int len = GetSize(10, 1000);
        int block = GetSize(3, 10);
        int left, x = 0;
        for (int i = 0; i < len; i++)
        {
            left = x + (block / 2);
            for (int j = 0; j < block; j++)
            {
                prep.Parameters[0].Value = x++;
                _ = prep.ExecuteNonQuery();
            }

            Execute(stat, "DELETE FROM TEST WHERE ID>" + left);
            stat.CommandText = "SELECT COUNT(*) FROM TEST";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                int count = rs.GetInt32(0);
                Trace("count: " + count);
            }
        }
    }

    private void TestInsert()
    {
        DeleteDb("bigDb");
        using DbConnection conn = GetConnection("bigDb");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID IDENTITY, NAME VARCHAR)");
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "INSERT INTO TEST(NAME) VALUES('Hello World')";
        int len = GetSize(1000, 10000);
        for (int i = 0; i < len; i++)
        {
            if (i % 1000 == 0)
            {
                Println("rows: " + i);
                Thread.Yield();
            }

            _ = prep.ExecuteNonQuery();
        }
    }
}
