// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.jdbc.TestManyJdbcObjects.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands. A reader the ported loop no
// longer needs is disposed at once (an ADO.NET command refuses a second open reader),
// which is also the shape every ADO.NET caller has. The reference's main() entry is the
// runner; xUnit runs Test() directly.
//
// Modified (port gap, named): conn.getMetaData() has no counterpart — the provider has
// no metadata surface yet — so the getMetaData warm-up loops of testManyConnections and
// the getColumns walk of testNestedResultSets are not ported; those two tests stay
// behind their config.networked gate, which the default configuration never opens.
namespace H2Sharp.Native.Tests.Jdbc;

/// <summary>
/// Tests the server by creating many JDBC objects (result sets and so on).
/// </summary>
public sealed class TestManyJdbcObjects : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestNestedResultSets();
        TestManyConnections();
        TestOneConnectionPrepare();
        DeleteDb("manyObjects");
    }

    private void TestNestedResultSets()
    {
        if (!Config.Networked)
        {
            return;
        }

        DeleteDb("manyObjects");
        using DbConnection conn = GetConnection("manyObjects");
        // The reference walks meta.getColumns and asks for the keys of TEST under every
        // row; the provider has no DatabaseMetaData yet (see the header).
        throw new TODOException("DatabaseMetaData.getColumns/getExportedKeys/getImportedKeys");
    }

    private void TestManyConnections()
    {
        if (!Config.Networked || Config.Memory)
        {
            return;
        }

        // SERVER_CACHED_OBJECTS = 1000: connections = 20 (1250)
        // SERVER_CACHED_OBJECTS = 500: connections = 40
        // SERVER_CACHED_OBJECTS = 50: connections = 120
        // The reference opens many connections and closes meta.getSchemas() over each;
        // the provider has no metadata surface yet (see the class header).
        throw new TODOException("DatabaseMetaData.getSchemas");
    }

    private void TestOneConnectionPrepare()
    {
        DeleteDb("manyObjects");
        using DbConnection conn = GetConnection("manyObjects");
        int size = GetSize(10, 1000);
        for (int i = 0; i < size; i++)
        {
            // The reference calls conn.getMetaData() as a warm-up; the provider has no
            // metadata surface yet (see the header).
        }

        for (int i = 0; i < size; i++)
        {
            using DbCommand command = conn.CreateCommand();
        }

        DbCommand stat = conn.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR)");
        Execute(stat, "INSERT INTO TEST VALUES(1, 'Hello')");
        stat.CommandText = "SELECT * FROM TEST WHERE 1=0";
        for (int i = 0; i < size; i++)
        {
            using DbDataReader reader = stat.ExecuteReader();
        }

        stat.CommandText = "SELECT * FROM TEST";
        for (int i = 0; i < size; i++)
        {
            using DbDataReader reader = stat.ExecuteReader();
        }

        for (int i = 0; i < size; i++)
        {
            using DbCommand command = conn.CreateCommand();
            command.CommandText = "SELECT * FROM TEST";
        }

        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "SELECT * FROM TEST WHERE 1=0";
            for (int i = 0; i < size; i++)
            {
                using DbDataReader reader = prep.ExecuteReader();
            }
        }

        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "SELECT * FROM TEST";
            for (int i = 0; i < size; i++)
            {
                using DbDataReader reader = prep.ExecuteReader();
            }
        }
    }
}
