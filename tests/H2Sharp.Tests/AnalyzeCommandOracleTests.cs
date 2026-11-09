/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 92: ANALYZE with data. The other suites only check that a malformed
 * SAMPLE_SIZE is refused; this drives the statement itself: a column with more
 * distinct values than Constants.SelectivityDistinctCount (10 000) resets the
 * counter mid-scan, a low-cardinality column forces the hash table to grow and
 * to see the same hash twice, and a bare ANALYZE walks every schema and table.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class AnalyzeCommandOracleTests
{
    [JavaOracleFact]
    public void AnalyzeCollectsSelectivity()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE TA(A INT, B INT, C INT)",
            // A: 20 000 distinct values, so the 10 000-count samples overflow and reset.
            // B: values that all share one hash bucket (mod 8), so the counter probes and
            // rehashes instead of filling clean slots.
            // C: one value against 20 000 rows — a selectivity that rounds down to zero.
            "INSERT INTO TA SELECT X, (X % 100) * 8 + 1, 7 FROM SYSTEM_RANGE(1, 20000)",
            // SAMPLE_SIZE 0 means every row, which is what the overflow needs.
            "ANALYZE TABLE TA SAMPLE_SIZE 0",
            // A table with no rows at all: every column ends at selectivity zero.
            "CREATE TABLE TE(A INT)",
            "ANALYZE TABLE TE",
            // A local temporary table is skipped by a bare ANALYZE.
            "CREATE LOCAL TEMPORARY TABLE TT(A INT)",
            // With no table named, every schema and table is analyzed.
            "ANALYZE",
            "SELECT COUNT(*) FROM TA",
            "DROP TABLE TT",
            "DROP TABLE TE",
            "DROP TABLE TA",
        ]);
    }
}
