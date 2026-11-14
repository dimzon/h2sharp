/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 208: more parser edges — the T/TS/GEOMETRY/JSON literals, the PostgreSQL-mode
 * catalog-qualified names and SHOW ALL, the MySQL-mode REPLACE INTO and empty IN, the
 * parenthesized MERGE source, ROW(...) rows, the COMMENT ON refusals, ALTER DOMAIN
 * constraints across schemas, DROP SCHEMA refusals, sequence option edges, the ALTER
 * COLUMN identity commands through the statement model, and the function and interval
 * refusals.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ParserMoreEdgesOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void TypedLiteralKeywords()
    {
        AssertSameAsReference("pme_literals",
            "SELECT T '12:30:00'",
            "SELECT CAST(TS '2020-01-02 12:30:00' AS VARCHAR)",
            "SELECT CAST(JSON '{\"a\":1}' AS VARCHAR)",
            "SELECT CAST(JSON X'7B2261223A317D' AS VARCHAR)",
            "SELECT JSON 1",
            "SELECT GEOMETRY 'POINT (1 2)'",
            "SELECT CAST(TIME '12:30:00' AT LOCAL AS VARCHAR)",
            "SELECT 1 AT",
            "SELECT X.AT FROM (SELECT 1 AS AT) X");
    }

    [JavaOracleFact]
    public void PostgreSqlModeNamesAndShowAll()
    {
        AssertSameAsReferenceInMode("pme_pg", "PostgreSQL",
            "CREATE TABLE T(A INT)",
            "INSERT INTO T VALUES (1)",
            "SELECT pme_pg..T.A FROM T",
            "SELECT A FROM pme_pg.T",
            "SELECT OTHERDB..T.A FROM T",
            "COMMENT ON COLUMN pme_pg..T.A IS 'x'",
            "SELECT REMARKS FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'T'",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void MySqlModeReplaceAndEmptyIn()
    {
        AssertSameAsReferenceInMode("pme_mysql", "MySQL",
            "CREATE TABLE T(ID INT PRIMARY KEY, V VARCHAR(10))",
            "INSERT INTO T VALUES (1, 'a'), (2, 'b')",
            "REPLACE INTO T VALUES (1, 'c')",
            "SELECT V FROM T WHERE ID = 1",
            "REPLACE INTO T (ID, V) (SELECT 3, 'd')",
            "SELECT COUNT(*) FROM T",
            "SELECT 1 WHERE 1 IN ()",
            "REPLACE X",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void MergeQuerySourceAndRowValues()
    {
        AssertSameAsReference("pme_merge_row",
            "CREATE TABLE T(ID INT PRIMARY KEY, V VARCHAR(10))",
            "MERGE INTO T KEY(ID) (SELECT 1, 'a')",
            "SELECT V FROM T",
            "INSERT INTO T VALUES ROW(2, 'b'), ROW(3, 'c')",
            "SELECT COUNT(*) FROM T",
            "DROP TABLE T");
    }

    /// <summary>
    /// The setup of the domain-refusal facts: two schemas and a domain in the first.
    /// </summary>
    private static readonly string[] DomainSetup =
    [
        "CREATE SCHEMA S1",
        "CREATE SCHEMA S2",
        "CREATE DOMAIN S1.D INT",
    ];

    [JavaOracleFact]
    public void AlterDomainConstraintAcrossSchemasIsRefused()
    {
        AssertSameAsReference("pme_dom_add", [.. DomainSetup,
            "ALTER DOMAIN S1.D ADD CONSTRAINT S2.C CHECK (VALUE > 0)"]);
    }

    [JavaOracleFact]
    public void AlterDomainDropAcrossSchemasIsRefused()
    {
        AssertSameAsReference("pme_dom_drop", [.. DomainSetup,
            "ALTER DOMAIN S1.D DROP CONSTRAINT S2.C"]);
    }

    [JavaOracleFact]
    public void AlterDomainRenameConstraintToAnotherSchemaIsRefused()
    {
        AssertSameAsReference("pme_dom_renc", [.. DomainSetup,
            "ALTER DOMAIN S1.D ADD CONSTRAINT C1 CHECK (VALUE > 0)",
            "ALTER DOMAIN S1.D RENAME CONSTRAINT C1 TO S2.C2"]);
    }

    [JavaOracleFact]
    public void AlterDomainRenameToAnotherSchemaIsRefused()
    {
        AssertSameAsReference("pme_dom_ren", [.. DomainSetup,
            "ALTER DOMAIN S1.D RENAME TO S2.D2"]);
    }

    [JavaOracleFact]
    public void AlterDomainRenameInTheSameSchemaWorks()
    {
        AssertSameAsReference("pme_dom_ren_ok", [.. DomainSetup,
            "ALTER DOMAIN S1.D RENAME TO D2",
            "SELECT DOMAIN_NAME FROM INFORMATION_SCHEMA.DOMAINS WHERE DOMAIN_NAME = 'D2'"]);
    }

    [JavaOracleFact]
    public void DropSchemaRefusals()
    {
        AssertSameAsReference("pme_drop_schema",
            "CREATE SCHEMA S1",
            "DROP SCHEMA IF EXISTS NOPE",
            "DROP SCHEMA NOPE",
            "DROP SCHEMA S1.S2",
            "DROP SCHEMA S1");
    }

    [JavaOracleFact]
    public void DomainCascadeAndRestrictDrops()
    {
        AssertSameAsReference("pme_dom_cascade", [.. DomainSetup,
            "DROP DOMAIN S1.D CASCADE"]);
        AssertSameAsReference("pme_dom_restrict", [.. DomainSetup,
            "DROP DOMAIN S1.D RESTRICT",
            "DROP DOMAIN S1.D"]);
    }

    [JavaOracleFact]
    public void CommentOnUnknownKindIsRefused()
    {
        AssertSameAsReference("pme_comment", "COMMENT ON FOO T IS 'x'");
    }

    [JavaOracleFact]
    public void AlterColumnIdentityCommands()
    {
        AssertSameAsReference("pme_identity",
            "CREATE TABLE H(ID BIGINT GENERATED ALWAYS AS IDENTITY, V VARCHAR(10))",
            "INSERT INTO H(V) VALUES ('a')",
            "ALTER TABLE H ALTER COLUMN ID SET GENERATED BY DEFAULT",
            "INSERT INTO H VALUES (5, 'b')",
            "ALTER TABLE H ALTER COLUMN ID RESTART WITH 10",
            "INSERT INTO H(V) VALUES ('c')",
            "SELECT ID, V FROM H ORDER BY ID",
            "ALTER TABLE H ALTER COLUMN ID DROP IDENTITY",
            "ALTER TABLE NOPE ALTER COLUMN ID DROP IDENTITY",
            "ALTER TABLE H ALTER COLUMN NOPE DROP IDENTITY",
            "DROP TABLE H");
    }

    [JavaOracleFact]
    public void SequenceOptionEdges()
    {
        AssertSameAsReference("pme_sequence",
            "CREATE SEQUENCE Q1 INCREMENT BY 1 NO CYCLE",
            "SELECT NEXT VALUE FOR Q1",
            "CREATE SEQUENCE Q2 JUNK",
            "DROP SEQUENCE Q1");
    }

    [JavaOracleFact]
    public void FunctionAndIntervalRefusals()
    {
        AssertSameAsReference("pme_refusals",
            "SELECT NOSUCHFUNC(1)",
            "SELECT EXTRACT(YEAH FROM CURRENT_TIMESTAMP)",
            "SELECT SUBSTRING('abc' FROM 'b')",
            "SELECT A FROM T GROUP",
            "CREATE TABLE G(A INT)",
            "SELECT A FROM G GROUP BY JUNK HAVING 1 = 1",
            "DROP TABLE G");
    }
}
