// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestSpaceReuse.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; FileUtils.size is
// FilePath.Size. The reference's main() entry is the runner; xUnit runs Test() directly.
using H2Sharp.Engine;
using H2Sharp.Store.FileSystem;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests if disk space is reused after deleting many rows.
/// </summary>
public sealed class TestSpaceReuse : TestDb
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
        DeleteDb("spaceReuse");
        long max = 0, now = 0, min = long.MaxValue;
        for (int i = 0; i < 20; i++)
        {
            DbConnection conn = GetConnection("spaceReuse");
            DbCommand stat = conn.CreateCommand();
            Execute(stat, "set retention_time 0");
            Execute(stat, "set write_delay 0"); // disable auto-commit so that free-unused runs on commit
            Execute(stat, "create table if not exists t(i int)");
            Execute(stat, "insert into t select x from system_range(1, 500)");
            conn.Close();
            conn = GetConnection("spaceReuse");
            DbCommand delete = conn.CreateCommand();
            Execute(delete, "delete from t");
            conn.Close();
            string fileName = GetBaseDir() + "/spaceReuse" + Constants.SuffixMvFile;
            now = FilePath.Get(fileName).Size();
            AssertTrue(now > 0);
            if (i < 10)
            {
                max = Math.Max(max, now);
            }
            else
            {
                min = Math.Min(min, now);
            }
        }

        AssertTrue("min: " + min + " max: " + max, min <= max);
        DeleteDb("spaceReuse");
    }
}
