/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 212: the PostgreSQL/MySQL compatibility statements and the view edges — SHOW of
 * every kind the parser builds, CREATE VIEW with a comment, REFRESH of a missing
 * materialized view, CREATE FORCE MATERIALIZED VIEW, ALTER INDEX/SCHEMA/VIEW renames
 * across schemas, DROP DOMAIN with IF EXISTS, the parameter binding in the statement text,
 * and the catalog-qualified three-part names.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ParserShowAndViewOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void ShowFormsAnswer()
    {
        AssertSameAsReference("psv_show",
            "CREATE SCHEMA S1",
            "CREATE TABLE T(ID INT PRIMARY KEY, V VARCHAR(10))",
            "INSERT INTO T VALUES (1, 'a')",
            "SHOW SEARCH_PATH",
            "SHOW SSL",
            "SHOW TABLES",
            "SHOW TABLES FROM PUBLIC",
            "SHOW COLUMNS FROM T",
            "SHOW COLUMNS FROM T FROM PUBLIC",
            "DROP TABLE T",
            "DROP SCHEMA S1");
    }

    [JavaOracleFact]
    public void ViewCommentsAndMaterializedViewEdges()
    {
        AssertSameAsReference("psv_views",
            "CREATE TABLE T(A INT)",
            "CREATE VIEW V COMMENT 'view' AS SELECT A FROM T",
            "SELECT REMARKS FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'V'",
            "REFRESH MATERIALIZED VIEW NOPE",
            "CREATE FORCE MATERIALIZED VIEW M AS SELECT A FROM T",
            "ALTER VIEW NOPE RECOMPILE",
            "DROP VIEW V",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void RenamesAcrossSchemasAreRefused()
    {
        AssertSameAsReference("psv_renames",
            "CREATE SCHEMA S1",
            "CREATE SCHEMA S2",
            "CREATE TABLE S1.T(A INT)",
            "CREATE INDEX I ON S1.T(A)",
            "CREATE VIEW S1.V AS SELECT A FROM S1.T",
            "ALTER INDEX S1.I RENAME TO S2.I2",
            "ALTER INDEX S1.I RENAME TO I2",
            "ALTER VIEW S1.V RENAME TO S2.V2",
            "ALTER VIEW S1.V RENAME TO V2",
            "ALTER SCHEMA NOPE RENAME TO X",
            "ALTER SCHEMA IF EXISTS NOPE RENAME TO X",
            "ALTER SCHEMA S1 RENAME TO S3",
            "DROP SCHEMA S2",
            "DROP SCHEMA S3");
    }

    [JavaOracleFact]
    public void DomainIfExistsAndParameterBinding()
    {
        AssertSameAsReference("psv_misc",
            "DROP DOMAIN IF EXISTS NOPE",
            "DROP DOMAIN NOPE",
            "SELECT ? {5: 1}",
            "SELECT ? {0: 42}",
            "SELECT ? {0: 42} + ? {1: 8}");
    }

    [JavaOracleFact]
    public void CatalogQualifiedThreePartNames()
    {
        // The database's own name drops out of a three-part name.
        AssertSameAsReference("psv_catalog",
            "CREATE TABLE T(A INT)",
            "INSERT INTO T VALUES (1)",
            "SELECT * FROM psv_catalog.PUBLIC.T",
            "SELECT A FROM psv_catalog.PUBLIC.T WHERE A = 1",
            "DROP TABLE T");
    }
}
