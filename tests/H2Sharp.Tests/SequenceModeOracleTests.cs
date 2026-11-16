/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 230: Oracle mode uses NUMERIC sequences; the cycle/restart paths and the duplicate
 * and missing-object errors are the remaining sequence branches.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class SequenceModeOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void OracleSequencesAreNumeric()
    {
        AssertSameAsReferenceInMode("seq_oracle", "Oracle",
            "CREATE SEQUENCE SEQ",
            "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.SEQUENCES WHERE SEQUENCE_NAME = 'SEQ'",
            "SELECT NEXT VALUE FOR SEQ",
            "ALTER SEQUENCE SEQ RESTART WITH 10",
            "SELECT NEXT VALUE FOR SEQ",
            "SELECT CURRENT VALUE FOR SEQ",
            "CREATE SEQUENCE SEQ",
            "DROP SEQUENCE SEQ");
    }

    [JavaOracleFact]
    public void CyclingAndErrorPaths()
    {
        AssertSameAsReference("seq_cycle",
            "ALTER SEQUENCE MISSING RESTART WITH 1",
            "CREATE SEQUENCE S2 START WITH 1 INCREMENT BY 1 MAXVALUE 2 CYCLE",
            "SELECT NEXT VALUE FOR S2",
            "SELECT NEXT VALUE FOR S2",
            "SELECT NEXT VALUE FOR S2",
            "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.SEQUENCES WHERE SEQUENCE_NAME = 'S2'",
            "ALTER SEQUENCE S2 RESTART WITH 5");
    }
}
