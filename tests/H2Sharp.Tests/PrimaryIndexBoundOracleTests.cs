/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 90: the bounds of the row-id index. A predicate on a numeric primary key is
 * turned into the long row key before the walk starts, and each type has its own edge:
 * NaN, the DECFLOAT infinities, and the out-of-range NUMERIC values. A predicate on
 * _ROWID_ itself reaches the key branch, where the bound is the key as it is.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class PrimaryIndexBoundOracleTests
{
    [JavaOracleFact]
    public void NumericPrimaryKeyBoundsMatchJdbc()
    {
        // Counted rather than selected: the row count is what shows whether the bound
        // emptied the walk or opened it, and it prints the same on both sides (the JDK
        // prints a DOUBLE 2.0 while the ADO projection hands back a CLR double 2).
        ExplainOracleTests.Check([
            "CREATE TABLE TD(ID DECFLOAT PRIMARY KEY)",
            "INSERT INTO TD VALUES (1), (2)",
            // -Infinity opens the lower bound; +Infinity empties the walk.
            "SELECT COUNT(*) FROM TD WHERE ID > CAST('-Infinity' AS DECFLOAT)",
            "SELECT COUNT(*) FROM TD WHERE ID > CAST('Infinity' AS DECFLOAT)",
            "SELECT COUNT(*) FROM TD WHERE ID < CAST('Infinity' AS DECFLOAT)",
            "SELECT COUNT(*) FROM TD WHERE ID < CAST('-Infinity' AS DECFLOAT)",
            "DROP TABLE TD",
            "CREATE TABLE TF(ID DOUBLE PRECISION PRIMARY KEY)",
            "INSERT INTO TF VALUES (1), (2)",
            "SELECT COUNT(*) FROM TF WHERE ID > CAST('NaN' AS DOUBLE)",
            "SELECT COUNT(*) FROM TF WHERE ID > 1.5",
            // A NaN upper bound is no bound at all on the last side.
            "SELECT COUNT(*) FROM TF WHERE ID < CAST('NaN' AS DOUBLE)",
            "DROP TABLE TF",
            "CREATE TABLE TR(ID REAL PRIMARY KEY)",
            "INSERT INTO TR VALUES (1), (2)",
            "SELECT COUNT(*) FROM TR WHERE ID < CAST('NaN' AS REAL)",
            "DROP TABLE TR",
        ]);
    }

    /// <summary>
    /// _ROWID_ is the key itself: the bound carries a key, not a column value, so the
    /// cursor takes it without a type conversion. The explicit row id also leaves the
    /// other columns at their defaults.
    /// </summary>
    [JavaOracleFact]
    public void RowidBoundsMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE T(A INT)",
            "INSERT INTO T VALUES (1), (2), (3)",
            "SELECT A FROM T WHERE _ROWID_ > 1 ORDER BY A",
            "INSERT INTO T(_ROWID_) VALUES (100)",
            "SELECT A FROM T WHERE _ROWID_ > 1 ORDER BY A",
            "DROP TABLE T",
        ]);
    }
}
