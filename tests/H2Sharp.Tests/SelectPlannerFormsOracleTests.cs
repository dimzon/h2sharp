/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 108: the Select planner forms — ORDER BY served by an index (same or reversed
 * order, different sort type, row id, constants, expressions), FOR UPDATE over an index,
 * the GROUP BY/HAVING condition placement, query reuse (Reset) and the plan text.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class SelectPlannerFormsOracleTests
{
    [JavaOracleFact]
    public void OrderByIndexFormsMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE S(ID INT PRIMARY KEY, A INT, B INT)",
            "CREATE INDEX SA ON S(A)",
            "CREATE INDEX SBD ON S(B DESC)",
            "INSERT INTO S VALUES (1, 30, 10), (2, 10, 30), (3, 20, 20), (4, NULL, NULL)",
            "SELECT * FROM S ORDER BY A",
            "SELECT * FROM S ORDER BY A DESC",
            "SELECT * FROM S ORDER BY B",
            "SELECT * FROM S ORDER BY B DESC",
            "SELECT * FROM S ORDER BY _ROWID_",
            "SELECT * FROM S ORDER BY _ROWID_ DESC",
            "SELECT * FROM S ORDER BY A + 1",
            "SELECT * FROM S ORDER BY 'x', A",
            "SELECT * FROM S ORDER BY 1",
            "SELECT * FROM S ORDER BY A NULLS LAST",
            "EXPLAIN SELECT * FROM S ORDER BY A",
            "EXPLAIN SELECT * FROM S ORDER BY A DESC",
            "EXPLAIN SELECT * FROM S ORDER BY B",
            "EXPLAIN SELECT * FROM S ORDER BY _ROWID_",
            "DROP TABLE S",
        ]);
    }

    [JavaOracleFact]
    public void GroupingAndForUpdateFormsMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE S(ID INT PRIMARY KEY, A INT)",
            "CREATE INDEX SA ON S(A)",
            "INSERT INTO S VALUES (1, 10), (2, 10), (3, 20)",
            // The group column condition goes to WHERE, the aggregate condition to HAVING.
            "SELECT A, COUNT(*) FROM S GROUP BY A HAVING A > 10 ORDER BY A",
            "SELECT A, COUNT(*) FROM S GROUP BY A HAVING COUNT(*) > 1 ORDER BY A",
            "SELECT A, COUNT(*) FROM S GROUP BY A HAVING COUNT(*) > 1 AND A > 0 ORDER BY A",
            "SELECT * FROM S WHERE ID = 1 FOR UPDATE",
            "SELECT * FROM S ORDER BY A FOR UPDATE",
            "EXPLAIN SELECT * FROM S ORDER BY A FOR UPDATE",
            // The same statement twice: the cached Select is reset in between.
            "SELECT ID FROM S WHERE ID = 1",
            "SELECT ID FROM S WHERE ID = 1",
            "SELECT * FROM S ORDER BY A",
            "SELECT * FROM S ORDER BY A",
            // Correlated subqueries mark the outer rows evaluatable once per row.
            "SELECT ID FROM S O WHERE EXISTS (SELECT 1 FROM S I WHERE I.ID = O.ID)",
            "SELECT (SELECT MAX(I.A) FROM S I WHERE I.ID <= O.ID) FROM S O ORDER BY O.ID",
            "SELECT * FROM S WHERE ID IN (SELECT ID FROM S WHERE A > 0)",
            "SELECT * FROM S ORDER BY A LIMIT 2",
            "SELECT * FROM S ORDER BY A LIMIT 2",
            // A derived table receives the outer condition.
            "SELECT * FROM (SELECT * FROM S) V WHERE V.ID = 1",
            "SELECT * FROM (SELECT A, COUNT(*) C FROM S GROUP BY A) V WHERE C > 0 ORDER BY A",
            "SELECT A + 0, COUNT(*) FROM S GROUP BY A + 0 ORDER BY 1",
            "DROP TABLE S",
        ]);
    }
}
