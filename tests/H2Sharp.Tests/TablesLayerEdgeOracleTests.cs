/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 213: the tables-layer edges — the IN lookups on a composite index and against
 * null bounds, the array-element errors of UPDATE, and the sequence options in the mode
 * whose sequences are decimal.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class TablesLayerEdgeOracleTests : ParserCoverageOracleBase
{
    private const string Setup =
        "CREATE TABLE T(A INT, B INT)"
        + ";CREATE INDEX IAB ON T(A, B)"
        + ";INSERT INTO T VALUES (1, 10), (2, 20), (3, 30), (NULL, NULL)";

    [JavaOracleFact]
    public void InLookupsOnACompositeIndex()
    {
        AssertSameAsReference("tle_in",
            Setup,
            "SELECT A, B FROM T WHERE A IN (1, 2, 3) AND B IN (10, 20)",
            "SELECT A, B FROM T WHERE (A, B) IN ((1, 10), (3, 30))",
            "SELECT A, B FROM T WHERE (A, B) IN (SELECT A, B FROM T WHERE A < 3)",
            "SELECT A, B FROM T WHERE (A, B) IN (1, 10)",
            "SELECT A, B FROM T WHERE (A, B) > (NULL, 1)",
            "SELECT A, B FROM T WHERE (A, B) > (1, NULL)",
            "SELECT A, B FROM T WHERE (A, B) = (NULL, NULL)",
            "SELECT A, B FROM T WHERE A IN ()",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void ArrayElementUpdateErrors()
    {
        AssertSameAsReference("tle_arrays",
            "CREATE TABLE T(ID INT, A INT ARRAY)",
            "INSERT INTO T VALUES (1, ARRAY[10, 20])",
            "UPDATE T SET A[1] = 11",
            "SELECT A[1] FROM T",
            "UPDATE T SET A[9] = 1",
            "UPDATE T SET ID[1] = 1",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void SequenceOptionsInTheDecimalMode()
    {
        AssertSameAsReferenceInMode("tle_seq", "MySQL",
            "CREATE SEQUENCE Q1",
            "SELECT NEXT VALUE FOR Q1",
            "CREATE SEQUENCE Q2 START WITH -5 MINVALUE -10 NO CYCLE",
            "SELECT NEXT VALUE FOR Q2",
            "CREATE SEQUENCE Q3 START WITH 5 INCREMENT BY 2 MAXVALUE 9 CYCLE",
            "SELECT NEXT VALUE FOR Q3",
            "SELECT NEXT VALUE FOR Q3",
            "SELECT NEXT VALUE FOR Q3",
            "SELECT NEXT VALUE FOR Q3",
            "ALTER SEQUENCE IF EXISTS NOPE RESTART WITH 1",
            "ALTER SEQUENCE Q1 RESTART WITH 100",
            "SELECT NEXT VALUE FOR Q1",
            "DROP SEQUENCE Q1, Q2, Q3");
    }

    [JavaOracleFact]
    public void RestartOfAColumnWithoutASequenceIsRefused()
    {
        AssertSameAsReference("tle_seq_col",
            "CREATE TABLE T(ID INT, V INT)",
            "ALTER TABLE T ALTER COLUMN ID RESTART WITH 5",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void ValuesInFromWithExpressions()
    {
        AssertSameAsReference("tle_values",
            "SELECT * FROM (VALUES (1 + 1))",
            "SELECT COUNT(*) FROM (VALUES (1), (2))",
            "SELECT X FROM (VALUES (1), (2)) V(X) WHERE X > 1");
    }
}
