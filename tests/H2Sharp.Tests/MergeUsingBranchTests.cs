/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 294: MERGE USING conformance with a composite ON condition whose index part (ID range)
 * is looser than the full predicate. The pending-missed-source flush itself needs a concurrent
 * row change and is covered by MergeUsingRaceTests; this case freezes the oracle result.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class MergeUsingBranchTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void RecheckFailureDefersAndFlushesTheSource()
    {
        AssertSameAsReferenceInMode("merge_using_missed_source", "Oracle",
            "CREATE TABLE P(ID INT, V INT)",
            "CREATE INDEX IDX_P_ID ON P(ID)",
            "INSERT INTO P VALUES (1, 10)",
            "MERGE INTO P USING (SELECT X AS ID FROM SYSTEM_RANGE(1, 3)) S "
            + "ON (P.ID >= S.ID AND P.V > 100) "
            + "WHEN MATCHED THEN UPDATE SET P.V = P.V "
            + "WHEN NOT MATCHED THEN INSERT (ID, V) VALUES (S.ID, S.ID)",
            "SELECT * FROM P ORDER BY ID, V");
    }
}
