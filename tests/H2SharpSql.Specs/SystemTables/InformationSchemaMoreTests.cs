// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/systemtables.html — information schema views and range table, one test per checklist row.
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.SystemTables;

public sealed class InformationSchemaMoreTests
{
    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
    }

    /// <summary>docs/systemtables.html:1187 — PARAMETERS</summary>
    [Fact]
    public void Parameters_ListsTheRoutineParameters() // SYS-1187
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE ALIAS MY_SQRT FOR 'java.lang.Math.sqrt'");

        Assert.True(connection.QuerySingle<bool>("SELECT COUNT(*) > 0 FROM INFORMATION_SCHEMA.PARAMETERS"));
    }

    /// <summary>docs/systemtables.html:1292 — QUERY_STATISTICS</summary>
    [Fact]
    public void QueryStatistics_IsAQueryableView() // SYS-1292
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Empty(connection.Query("SELECT * FROM INFORMATION_SCHEMA.QUERY_STATISTICS WHERE 1 = 0").AsList());
    }

    /// <summary>docs/systemtables.html:1337 — REFERENTIAL_CONSTRAINTS</summary>
    [Fact]
    public void ReferentialConstraints_ListsTheForeignKeys() // SYS-1337
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE T1(ID INT PRIMARY KEY)");
        _ = connection.Execute("CREATE TABLE T2(ID INT, T1_ID INT, FOREIGN KEY (T1_ID) REFERENCES T1(ID))");

        Assert.True(connection.QuerySingle<bool>(
            "SELECT COUNT(*) >= 1 FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS"));
    }

    /// <summary>docs/systemtables.html:1373 — RIGHTS</summary>
    [Fact]
    public void Rights_ListsTheGrants() // SYS-1373
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        _ = connection.Execute("CREATE USER U2 PASSWORD 'pass'");
        _ = connection.Execute("GRANT SELECT ON TEST TO U2");

        Assert.True(connection.QuerySingle<bool>(
            "SELECT COUNT(*) >= 1 FROM INFORMATION_SCHEMA.RIGHTS WHERE GRANTEE = 'U2'"));
    }

    /// <summary>docs/systemtables.html:1400 — ROLES</summary>
    [Fact]
    public void Roles_ListsTheRoles() // SYS-1400
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE ROLE R2");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.ROLES WHERE ROLE_NAME = 'R2'"));
    }

    /// <summary>docs/systemtables.html:1415 — ROUTINES</summary>
    [Fact]
    public void Routines_ListsTheRoutines() // SYS-1415
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE ALIAS MY_SQRT FOR 'java.lang.Math.sqrt'");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_NAME = 'MY_SQRT'"));
    }

    /// <summary>docs/systemtables.html:1535 — SCHEMATA</summary>
    [Fact]
    public void Schemata_ListsTheSchemas() // SYS-1535
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = 'PUBLIC'"));
    }

    /// <summary>docs/systemtables.html:1571 — SEQUENCES</summary>
    [Fact]
    public void Sequences_ListsTheSequences() // SYS-1571
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SEQUENCES WHERE SEQUENCE_NAME = 'SEQ'"));
    }

    /// <summary>docs/systemtables.html:1634 — SESSIONS</summary>
    [Fact]
    public void Sessions_ListsTheSessions() // SYS-1634
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SESSIONS WHERE SESSION_ID = SESSION_ID()"));
    }

    /// <summary>docs/systemtables.html:1684 — SESSION_STATE</summary>
    [Fact]
    public void SessionState_IsAQueryableView() // SYS-1684
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Empty(connection.Query("SELECT * FROM INFORMATION_SCHEMA.SESSION_STATE WHERE 1 = 0").AsList());
    }

    /// <summary>docs/systemtables.html:1699 — SETTINGS</summary>
    [Fact]
    public void Settings_ListsTheSettings() // SYS-1699
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT COUNT(*) > 0 FROM INFORMATION_SCHEMA.SETTINGS"));
    }

    /// <summary>docs/systemtables.html:1714 — SYNONYMS</summary>
    [Fact]
    public void Synonyms_ListsTheSynonyms() // SYS-1714
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        _ = connection.Execute("CREATE SYNONYM T FOR TEST");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SYNONYMS WHERE SYNONYM_NAME = 'T'"));
    }

    /// <summary>docs/systemtables.html:1747 — TABLES</summary>
    [Fact]
    public void Tables_ListsTheTables() // SYS-1747
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TEST'"));
    }

    /// <summary>docs/systemtables.html:1793 — TABLE_CONSTRAINTS</summary>
    [Fact]
    public void TableConstraints_ListsTheConstraints() // SYS-1793
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT PRIMARY KEY)");

        Assert.True(connection.QuerySingle<bool>(
            "SELECT COUNT(*) >= 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_NAME = 'TEST'"));
    }

    /// <summary>docs/systemtables.html:1852 — TABLE_PRIVILEGES</summary>
    [Fact]
    public void TablePrivileges_ListsTheTableGrants() // SYS-1852
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        _ = connection.Execute("CREATE USER U2 PASSWORD 'pass'");
        _ = connection.Execute("GRANT SELECT, INSERT ON TEST TO U2");

        Assert.True(connection.QuerySingle<bool>(
            "SELECT COUNT(*) >= 1 FROM INFORMATION_SCHEMA.TABLE_PRIVILEGES WHERE GRANTEE = 'U2'"));
    }

    /// <summary>docs/systemtables.html:1887 — TRIGGERS</summary>
    [Fact]
    public void Triggers_ListsTheTriggers() // SYS-1887
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        _ = connection.Execute("CREATE TRIGGER TRIG_INS BEFORE INSERT ON TEST FOR EACH ROW CALL 'MyTrigger'");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TRIGGERS WHERE TRIGGER_NAME = 'TRIG_INS'"));
    }

    /// <summary>docs/systemtables.html:1938 — USERS</summary>
    [Fact]
    public void Users_ListsTheUsers() // SYS-1938
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE USER U2 PASSWORD 'pass'");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.USERS WHERE USER_NAME = 'U2'"));
    }

    /// <summary>docs/systemtables.html:1957 — VIEWS</summary>
    [Fact]
    public void Views_ListsTheViews() // SYS-1957
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        _ = connection.Execute("CREATE VIEW V AS SELECT ID FROM TEST");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.VIEWS WHERE VIEW_NAME = 'V'"));
    }

    /// <summary>docs/systemtables.html:2003 — Range Table</summary>
    [Fact]
    public void RangeTable_GeneratesTheNumbers() // SYS-2003
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5L, connection.QuerySingle<long>("SELECT COUNT(*) FROM SYSTEM_RANGE(1, 5)"));
        Assert.Equal(new long[] { 1, 2, 3 }, connection.Query<long>("SELECT X FROM SYSTEM_RANGE(1, 3)").AsList());
    }
}
