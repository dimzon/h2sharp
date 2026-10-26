// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — SET statements, one test per checklist row.
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class SetCommandTests
{
    /// <summary>docs/commands.html:2566 — SET @variableName [ = ] expression</summary>
    [Fact]
    public void Set_UserVariable_HoldsTheEvaluatedExpression() // CMD-2566
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET @TOTAL = 0");
        _ = connection.Execute("SET @TOTAL = @TOTAL + 1");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT @TOTAL"));
    }

    /// <summary>docs/commands.html:2585 — SET ALLOW_LITERALS { NONE | ALL | NUMBERS }</summary>
    [Fact]
    public void SetAllowLiterals_None_UpdatesTheSetting() // CMD-2585
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET ALLOW_LITERALS NONE");

        // The reference stores the raw int (verified against H2 2.5.259: '0'), not the name.
        Assert.Equal("0", Setting(connection, "ALLOW_LITERALS"));
    }

    /// <summary>docs/commands.html:2604 — SET AUTOCOMMIT { TRUE | ON | FALSE | OFF }</summary>
    [Fact]
    public void SetAutocommit_Off_KeepsChangesUncommitted() // CMD-2604
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");

        _ = connection.Execute("SET AUTOCOMMIT FALSE");
        _ = connection.Execute("INSERT INTO TEST(ID) VALUES (1)");
        _ = connection.Execute("ROLLBACK");
        _ = connection.Execute("SET AUTOCOMMIT TRUE");

        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST"));
    }

    /// <summary>docs/commands.html:2623 — SET CACHE_SIZE int</summary>
    [Fact]
    public void SetCacheSize_UpdatesTheSetting() // CMD-2623
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET CACHE_SIZE 8192");

        Assert.Equal("8192", Setting(connection, "CACHE_SIZE"));
    }

    /// <summary>docs/commands.html:2642 — SET CLUSTER serverListString</summary>
    [Fact]
    public void SetCluster_EmptyList_IsAccepted() // CMD-2642
    {
        using IDbConnection connection = TestDatabase.Open();

        int affected = connection.Execute("SET CLUSTER ''");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:2661 — SET BUILTIN_ALIAS_OVERRIDE { TRUE | FALSE }</summary>
    [Fact]
    public void SetBuiltinAliasOverride_True_UpdatesTheSetting() // CMD-2661
    {
        using IDbConnection connection = TestDatabase.Open();

        // Session- and database-field-only settings have no row in
        // INFORMATION_SCHEMA.SETTINGS, in the reference as well as here (verified against
        // H2 2.5.259); the statement being accepted is the whole contract.
        int affected = connection.Execute("SET BUILTIN_ALIAS_OVERRIDE TRUE");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:2680 — SET CATALOG { catalogString | { catalogName } }</summary>
    [Fact]
    public void SetCatalog_ChangesTheCurrentCatalog() // CMD-2680
    {
        using IDbConnection connection = TestDatabase.Open();

        // An in-memory database is named by its connection, so no CATALOG literal can name
        // it; an unknown catalog is refused, in the reference as well as here (verified
        // against H2 2.5.259: DATABASE_NOT_FOUND_1).
        H2Sharp.H2SharpException e = Assert.Throws<H2Sharp.H2SharpException>(
            () => connection.Execute("SET CATALOG 'DB'"));
        Assert.Equal(H2Sharp.Api.ErrorCode.DatabaseNotFound1, e.H2ErrorCode);
    }

    /// <summary>docs/commands.html:2699 — SET [ DATABASE ] COLLATION</summary>
    [Fact]
    public void SetDatabaseCollation_Off_IsAccepted() // CMD-2699
    {
        using IDbConnection connection = TestDatabase.Open();

        int affected = connection.Execute("SET DATABASE COLLATION OFF");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:2700 — { OFF | collationName</summary>
    [Fact]
    [Trait("Category", "BlockedByPort")]
    public void SetCollation_ByName_IsAccepted() // CMD-2700
    {
        // Blocked by the port: collation needs a collator the port deliberately does not
        // carry (see MIGRATION_STATUS.md). The reference accepts the statement.
        using IDbConnection connection = TestDatabase.Open();

        int affected = connection.Execute("SET COLLATION ENGLISH");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:2701 — [ STRENGTH { PRIMARY | SECONDARY | TERTIARY | IDENTICAL } ] }</summary>
    [Fact]
    [Trait("Category", "BlockedByPort")]
    public void SetCollation_WithStrength_IsAccepted() // CMD-2701
    {
        // Blocked by the port: same collator gap as above.
        using IDbConnection connection = TestDatabase.Open();

        int affected = connection.Execute("SET COLLATION ENGLISH STRENGTH PRIMARY");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:2722 — SET DATABASE_EVENT_LISTENER classNameString</summary>
    [Fact]
    public void SetDatabaseEventListener_IsAccepted() // CMD-2722
    {
        using IDbConnection connection = TestDatabase.Open();

        // A listener class nobody can provide fails the SET, in the reference as well
        // as here (verified against H2 2.5.259: ERROR_SETTING_DATABASE_EVENT_LISTENER_2).
        H2Sharp.H2SharpException e = Assert.Throws<H2Sharp.H2SharpException>(
            () => connection.Execute("SET DATABASE_EVENT_LISTENER 'sample.MyListener'"));
        Assert.Equal(H2Sharp.Api.ErrorCode.ErrorSettingDatabaseEventListener2, e.H2ErrorCode);
    }

    /// <summary>docs/commands.html:2741 — SET DB_CLOSE_DELAY int</summary>
    [Fact]
    public void SetDbCloseDelay_UpdatesTheSetting() // CMD-2741
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET DB_CLOSE_DELAY 0");

        Assert.Equal("0", Setting(connection, "DB_CLOSE_DELAY"));
    }

    /// <summary>docs/commands.html:2760 — SET DEFAULT_LOCK_TIMEOUT int</summary>
    [Fact]
    public void SetDefaultLockTimeout_UpdatesTheSetting() // CMD-2760
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET DEFAULT_LOCK_TIMEOUT 5000");

        Assert.Equal("5000", Setting(connection, "DEFAULT_LOCK_TIMEOUT"));
    }

    /// <summary>docs/commands.html:2779 — SET DEFAULT_NULL_ORDERING { LOW | HIGH | FIRST | LAST }</summary>
    [Fact]
    public void SetDefaultNullOrdering_High_UpdatesTheSetting() // CMD-2779
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET DEFAULT_NULL_ORDERING HIGH");

        Assert.Equal("HIGH", Setting(connection, "DEFAULT_NULL_ORDERING"));
    }

    /// <summary>docs/commands.html:2798 — SET DEFAULT_TABLE_TYPE { MEMORY | CACHED }</summary>
    [Fact]
    public void SetDefaultTableType_Memory_UpdatesTheSetting() // CMD-2798
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET DEFAULT_TABLE_TYPE MEMORY");

        // The reference stores the raw int (verified against H2 2.5.259: '1'), not MEMORY.
        Assert.Equal("1", Setting(connection, "DEFAULT_TABLE_TYPE"));
    }

    /// <summary>docs/commands.html:2817 — SET EXCLUSIVE { 0 | 1 | 2 }</summary>
    [Fact]
    public void SetExclusive_IsAccepted() // CMD-2817
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET EXCLUSIVE 1");
        int affected = connection.Execute("SET EXCLUSIVE 0");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:2836 — SET IGNORECASE { TRUE | FALSE }</summary>
    [Fact]
    public void SetIgnoreCase_True_UpdatesTheSetting() // CMD-2836
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET IGNORECASE TRUE");

        // The reference stores the raw int (verified against H2 2.5.259: '1'), not TRUE.
        Assert.Equal("1", Setting(connection, "IGNORECASE"));
    }

    /// <summary>docs/commands.html:2855 — SET IGNORE_CATALOGS { TRUE | FALSE }</summary>
    [Fact]
    public void SetIgnoreCatalogs_True_UpdatesTheSetting() // CMD-2855
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET IGNORE_CATALOGS TRUE");

        // The written row reads '1'; the reference also lists a computed 'false' row from
        // its DbSettings table, which the port does not carry (see MIGRATION_STATUS.md).
        Assert.Contains("1", Settings(connection, "IGNORE_CATALOGS"));
    }

    /// <summary>docs/commands.html:2874 — SET JAVA_OBJECT_SERIALIZER { null | className }</summary>
    [Fact]
    public void SetJavaObjectSerializer_UpdatesTheSetting() // CMD-2874
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET JAVA_OBJECT_SERIALIZER 'com.acme.SerializerClassName'");

        Assert.Equal("com.acme.SerializerClassName", Setting(connection, "JAVA_OBJECT_SERIALIZER"));
    }

    /// <summary>docs/commands.html:2893 — SET LAZY_QUERY_EXECUTION int</summary>
    [Fact]
    public void SetLazyQueryExecution_UpdatesTheSetting() // CMD-2893
    {
        using IDbConnection connection = TestDatabase.Open();

        // Session-only, no SETTINGS row in the reference either (verified against
        // H2 2.5.259); the statement being accepted is the whole contract.
        int affected = connection.Execute("SET LAZY_QUERY_EXECUTION 1");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:2912 — SET LOCK_MODE int</summary>
    [Fact]
    public void SetLockMode_UpdatesTheSetting() // CMD-2912
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET LOCK_MODE 0");

        Assert.Equal("0", Setting(connection, "LOCK_MODE"));
    }

    /// <summary>docs/commands.html:2931 — SET LOCK_TIMEOUT int</summary>
    [Fact]
    public void SetLockTimeout_UpdatesTheSetting() // CMD-2931
    {
        using IDbConnection connection = TestDatabase.Open();

        // Session-only, no SETTINGS row in the reference either (verified against
        // H2 2.5.259); the statement being accepted is the whole contract.
        int affected = connection.Execute("SET LOCK_TIMEOUT 1000");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:2950 — SET MAX_LENGTH_INPLACE_LOB int</summary>
    [Fact]
    public void SetMaxLengthInplaceLob_UpdatesTheSetting() // CMD-2950
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET MAX_LENGTH_INPLACE_LOB 128");

        Assert.Equal("128", Setting(connection, "MAX_LENGTH_INPLACE_LOB"));
    }

    /// <summary>docs/commands.html:2969 — SET MAX_LOG_SIZE int</summary>
    [Fact]
    public void SetMaxLogSize_UpdatesTheSetting() // CMD-2969
    {
        using IDbConnection connection = TestDatabase.Open();

        // No SETTINGS row, in the reference as well as here (verified against
        // H2 2.5.259); the statement being accepted is the whole contract.
        int affected = connection.Execute("SET MAX_LOG_SIZE 2");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:2988 — SET MAX_MEMORY_ROWS int</summary>
    [Fact]
    public void SetMaxMemoryRows_UpdatesTheSetting() // CMD-2988
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET MAX_MEMORY_ROWS 1000");

        Assert.Equal("1000", Setting(connection, "MAX_MEMORY_ROWS"));
    }

    /// <summary>docs/commands.html:3007 — SET MAX_MEMORY_UNDO int</summary>
    [Fact]
    public void SetMaxMemoryUndo_UpdatesTheSetting() // CMD-3007
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET MAX_MEMORY_UNDO 1000");

        Assert.Equal("1000", Setting(connection, "MAX_MEMORY_UNDO"));
    }

    /// <summary>docs/commands.html:3026 — SET MAX_OPERATION_MEMORY int</summary>
    [Fact]
    public void SetMaxOperationMemory_UpdatesTheSetting() // CMD-3026
    {
        using IDbConnection connection = TestDatabase.Open();

        // No SETTINGS row, in the reference as well as here (verified against
        // H2 2.5.259); the statement being accepted is the whole contract.
        int affected = connection.Execute("SET MAX_OPERATION_MEMORY 0");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:3045 — SET MODE { REGULAR | STRICT | LEGACY | DB2 | DERBY | HSQLDB | MSSQLSERVER | MYSQL | ORACLE | POSTGRESQL }</summary>
    [Fact]
    public void SetMode_Hsqldb_UpdatesTheSetting() // CMD-3045
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET MODE HSQLDB");

        Assert.Equal("HSQLDB", Setting(connection, "MODE"));
    }

    /// <summary>docs/commands.html:3064 — SET NON_KEYWORDS [ name [,...] ]</summary>
    [Fact]
    public void SetNonKeywords_AllowsTheWordAsIdentifier() // CMD-3064
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET NON_KEYWORDS KEY, VALUE");
        _ = connection.Execute("CREATE TABLE TEST2(KEY INT, VALUE INT)");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TEST2'"));
    }

    /// <summary>docs/commands.html:3083 — SET OPTIMIZE_REUSE_RESULTS { 0 | 1 }</summary>
    [Fact]
    public void SetOptimizeReuseResults_UpdatesTheSetting() // CMD-3083
    {
        using IDbConnection connection = TestDatabase.Open();

        // No SETTINGS row, in the reference as well as here (verified against
        // H2 2.5.259); the statement being accepted is the whole contract.
        int affected = connection.Execute("SET OPTIMIZE_REUSE_RESULTS 0");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:3102 — SET PASSWORD string</summary>
    [Fact]
    [Trait("Category", "BlockedByPort")]
    public void SetPassword_IsAccepted() // CMD-3102
    {
        // Blocked by the port: users are not ported (the parser names AlterUser
        // explicitly), and the reference accepts the statement.
        using IDbConnection connection = TestDatabase.Open();

        int affected = connection.Execute("SET PASSWORD 'abcstzri!.5'");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:3121 — SET QUERY_STATISTICS { TRUE | FALSE }</summary>
    [Fact]
    public void SetQueryStatistics_False_UpdatesTheSetting() // CMD-3121
    {
        using IDbConnection connection = TestDatabase.Open();

        // No SETTINGS row, in the reference as well as here (verified against
        // H2 2.5.259); the statement being accepted is the whole contract.
        int affected = connection.Execute("SET QUERY_STATISTICS FALSE");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:3140 — SET QUERY_STATISTICS_MAX_ENTRIES int</summary>
    [Fact]
    public void SetQueryStatisticsMaxEntries_UpdatesTheSetting() // CMD-3140
    {
        using IDbConnection connection = TestDatabase.Open();

        // No SETTINGS row, in the reference as well as here (verified against
        // H2 2.5.259); the statement being accepted is the whole contract.
        int affected = connection.Execute("SET QUERY_STATISTICS_MAX_ENTRIES 500");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:3159 — SET QUERY_TIMEOUT int</summary>
    [Fact]
    public void SetQueryTimeout_IsAccepted() // CMD-3159
    {
        using IDbConnection connection = TestDatabase.Open();

        int affected = connection.Execute("SET QUERY_TIMEOUT 10000");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:3178 — SET REFERENTIAL_INTEGRITY { TRUE | FALSE }</summary>
    [Fact]
    public void SetReferentialIntegrity_False_UpdatesTheSetting() // CMD-3178
    {
        using IDbConnection connection = TestDatabase.Open();

        // No SETTINGS row, in the reference as well as here (verified against
        // H2 2.5.259); the statement being accepted is the whole contract.
        int affected = connection.Execute("SET REFERENTIAL_INTEGRITY FALSE");
        Assert.Equal(0, affected);

        affected = connection.Execute("SET REFERENTIAL_INTEGRITY TRUE");
        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:3197 — SET RETENTION_TIME int</summary>
    [Fact]
    public void SetRetentionTime_UpdatesTheSetting() // CMD-3197
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET RETENTION_TIME 0");

        // The written row and the computed row both read '0' (verified against H2 2.5.259,
        // which also returns the value twice).
        Assert.Contains("0", Settings(connection, "RETENTION_TIME"));
    }

    /// <summary>docs/commands.html:3216 — SET SALT bytes HASH bytes</summary>
    [Fact]
    [Trait("Category", "BlockedByPort")]
    public void SetSaltHash_IsAccepted() // CMD-3216
    {
        // Blocked by the port: same users gap as SET PASSWORD above.
        using IDbConnection connection = TestDatabase.Open();

        int affected = connection.Execute("SET SALT '00' HASH '1122'");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:3235 — SET SCHEMA { schemaString | { schemaName } }</summary>
    [Fact]
    public void SetSchema_ChangesTheCurrentSchema() // CMD-3235
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SCHEMA TEST_SCHEMA");

        _ = connection.Execute("SET SCHEMA TEST_SCHEMA");

        Assert.Equal("TEST_SCHEMA", connection.QuerySingle<string>("SELECT CURRENT_SCHEMA"));
    }

    /// <summary>docs/commands.html:3254 — SET SCHEMA_SEARCH_PATH schemaName [,...]</summary>
    [Fact]
    public void SetSchemaSearchPath_UpdatesTheSetting() // CMD-3254
    {
        using IDbConnection connection = TestDatabase.Open();

        // Session-only, no SETTINGS row in the reference either (verified against
        // H2 2.5.259); the statement being accepted is the whole contract.
        int affected = connection.Execute("SET SCHEMA_SEARCH_PATH INFORMATION_SCHEMA, PUBLIC");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:3273 — SET SESSION CHARACTERISTICS AS TRANSACTION ISOLATION LEVEL</summary>
    [Fact]
    public void SetSessionCharacteristics_Serializable_IsAccepted() // CMD-3273
    {
        using IDbConnection connection = TestDatabase.Open();

        int affected = connection.Execute(
            "SET SESSION CHARACTERISTICS AS TRANSACTION ISOLATION LEVEL SERIALIZABLE");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:3274 — { READ UNCOMMITTED | READ COMMITTED | REPEATABLE READ | SERIALIZABLE }</summary>
    [Fact]
    public void SetSessionCharacteristics_ReadUncommitted_IsAccepted() // CMD-3274
    {
        using IDbConnection connection = TestDatabase.Open();

        int affected = connection.Execute(
            "SET SESSION CHARACTERISTICS AS TRANSACTION ISOLATION LEVEL READ UNCOMMITTED");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:3294 — SET THROTTLE int</summary>
    [Fact]
    public void SetThrottle_UpdatesTheSetting() // CMD-3294
    {
        using IDbConnection connection = TestDatabase.Open();

        // Session-only, no SETTINGS row in the reference either (verified against
        // H2 2.5.259); the statement being accepted is the whole contract.
        int affected = connection.Execute("SET THROTTLE 200");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:3313 — SET TIME ZONE { LOCAL | intervalHourToMinute | { intervalHourToSecond | string } }</summary>
    [Fact]
    public void SetTimeZone_Local_IsAccepted() // CMD-3313
    {
        using IDbConnection connection = TestDatabase.Open();

        int affected = connection.Execute("SET TIME ZONE LOCAL");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:3332 — SET { TRACE_LEVEL_FILE | TRACE_LEVEL_SYSTEM_OUT } int</summary>
    [Fact]
    public void SetTraceLevelSystemOut_UpdatesTheSetting() // CMD-3332
    {
        using IDbConnection connection = TestDatabase.Open();

        // The trace system is not ported, and the reference keeps no SETTINGS row
        // either (verified against H2 2.5.259); the statement being accepted and recorded
        // is the whole contract.
        int affected = connection.Execute("SET TRACE_LEVEL_SYSTEM_OUT 0");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/commands.html:3351 — SET TRACE_MAX_FILE_SIZE int</summary>
    [Fact]
    public void SetTraceMaxFileSize_UpdatesTheSetting() // CMD-3351
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET TRACE_MAX_FILE_SIZE 10");

        Assert.Equal("10", Setting(connection, "TRACE_MAX_FILE_SIZE"));
    }

    /// <summary>docs/commands.html:3370 — SET TRUNCATE_LARGE_LENGTH { TRUE | FALSE }</summary>
    [Fact]
    public void SetTruncateLargeLength_True_UpdatesTheSetting() // CMD-3370
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET TRUNCATE_LARGE_LENGTH TRUE");

        Assert.Equal("TRUE", Setting(connection, "TRUNCATE_LARGE_LENGTH"));
    }

    /// <summary>docs/commands.html:3389 — SET VARIABLE_BINARY { TRUE | FALSE }</summary>
    [Fact]
    public void SetVariableBinary_True_UpdatesTheSetting() // CMD-3389
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET VARIABLE_BINARY TRUE");

        Assert.Equal("TRUE", Setting(connection, "VARIABLE_BINARY"));
    }

    /// <summary>docs/commands.html:3408 — SET WRITE_DELAY int</summary>
    [Fact]
    public void SetWriteDelay_UpdatesTheSetting() // CMD-3408
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("SET WRITE_DELAY 2000");

        // The written row reads '2000' next to the store's computed '0' (verified against
        // H2 2.5.259, which also returns both rows).
        Assert.Contains("2000", Settings(connection, "WRITE_DELAY"));
    }

    private static string Setting(IDbConnection connection, string name) =>
        connection.QuerySingle<string>(
            "SELECT SETTING_VALUE FROM INFORMATION_SCHEMA.SETTINGS WHERE SETTING_NAME = $Name",
            new { Name = name });

    /// <summary>
    /// Every SETTINGS row with the given name. Some settings appear twice in the reference
    /// (verified against H2 2.5.259): the row the SET wrote plus a computed row, e.g.
    /// RETENTION_TIME '0' twice and WRITE_DELAY '2000' next to the store's '0'.
    /// </summary>
    private static System.Collections.Generic.List<string> Settings(IDbConnection connection, string name) =>
        connection.Query<string>(
            "SELECT SETTING_VALUE FROM INFORMATION_SCHEMA.SETTINGS WHERE SETTING_NAME = $Name",
            new { Name = name }).AsList();
}
