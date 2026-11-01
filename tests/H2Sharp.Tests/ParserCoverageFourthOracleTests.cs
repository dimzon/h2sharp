/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Parser coverage (fourth wave): type spelling breadth, operators, session functions,
 * precision forms, comments on index/sequence, index options, schema-qualified names —
 * run through the ADO.NET layer and compared with the reference jar.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ParserCoverageFourthOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void TypeSpellingBreadth()
    {
        AssertSameAsReference("parser_fourth1",
            "CREATE TABLE T1(A INT, B INTEGER, C BIGINT, D SMALLINT, E TINYINT,"
            + " F BOOLEAN, G DECIMAL(8,3), H DEC(6), I NUMERIC, J DOUBLE PRECISION,"
            + " K FLOAT, L FLOAT(24), M REAL, N DATE, O TIME,"
            + " P TIME WITHOUT TIME ZONE, Q TIMESTAMP, R TIMESTAMP WITH TIME ZONE,"
            + " S CHARACTER(5), T CHAR VARYING(10), U CHARACTER VARYING(10),"
            + " V VARCHAR_IGNORECASE(20), W BLOB, X CLOB, Y BINARY(8), Z VARBINARY(8))",
            "SELECT COUNT(*) FROM T1",
            "DROP TABLE T1");
    }

    [JavaOracleFact]
    public void PrecisionForms()
    {
        AssertSameAsReference("parser_fourth2",
            "CREATE TABLE P1(A DECIMAL(10), B DECIMAL(10,2), C FLOAT(30), D TIME(3),"
            + " E TIMESTAMP(6), F INTERVAL DAY(3) TO SECOND(6))",
            "SELECT COUNT(*) FROM P1",
            "DROP TABLE P1");
    }

    [JavaOracleFact]
    public void OperatorsAndTests()
    {
        AssertSameAsReference("parser_fourth3",
            "SELECT CASE WHEN 1 = 1 THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 1 <> 2 THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 1 != 2 THEN 1 ELSE 0 END",
            "SELECT CASE WHEN NOT (1 = 1) THEN 1 ELSE 0 END",
            "SELECT CASE WHEN TRUE OR FALSE THEN 1 ELSE 0 END",
            "SELECT CASE WHEN TRUE AND FALSE THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 1 IS DISTINCT FROM 2 THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 1 IS NOT DISTINCT FROM 1 THEN 1 ELSE 0 END",
            "SELECT COALESCE(NULL, NULL, 3)",
            "SELECT IIF(1 > 0, 'y', 'n')",
            "SELECT CASE WHEN 1 = 1 THEN 'a' WHEN 1 = 2 THEN 'b' ELSE 'c' END",
            "SELECT CASE WHEN NULL IS NULL THEN 1 ELSE 0 END");
    }

    [JavaOracleFact]
    public void SessionFunctionsAndSchemaNames()
    {
        AssertSameAsReference("parser_fourth4",
            "SELECT DATABASE() FROM DUAL",
            "SELECT CURRENT_SCHEMA() FROM DUAL",
            "CREATE SCHEMA S1",
            "CREATE TABLE S1.T(SID INT)",
            "INSERT INTO S1.T VALUES(7)",
            "SELECT SID FROM S1.T",
            "DROP TABLE S1.T",
            "DROP SCHEMA S1");
    }

    [JavaOracleFact]
    public void IndexOptionsAndComments()
    {
        AssertSameAsReference("parser_fourth5",
            "CREATE TABLE P(ID INT, A INT, B INT)",
            "CREATE INDEX IX1 ON P(A DESC, B ASC)",
            "CREATE HASH INDEX IX2 ON P(A)",
            "CREATE INDEX IF NOT EXISTS IX1 ON P(A)",
            "SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME='P' ORDER BY INDEX_NAME",
            "COMMENT ON INDEX IX1 IS 'the index'",
            "DROP INDEX IX1",
            "DROP INDEX IF EXISTS IX1",
            "DROP INDEX IX2",
            "CREATE SEQUENCE SQ1",
            "COMMENT ON SEQUENCE SQ1 IS 'the sequence'",
            "DROP SEQUENCE SQ1",
            "DROP TABLE P");
    }

    [JavaOracleFact]
    public void MultiColumnConstraints()
    {
        AssertSameAsReference("parser_fourth6",
            "CREATE TABLE T(ID INT, A INT, B INT, PRIMARY KEY(ID), UNIQUE(A, B))",
            "INSERT INTO T VALUES(1, 10, 20)",
            "INSERT INTO T VALUES(1, 30, 40)",
            "CREATE TABLE PARENT(PID INT PRIMARY KEY)",
            "CREATE TABLE CHILD(CID INT PRIMARY KEY, PID INT,"
            + " FOREIGN KEY(PID) REFERENCES PARENT(PID) ON DELETE SET NULL)",
            "INSERT INTO PARENT VALUES(1)",
            "INSERT INTO CHILD VALUES(1, 1)",
            "DELETE FROM PARENT WHERE PID = 1",
            "SELECT PID FROM CHILD",
            "DROP TABLE CHILD, PARENT, T");
    }
}
