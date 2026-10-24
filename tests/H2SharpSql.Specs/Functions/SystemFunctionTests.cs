// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/functions.html — system functions, one test per checklist row.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class SystemFunctionTests
{
    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
    }

    /// <summary>docs/functions.html:3201 — ABORT_SESSION(sessionInt)</summary>
    [Fact]
    public void AbortSession_UnknownSession_IsFalse() // FUN-3201
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.False(connection.QuerySingle<bool>("SELECT ABORT_SESSION(SESSION_ID() + 1000000)"));
    }

    /// <summary>docs/functions.html:3363 — AUTOCOMMIT()</summary>
    [Fact]
    public void Autocommit_IsOnByDefault() // FUN-3363
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT AUTOCOMMIT()"));
    }

    /// <summary>docs/functions.html:3381 — CANCEL_SESSION(sessionInt)</summary>
    [Fact]
    public void CancelSession_UnknownSession_IsFalse() // FUN-3381
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.False(connection.QuerySingle<bool>("SELECT CANCEL_SESSION(SESSION_ID() + 1000000)"));
    }

    /// <summary>docs/functions.html:3399 — CASEWHEN(boolean, aValue, bValue)</summary>
    [Fact]
    public void CaseWhen_PicksTheBranch() // FUN-3399
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("a", connection.QuerySingle<string>("SELECT CASEWHEN(TRUE, 'a', 'b')"));
    }

    /// <summary>docs/functions.html:3417 — { COALESCE | { NVL } } (aValue, bValue [,...])</summary>
    [Fact]
    public void Coalesce_ReturnsTheFirstNonNull() // FUN-3417
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("x", connection.QuerySingle<string>("SELECT COALESCE(NULL, 'x')"));
        Assert.Equal("x", connection.QuerySingle<string>("SELECT NVL(NULL, 'x')"));
    }

    /// <summary>docs/functions.html:3418 — | IFNULL(aValue, bValue)</summary>
    [Fact]
    public void IfNull_ReturnsTheFallback() // FUN-3418
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("x", connection.QuerySingle<string>("SELECT IFNULL(NULL, 'x')"));
    }

    /// <summary>docs/functions.html:3437 — CONVERT(value, dataTypeOrDomain)</summary>
    [Fact]
    public void Convert_ChangesTheDataType() // FUN-3437
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(42, connection.QuerySingle<int>("SELECT CONVERT('42', INT)"));
    }

    /// <summary>docs/functions.html:3455 — CURRVAL( [ schemaNameString, ] sequenceString )</summary>
    [Fact]
    public void Currval_ReturnsTheLastGeneratedValue() // FUN-3455
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE TEST_SEQ");
        _ = connection.ExecuteScalar<long>("SELECT NEXT VALUE FOR TEST_SEQ");

        Assert.Equal(1L, connection.QuerySingle<long>("SELECT CURRVAL('TEST_SEQ')"));
    }

    /// <summary>docs/functions.html:3473 — CSVWRITE ( fileNameString, queryString [, csvOptions [, lineSepString] ] )</summary>
    [Fact]
    [Trait("Category", "BlockedByPort")]
    public void CsvWrite_WritesTheFile() // FUN-3473
    {
        // Blocked by the port: needs the CALL statement plus CSV writing with file I/O.
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1L, connection.QuerySingle<long>("CALL CSVWRITE('test.csv', 'SELECT 1 AS ID')"));
        Assert.Equal(1L, connection.QuerySingle<long>(
            "CALL CSVWRITE('test2.csv', 'SELECT 1 AS ID', 'UTF-8,|')"));
    }

    /// <summary>docs/functions.html:3491 — CURRENT_SCHEMA | SCHEMA()</summary>
    [Fact]
    public void CurrentSchema_IsPublicByDefault() // FUN-3491
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("PUBLIC", connection.QuerySingle<string>("SELECT CURRENT_SCHEMA"));
        Assert.Equal("PUBLIC", connection.QuerySingle<string>("SELECT SCHEMA()"));
    }

    /// <summary>docs/functions.html:3509 — CURRENT_CATALOG | DATABASE()</summary>
    [Fact]
    public void CurrentCatalog_MatchesDatabaseFunction() // FUN-3509
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(connection.QuerySingle<string>("SELECT CURRENT_CATALOG"),
            connection.QuerySingle<string>("SELECT DATABASE()"));
        Assert.NotNull(connection.QuerySingle<string>("SELECT CURRENT_CATALOG"));
    }

    /// <summary>docs/functions.html:3527 — DATABASE_PATH()</summary>
    [Fact]
    public void DatabasePath_IsAnExpression() // FUN-3527
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT DATABASE_PATH() IS NULL OR DATABASE_PATH() IS NOT NULL"));
    }

    /// <summary>docs/functions.html:3545 — DATA_TYPE_SQL</summary>
    [Fact]
    [Trait("Category", "BlockedByPort")]
    public void DataTypeSql_ForDomain_ReturnsTheSqlType() // FUN-3545
    {
        // Blocked by the port: CREATE DOMAIN is not ported, so there is no domain to ask.
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT");

        Assert.Equal("INTEGER",
            connection.QuerySingle<string>("SELECT DATA_TYPE_SQL('PUBLIC', 'D', 'DOMAIN', 'TYPE')"));
    }

    /// <summary>docs/functions.html:3546 — (objectSchemaString, objectNameString, objectTypeString, typeIdentifierString)</summary>
    [Fact]
    public void DataTypeSql_ForTableColumn_ReturnsTheSqlType() // FUN-3546
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        // The column is addressed by its 1-based ordinal, as the reference requires
        // (verified against H2 2.5.259: a name answers NULL).
        Assert.Equal("INTEGER",
            connection.QuerySingle<string>("SELECT DATA_TYPE_SQL('PUBLIC', 'TEST', 'TABLE', '1')"));
    }

    /// <summary>docs/functions.html:3565 — DB_OBJECT_ID({{'ROLE'|'SETTING'|'SCHEMA'|'USER'}, objectNameString</summary>
    [Fact]
    public void DbObjectId_ForSchema_ReturnsTheId() // FUN-3565
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT DB_OBJECT_ID('SCHEMA', 'PUBLIC') > 0"));
    }

    /// <summary>docs/functions.html:3566 — | {'CONSTANT'|'CONSTRAINT'|'DOMAIN'|'INDEX'|'ROUTINE'|'SEQUENCE'</summary>
    [Fact]
    public void DbObjectId_ForTable_ReturnsTheId() // FUN-3566
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.True(connection.QuerySingle<bool>("SELECT DB_OBJECT_ID('TABLE', 'PUBLIC', 'TEST') > 0"));
    }

    /// <summary>docs/functions.html:3567 — |'SYNONYM'|'TABLE'|'TRIGGER'}, schemaNameString, objectNameString })</summary>
    [Fact]
    public void DbObjectId_ForSequence_ReturnsTheId() // FUN-3567
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ");

        Assert.True(connection.QuerySingle<bool>("SELECT DB_OBJECT_ID('SEQUENCE', 'PUBLIC', 'SEQ') > 0"));
    }

    /// <summary>docs/functions.html:3587 — DB_OBJECT_SQL({{'ROLE'|'SETTING'|'SCHEMA'|'USER'}, objectNameString</summary>
    [Fact]
    [Trait("Category", "BlockedByPort")]
    public void DbObjectSql_ForSchema_ReturnsTheDefinition() // FUN-3587
    {
        // Blocked by the port: rendering a schema needs its owner (a user), and users
        // are not ported (Schema.getCreateSQL names the owner explicitly).
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SCHEMA S2");

        Assert.True(connection.QuerySingle<bool>("SELECT DB_OBJECT_SQL('SCHEMA', 'S2') IS NOT NULL"));
    }

    /// <summary>docs/functions.html:3588 — | {'CONSTANT'|'CONSTRAINT'|'DOMAIN'|'INDEX'|'ROUTINE'|'SEQUENCE'</summary>
    [Fact]
    public void DbObjectSql_ForTable_ReturnsTheDefinition() // FUN-3588
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.True(connection.QuerySingle<bool>("SELECT DB_OBJECT_SQL('TABLE', 'PUBLIC', 'TEST') IS NOT NULL"));
    }

    /// <summary>docs/functions.html:3589 — |'SYNONYM'|'TABLE'|'TRIGGER'}, schemaNameString, objectNameString })</summary>
    [Fact]
    public void DbObjectSql_ForSequence_ReturnsTheDefinition() // FUN-3589
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ");

        Assert.True(connection.QuerySingle<bool>("SELECT DB_OBJECT_SQL('SEQUENCE', 'PUBLIC', 'SEQ') IS NOT NULL"));
    }

    /// <summary>docs/functions.html:3609 — DB_OBJECT_SIZE({'INDEX'|'TABLE'}, schemaNameString, objectNameString)</summary>
    [Fact]
    public void DbObjectSize_ReturnsTheNumberOfBytes() // FUN-3609
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.True(connection.QuerySingle<bool>("SELECT DB_OBJECT_SIZE('TABLE', 'PUBLIC', 'TEST') >= 0"));
    }

    /// <summary>docs/functions.html:3627 — DB_OBJECT_TOTAL_SIZE('TABLE', schemaNameString, objectNameString)</summary>
    [Fact]
    public void DbObjectTotalSize_IncludesTheIndexes() // FUN-3627
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.True(connection.QuerySingle<bool>("SELECT DB_OBJECT_TOTAL_SIZE('TABLE', 'PUBLIC', 'TEST') >= 0"));
    }

    /// <summary>docs/functions.html:3645 — DB_OBJECT_APPROXIMATE_SIZE({'INDEX'|'TABLE'}, schemaNameString, objectNameString)</summary>
    [Fact]
    public void DbObjectApproximateSize_ReturnsTheNumberOfBytes() // FUN-3645
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.True(connection.QuerySingle<bool>(
            "SELECT DB_OBJECT_APPROXIMATE_SIZE('TABLE', 'PUBLIC', 'TEST') >= 0"));
    }

    /// <summary>docs/functions.html:3663 — DB_OBJECT_APPROXIMATE_TOTAL_SIZE('TABLE', schemaNameString, objectNameString)</summary>
    [Fact]
    public void DbObjectApproximateTotalSize_IncludesTheIndexes() // FUN-3663
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.True(connection.QuerySingle<bool>(
            "SELECT DB_OBJECT_APPROXIMATE_TOTAL_SIZE('TABLE', 'PUBLIC', 'TEST') >= 0"));
    }

    /// <summary>docs/functions.html:3681 — DECODE(value, whenValue, thenValue [,...])</summary>
    [Fact]
    public void Decode_ReturnsTheFirstMatch() // FUN-3681
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("two", connection.QuerySingle<string>("SELECT DECODE(2, 1, 'one', 2, 'two')"));
        Assert.Equal("other", connection.QuerySingle<string>("SELECT DECODE(3, 1, 'one', 2, 'two', 'other')"));
        Assert.True(connection.QuerySingle<bool>("SELECT DECODE(3, 1, 'one', 2, 'two') IS NULL"));
    }

    /// <summary>docs/functions.html:3699 — DISK_SPACE_USED(tableNameString)</summary>
    [Fact]
    public void DiskSpaceUsed_ReturnsANumber() // FUN-3699
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.True(connection.QuerySingle<bool>("SELECT DISK_SPACE_USED('TEST') >= 0"));
    }

    /// <summary>docs/functions.html:3717 — SIGNAL(sqlStateString, messageString)</summary>
    [Fact]
    public void Signal_RaisesAnError() // FUN-3717
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = Assert.ThrowsAny<Exception>(() => connection.Execute("CALL SIGNAL('45000', 'boom')"));
    }

    /// <summary>docs/functions.html:3735 — ESTIMATED_ENVELOPE(tableNameString, columnNameString)</summary>
    [Fact]
    [Trait("Category", "BlockedByPort")]
    public void EstimatedEnvelope_ReturnsTheBoundingBox() // FUN-3735
    {
        // Blocked by the port: the envelope is read off a spatial index, and CREATE
        // SPATIAL INDEX is not ported.
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT, G GEOMETRY)");
        _ = connection.Execute("CREATE SPATIAL INDEX IDX_G ON TEST(G)");
        _ = connection.Execute("INSERT INTO TEST(ID, G) VALUES (1, CAST('POINT(1 2)' AS GEOMETRY))");

        Assert.True(connection.QuerySingle<bool>("SELECT ESTIMATED_ENVELOPE('TEST', 'G') IS NOT NULL"));
    }

    /// <summary>docs/functions.html:3753 — FILE_READ(fileNameString [,encodingString])</summary>
    [Fact]
    [Trait("Category", "BlockedByPort")]
    public void FileRead_ReadsTheWrittenFile() // FUN-3753
    {
        // Blocked by the port: file functions build LOBs through the LOB storage,
        // which is not ported.
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("SELECT FILE_WRITE('Hello world', 'test.txt')");

        Assert.Equal(11, connection.QuerySingle<int>("SELECT OCTET_LENGTH(FILE_READ('test.txt'))"));
        Assert.Equal(11, connection.QuerySingle<int>("SELECT CHAR_LENGTH(FILE_READ('test.txt', 'UTF-8'))"));
    }

    /// <summary>docs/functions.html:3771 — FILE_WRITE(blobValue, fileNameString)</summary>
    [Fact]
    [Trait("Category", "BlockedByPort")]
    public void FileWrite_ReportsTheBytesWritten() // FUN-3771
    {
        // Blocked by the port: same LOB storage gap as FILE_READ above.
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(11L, connection.QuerySingle<long>("SELECT FILE_WRITE('Hello world', 'test.txt')"));
    }

    /// <summary>docs/functions.html:3789 — GREATEST(aValue, bValue [,...]) [{RESPECT|IGNORE} NULLS]</summary>
    [Fact]
    public void Greatest_WithAndWithoutNulls() // FUN-3789
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>("SELECT GREATEST(1, 2, 3)"));
        Assert.True(connection.QuerySingle<bool>("SELECT GREATEST(1, NULL) IS NULL"));
        // IGNORE NULLS trails the call, as the grammar documents; inside the parentheses
        // both engines refuse it (verified against H2 2.5.259).
        Assert.Equal(3, connection.QuerySingle<int>("SELECT GREATEST(1, NULL, 3) IGNORE NULLS"));
    }

    /// <summary>docs/functions.html:3807 — LEAST(aValue, bValue [,...]) [{RESPECT|IGNORE} NULLS]</summary>
    [Fact]
    public void Least_WithAndWithoutNulls() // FUN-3807
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT LEAST(1, 2, 3)"));
        Assert.True(connection.QuerySingle<bool>("SELECT LEAST(1, NULL) IS NULL"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT LEAST(1, NULL, 3) IGNORE NULLS"));
    }

    /// <summary>docs/functions.html:3825 — LOCK_MODE()</summary>
    [Fact]
    public void LockMode_ReturnsTheCurrentMode() // FUN-3825
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT LOCK_MODE() >= 0"));
    }

    /// <summary>docs/functions.html:3843 — LOCK_TIMEOUT()</summary>
    [Fact]
    public void LockTimeout_ReturnsTheTimeout() // FUN-3843
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT LOCK_TIMEOUT() >= 0"));
    }

    /// <summary>docs/functions.html:3861 — MEMORY_FREE()</summary>
    [Fact]
    [Trait("Category", "BlockedByPort")]
    public void MemoryFree_ReturnsTheFreeMemory() // FUN-3861
    {
        // Blocked by the port: the reference reads Runtime.freeMemory(), which has no
        // .NET equivalent (see docs/APPROVED_DEVIATIONS.md P-003).
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT MEMORY_FREE() > 0"));
    }

    /// <summary>docs/functions.html:3879 — MEMORY_USED()</summary>
    [Fact]
    [Trait("Category", "BlockedByPort")]
    public void MemoryUsed_ReturnsTheUsedMemory() // FUN-3879
    {
        // Blocked by the port: same Runtime.totalMemory() gap as MEMORY_FREE above.
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT MEMORY_USED() > 0"));
    }

    /// <summary>docs/functions.html:3897 — NEXTVAL ( [ schemaNameString, ] sequenceString )</summary>
    [Fact]
    public void Nextval_GeneratesTheValue() // FUN-3897
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE TEST_SEQ");

        Assert.Equal(1L, connection.QuerySingle<long>("SELECT NEXTVAL('TEST_SEQ')"));
    }

    /// <summary>docs/functions.html:3915 — NULLIF(aValue, bValue)</summary>
    [Fact]
    public void NullIf_ReturnsNullForEqualValues() // FUN-3915
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT NULLIF(1, 1) IS NULL"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT NULLIF(1, 2)"));
    }

    /// <summary>docs/functions.html:3933 — NVL2(testValue, aValue, bValue)</summary>
    [Fact]
    public void Nvl2_PicksByNullness() // FUN-3933
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("b", connection.QuerySingle<string>("SELECT NVL2(NULL, 'a', 'b')"));
        Assert.Equal("a", connection.QuerySingle<string>("SELECT NVL2(1, 'a', 'b')"));
    }

    /// <summary>docs/functions.html:3951 — READONLY()</summary>
    [Fact]
    public void ReadOnly_IsOffForAWritableConnection() // FUN-3951
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.False(connection.QuerySingle<bool>("SELECT READONLY()"));
    }

    /// <summary>docs/functions.html:3969 — ROWNUM()</summary>
    [Fact]
    public void RowNum_ReturnsTheRowNumber() // FUN-3969
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT ROWNUM() >= 0 FROM SYSTEM_RANGE(1, 1)"));
    }

    /// <summary>docs/functions.html:3987 — SESSION_ID()</summary>
    [Fact]
    public void SessionId_IsPositive() // FUN-3987
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT SESSION_ID() > 0"));
    }

    /// <summary>docs/functions.html:4005 — SET(@variableName, value)</summary>
    [Fact]
    public void Set_UpdatesTheVariable() // FUN-4005
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>("SELECT SET(@X, 5)"));
        Assert.Equal(5, connection.QuerySingle<int>("SELECT @X"));
    }

    /// <summary>docs/functions.html:4023 — TRANSACTION_ID()</summary>
    [Fact]
    public void TransactionId_IsAnExpression() // FUN-4023
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>(
            "SELECT TRANSACTION_ID() IS NULL OR TRANSACTION_ID() IS NOT NULL"));
    }

    /// <summary>docs/functions.html:4041 — TRUNCATE_VALUE(value, precisionInt, forceBoolean)</summary>
    [Fact]
    public void TruncateValue_TruncatesToThePrecision() // FUN-4041
    {
        using IDbConnection connection = TestDatabase.Open();

        // Reference rounds to significant digits (verified against H2 2.5.259).
        Assert.Equal(123.46m, connection.QuerySingle<decimal>("SELECT TRUNCATE_VALUE(123.456, 5, TRUE)"));
        Assert.Equal(123.46m, connection.QuerySingle<decimal>("SELECT TRUNCATE_VALUE(123.456, 5, FALSE)"));
    }

    /// <summary>docs/functions.html:4059 — CURRENT_PATH</summary>
    [Fact]
    public void CurrentPath_ReturnsTheSchemaPath() // FUN-4059
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT CURRENT_PATH IS NOT NULL"));
    }

    /// <summary>docs/functions.html:4077 — CURRENT_ROLE</summary>
    [Fact]
    public void CurrentRole_IsPublicOrUnset() // FUN-4077
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT CURRENT_ROLE IS NULL OR CURRENT_ROLE = 'PUBLIC'"));
    }

    /// <summary>docs/functions.html:4095 — CURRENT_USER | SESSION_USER | SYSTEM_USER | USER</summary>
    [Fact]
    public void CurrentUser_MatchesTheOtherSpellings() // FUN-4095
    {
        using IDbConnection connection = TestDatabase.Open();

        // A fresh in-memory database is opened without credentials, so the current user is
        // the empty default, as in the reference (verified against H2 2.5.259: '').
        // What matters is that every spelling names the same user.
        Assert.Equal("", connection.QuerySingle<string>("SELECT CURRENT_USER"));
        Assert.True(connection.QuerySingle<bool>(
            "SELECT SESSION_USER = CURRENT_USER AND SYSTEM_USER = CURRENT_USER AND USER = CURRENT_USER"));
    }

    /// <summary>docs/functions.html:4113 — H2VERSION()</summary>
    [Fact]
    public void H2Version_ReturnsTheVersionString() // FUN-4113
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.NotEmpty(connection.QuerySingle<string>("SELECT H2VERSION()"));
    }
}
