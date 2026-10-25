// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestMergeUsing.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements/result sets are ADO.NET commands/readers, the result
// set meta data is the reader's FieldCount, and the reference's main() entry is the
// runner, xUnit runs Test() directly.
//
// Skipped (port gap): the reference implements org.h2.api.Trigger and registers three
// BEFORE triggers on PARENT; the port has no Trigger API, so the trigger case is
// dropped from test() and the trigger methods are absent. Everything else (MERGE ...
// USING with all clause combinations, the MINUS diff, and FINAL TABLE) is ported.
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test merge using syntax.
/// </summary>
public sealed class TestMergeUsing : TestDb
{
    private const string GatherOrderedResultsSql =
        "SELECT ID, NAME FROM PARENT ORDER BY ID ASC";

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        // Simple ID,NAME inserts, target table with PK initially empty
        TestMergeUsingCase(
            "CREATE TABLE PARENT(ID INT, NAME VARCHAR, PRIMARY KEY(ID) );",
            "MERGE INTO PARENT AS P USING (SELECT X AS ID, 'Marcy'||X AS NAME "
            + "FROM SYSTEM_RANGE(1,2) ) AS S ON (P.ID = S.ID AND 1=1 AND S.ID = P.ID) "
            + "WHEN MATCHED THEN "
            + "UPDATE SET P.NAME = S.NAME WHERE 2 = 2 WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (S.ID, S.NAME)",
            GatherOrderedResultsSql,
            "SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,2)", 2);
        // Simple NAME updates, target table missing PK
        TestMergeUsingCase(
            "CREATE TABLE PARENT AS (SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,2) );",
            "MERGE INTO PARENT AS P USING ("
            + "SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,2) ) AS S "
            + "ON (P.ID = S.ID AND 1=1 AND S.ID = P.ID) "
            + "WHEN MATCHED THEN UPDATE SET P.NAME = S.NAME||S.ID WHERE 1 = 1 WHEN NOT MATCHED THEN "
            + "INSERT (ID, NAME) VALUES (S.ID, S.NAME)",
            GatherOrderedResultsSql,
            "SELECT X AS ID, 'Marcy'||X||X AS NAME FROM SYSTEM_RANGE(1,2)",
            2);
        // No NAME updates, WHERE clause is always false, insert clause missing
        TestMergeUsingCase(
            "CREATE TABLE PARENT AS (SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,2) );",
            "MERGE INTO PARENT AS P USING ("
            + "SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,2) ) AS S ON (P.ID = S.ID) "
            + "WHEN MATCHED THEN UPDATE SET P.NAME = S.NAME||S.ID WHERE 1 = 2",
            GatherOrderedResultsSql,
            "SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,2)", 0);
        // No NAME updates, no WHERE clause, insert clause missing
        TestMergeUsingCase(
            "CREATE TABLE PARENT AS (SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,2) );",
            "MERGE INTO PARENT AS P USING ("
            + "SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,2) ) AS S ON (P.ID = S.ID) "
            + "WHEN MATCHED THEN UPDATE SET P.NAME = S.NAME||S.ID",
            GatherOrderedResultsSql,
            "SELECT X AS ID, 'Marcy'||X||X AS NAME FROM SYSTEM_RANGE(1,2)",
            2);
        // Two delete updates done, no WHERE clause, insert clause missing
        TestMergeUsingCase(
            "CREATE TABLE PARENT AS (SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,2) );",
            "MERGE INTO PARENT AS P USING ("
            + "SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,2) ) AS S ON (P.ID = S.ID) "
            + "WHEN MATCHED THEN DELETE",
            GatherOrderedResultsSql,
            "SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,2) WHERE 1=0",
            2);
        // One insert, one update one delete happens, target table missing PK
        TestMergeUsingCase(
            "CREATE TABLE PARENT AS (SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,2) );",
            "MERGE INTO PARENT AS P USING ("
            + "SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,3) ) AS S ON (P.ID = S.ID) "
            + "WHEN MATCHED THEN UPDATE SET P.NAME = S.NAME||S.ID WHERE P.ID = 2 "
            + "WHEN MATCHED THEN DELETE WHERE P.ID = 1 WHEN NOT MATCHED THEN "
            + "INSERT (ID, NAME) VALUES (S.ID, S.NAME)",
            GatherOrderedResultsSql,
            "SELECT X AS ID, 'Marcy'||X||X AS NAME FROM SYSTEM_RANGE(2,2) UNION ALL "
            + "SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(3,3)",
            3);
        // One insert, one update one delete happens, target table missing PK
        TestMergeUsingCase(
            "CREATE TABLE PARENT AS (SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,2) );"
            + "CREATE TABLE SOURCE AS (SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,3)  );",
            "MERGE INTO PARENT AS P USING SOURCE AS S ON (P.ID = S.ID) "
            + "WHEN MATCHED THEN UPDATE SET P.NAME = S.NAME||S.ID WHERE P.ID = 2 "
            + "WHEN MATCHED THEN DELETE WHERE P.ID = 1 WHEN NOT MATCHED THEN "
            + "INSERT (ID, NAME) VALUES (S.ID, S.NAME)",
            GatherOrderedResultsSql,
            "SELECT X AS ID, 'Marcy'||X||X AS NAME FROM SYSTEM_RANGE(2,2) UNION ALL "
            + "SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(3,3)",
            3);
        // One insert, one update one delete happens, target table missing PK,
        // no source alias
        TestMergeUsingCase(
            "CREATE TABLE PARENT AS (SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,2) );"
            + "CREATE TABLE SOURCE AS (SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,3)  );",
            "MERGE INTO PARENT AS P USING SOURCE ON (P.ID = SOURCE.ID) "
            + "WHEN MATCHED THEN UPDATE SET P.NAME = SOURCE.NAME||SOURCE.ID WHERE P.ID = 2 "
            + "WHEN MATCHED THEN DELETE WHERE P.ID = 1 "
            + "WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (SOURCE.ID, SOURCE.NAME)",
            GatherOrderedResultsSql,
            "SELECT X AS ID, 'Marcy'||X||X AS NAME FROM SYSTEM_RANGE(2,2) UNION ALL "
            + "SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(3,3)",
            3);
        // One insert, one update one delete happens, target table missing PK,
        // no source or target alias
        TestMergeUsingCase(
            "CREATE TABLE PARENT AS (SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,2) );"
            + "CREATE TABLE SOURCE AS (SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,3)  );",
            "MERGE INTO PARENT USING SOURCE ON (PARENT.ID = SOURCE.ID) "
            + "WHEN MATCHED THEN UPDATE SET PARENT.NAME = SOURCE.NAME||SOURCE.ID WHERE PARENT.ID = 2 "
            + "WHEN MATCHED THEN DELETE WHERE PARENT.ID = 1 "
            + "WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (SOURCE.ID, SOURCE.NAME)",
            GatherOrderedResultsSql,
            "SELECT X AS ID, 'Marcy'||X||X AS NAME FROM SYSTEM_RANGE(2,2) UNION ALL "
            + "SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(3,3)",
            3);

        // Only insert clause, no update or delete clauses
        TestMergeUsingCase(
            "CREATE TABLE PARENT AS (SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,1) );"
            + "DELETE FROM PARENT;",
            "MERGE INTO PARENT AS P USING ("
            + "SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,3) ) AS S ON (P.ID = S.ID) "
            + "WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (S.ID, S.NAME)",
            GatherOrderedResultsSql,
            "SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,3)", 3);
        // no insert, no update, no delete clauses - essentially a no-op
        TestMergeUsingException(
            "CREATE TABLE PARENT AS (SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,1) );"
            + "DELETE FROM PARENT;",
            "MERGE INTO PARENT AS P USING ("
            + "SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,3) ) AS S ON (P.ID = S.ID)",
            GatherOrderedResultsSql,
            "SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1,3) WHERE X<0",
            0,
            "WHEN\"");
        // The reference's trigger case is skipped: the port has no Trigger API (see the
        // header).
        TestDataChangeDeltaTable();
    }

    /// <summary>
    /// Run a test case of the merge using syntax.
    /// </summary>
    /// <param name="setupSql">one or more SQL statements to setup the case</param>
    /// <param name="statementUnderTest">the merge statement being tested</param>
    /// <param name="gatherResultsSql">a select which gathers the results of the merge
    /// from the target table</param>
    /// <param name="expectedResultsSql">a select which returns the expected results
    /// in the target table</param>
    /// <param name="expectedRowUpdateCount">how many updates should be expected from
    /// the merge using</param>
    private void TestMergeUsingCase(string setupSql, string statementUnderTest,
        string gatherResultsSql, string expectedResultsSql,
        int expectedRowUpdateCount)
    {
        DeleteDb("mergeUsingQueries");

        try
        {
            using DbConnection conn = GetConnection("mergeUsingQueries;MODE=Oracle");
            DbCommand stat = conn.CreateCommand();
            Execute(stat, setupSql);

            using DbCommand prep = conn.CreateCommand();
            prep.CommandText = statementUnderTest;
            int rowCountUpdate = prep.ExecuteNonQuery();

            // compare actual results from SQL result set with expected results
            // - by diffing (aka set MINUS operation)
            stat.CommandText = "( " + gatherResultsSql + " ) MINUS ( "
                + expectedResultsSql + " )";
            using DbDataReader rs = stat.ExecuteReader();

            int rowCount = 0;
            StringBuilder diffBuffer = new();
            while (rs.Read())
            {
                rowCount++;
                diffBuffer.Append('|');
                for (int i = 0; i < rs.FieldCount; i++)
                {
                    diffBuffer.Append(rs.GetValue(i));
                    diffBuffer.Append("|\n");
                }
            }

            AssertEquals("Differences between expected and actual output found:"
                + diffBuffer, 0, rowCount);
            AssertEquals("Expected update counts differ",
                expectedRowUpdateCount, rowCountUpdate);
        }
        finally
        {
            DeleteDb("mergeUsingQueries");
        }
    }

    /// <summary>
    /// Run a test case of the merge using syntax, expecting an exception message.
    /// </summary>
    /// <param name="setupSql">one or more SQL statements to setup the case</param>
    /// <param name="statementUnderTest">the merge statement being tested</param>
    /// <param name="gatherResultsSql">a select which gathers the results of the merge
    /// from the target table</param>
    /// <param name="expectedResultsSql">a select which returns the expected results
    /// in the target table</param>
    /// <param name="expectedRowUpdateCount">how many updates should be expected from
    /// the merge using</param>
    /// <param name="exceptionMessage">the exception message expected</param>
    private void TestMergeUsingException(string setupSql,
        string statementUnderTest, string gatherResultsSql,
        string expectedResultsSql, int expectedRowUpdateCount,
        string exceptionMessage)
    {
        try
        {
            TestMergeUsingCase(setupSql, statementUnderTest, gatherResultsSql,
                expectedResultsSql, expectedRowUpdateCount);
        }
        catch (Exception e)
        {
            AssertContains(e.Message, exceptionMessage);
            return;
        }

        Fail("Failed to see exception with message:" + exceptionMessage);
    }

    private void TestDataChangeDeltaTable()
    {
        DeleteDb("mergeUsingQueries");
        try
        {
            using DbConnection conn = GetConnection("mergeUsingQueries");
            DbCommand stat = conn.CreateCommand();
            Execute(stat,
                "CREATE TABLE TEST(ID INT PRIMARY KEY, C INTEGER) AS (VALUES (1, 2), (2, 3), (3, 4))");
            using DbCommand prep = conn.CreateCommand();
            prep.CommandText = "SELECT TEST.ID FROM FINAL TABLE ( "
                + "MERGE INTO TEST USING ( "
                + "SELECT T.ID, T.C FROM (SELECT 1, 3) T(ID, C) "
                + ") T ON TEST.ID = T.ID "
                + "WHEN MATCHED AND TEST.ID = 1 THEN "
                + "UPDATE SET C = T.C "
                + "WHEN NOT MATCHED THEN INSERT(ID, C) VALUES (T.ID, T.C) "
                + ") TEST";
            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(1L, rs.GetInt64(0));
            }

            using (DbDataReader rs = prep.ExecuteReader())
            {
                AssertTrue(rs.Read());
                AssertEquals(1L, rs.GetInt64(0));
            }
        }
        finally
        {
            DeleteDb("mergeUsingQueries");
        }
    }
}
