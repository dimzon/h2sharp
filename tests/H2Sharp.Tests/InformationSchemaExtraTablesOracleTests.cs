/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The INFORMATION_SCHEMA tables the first catalogue suite did not query: domains,
 * constants, check constraints, enum element types, key column usage beyond a primary
 * key, index nulls-distinct flags, sequences, and the element/field metadata.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The remaining catalogue tables against the reference, over the same schema objects.
/// </summary>
/// <remarks>
/// Every table is read whole (<c>SELECT *</c>): a column the port declares and the
/// reference does not, or the other way round, changes the row width and shows up as a
/// difference instead of being silently ignored.
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class InformationSchemaExtraTablesOracleTests
{
    [JavaOracleFact]
    public void DomainsAndTheirConstraintsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE DOMAIN POSINT AS INT CHECK (VALUE > 0)",
            "CREATE DOMAIN EMAIL AS VARCHAR(100) DEFAULT 'none'",
            "SELECT * FROM INFORMATION_SCHEMA.DOMAINS WHERE DOMAIN_NAME IN ('POSINT', 'EMAIL') ORDER BY DOMAIN_NAME",
            "SELECT * FROM INFORMATION_SCHEMA.DOMAIN_CONSTRAINTS WHERE DOMAIN_NAME IN ('POSINT', 'EMAIL') ORDER BY CONSTRAINT_NAME",
            "CREATE TABLE DT(V POSINT, W EMAIL)",
            "SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'DT' ORDER BY ORDINAL_POSITION",
            "SELECT * FROM INFORMATION_SCHEMA.DOMAINS WHERE DOMAIN_NAME IN ('POSINT', 'EMAIL') AND IS_NULLABLE = 'NO'",
        ]);
    }

    [JavaOracleFact]
    public void ConstantsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE CONSTANT PI VALUE 3.14159",
            "CREATE CONSTANT GREETING VALUE 'hi'",
            "CREATE CONSTANT ANSWER VALUE 42",
            "SELECT * FROM INFORMATION_SCHEMA.CONSTANTS WHERE CONSTANT_NAME IN ('PI', 'GREETING', 'ANSWER') ORDER BY CONSTANT_NAME",
        ]);
    }

    [JavaOracleFact]
    public void CheckConstraintsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE CC(ID INT CHECK (ID > 0), N INT, CONSTRAINT N_POS CHECK (N >= 0))",
            "SELECT * FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS "
                + "WHERE CHECK_CLAUSE LIKE '%ID > 0%' OR CONSTRAINT_NAME = 'N_POS' ORDER BY CONSTRAINT_NAME",
            "SELECT * FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_NAME = 'CC' ORDER BY CONSTRAINT_NAME",
            // A CHECK constraint names the columns its expression reads.
            "SELECT * FROM INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE "
                + "WHERE CONSTRAINT_NAME = 'N_POS' ORDER BY COLUMN_NAME",
        ]);
    }

    [JavaOracleFact]
    public void KeyColumnUsageBeyondAPrimaryKeyMatchesJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE P(ID INT PRIMARY KEY)",
            "CREATE TABLE C2(ID INT PRIMARY KEY, P_ID INT, "
                + "CONSTRAINT C2_FK FOREIGN KEY (P_ID) REFERENCES P(ID), CONSTRAINT C2_U UNIQUE(P_ID))",
            "SELECT * FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE TABLE_NAME IN ('C2', 'P') "
                + "ORDER BY TABLE_SCHEMA, CONSTRAINT_NAME, ORDINAL_POSITION",
            "SELECT * FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_NAME = 'C2_FK'",
            "SELECT * FROM INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE WHERE TABLE_NAME IN ('C2', 'P') "
                + "ORDER BY CONSTRAINT_NAME, COLUMN_NAME",
            // A check constraint carries no key columns, and the view still walks it.
            "CREATE TABLE C3(N INT CHECK (N <> 0))",
            "SELECT * FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE TABLE_NAME = 'C3'",
            // A foreign key that names the second column of its unique constraint.
            "CREATE TABLE P3(A INT, B INT, CONSTRAINT P3_UB UNIQUE (B, A))",
            "CREATE TABLE C5(X INT, Y INT, CONSTRAINT C5_FK FOREIGN KEY (X, Y) REFERENCES P3(B, A))",
            "SELECT * FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE TABLE_NAME = 'C5' "
                + "ORDER BY CONSTRAINT_NAME, ORDINAL_POSITION",
        ]);
    }

    [JavaOracleFact]
    public void IndexNullsDistinctMatchesJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE IX(ID INT, N INT)",
            "CREATE UNIQUE NULLS DISTINCT INDEX IX_ND ON IX(N)",
            "CREATE UNIQUE NULLS NOT DISTINCT INDEX IX_NND ON IX(N)",
            "CREATE UNIQUE NULLS ALL DISTINCT INDEX IX_NAD ON IX(ID, N)",
            // INDEX_CLASS is the runtime class of the implementation: the reference answers
            // its Java class name, the port its .NET full name. The information they share is
            // the implementation family, so that is what the comparison reads.
            "SELECT INDEX_NAME, NULLS_DISTINCT, IS_GENERATED, "
                + "CASE WHEN INDEX_CLASS LIKE '%MVSecondaryIndex' THEN 'MVSecondaryIndex' "
                + "WHEN INDEX_CLASS LIKE '%MVPrimaryIndex' THEN 'MVPrimaryIndex' "
                + "WHEN INDEX_CLASS LIKE '%NonUniqueHashIndex' THEN 'NonUniqueHashIndex' "
                + "WHEN INDEX_CLASS LIKE '%ScanIndex' THEN 'ScanIndex' "
                + "ELSE INDEX_CLASS END AS INDEX_CLASS "
                + "FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'IX' ORDER BY INDEX_NAME",
            "SELECT * FROM INFORMATION_SCHEMA.INDEX_COLUMNS WHERE TABLE_NAME = 'IX' "
                + "ORDER BY INDEX_NAME, ORDINAL_POSITION",
        ]);
    }

    [JavaOracleFact]
    public void SequencesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE SEQUENCE S1 START WITH 10 INCREMENT BY 2 MINVALUE 5 MAXVALUE 100 CYCLE",
            "CREATE SEQUENCE S2",
            "SELECT * FROM INFORMATION_SCHEMA.SEQUENCES WHERE SEQUENCE_NAME IN ('S1', 'S2') ORDER BY SEQUENCE_NAME",
        ]);
    }

    [JavaOracleFact]
    public void SessionsCatalogNameAndSynonymsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "SELECT * FROM INFORMATION_SCHEMA.INFORMATION_SCHEMA_CATALOG_NAME",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SESSIONS",
            "SELECT USER_NAME, IS_ADMIN FROM INFORMATION_SCHEMA.SESSIONS",
            "CREATE TABLE T(ID INT PRIMARY KEY)",
            "CREATE SYNONYM SY FOR T",
            "SELECT * FROM INFORMATION_SCHEMA.SYNONYMS WHERE SYNONYM_NAME = 'SY'",
            "SELECT TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SY'",
        ]);
    }

    [JavaOracleFact]
    public void GeneratedAndOnUpdateColumnsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE DOMAIN D INT ON UPDATE 1",
            "CREATE TABLE GC(A INT, B INT DEFAULT 0 ON UPDATE A + 1, C INT GENERATED ALWAYS AS (A * 2), S D)",
            "SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'GC' ORDER BY ORDINAL_POSITION",
            "SELECT * FROM INFORMATION_SCHEMA.DOMAINS WHERE DOMAIN_NAME = 'D'",
        ]);
    }

    [JavaOracleFact]
    public void RichColumnTypesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE IT(I INTERVAL DAY(3) TO SECOND(2), J INTERVAL YEAR TO MONTH, "
                + "G GEOMETRY(POINT, 4326), E ENUM('a', 'b'))",
            "SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'IT' ORDER BY ORDINAL_POSITION",
            "SELECT * FROM INFORMATION_SCHEMA.ELEMENT_TYPES ORDER BY 1, 2, 3, 4",
            "SELECT * FROM INFORMATION_SCHEMA.ENUM_VALUES ORDER BY 1, 2, 3, 4",
            // An enum domain lists its values through the domain arm of the same view.
            "CREATE DOMAIN ED AS ENUM('x', 'y')",
            "SELECT * FROM INFORMATION_SCHEMA.ENUM_VALUES ORDER BY 1, 2, 3, 4",
            "SELECT * FROM INFORMATION_SCHEMA.DOMAINS WHERE DOMAIN_NAME = 'ED'",
        ]);
    }

    [JavaOracleFact]
    public void ElementTypesFieldsAndEnumValuesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE ET(X INT ARRAY, Y ROW(A INT, B VARCHAR))",
            "SELECT * FROM INFORMATION_SCHEMA.ELEMENT_TYPES ORDER BY 1, 2, 3, 4",
            "SELECT * FROM INFORMATION_SCHEMA.FIELDS ORDER BY 1, 2, 3, 4",
            "CREATE TABLE E(X ENUM('a', 'b'))",
            "SELECT * FROM INFORMATION_SCHEMA.ENUM_VALUES ORDER BY 1, 2, 3, 4",
            "INSERT INTO E VALUES ('a'), ('b'), ('a')",
            "SELECT X, COUNT(*) FROM E GROUP BY X ORDER BY X",
        ]);
    }
}
