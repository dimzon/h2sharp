// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestIssue_3040.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands, and Connection.commit() is the
// COMMIT statement through a command, which is what the reference's commit does on the
// engine. The reference's main() entry is the runner; xUnit runs Test() directly.
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests the issue 3040: a common table expression opening a temporary table could reuse
/// the id of another temporary table, and a second connection's create could then collide
/// with it.
/// </summary>
public sealed class TestIssue_3040 : TestDb
{
    /// <summary>
    /// The table the query runs against.
    /// </summary>
    public const string TableToQuery = "TO_QUERY";

    /// <summary>
    /// The query with the common table expression.
    /// </summary>
    public const string QueryStatement = "WITH TMP_TO_QUERY"
        + " as (SELECT avg(SIMPLE_VALUE) AVG_SIMPLE_VALUE FROM public." + TableToQuery
        + ")  SELECT * FROM TMP_TO_QUERY";

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        CreateTableTest();
    }

    /// <summary>
    /// The reference's createTableTest.
    /// </summary>
    private void CreateTableTest()
    {
        DeleteDb(GetTestName());
        using (DbConnection connection = GetConnection(GetTestName()))
        {
            CreateTable(connection, TableToQuery);

            RunCTE(connection);

            // another connection to simulate parallel execution with connection
            // pools sequence used for GENERATED_ID will get the same ID as temp
            // table used for CTE query
            using (DbConnection conn2 = GetConnection(GetTestName()))
            {
                CreateTable(conn2, "WITH_MISSING_SEQUENCE");
            }

            // commit to release again already released systemIds, could be just
            // another query to trigger tx commit
            Commit(connection);

            // id reused again, sequence entry from MVStore gets dropped as side
            // effect.
            RunCTE(connection);
        }

        // try to reconnect to already corrupted file
        using (DbConnection connection = GetConnection(GetTestName()))
        {
            RunCTE(connection);
        }
    }

    /// <summary>
    /// The reference calls Connection.commit(), which runs COMMIT on the engine even in
    /// autocommit mode; the provider has no commit method, so the same statement runs
    /// through a command.
    /// </summary>
    private static void Commit(DbConnection connection)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "COMMIT";
        _ = command.ExecuteNonQuery();
    }

    private static void CreateTable(DbConnection connection, string tableName)
    {
        try
        {
            using DbCommand st = connection.CreateCommand();
            st.CommandText = "CREATE TABLE public." + tableName
                + " (GENERATED_ID IDENTITY PRIMARY KEY, SIMPLE_VALUE INT)";
            _ = st.ExecuteNonQuery();
        }
        catch (DbException ex)
        {
            Console.WriteLine("error: " + ex);
            Console.Error.WriteLine(ex);
        }
    }

    private static void RunCTE(DbConnection connection)
    {
        try
        {
            using DbCommand st = connection.CreateCommand();
            st.CommandText = QueryStatement;
            using DbDataReader reader = st.ExecuteReader();
            while (reader.Read())
            {
                // just loop
            }
        }
        catch (DbException ex)
        {
            Console.WriteLine("error: " + ex);
            Console.Error.WriteLine(ex);
        }
    }
}
