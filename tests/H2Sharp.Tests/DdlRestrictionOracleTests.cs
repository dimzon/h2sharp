/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 228: DROP SCHEMA ... RESTRICT has to see every object in the schema, not just tables.
 * The reference's own ddl/dropSchema.sql found that a synonym slipped through.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class DdlRestrictionOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void RestrictSeesSynonymsConstantsAndSequences()
    {
        AssertSameAsReference("ddl_restrict_objects",
            "CREATE TABLE SRC(ID INT)",
            "CREATE SCHEMA S",
            "CREATE SYNONYM S.SYN FOR SRC",
            "DROP SCHEMA S RESTRICT",
            "DROP SCHEMA S CASCADE",
            "CREATE SCHEMA S",
            "CREATE CONSTANT S.C VALUE 1",
            "DROP SCHEMA S RESTRICT",
            "DROP SCHEMA S CASCADE",
            "CREATE SCHEMA S",
            "CREATE SEQUENCE S.Q",
            "DROP SCHEMA S RESTRICT",
            "DROP SCHEMA S CASCADE",
            "CREATE SCHEMA S",
            "CREATE DOMAIN S.D AS INT",
            "DROP SCHEMA S RESTRICT",
            "DROP SCHEMA S CASCADE",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = 'S'");
    }

    [JavaOracleFact]
    public void RestrictSeesTablesAndCascadeRemovesThem()
    {
        AssertSameAsReference("ddl_restrict_tables",
            "CREATE SCHEMA S",
            "CREATE TABLE S.T(ID INT PRIMARY KEY, V INT UNIQUE)",
            "DROP SCHEMA S RESTRICT",
            "SELECT COUNT(*) FROM S.T",
            "DROP SCHEMA S CASCADE",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = 'S'");
    }
}
