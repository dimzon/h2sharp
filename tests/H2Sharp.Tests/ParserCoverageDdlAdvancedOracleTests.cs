/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Parser coverage (advanced DDL): table options, drops, domains, synonyms, set
 * statements, compatibility-mode DDL — run through the ADO.NET layer and compared with
 * the reference jar.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ParserCoverageDdlAdvancedOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void TableOptionsAndDrops()
    {
        AssertSameAsReference("parser_ddladv1",
            "CREATE MEMORY TABLE M1(ID INT)",
            "CREATE CACHED TABLE C1(ID INT)",
            "CREATE TABLE IF NOT EXISTS M1(ID INT)",
            "CREATE TEMPORARY TABLE T1(ID INT)",
            "CREATE GLOBAL TEMPORARY TABLE G1(ID INT)",
            "CREATE TABLE AS1 AS SELECT 1 AS X",
            "SELECT X FROM AS1",
            "CREATE TABLE AS2(X INT) AS SELECT 1",
            "SELECT X FROM AS2",
            "CREATE TABLE AS3 AS SELECT 1 AS X WITH NO DATA",
            "SELECT COUNT(*) FROM AS3",
            "CREATE TABLE CM1(ID INT PRIMARY KEY) COMMENT 'hello'",
            "SELECT REMARKS FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='CM1'",
            "DROP TABLE IF EXISTS NO_SUCH_TABLE",
            "DROP TABLE IF EXISTS M1, C1",
            "DROP TABLE T1, G1",
            "DROP TABLE AS1 RESTRICT",
            "DROP TABLE AS2, AS3 CASCADE",
            "DROP TABLE CM1");
    }

    [JavaOracleFact]
    public void DomainsAndSynonyms()
    {
        AssertSameAsReference("parser_ddladv2",
            "CREATE DOMAIN POSITIVE_INT AS INT CHECK (VALUE > 0)",
            "CREATE TABLE DT1(X POSITIVE_INT)",
            "INSERT INTO DT1 VALUES(5)",
            "SELECT X FROM DT1",
            "INSERT INTO DT1 VALUES(-1)",
            "ALTER DOMAIN POSITIVE_INT RENAME TO POSINT",
            "ALTER DOMAIN POSINT SET DEFAULT 2",
            "INSERT INTO DT1 VALUES(7)",
            "SELECT X FROM DT1 ORDER BY X",
            "ALTER DOMAIN POSINT DROP DEFAULT",
            "DROP DOMAIN POSINT",
            "CREATE TABLE SRC(ID INT)",
            "CREATE SYNONYM SSRC FOR SRC",
            "INSERT INTO SRC VALUES(1)",
            "SELECT ID FROM SSRC",
            "CREATE TABLE SRC2(ID INT)",
            "CREATE SYNONYM IF NOT EXISTS SSRC FOR SRC2",
            "DROP SYNONYM SSRC",
            "DROP TABLE SRC, SRC2");
    }

    [JavaOracleFact]
    public void SetStatements()
    {
        AssertSameAsReference("parser_ddladv3",
            "SET LOCK_TIMEOUT 5000",
            "SELECT SETTING_VALUE FROM INFORMATION_SCHEMA.SETTINGS WHERE SETTING_NAME='LOCK_TIMEOUT'",
            "SET MAX_MEMORY_ROWS 100",
            "SELECT SETTING_VALUE FROM INFORMATION_SCHEMA.SETTINGS WHERE SETTING_NAME='MAX_MEMORY_ROWS'",
            "SET DEFAULT_TABLE_TYPE 0",
            "SELECT SETTING_VALUE FROM INFORMATION_SCHEMA.SETTINGS WHERE SETTING_NAME='DEFAULT_TABLE_TYPE'",
            "SET QUERY_TIMEOUT 3",
            "SELECT SETTING_VALUE FROM INFORMATION_SCHEMA.SETTINGS WHERE SETTING_NAME='QUERY_TIMEOUT'",
            "SET TRUNCATE_LARGE_LENGTH FALSE",
            "SELECT SETTING_VALUE FROM INFORMATION_SCHEMA.SETTINGS WHERE SETTING_NAME='TRUNCATE_LARGE_LENGTH'",
            "SET LOCK_MODE 1",
            "SET WRITE_DELAY 10",
            "SET MAX_LENGTH_INPLACE_LOB 64",
            "SET SCHEMA PUBLIC",
            "SET TIME ZONE 'UTC'",
            "SET SESSION CHARACTERISTICS AS TRANSACTION ISOLATION LEVEL READ COMMITTED");
    }

    [JavaOracleFact]
    public void OracleModeDdl()
    {
        AssertSameAsReferenceInMode("parser_ddladv4", "Oracle",
            "CREATE TABLE OT(C INT NOT NULL ENABLE)",
            "CREATE TABLE OT2(C INT NOT NULL DISABLE)",
            "INSERT INTO OT2 VALUES(NULL)",
            "SELECT COUNT(*) FROM OT2",
            "ALTER TABLE OT MODIFY C NOT NULL ENABLE VALIDATE",
            "ALTER TABLE OT MODIFY C NULL ENABLE NOVALIDATE",
            "DROP TABLE OT, OT2");
    }

    [JavaOracleFact]
    public void SqlServerModeDdl()
    {
        AssertSameAsReferenceInMode("parser_ddladv5", "MSSQLServer",
            "CREATE TABLE ST(ID INT PRIMARY KEY IDENTITY)",
            "INSERT INTO ST DEFAULT VALUES",
            "SELECT ID FROM ST",
            "CREATE TABLE ST2(ID INT PRIMARY KEY NOT NULL IDENTITY)",
            "DROP TABLE ST, ST2",
            "CREATE TABLE SRC(ID INT)",
            "INSERT INTO SRC VALUES(1)",
            "SELECT * FROM SRC WITH(NOLOCK)",
            "SELECT * FROM SRC WITH(NOLOCK, INDEX(ID))",
            "DROP TABLE SRC");
    }
}
