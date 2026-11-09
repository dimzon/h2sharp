/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 66: line-targeted coverage of Select.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class SelectCoverageSprintOracleTests
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
    public void GroupByCopiesAndAliasMatch()
    {
        ExplainOracleTests.Check([
            .. Tables,
            "SELECT A AS A, COUNT(*) FROM T GROUP BY A",
            "SELECT A, A, COUNT(*) FROM T GROUP BY A",
            "SELECT A, B, COUNT(*) FROM T GROUP BY A, B, A",
            "SELECT A AS X, A AS X, COUNT(*) FROM T GROUP BY X",
            "EXPLAIN SELECT A AS A, COUNT(*) FROM T GROUP BY A",
            "EXPLAIN SELECT A, A, COUNT(*) FROM T GROUP BY A",
        ]);
    }

    [JavaOracleFact]
    public void WithTiesPaging()
    {
        ExplainOracleTests.Check([
            .. Tables,
            "SELECT ID FROM T ORDER BY A FETCH FIRST 2 ROWS WITH TIES",
            "SELECT ID FROM T ORDER BY A DESC FETCH FIRST 2 ROWS WITH TIES",
            "SELECT ID FROM T ORDER BY A NULLS LAST FETCH FIRST 3 ROWS WITH TIES",
            "EXPLAIN SELECT ID FROM T ORDER BY A FETCH FIRST 2 ROWS WITH TIES",
        ]);
    }

    [JavaOracleFact]
    public void WindowGroupHavingRefusal()
    {
        ExplainOracleTests.Check([
            .. Tables,
            "SELECT A, COUNT(*) FROM T GROUP BY A HAVING COUNT(*) >= 1",
            "SELECT A, ROW_NUMBER() OVER (PARTITION BY A ORDER BY ID) RN FROM T QUALIFY RN = 1",
            "SELECT A, COUNT(*) OVER (PARTITION BY A) C FROM T WHERE A IS NOT NULL",
        ]);
    }

    [JavaOracleFact]
    public void LazyFlatAndGroupSorted()
    {
        ExplainOracleTests.Check([
            .. Tables,
            "SET LAZY_QUERY_EXECUTION TRUE",
            "SELECT ID, A FROM T WHERE A >= 10 ORDER BY ID",
            "SELECT A, COUNT(*) FROM T GROUP BY A ORDER BY A",
            "SELECT ID FROM T ORDER BY ID OFFSET 1 ROWS FETCH NEXT 2 ROWS ONLY",
            "SET LAZY_QUERY_EXECUTION FALSE",
        ]);
    }
}