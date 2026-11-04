/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The group-sorted grouping: a GROUP BY whose expressions are the leading columns of an
 * index reads the groups in index order instead of hashing them. The plans and the rows are
 * checked against the reference over aliases, composite keys, HAVING, hidden group keys,
 * expressions that cannot use the index, an index chosen by a WHERE clause, limits, ORDER BY
 * and NULL keys.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class GroupSortedOracleTests
{
    private static readonly string[] Tables =
    [
        "CREATE TABLE G(ID INT PRIMARY KEY, A INT, B INT, N VARCHAR)",
        "CREATE INDEX G_AB ON G(A, B)",
        "INSERT INTO G VALUES (1, 10, 100, 'x'), (2, 20, 200, 'y'), (3, 20, 100, 'z'), "
            + "(4, NULL, 300, NULL), (5, 10, 100, 'w'), (6, 30, NULL, 'v')",
    ];

    [JavaOracleFact]
    public void GroupSortedPlansAndRowsMatchJdbc()
    {
        ExplainOracleTests.Check([
            .. Tables,
            "EXPLAIN SELECT A, COUNT(*) FROM G GROUP BY A",
            "SELECT A, COUNT(*) FROM G GROUP BY A ORDER BY A",
            "EXPLAIN SELECT A, B, COUNT(*) FROM G GROUP BY A, B",
            "SELECT A, B, COUNT(*) FROM G GROUP BY A, B ORDER BY A, B",
            // The index is (A, B), so B, A is not its leading columns: the hash path.
            "EXPLAIN SELECT B, A, COUNT(*) FROM G GROUP BY B, A",
            "SELECT B, A, COUNT(*) FROM G GROUP BY B, A ORDER BY B, A",
            "EXPLAIN SELECT A, COUNT(*) FROM G GROUP BY A HAVING COUNT(*) > 1",
            "SELECT A, COUNT(*) FROM G GROUP BY A HAVING COUNT(*) > 1 ORDER BY A",
            "SELECT A, COUNT(*) FROM G GROUP BY A HAVING COUNT(*) > 9 ORDER BY A",
            // A hidden group key: the select list has the aggregate only.
            "EXPLAIN SELECT COUNT(*) FROM G GROUP BY A",
            "SELECT COUNT(*) FROM G GROUP BY A ORDER BY 1",
            // An expression that is not a column cannot be read off the index.
            "EXPLAIN SELECT A + 0, COUNT(*) FROM G GROUP BY A + 0",
            "SELECT A + 0, COUNT(*) FROM G GROUP BY A + 0 ORDER BY 1",
            // The primary key is not a scan, so the group-sorted index is not swapped in.
            "EXPLAIN SELECT A, COUNT(*) FROM G WHERE ID > 0 GROUP BY A",
            "SELECT A, COUNT(*) FROM G WHERE ID > 0 GROUP BY A ORDER BY A",
            // Limits: the offset is skipped while walking, the fetch trims the result.
            "EXPLAIN SELECT A, COUNT(*) FROM G GROUP BY A LIMIT 2",
            "SELECT A, COUNT(*) FROM G GROUP BY A ORDER BY A LIMIT 2",
            "SELECT A, COUNT(*) FROM G GROUP BY A ORDER BY A LIMIT 2 OFFSET 1",
            "SELECT A, COUNT(*) FROM G GROUP BY A ORDER BY A DESC",
            "SELECT A, SUM(B) FROM G GROUP BY A ORDER BY A",
            "SELECT A AS X, COUNT(*) FROM G GROUP BY X ORDER BY X",
            "SELECT A, B, SUM(ID) FROM G GROUP BY A, B ORDER BY A, B DESC",
            // An empty table answers no groups.
            "CREATE TABLE E(ID INT, A INT)",
            "CREATE INDEX E_A ON E(A)",
            "EXPLAIN SELECT A, COUNT(*) FROM E GROUP BY A",
            "SELECT A, COUNT(*) FROM E GROUP BY A",
            // Two suitable indexes: the first one the table lists is used, and B alone is
            // also a leading-column index.
            "CREATE TABLE H(ID INT PRIMARY KEY, A INT, B INT)",
            "CREATE INDEX H_AB ON H(A, B)",
            "CREATE INDEX H_B ON H(B)",
            "INSERT INTO H VALUES (1, 10, 100), (2, 10, 200), (3, 20, 100)",
            "SELECT A, COUNT(*) FROM H GROUP BY A ORDER BY A",
            "SELECT B, COUNT(*) FROM H GROUP BY B ORDER BY B",
            "SELECT A, B, COUNT(*) FROM H GROUP BY A, B ORDER BY A, B",
        ]);
    }
}
