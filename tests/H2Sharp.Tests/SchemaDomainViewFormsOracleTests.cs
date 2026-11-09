/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 111: the schema/domain/view forms — a view with its own column list, schema
 * create/rename/drop with cascade, the generated name of an unnamed UNIQUE constraint,
 * a domain with a CHECK, and assignment conversions of string values into numeric,
 * date, timestamp and boolean columns.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class SchemaDomainViewFormsOracleTests
{
    [JavaOracleFact]
    public void ViewColumnListSchemasAndDomainsMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE VIEW V(A, B) AS SELECT 1, 2",
            "SELECT * FROM V",
            "CREATE SCHEMA S2",
            "CREATE TABLE S2.T(A INT, B INT)",
            "CREATE UNIQUE INDEX UI ON S2.T(A)",
            "ALTER TABLE S2.T ADD UNIQUE(B)",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA = 'S2'",
            "ALTER SCHEMA S2 RENAME TO S3",
            "CREATE TABLE S3.T2(A INT)",
            "DROP TABLE S3.T2 CASCADE",
            "DROP SCHEMA S3 CASCADE",
            "CREATE DOMAIN D AS INT CHECK(VALUE > 0)",
            "CREATE TABLE DT(X D)",
            "INSERT INTO DT VALUES (5)",
            "INSERT INTO DT VALUES (-1)",
            "SELECT X FROM DT",
            "DROP TABLE DT",
        ]);
    }

    [JavaOracleFact]
    public void AssignmentsConvertFromStringAndTheColumnsReportTheirShape()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE C(A INT, B NUMERIC(10, 2), C DATE, D TIMESTAMP, E BOOLEAN)",
            "INSERT INTO C VALUES ('12', '1.50', '2020-01-02', '2020-01-02 03:04:05', 'TRUE')",
            "SELECT CAST(A AS VARCHAR), CAST(B AS VARCHAR), CAST(C AS VARCHAR), "
                + "CAST(D AS VARCHAR), CAST(E AS VARCHAR) FROM C",
            "SELECT COLUMN_NAME, TYPE_NAME, NUMERIC_PRECISION, NUMERIC_SCALE "
                + "FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'C' ORDER BY ORDINAL_POSITION",
            "DROP TABLE C",
        ]);
    }
}
