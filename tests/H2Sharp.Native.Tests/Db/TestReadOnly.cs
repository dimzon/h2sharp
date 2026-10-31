// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestReadOnly.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands, and the proxy form of
// assertThrows becomes the lambda form. Connection.isReadOnly is the READ_ONLY row of
// INFORMATION_SCHEMA.SETTINGS through the ADO layer, which is the same value the JDBC
// flag reports. The JDK file calls (createTempFile, setReadOnly) are their System.IO
// counterparts, and FileLister.getDatabaseFiles, which the port does not have, is the
// database's .mv.db file, which is the one the walk would set.
//
// Out of scope by decision: the TestReadOnlyDbCreate sub-case creates a local temporary
// LINKED table (D-030 in docs/APPROVED_DEVIATIONS.md) over a jdbc:mem URL (D-003). The
// read-only refusal itself is ported: a write against a read-only database answers
// DATABASE_IS_READ_ONLY from Table.CheckWritingAllowed, called at the DML entry points.
//
// Modified (port gaps, named): testReadOnlyInZip needs the Backup tool, the TCP server
// and the zip file system, none of which are ported, so it is skipped here until they
// are. The reference's main() entry is the runner; xUnit runs Test() directly.
using H2Sharp.Api;
using H2Sharp.Engine;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test for the read-only database feature.
/// </summary>
public sealed class TestReadOnly : TestDb
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
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        // testReadOnlyInZip: skipped, see the header (Backup tool, TCP server, zip file
        // system are not ported).
        TestReadOnlyTempTableResult();
        TestReadOnlyConnect();
        TestReadOnlyDbCreate();
        TestReadOnlyFiles(true);
        TestReadOnlyFiles(false);
    }

    private void TestReadOnlyTempTableResult()
    {
        DeleteDb("readonlyTemp");
        DbConnection conn = GetConnection("readonlyTemp");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT) AS "
            + "SELECT X FROM SYSTEM_RANGE(1, 20)");
        conn.Close();
        conn = GetConnection(
            "readonlyTemp;ACCESS_MODE_DATA=r;"
            + "MAX_MEMORY_ROWS=10");
        stat = conn.CreateCommand();
        Execute(stat, "SELECT DISTINCT ID FROM TEST");
        conn.Close();
        DeleteDb("readonlyTemp");
    }

    private void TestReadOnlyDbCreate()
    {
        DeleteDb("readonlyDbCreate");
        DbConnection conn = GetConnection("readonlyDbCreate");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table a(id int)");
        Execute(stat, "create index ai on a(id)");
        conn.Close();
        conn = GetConnection("readonlyDbCreate;ACCESS_MODE_DATA=r");
        stat = conn.CreateCommand();
        Execute(stat, "create table if not exists a(id int)");
        Execute(stat, "create index if not exists ai on a(id)");
        AssertThrows(ErrorCode.DatabaseIsReadOnly, () => Execute(stat, "CREATE TABLE TEST(ID INT)"));
        AssertThrows(ErrorCode.TableOrViewNotFound1, () => Execute(stat, "SELECT * FROM TEST"));
        Execute(stat, "create local temporary linked table test("
            + "null, 'jdbc:h2:mem:test3', 'sa', 'sa', 'INFORMATION_SCHEMA.TABLES')");
        stat.CommandText = "select * from test";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }

        conn.Close();
    }

    private void TestReadOnlyFiles(bool setReadOnly)
    {
        string tempFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        using (File.Create(tempFile))
        {
        }

        AssertTrue((new FileInfo(tempFile).Attributes & FileAttributes.ReadOnly) == 0);
        File.SetAttributes(tempFile, FileAttributes.ReadOnly);
        AssertFalse((new FileInfo(tempFile).Attributes & FileAttributes.ReadOnly) == 0);
        File.Delete(tempFile);

        DeleteDb("readonlyFiles");
        DbConnection conn = GetConnection("readonlyFiles");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR)");
        Execute(stat, "INSERT INTO TEST VALUES(1, 'Hello')");
        Execute(stat, "INSERT INTO TEST VALUES(2, 'World')");
        AssertFalse(IsReadOnly(conn));
        conn.Close();

        if (setReadOnly)
        {
            SetReadOnly();
            conn = GetConnection("readonlyFiles");
        }
        else
        {
            conn = GetConnection("readonlyFiles;ACCESS_MODE_DATA=r");
        }

        AssertTrue(IsReadOnly(conn));
        stat = conn.CreateCommand();
        Execute(stat, "SELECT * FROM TEST");
        AssertThrows(ErrorCode.DatabaseIsReadOnly, () => Execute(stat, "DELETE FROM TEST"));
        conn.Close();

        if (setReadOnly)
        {
            conn = GetConnection(
                "readonlyFiles;DB_CLOSE_DELAY=1");
        }
        else
        {
            conn = GetConnection(
                "readonlyFiles;DB_CLOSE_DELAY=1;ACCESS_MODE_DATA=r");
        }

        stat = conn.CreateCommand();
        Execute(stat, "SELECT * FROM TEST");
        AssertThrows(ErrorCode.DatabaseIsReadOnly, () => Execute(stat, "DELETE FROM TEST"));
        Execute(stat, "SET DB_CLOSE_DELAY=0");
        conn.Close();
    }

    /// <summary>
    /// The reference's FileLister.getDatabaseFiles + FileUtils.setReadOnly: the files of
    /// the database go read-only; the walk is not ported, so the store file, which is
    /// what the walk would touch, is set directly.
    /// </summary>
    private void SetReadOnly()
    {
        string fileName = GetBaseDir() + "/readonlyFiles" + Constants.SuffixMvFile;
        File.SetAttributes(fileName, FileAttributes.ReadOnly);
    }

    /// <summary>
    /// The reference's Connection.isReadOnly: the READ_ONLY row of
    /// INFORMATION_SCHEMA.SETTINGS, which is the value the JDBC flag reports.
    /// </summary>
    /// <param name="conn">the connection</param>
    /// <returns>whether the database is read-only</returns>
    private static bool IsReadOnly(DbConnection conn)
    {
        DbCommand command = conn.CreateCommand();
        command.CommandText =
            "SELECT SETTING_VALUE FROM INFORMATION_SCHEMA.SETTINGS WHERE SETTING_NAME = 'READ_ONLY'";
        using DbDataReader rs = command.ExecuteReader();
        return rs.Read() && !rs.IsDBNull(0) && rs.GetInt64(0) != 0;
    }

    private void TestReadOnlyConnect()
    {
        DeleteDb("readonlyConnect");
        DbConnection conn = GetConnection("readonlyConnect;OPEN_NEW=TRUE");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(id identity)");
        Execute(stat, "insert into test select x from system_range(1, 11)");
        AssertThrows(ErrorCode.DatabaseAlreadyOpen1,
            () => _ = GetConnection("readonlyConnect;ACCESS_MODE_DATA=r;OPEN_NEW=TRUE"));
        conn.Close();
        DeleteDb("readonlyConnect");
    }
}
