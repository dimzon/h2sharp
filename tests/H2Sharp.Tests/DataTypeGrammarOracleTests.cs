/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The data type grammar: the character and national character spellings, the timestamp and
 * time zone forms, the interval qualifiers with their precision and scale, the geometry
 * qualifiers, ARRAY and ROW, the metadata each reports, and the precision and scale refusals.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class DataTypeGrammarOracleTests
{
    private const string ColumnMetadata =
        "SELECT COLUMN_NAME, DATA_TYPE, DATA_TYPE_SQL('PUBLIC', 'TEST', 'TABLE', DTD_IDENTIFIER) TYPE, "
        + "DATETIME_PRECISION, INTERVAL_TYPE, INTERVAL_PRECISION, GEOMETRY_TYPE, GEOMETRY_SRID "
        + "FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TEST' ORDER BY ORDINAL_POSITION";

    [JavaOracleFact]
    public void TheCharacterAndTimeSpellingsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE TEST("
                + "C1 CHAR VARYING(10), C2 CHAR LARGE OBJECT, C3 CHARACTER VARYING(10), "
                + "C4 CHARACTER LARGE OBJECT, C5 VARCHAR(10), C6 VARCHAR2(10), C7 CLOB, "
                + "N1 NATIONAL CHARACTER(5), N2 NATIONAL CHARACTER VARYING(5), "
                + "N3 NATIONAL CHARACTER LARGE OBJECT, N4 NATIONAL CHAR(5), "
                + "N5 NATIONAL CHAR VARYING(5), N6 NCHAR(5), N7 NCHAR VARYING(5), "
                + "N8 NCHAR LARGE OBJECT, N9 NCLOB)",
            ColumnMetadata,
            "DROP TABLE TEST",
            "CREATE TABLE TEST("
                + "T1 TIMESTAMP, T2 TIMESTAMP(6), T3 TIMESTAMP(6, 4), "
                + "T4 TIMESTAMP WITH TIME ZONE, T5 TIMESTAMP(3) WITH TIME ZONE, "
                + "T6 TIMESTAMP WITHOUT TIME ZONE, T7 TIMESTAMP(2) WITHOUT TIME ZONE, "
                + "M1 TIME, M2 TIME(3), M3 TIME(3) WITH TIME ZONE, M4 TIME WITHOUT TIME ZONE, "
                + "D1 DATE, D2 TIME WITH TIME ZONE)",
            ColumnMetadata,
            "DROP TABLE TEST",
        ]);
    }

    [JavaOracleFact]
    public void TheIntervalQualifiersMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE TEST("
                + "I01 INTERVAL YEAR, I02 INTERVAL MONTH, I03 INTERVAL DAY, I04 INTERVAL HOUR, "
                + "I05 INTERVAL MINUTE, I06 INTERVAL SECOND, I07 INTERVAL YEAR TO MONTH, "
                + "I08 INTERVAL DAY TO HOUR, I09 INTERVAL DAY TO MINUTE, I10 INTERVAL DAY TO SECOND, "
                + "I11 INTERVAL HOUR TO MINUTE, I12 INTERVAL HOUR TO SECOND, "
                + "I13 INTERVAL MINUTE TO SECOND)",
            "CREATE TABLE TEST("
                + "J01 INTERVAL YEAR(5), J02 INTERVAL MONTH(5), J03 INTERVAL DAY(5), "
                + "J04 INTERVAL HOUR(5), J05 INTERVAL MINUTE(5), J06 INTERVAL SECOND(5, 9), "
                + "J07 INTERVAL YEAR(5) TO MONTH, J08 INTERVAL DAY(5) TO HOUR, "
                + "J09 INTERVAL DAY(5) TO MINUTE, J10 INTERVAL DAY(5) TO SECOND(9), "
                + "J11 INTERVAL HOUR(5) TO MINUTE, J12 INTERVAL HOUR(5) TO SECOND(9), "
                + "J13 INTERVAL MINUTE(5) TO SECOND(9))",
            ColumnMetadata,
            "DROP TABLE TEST",
            // The literals each qualifier parses, read back.
            "CREATE TABLE TEST(ID INT, "
                + "I01 INTERVAL YEAR, I02 INTERVAL MONTH, I03 INTERVAL DAY, I04 INTERVAL HOUR, "
                + "I05 INTERVAL MINUTE, I06 INTERVAL SECOND, I07 INTERVAL YEAR TO MONTH, "
                + "I08 INTERVAL DAY TO HOUR, I09 INTERVAL DAY TO MINUTE, I10 INTERVAL DAY TO SECOND, "
                + "I11 INTERVAL HOUR TO MINUTE, I12 INTERVAL HOUR TO SECOND, "
                + "I13 INTERVAL MINUTE TO SECOND)",
            "INSERT INTO TEST VALUES (1, INTERVAL '1' YEAR, INTERVAL '1' MONTH, INTERVAL '1' DAY, "
                + "INTERVAL '1' HOUR, INTERVAL '1' MINUTE, INTERVAL '1.123456789' SECOND, "
                + "INTERVAL '1-2' YEAR TO MONTH, INTERVAL '1 2' DAY TO HOUR, "
                + "INTERVAL '1 2:3' DAY TO MINUTE, INTERVAL '1 2:3:4.123456789' DAY TO SECOND, "
                + "INTERVAL '1:2' HOUR TO MINUTE, INTERVAL '1:2:3.123456789' HOUR TO SECOND, "
                + "INTERVAL '1:2.123456789' MINUTE TO SECOND)",
            "SELECT I01, I02, I03, I04, I05, I06 FROM TEST",
            "SELECT I07, I08, I09, I10 FROM TEST",
            "SELECT I11, I12, I13 FROM TEST",
            "DROP TABLE TEST",
            // Refusals: an unknown pair, a bad precision, a bad scale.
            "CREATE TABLE TEST(A INTERVAL DAY TO YEAR)",
            "CREATE TABLE TEST(A INTERVAL YEAR(0))",
            "CREATE TABLE TEST(A INTERVAL SECOND(5, 10))",
        ]);
    }

    [JavaOracleFact]
    public void TheGeometryQualifiersMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE TEST(G GEOMETRY, G_S GEOMETRY(GEOMETRY, 1), P GEOMETRY(POINT), "
                + "P_S GEOMETRY(POINT, 1), PZ1 GEOMETRY(POINT Z), PZ2 GEOMETRY(POINTZ), "
                + "PZ1_S GEOMETRY(POINT Z, 1), PZ2_S GEOMETRY(POINTZ, 1), PM GEOMETRY(POINT M), "
                + "PZM GEOMETRY(POINT ZM), PZM_S GEOMETRY(POINT ZM, -100), "
                + "LS GEOMETRY(LINESTRING), PG GEOMETRY(POLYGON), MP GEOMETRY(MULTIPOINT), "
                + "MLS GEOMETRY(MULTILINESTRING), MPG GEOMETRY(MULTIPOLYGON), "
                + "GC GEOMETRY(GEOMETRYCOLLECTION))",
            ColumnMetadata,
            "DROP TABLE TEST",
            // The geometry type name and dimension system refusals.
            "CREATE TABLE TEST(G GEOMETRY(BADTYPE))",
            "CREATE TABLE TEST(G GEOMETRY(POINT BAD))",
            "CREATE TABLE TEST(G GEOMETRY(1))",
            "CREATE TABLE TEST(G GEOMETRY(POINT, 1, 2))",
        ]);
    }

    [JavaOracleFact]
    public void TheArrayAndRowTypesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE TEST(A INT ARRAY, B INT ARRAY[3], C VARCHAR(10) ARRAY, "
                + "D INT ARRAY[3] ARRAY[2])",
            "SELECT COLUMN_NAME, DATA_TYPE, DATA_TYPE_SQL('PUBLIC', 'TEST', 'TABLE', DTD_IDENTIFIER) "
                + "FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TEST' ORDER BY ORDINAL_POSITION",
            "DROP TABLE TEST",
            // ROW types, their metadata and the duplicate field refusal.
            "CREATE TABLE TEST(R ROW(A INT, B VARCHAR(10)), S ROW(A INT, B ROW(C INT)))",
            "SELECT COLUMN_NAME, DATA_TYPE, DATA_TYPE_SQL('PUBLIC', 'TEST', 'TABLE', DTD_IDENTIFIER) "
                + "FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TEST' ORDER BY ORDINAL_POSITION",
            "DROP TABLE TEST",
            "CREATE TABLE TEST(R ROW(A INT, A INT))",
            "CREATE TABLE TEST(R ROW())",
            // The ARRAY keyword where a data type starts is refused in Regular mode.
            "CREATE TABLE TEST(A ARRAY)",
            "CREATE TABLE TEST(A ARRAY INT)",
        ]);
    }

    [JavaOracleFact]
    public void ThePrecisionAndScaleRefusalsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE TEST(A VARCHAR(0))",
            "CREATE TABLE TEST(A CHAR(0))",
            "CREATE TABLE TEST(A BINARY(0))",
            "CREATE TABLE TEST(A VARBINARY(0))",
            "CREATE TABLE TEST(A VARCHAR(1000000001))",
            "CREATE TABLE TEST(A CHAR(1000000001))",
            "CREATE TABLE TEST(A NUMERIC(0))",
            "CREATE TABLE TEST(A NUMERIC(100001))",
            "CREATE TABLE TEST(A NUMERIC(2, -1))",
            "CREATE TABLE TEST(A NUMERIC(2, 100001))",
            "CREATE TABLE TEST(A DECIMAL(0))",
            "CREATE TABLE TEST(A DECFLOAT(0))",
            "CREATE TABLE TEST(A DECFLOAT(100001))",
            "CREATE TABLE TEST(A TIMESTAMP(10))",
            "CREATE TABLE TEST(A TIMESTAMP(-1))",
            "CREATE TABLE TEST(A TIME(10))",
            "CREATE TABLE TEST(A TIMESTAMP WITH TIME ZONE(10))",
            "CREATE TABLE TEST(A INTERVAL DAY(10))",
            "CREATE TABLE TEST(A INTERVAL DAY(0))",
            // A name that is not a type is a domain lookup, and there is no such domain.
            "CREATE TABLE TEST(A NOSUCHDOMAIN)",
            "CREATE TABLE TEST(A PUBLIC.NOSUCHDOMAIN)",
            "CREATE TABLE TEST(A NOSUCHSCHEMA.NOSUCHDOMAIN)",
            "CREATE TABLE TEST(A PG_CATALOG.NOSUCHDOMAIN)",
        ]);
    }
}
