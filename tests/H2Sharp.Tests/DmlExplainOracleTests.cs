/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 219: the EXPLAIN forms of the data-changing statements — the reference's
 * parseExplain accepts DELETE, UPDATE, INSERT and MERGE next to a query, and each builds
 * its command through the statement object the parser read.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class DmlExplainOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void ExplainOfTheDataChangingStatements()
    {
        AssertSameLinesAsReference("dml_explain",
            "CREATE TABLE T(A INT PRIMARY KEY, B VARCHAR(10))",
            "INSERT INTO T VALUES (1, 'a'), (2, 'b')",
            "EXPLAIN UPDATE T SET B = 'c' WHERE A = 1",
            "EXPLAIN DELETE FROM T WHERE A = 2",
            "EXPLAIN INSERT INTO T VALUES (3, 'c')",
            "EXPLAIN INSERT INTO T(A) VALUES (4)",
            "EXPLAIN MERGE INTO T KEY(A) VALUES (1, 'x')",
            "EXPLAIN PLAN FOR UPDATE T SET B = 'd'",
            "DROP TABLE T");
    }
}
