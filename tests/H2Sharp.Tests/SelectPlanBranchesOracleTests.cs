/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The planner branches of Select against the reference: how ORDER BY meets the indexes,
 * how GROUP BY merges with the select list, where join conditions land, what FOR UPDATE
 * checks, and the paged reads.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The decisions <see cref="H2Sharp.Command.Query.Select"/> makes during planning and
/// execution, checked over SQL.
/// </summary>
/// <remarks>
/// EXPLAIN is the readable form of the plan: which index answers an ORDER BY, whether the
/// sort was avoided, where a condition was pushed. The same statements then run, so a plan
/// that matches but answers differently still fails.
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class SelectPlanBranchesOracleTests
{
    private static readonly string[] Tables =
    [
        "CREATE TABLE T(ID INT PRIMARY KEY, A INT, B INT, N VARCHAR)",
        "CREATE INDEX T_AB ON T(A, B)",
        "CREATE INDEX T_N ON T(N)",
        "INSERT INTO T VALUES (1, 10, 100, 'x'), (2, 20, 200, 'y'), (3, 20, 100, 'z'), "
            + "(4, NULL, 300, NULL)",
    ];

    [JavaOracleFact]
    public void IndexSortDecisionsMatchJdbc()
    {
        ExplainOracleTests.Check([
            .. Tables,
            "EXPLAIN SELECT * FROM T ORDER BY ID",
            "EXPLAIN SELECT * FROM T ORDER BY ID DESC",
            "EXPLAIN SELECT * FROM T ORDER BY A",
            "EXPLAIN SELECT * FROM T ORDER BY A DESC",
            "EXPLAIN SELECT * FROM T ORDER BY A, B",
            "EXPLAIN SELECT * FROM T ORDER BY A, B DESC",
            "EXPLAIN SELECT * FROM T ORDER BY B, A",
            "EXPLAIN SELECT * FROM T ORDER BY A NULLS FIRST",
            "EXPLAIN SELECT * FROM T ORDER BY A NULLS LAST",
            "EXPLAIN SELECT * FROM T WHERE A = 20 ORDER BY B",
            "EXPLAIN SELECT * FROM T WHERE ID > 1 ORDER BY ID",
            "EXPLAIN SELECT * FROM T ORDER BY N",
            "EXPLAIN SELECT * FROM T ORDER BY N DESC, ID",
            "EXPLAIN SELECT * FROM T ORDER BY ID LIMIT 2",
            "EXPLAIN SELECT * FROM T ORDER BY ID LIMIT 2 OFFSET 1",
            "EXPLAIN SELECT * FROM (SELECT * FROM T ORDER BY ID LIMIT 2) X ORDER BY ID DESC",
            "EXPLAIN SELECT * FROM T ORDER BY ID FETCH FIRST 2 ROWS ONLY",
            "EXPLAIN SELECT * FROM T ORDER BY ID FETCH FIRST 2 ROWS WITH TIES",
            "EXPLAIN SELECT * FROM T ORDER BY ID OFFSET 1 ROWS FETCH NEXT 2 ROWS ONLY",
            "SELECT * FROM T ORDER BY A, B",
            "SELECT * FROM T ORDER BY A DESC NULLS LAST",
            "SELECT * FROM T ORDER BY ID LIMIT 2 OFFSET 1",
            "SELECT * FROM T ORDER BY ID FETCH FIRST 2 ROWS WITH TIES",
            "SELECT * FROM T ORDER BY N NULLS FIRST, ID",
        ]);
    }

    [JavaOracleFact]
    public void GroupByMergesMatchJdbc()
    {
        ExplainOracleTests.Check([
            .. Tables,
            "SELECT A, COUNT(*) FROM T GROUP BY 1 ORDER BY A",
            "SELECT A AS X, COUNT(*) FROM T GROUP BY X ORDER BY X",
            "SELECT A + 1, COUNT(*) FROM T GROUP BY A + 1 ORDER BY 1",
            "SELECT A, B, COUNT(*) FROM T GROUP BY A, B ORDER BY A, B",
            "SELECT A, COUNT(*) FROM T GROUP BY A HAVING COUNT(*) > 1 ORDER BY A",
            "SELECT A * 2 AS D, COUNT(*) FROM T GROUP BY A * 2 ORDER BY D",
            "SELECT A, B, COUNT(*) FROM T GROUP BY A, B, A ORDER BY A, B",
            "EXPLAIN SELECT A, COUNT(*) FROM T GROUP BY 1",
            "EXPLAIN SELECT A + 1, COUNT(*) FROM T GROUP BY A + 1",
        ]);
    }

    /// <summary>
    /// A grouping answered by a sorted index prints a marker in the reference's plan and
    /// groups from the index: one group per key, the previous key compared with the row's,
    /// and HAVING deciding each finished group.
    /// </summary>
    [JavaOracleFact]
    public void GroupBySortedPlanMatchesJdbc()
    {
        ExplainOracleTests.Check([
            .. Tables,
            "EXPLAIN SELECT A AS X, COUNT(*) FROM T GROUP BY X",
            "EXPLAIN SELECT A, COUNT(*) FROM T GROUP BY A HAVING COUNT(*) > 1",
            "SELECT A AS X, COUNT(*) FROM T GROUP BY X",
            "SELECT A, COUNT(*) FROM T GROUP BY A HAVING COUNT(*) > 1",
        ]);
    }

    [JavaOracleFact]
    public void JoinConditionsMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE J1(ID INT, V INT)",
            "CREATE TABLE J2(ID INT, W INT)",
            "INSERT INTO J1 VALUES (1, 10), (2, 20), (3, 30)",
            "INSERT INTO J2 VALUES (1, 100), (2, 200), (4, 400)",
            "EXPLAIN SELECT * FROM J1 JOIN J2 ON J1.ID = J2.ID WHERE J1.V > 1",
            "EXPLAIN SELECT * FROM J1 JOIN J2 ON J1.ID = J2.ID WHERE J2.W = 100",
            "EXPLAIN SELECT * FROM J1 JOIN J2 ON J1.ID = J2.ID AND J2.W = 100",
            "EXPLAIN SELECT * FROM J1 LEFT JOIN J2 ON J1.ID = J2.ID WHERE J1.V = 10",
            "EXPLAIN SELECT * FROM J1 JOIN J2 ON J1.ID = J2.ID WHERE J1.V = J2.W",
            "SELECT * FROM J1 JOIN J2 ON J1.ID = J2.ID WHERE J1.V = 10",
            "SELECT * FROM J1 JOIN J2 ON J1.ID = J2.ID WHERE J2.W = 100",
            "SELECT * FROM J1 LEFT JOIN J2 ON J1.ID = J2.ID WHERE J1.V = 20",
            "SELECT * FROM J1 JOIN J2 ON J1.ID = J2.ID WHERE J1.V = J2.W",
        ]);
    }

    [JavaOracleFact]
    public void ForUpdateConditionsMatchJdbc()
    {
        ExplainOracleTests.Check([
            .. Tables,
            "BEGIN",
            "SELECT * FROM T WHERE A = 20 FOR UPDATE",
            "SELECT * FROM T WHERE B > 100 FOR UPDATE",
            "SELECT * FROM T WHERE N IS NULL FOR UPDATE",
            "SELECT ID FROM T WHERE A = 10 FOR UPDATE",
            "COMMIT",
            "SELECT COUNT(*) FROM T",
        ]);
    }

    [JavaOracleFact]
    public void QualifyAndPagingMatchJdbc()
    {
        ExplainOracleTests.Check([
            .. Tables,
            "EXPLAIN SELECT ID, ROW_NUMBER() OVER (ORDER BY ID) RN FROM T QUALIFY RN = 1",
            "SELECT ID, ROW_NUMBER() OVER (ORDER BY ID) RN FROM T QUALIFY RN = 1",
            "SELECT ID, ROW_NUMBER() OVER (PARTITION BY A ORDER BY ID) RN FROM T "
                + "QUALIFY RN = 1 ORDER BY ID",
            "EXPLAIN SELECT ID, ROW_NUMBER() OVER (PARTITION BY A ORDER BY ID) RN FROM T "
                + "QUALIFY RN = 1",
            "SELECT 1 UNION SELECT 2.5",
            "EXPLAIN SELECT 1 UNION ALL SELECT 2.5",
            "SELECT CAST(1 AS SMALLINT) UNION SELECT 40000",
        ]);
    }
}
