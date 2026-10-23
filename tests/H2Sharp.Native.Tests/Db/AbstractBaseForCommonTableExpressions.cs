// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.AbstractBaseForCommonTableExpressions.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; the metadata questions
// (getColumnLabel, getColumnTypeName, getColumnCount) are the reader's GetName,
// GetDataTypeName and FieldCount. The reference's main() entry is the runner; xUnit runs
// Test() directly.
using System.Linq;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Base class for common table expression tests.
/// </summary>
public abstract class AbstractBaseForCommonTableExpressions : TestDb
{
    /// <summary>
    /// Test a query.
    /// </summary>
    /// <param name="maxRetries">the number of times the query is run</param>
    /// <param name="expectedRowData">the expected result data</param>
    /// <param name="expectedColumnNames">the expected columns of the result</param>
    /// <param name="expectedNumberOfRows">the expected number of rows</param>
    /// <param name="setupSQL">the SQL statement used for setup</param>
    /// <param name="withQuery">the query</param>
    /// <param name="closeAndReopenDatabaseConnectionOnIteration">whether the connection
    /// should be re-opened each time</param>
    /// <param name="expectedColumnTypes">the expected datatypes of the result</param>
    /// <param name="anyOrder">whether any order of rows should be allowed.
    /// If <c>true</c>, this method may sort expectedRowData.</param>
    protected void TestRepeatedQueryWithSetup(int maxRetries, string?[] expectedRowData,
        string[] expectedColumnNames, int expectedNumberOfRows, string setupSQL,
        string withQuery, int closeAndReopenDatabaseConnectionOnIteration,
        string[] expectedColumnTypes, bool anyOrder)
    {
        _ = expectedNumberOfRows;
        DeleteDb("commonTableExpressionQueries");
        DbConnection conn = GetConnection("commonTableExpressionQueries");

        if (anyOrder)
        {
            Array.Sort(expectedRowData, (a, b) => string.CompareOrdinal(a, b));
        }

        List<string> rowData = [];
        StringBuilder buf = new();

        for (int queryRunTries = 1; queryRunTries <= maxRetries; queryRunTries++)
        {
            DbCommand stat = conn.CreateCommand();
            stat.CommandText = setupSQL;
            _ = stat.ExecuteNonQuery();

            // close and re-open connection for one iteration to make sure the query work
            // between connections
            if (queryRunTries == closeAndReopenDatabaseConnectionOnIteration)
            {
                conn.Close();

                conn = GetConnection("commonTableExpressionQueries");
            }

            DbCommand prep = conn.CreateCommand();
            prep.CommandText = withQuery;

            using (DbDataReader rs = prep.ExecuteReader())
            {
                for (int columnIndex = 0; columnIndex < rs.FieldCount; columnIndex++)
                {
                    AssertNotNull(rs.GetName(columnIndex));
                    AssertEquals(expectedColumnNames[columnIndex], rs.GetName(columnIndex));
                    AssertEquals(
                        "wrong type of column " + rs.GetName(columnIndex) + " on iteration #"
                            + queryRunTries,
                        expectedColumnTypes[columnIndex], rs.GetDataTypeName(columnIndex));
                }

                rowData.Clear();
                while (rs.Read())
                {
                    buf.Clear();
                    for (int columnIndex = 0; columnIndex < rs.FieldCount; columnIndex++)
                    {
                        _ = buf.Append('|').Append(rs.IsDBNull(columnIndex)
                            ? null : rs.GetString(columnIndex));
                    }

                    rowData.Add(buf.ToString());
                }
            }

            if (anyOrder)
            {
                rowData.Sort(string.CompareOrdinal);
            }

            AssertEquals(expectedRowData, rowData.Cast<object?>().ToArray());

            prep.Dispose();
        }

        conn.Close();
        DeleteDb("commonTableExpressionQueries");
    }
}
