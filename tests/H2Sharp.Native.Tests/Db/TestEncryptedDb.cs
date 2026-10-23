// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestEncryptedDb.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands, and the proxy form of
// assertThrows becomes the lambda form, because a sealed class cannot be proxied. The
// reference's main() entry is the runner; xUnit runs Test() directly.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test using an encrypted database.
/// </summary>
public sealed class TestEncryptedDb : TestDb
{
    /// <summary>
    /// Whether this test is enabled in the current configuration.
    /// </summary>
    /// <returns><c>true</c> when the test runs</returns>
    public override bool IsEnabled()
    {
        if (Config.Memory || Config.Cipher is not null)
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
        DeleteDb("encrypted");
        AssertThrows(ErrorCode.FeatureNotSupported1,
            () => _ = GetConnection("encrypted;CIPHER=AES;PAGE_SIZE=2048", "sa", "1234 1234"));
        using (DbConnection conn = GetConnection("encrypted;CIPHER=AES", "sa", "123 123"))
        {
            DbCommand stat = conn.CreateCommand();
            Execute(stat, "CREATE TABLE TEST(ID INT)");
            Execute(stat, "CHECKPOINT");
            Execute(stat, "SET WRITE_DELAY 0");
            Execute(stat, "INSERT INTO TEST VALUES(1)");
            Execute(stat, "SHUTDOWN IMMEDIATELY");
        }

        AssertThrows(ErrorCode.FileEncryptionError1,
            () => _ = GetConnection("encrypted;CIPHER=AES", "sa", "1234 1234"));

        using (DbConnection conn = GetConnection("encrypted;CIPHER=AES", "sa", "123 123"))
        {
            DbCommand stat = conn.CreateCommand();
            stat.CommandText = "SELECT * FROM TEST";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(1, rs.GetInt32(0));
                AssertFalse(rs.Read());
            }
        }

        DeleteDb("encrypted");
    }
}
