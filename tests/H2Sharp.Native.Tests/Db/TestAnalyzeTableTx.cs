// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestAnalyzeTableTx.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; Statement.executeUpdate(sql) /
// executeQuery(sql) set CommandText and run. The reference's main() entry is the runner;
// xUnit runs Test() directly.
namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests the transaction handling of ANALYZE: many sessions insert into one table and
/// the reader must see exactly what they wrote.
/// </summary>
public sealed class TestAnalyzeTableTx : TestDb
{
    private const int C = 10_000;

    /// <summary>
    /// Whether this test is enabled in the current configuration.
    /// </summary>
    /// <returns><c>true</c> when the test runs</returns>
    public override bool IsEnabled()
    {
        return !Config.Networked && !Config.Big;
    }

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb(GetTestName());
        DbConnection[] connections = new DbConnection[C];
        try
        {
            using DbConnection shared = GetConnection(GetTestName());
            DbCommand statement = shared.CreateCommand();
            statement.CommandText = "DROP TABLE IF EXISTS TEST";
            _ = statement.ExecuteNonQuery();
            statement.CommandText = "CREATE TABLE TEST(ID INT PRIMARY KEY)";
            _ = statement.ExecuteNonQuery();
            for (int i = 0; i < C; i++)
            {
                DbConnection c = GetConnection(GetTestName());
                DbCommand insert = c.CreateCommand();
                insert.CommandText = "INSERT INTO TEST VALUES (" + i + ')';
                _ = insert.ExecuteNonQuery();
                connections[i] = c;
            }

            statement.CommandText = "SELECT * FROM TEST";
            using (DbDataReader rs = statement.ExecuteReader())
            {
                for (int i = 0; i < C; i++)
                {
                    if (!rs.Read())
                    {
                        throw new Exception("next");
                    }

                    if (rs.GetInt32(0) != i)
                    {
                        throw new Exception(i.ToString());
                    }
                }
            }
        }
        finally
        {
            foreach (DbConnection? connection in connections)
            {
                if (connection is not null)
                {
                    try
                    {
                        connection.Close();
                    }
                    catch (Exception)
                    {
                        // the reference swallows the failure the same way
                    }
                }
            }
        }
    }
}
