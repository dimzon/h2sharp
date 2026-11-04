/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * UNIQUE(SELECT ...) as the reference predicates/unique.sql defines it: duplicate rows, the
 * three NULLS modes, a DISTINCT subquery optimizing the predicate away, DISTINCT ON (which
 * does not), a correlated subquery, and the plans.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class UniquePredicateOracleTests
{
    private const string Setup =
        "CREATE TABLE TEST(ID INT PRIMARY KEY, GR INT, A INT, B INT, C INT) AS VALUES "
        + "(1, 1, NULL, NULL, NULL), (2, 1, NULL, NULL, NULL), (3, 1, NULL, 1, 1), "
        + "(4, 1, NULL, 1, 1), (5, 1, 1, 1, 1), (6, 1, 1, 1, 2), (7, 2, 1, 2, 1)";

    [JavaOracleFact]
    public void UniquePredicatesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            Setup,
            "SELECT UNIQUE(SELECT A, B FROM TEST)",
            "SELECT UNIQUE(TABLE TEST)",
            "SELECT UNIQUE(SELECT A, B, C FROM TEST)",
            "SELECT UNIQUE(SELECT A, B FROM TEST)",
            "SELECT UNIQUE(SELECT DISTINCT A, B FROM TEST)",
            "SELECT UNIQUE NULLS DISTINCT(SELECT A, B FROM TEST)",
            "SELECT UNIQUE NULLS DISTINCT(SELECT A, B FROM TEST WHERE ID <> 6)",
            "SELECT UNIQUE NULLS ALL DISTINCT(SELECT A, B FROM TEST)",
            "SELECT UNIQUE NULLS ALL DISTINCT(SELECT A, B FROM TEST WHERE ID <> 6)",
            "SELECT UNIQUE NULLS ALL DISTINCT(SELECT A, B FROM TEST WHERE ID NOT IN(4, 6))",
            "SELECT UNIQUE NULLS NOT DISTINCT(SELECT A, B FROM TEST)",
            "SELECT UNIQUE NULLS NOT DISTINCT(SELECT A, B FROM TEST WHERE ID <> 6)",
            "SELECT UNIQUE NULLS NOT DISTINCT(SELECT A, B FROM TEST WHERE ID NOT IN(4, 6))",
            "SELECT UNIQUE NULLS NOT DISTINCT(SELECT A, B FROM TEST WHERE ID NOT IN(2, 4, 6))",
            // DISTINCT ON is not a plain DISTINCT: the reference still checks the rows.
            "SELECT UNIQUE(SELECT DISTINCT ON (A) A, B FROM TEST)",
            "SELECT UNIQUE NULLS ALL DISTINCT(SELECT DISTINCT ON (A) A, B FROM TEST)",
            "SELECT G, UNIQUE(SELECT A, B, C FROM TEST WHERE GR = G) FROM (VALUES 1, 2, 3) V(G)",
            "SELECT G, UNIQUE(SELECT A, B FROM TEST WHERE GR = G ORDER BY A + B) "
                + "FROM (VALUES 1, 2, 3) V(G)",
            "SELECT UNIQUE(SELECT X FROM (VALUES 1, 2, 3) V(X) WHERE X > 3)",
            "SELECT UNIQUE(SELECT X FROM (VALUES 1, 1, 2) V(X))",
            // A set operation reaches the predicate through its own result target.
            "SELECT UNIQUE(SELECT X FROM (VALUES 1, 2) V(X) UNION ALL SELECT 1)",
            "SELECT UNIQUE(SELECT X FROM (VALUES 1, 2) V(X) UNION ALL SELECT 3)",
            "SELECT UNIQUE(SELECT X FROM (VALUES 1, 2) V(X) EXCEPT SELECT 1)",
            "SELECT UNIQUE(SELECT X FROM (VALUES 1, 2) V(X) INTERSECT SELECT 1)",
            "SELECT UNIQUE(SELECT X FROM (VALUES 1, 2) V(X) UNION SELECT DISTINCT X FROM (VALUES 1) W(X))",
        ]);
    }

    [JavaOracleFact]
    public void TheUniquePlansMatchJdbc()
    {
        ExplainOracleTests.Check([
            Setup,
            "EXPLAIN SELECT UNIQUE(SELECT A, B FROM TEST)",
            "EXPLAIN SELECT UNIQUE(SELECT DISTINCT A, B FROM TEST)",
            "EXPLAIN SELECT UNIQUE NULLS ALL DISTINCT(SELECT A, B FROM TEST)",
            "EXPLAIN SELECT UNIQUE NULLS DISTINCT(SELECT A, B FROM TEST)",
        ]);
    }
}
