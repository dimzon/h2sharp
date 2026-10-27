// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/systemtables.html — information schema views, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.SystemTables;

public sealed class InformationSchemaTests
{
    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
    }

    /// <summary>docs/systemtables.html:284 — Information Schema</summary>
    [Fact]
    public void InformationSchema_IsQueryable() // SYS-0284
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT COUNT(*) > 0 FROM INFORMATION_SCHEMA.SCHEMATA"));
    }

    /// <summary>docs/systemtables.html:294 — CHECK_CONSTRAINTS</summary>
    [Fact]
    public void CheckConstraints_ListsTheChecks() // SYS-0294
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT CHECK (ID > 0))");

        Assert.True(connection.QuerySingle<bool>("SELECT COUNT(*) >= 1 FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS"));
    }

    /// <summary>docs/systemtables.html:315 — COLLATIONS</summary>
    [Fact]
    public void Collations_ListsTheCollations() // SYS-0315
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT COUNT(*) > 0 FROM INFORMATION_SCHEMA.COLLATIONS"));
    }

    /// <summary>docs/systemtables.html:339 — COLUMNS</summary>
    [Fact]
    public void Columns_ListsTheTableColumns() // SYS-0339
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(2, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TEST'"));
    }

    /// <summary>docs/systemtables.html:495 — COLUMN_PRIVILEGES</summary>
    [Fact]
    [Trait("Category", "BlockedByPort")]
    public void ColumnPrivileges_ListsTheColumnGrants() // SYS-0495
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        _ = connection.Execute("CREATE USER U2 PASSWORD 'pass'");
        _ = connection.Execute("GRANT SELECT(ID) ON TEST TO U2");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMN_PRIVILEGES WHERE GRANTEE = 'U2'"));
    }

    /// <summary>docs/systemtables.html:529 — CONSTANTS</summary>
    [Fact]
    public void Constants_ListsTheConstants() // SYS-0529
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE CONSTANT C VALUE 1");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.CONSTANTS WHERE CONSTANT_NAME = 'C'"));
    }

    /// <summary>docs/systemtables.html:622 — CONSTRAINT_COLUMN_USAGE</summary>
    [Fact]
    public void ConstraintColumnUsage_MapsConstraintsToColumns() // SYS-0622
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(50))");

        Assert.True(connection.QuerySingle<bool>(
            "SELECT COUNT(*) >= 1 FROM INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE WHERE TABLE_NAME = 'TEST'"));
    }

    /// <summary>docs/systemtables.html:652 — DOMAINS</summary>
    [Fact]
    public void Domains_ListsTheDomains() // SYS-0652
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.DOMAINS WHERE DOMAIN_NAME = 'D'"));
    }

    /// <summary>docs/systemtables.html:757 — DOMAIN_CONSTRAINTS</summary>
    [Fact]
    public void DomainConstraints_ListsTheDomainChecks() // SYS-0757
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN D AS INT CHECK (VALUE > 0)");

        Assert.True(connection.QuerySingle<bool>(
            "SELECT COUNT(*) >= 1 FROM INFORMATION_SCHEMA.DOMAIN_CONSTRAINTS"));
    }

    /// <summary>docs/systemtables.html:794 — ELEMENT_TYPES</summary>
    [Fact]
    public void ElementTypes_ListsTheArrayElements() // SYS-0794
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(A INT ARRAY)");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.ELEMENT_TYPES WHERE OBJECT_NAME = 'TEST'"));
    }

    /// <summary>docs/systemtables.html:887 — ENUM_VALUES</summary>
    [Fact]
    public void EnumValues_ListsTheEnumItems() // SYS-0887
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(A ENUM('a', 'b'))");

        Assert.Equal(2, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.ENUM_VALUES WHERE OBJECT_NAME = 'TEST'"));
    }

    /// <summary>docs/systemtables.html:917 — FIELDS</summary>
    [Fact]
    public void Fields_ListsTheRowFields() // SYS-0917
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(A ROW(B INT, C VARCHAR(10)))");

        Assert.True(connection.QuerySingle<bool>(
            "SELECT COUNT(*) >= 2 FROM INFORMATION_SCHEMA.FIELDS WHERE OBJECT_NAME = 'TEST'"));
    }

    /// <summary>docs/systemtables.html:1016 — INDEXES</summary>
    [Fact]
    public void Indexes_ListsTheIndexes() // SYS-1016
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        _ = connection.Execute("CREATE INDEX IDX ON TEST(ID)");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'TEST' AND INDEX_NAME = 'IDX'"));
    }

    /// <summary>docs/systemtables.html:1061 — INDEX_COLUMNS</summary>
    [Fact]
    public void IndexColumns_ListsTheIndexedColumns() // SYS-1061
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        _ = connection.Execute("CREATE INDEX IDX ON TEST(ID)");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.INDEX_COLUMNS WHERE INDEX_NAME = 'IDX'"));
    }

    /// <summary>docs/systemtables.html:1103 — INFORMATION_SCHEMA_CATALOG_NAME</summary>
    [Fact]
    public void InformationSchemaCatalogName_ReturnsTheCatalog() // SYS-1103
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.NotNull(connection.QuerySingle<string>(
            "SELECT CATALOG_NAME FROM INFORMATION_SCHEMA.INFORMATION_SCHEMA_CATALOG_NAME"));
    }

    /// <summary>docs/systemtables.html:1115 — IN_DOUBT</summary>
    [Fact]
    public void InDoubt_IsAQueryableView() // SYS-1115
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Empty(connection.Query("SELECT * FROM INFORMATION_SCHEMA.IN_DOUBT WHERE 1 = 0").AsList());
    }

    /// <summary>docs/systemtables.html:1130 — KEY_COLUMN_USAGE</summary>
    [Fact]
    public void KeyColumnUsage_ListsTheKeyColumns() // SYS-1130
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT PRIMARY KEY)");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE TABLE_NAME = 'TEST'"));
    }

    /// <summary>docs/systemtables.html:1166 — LOCKS</summary>
    [Fact]
    public void Locks_IsAQueryableView() // SYS-1166
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT COUNT(*) >= 0 FROM INFORMATION_SCHEMA.LOCKS"));
    }
}
