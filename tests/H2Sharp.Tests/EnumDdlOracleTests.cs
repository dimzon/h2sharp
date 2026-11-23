/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Enum-typed columns carrying a UNIQUE constraint and a foreign key that references such a
 * column. The provider's scaffolding tests create exactly this shape; the oracle decides
 * whether the port understands the DDL or the test is asking for something the reference
 * does not support.
 */
using System;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class EnumDdlOracleTests : ParserCoverageOracleBase
{
    /// <summary>
    /// Enum columns, a UNIQUE constraint over one of them, and a foreign key referencing
    /// another unique column on the same table.
    /// </summary>
    [JavaOracleFact]
    public void Enum_column_with_unique_and_foreign_key_matches_the_reference()
        => AssertSameAsReference(
            "enum_ddl_" + Guid.NewGuid().ToString("N"),
            """
            CREATE TYPE "mood" AS ENUM ('happy', 'sad')
            """,
            """
            CREATE TABLE "foo" ("mood" "mood" UNIQUE, some_num int UNIQUE)
            """,
            """
            CREATE TABLE "bar" (foreign_key int REFERENCES "foo"(some_num))
            """,
            """
            INSERT INTO "foo" VALUES ('happy', 1)
            """,
            """
            SELECT "mood", some_num FROM "foo"
            """,
            """
            SELECT DOMAIN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.DOMAINS ORDER BY DOMAIN_NAME
            """);
}
