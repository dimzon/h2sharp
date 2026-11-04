/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The IN list: NULL on either side, the single-value fold to an equality, the constant set,
 * the unique-index path of a row-value IN, the OR merge that extends a list, and IN inside a
 * grouping expression.
 */
using System.Data.Common;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class InListOracleTests
{
    [JavaOracleFact]
    public void NullsAndConstantsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "SELECT NULL IN (1, 2)",
            "SELECT 1 IN (2, NULL)",
            "SELECT 1 IN (1, NULL)",
            "SELECT 1 NOT IN (2, NULL)",
            "SELECT 1 NOT IN (1, NULL)",
            "SELECT 'x' IN (1, 2)",
            "SELECT 1 IN (1)",
            "SELECT 1 IN (2)",
            "SELECT 1 NOT IN (1)",
            "SELECT 1 IN (1, 2)",
            "SELECT 1 IN (2, 3)",
            "SELECT NOT (1 IN (1, 2))",
            "SELECT 1 IN (1, 'a')",
            "SELECT CAST('a' AS ENUM('a', 'b')) IN ('a', 'b')",
            "SELECT CAST('a' AS ENUM('a', 'b')) IN ('b')",
        ]);
    }

    [JavaOracleFact]
    public void WhenOperandsAndAggregatesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE T(ID INT)",
            "INSERT INTO T VALUES (1), (2), (3), (4)",
            "SELECT CASE WHEN CAST('a' AS ENUM('a', 'b')) IN ('a', 'b') THEN 1 ELSE 0 END",
            "SELECT CASE WHEN NULL IN (1, 2) THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 1 NOT IN (1, 2) THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 1 IN (1, 2) THEN 1 ELSE 0 END",
            "SELECT SUM(CASE WHEN ID IN (1, 2) THEN ID ELSE 0 END) FROM T",
            "SELECT ID, COUNT(*) FROM T GROUP BY CASE WHEN ID IN (1, 2) THEN 1 ELSE 0 END ORDER BY 1",
            // A non-constant element keeps the list itself inside the CASE and the aggregate.
            "SELECT ID FROM T WHERE CASE WHEN ID IN (1, ID) THEN TRUE ELSE FALSE END ORDER BY ID",
            "SELECT SUM(CASE WHEN ID IN (1, ID) THEN ID ELSE 0 END) FROM T",
            "SELECT CASE WHEN ID IN (1, ID) THEN 1 ELSE 0 END, COUNT(*) FROM T "
                + "GROUP BY CASE WHEN ID IN (1, ID) THEN 1 ELSE 0 END ORDER BY 1",
        ]);
    }

    [JavaOracleFact]
    public void ColumnListsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE T(ID INT, V VARCHAR)",
            "INSERT INTO T VALUES (1, 'a'), (2, 'b'), (3, NULL), (NULL, 'c')",
            "SELECT ID FROM T WHERE ID IN (1) ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (1, 2) ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (1, NULL) ORDER BY ID",
            "SELECT ID FROM T WHERE ID NOT IN (1, 2) ORDER BY ID",
            "SELECT ID FROM T WHERE ID NOT IN (1, NULL) ORDER BY ID",
            "SELECT ID FROM T WHERE V IN ('a', 'b') ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (1) IS NULL ORDER BY ID",
            // A non-constant element keeps the list a ConditionInList at run time, which is
            // where the NULL left value and the NULL comparison are evaluated.
            "SELECT ID FROM T WHERE ID IN (1, ID) ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (2, ID + 1) ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (1, V) ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (1, ID) IS NULL ORDER BY ID",
        ]);
    }

    [JavaOracleFact]
    public void OrMergesAnEqualityIntoTheListMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE T(ID INT)",
            "INSERT INTO T VALUES (1), (2), (3), (4), (NULL)",
            "SELECT ID FROM T WHERE ID IN (1, 2) OR ID = 3 ORDER BY ID",
            "SELECT ID FROM T WHERE ID = 3 OR ID IN (1, 2) ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (1, 2) OR ID = 3 OR ID = 4 ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (1, 2) OR ID = 3 AND ID > 0 ORDER BY ID",
            "SELECT ID FROM T WHERE ID NOT IN (1, 2) OR ID = 3 ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (1, 2) OR V = 3 ORDER BY ID",
            // A non-constant element keeps ConditionInList, whose own OR merge then runs.
            "SELECT ID FROM T WHERE ID + 0 IN (1, ID) OR ID = 3 ORDER BY ID",
            "SELECT ID FROM T WHERE ID + 0 IN (1, ID) OR ID + 0 = 3 ORDER BY ID",
            "SELECT ID FROM T WHERE ID = 3 OR ID + 0 IN (1, ID) ORDER BY ID",
        ]);
    }

    [JavaOracleFact]
    public void RowValueInOverAUniqueIndexMatchesJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE U(A INT, B INT, CONSTRAINT UQ UNIQUE(A, B))",
            "INSERT INTO U VALUES (1, 2), (3, 4), (5, 6), (NULL, 2)",
            "SELECT A, B FROM U WHERE (A, B) IN ((1, 2), (3, 4)) ORDER BY A, B",
            "SELECT A, B FROM U WHERE (A, B) IN ((1, 2)) ORDER BY A, B",
            "SELECT A, B FROM U WHERE (A, B) IN ((1, 2, 3), (3, 4)) ORDER BY A, B",
            "SELECT A, B FROM U WHERE (A, B) IN ('x', (3, 4)) ORDER BY A, B",
            "SELECT A, B FROM U WHERE (A, B) IN (ARRAY[1, 2]) ORDER BY A, B",
            "SELECT A, B FROM U WHERE (A, B) NOT IN ((1, 2), (3, 4)) ORDER BY A, B",
            "SELECT A, B FROM U WHERE (A, B) IN ((1, 2), (3, 4)) OR A = 5 ORDER BY A, B",
            // Row values that are not all constants keep ConditionInList, whose unique-index
            // path then decides column by column.
            "SELECT A, B FROM U WHERE (A, B) IN (ROW(A, B), ROW(3, 4)) ORDER BY A, B",
            "SELECT A, B FROM U WHERE (A, B) IN (ROW(A, B), (3, 4)) ORDER BY A, B",
            "SELECT A, B FROM U WHERE (A, B) IN (ARRAY[A, B], ARRAY[3, 4]) ORDER BY A, B",
            "SELECT A, B FROM U WHERE (A, B) IN (ROW(A, B, 0), ROW(3, 4)) ORDER BY A, B",
            "SELECT A, B FROM U WHERE (A, B) IN (ROW(A, B), (3, 4, 5)) ORDER BY A, B",
            "SELECT A, B FROM U WHERE (A, B) IN (ROW(A, B), 1) ORDER BY A, B",
        ]);
    }

    [JavaOracleFact]
    public void InInsideGroupingMatchesJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE T(ID INT)",
            "INSERT INTO T VALUES (1), (2), (3), (4)",
            "SELECT ID IN (1, 2), COUNT(*) FROM T GROUP BY ID IN (1, 2) ORDER BY 1",
            "SELECT ID IN (1, 2) AS X, COUNT(*) FROM T GROUP BY X ORDER BY X",
            "SELECT COUNT(*) FROM T HAVING ID IN (1, 2)",
        ]);
    }

    [JavaOracleFact]
    public void TheInPlansMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE U(A INT, B INT, CONSTRAINT UQ UNIQUE(A, B))",
            "EXPLAIN SELECT A, B FROM U WHERE (A, B) IN ((1, 2), (3, 4))",
            "CREATE TABLE T(ID INT)",
            "CREATE INDEX I ON T(ID)",
            "EXPLAIN SELECT ID FROM T WHERE ID IN (1, 2) OR ID = 3",
            "EXPLAIN SELECT ID FROM T WHERE ID IN (1, 2)",
            "EXPLAIN SELECT ID FROM T WHERE ID IN (1)",
            "EXPLAIN SELECT ID FROM T WHERE ID NOT IN (1, 2)",
        ]);
    }

    [Fact]
    public void AParameterInTheListTakesItsColumnType()
    {
        using H2Connection connection = new(
            "Data Source=in_list_param_" + System.Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        using (DbCommand create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE T(ID INT)";
            create.ExecuteNonQuery();
        }

        // The prepare step binds the parameter to the column the IN list compares, so the
        // list is built as an INT list even before a row is read.
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ID FROM T WHERE ID IN (?, 2)";
        DbParameter parameter = command.CreateParameter();
        parameter.Value = 1;
        command.Parameters.Add(parameter);
        using DbDataReader reader = command.ExecuteReader();
        Assert.Equal(1, reader.FieldCount);
        Assert.False(reader.Read());
    }
}
