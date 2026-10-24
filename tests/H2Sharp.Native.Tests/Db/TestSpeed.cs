// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestSpeed.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands. The reference's main() entry is
// the runner; xUnit runs Test() directly.
using System.Diagnostics;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Various small performance tests.
/// </summary>
public sealed class TestSpeed : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("speed");
        using DbConnection conn = GetConnection("speed");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "DROP TABLE IF EXISTS TEST");
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(255))");
        int len = GetSize(1, 10000);
        for (int i = 0; i < len; i++)
        {
            Execute(stat, "SELECT ID, NAME FROM TEST ORDER BY ID");
        }

        long time = Stopwatch.GetTimestamp();

        Execute(stat, "DROP TABLE IF EXISTS TEST");
        Execute(stat, "CREATE CACHED TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(255))");
        DbCommand prep = conn.CreateCommand();
        prep.CommandText = "INSERT INTO TEST VALUES(?, ?)";
        prep.Parameters.Add(new H2Parameter());
        prep.Parameters.Add(new H2Parameter());

        int max = GetSize(1, 10000);
        for (int i = 0; i < max; i++)
        {
            prep.Parameters[0].Value = i;
            prep.Parameters[1].Value = "abchelloasdfaldsjflajdflajdslfoajlskdfkjasdf"
                + "abcfasdfadsfadfsalksdjflasjflajsdlkfjaksdjflkskd" + i;
            _ = prep.ExecuteNonQuery();
        }

        time = Stopwatch.GetTimestamp() - time;
        Trace((time * 1000 / Stopwatch.Frequency) + " insert");

        time = Stopwatch.GetTimestamp();

        prep = conn.CreateCommand();
        prep.CommandText = "UPDATE TEST "
            + "SET NAME='Another data row which is long' WHERE ID=?";
        prep.Parameters.Add(new H2Parameter());
        for (int i = 0; i < max; i++)
        {
            prep.Parameters[0].Value = i;
            _ = prep.ExecuteNonQuery();
        }

        time = Stopwatch.GetTimestamp() - time;
        Trace((time * 1000 / Stopwatch.Frequency) + " update");

        time = Stopwatch.GetTimestamp();
        conn.Close();
        time = Stopwatch.GetTimestamp() - time;
        Trace((time * 1000 / Stopwatch.Frequency) + " close");
        DeleteDb("speed");
    }
}
