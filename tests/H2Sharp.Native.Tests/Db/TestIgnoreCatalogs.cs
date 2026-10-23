// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestIgnoreCatalogs.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands, and the proxy form of
// assertThrows becomes the lambda form. The reference's main() entry is the runner;
// xUnit runs Test() directly.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests the IGNORE_CATALOGS setting and the three-part MSSQLServer names it reads.
/// </summary>
public sealed class TestIgnoreCatalogs : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        CanCommentOn();
        CanUseDefaultSchema();
        CanYetIdentifyWrongCatalogName();
        CanUseSettingInUrl();
        CanUseSetterSyntax();
        CanCatalogNameEqualSchemaName();
        CanUseCatalogAtIndexName();
        CanCommentOn();
        CanAllCombined();
        DoesNotAcceptEmptySchemaWhenNotMSSQL();
    }

    private void DoesNotAcceptEmptySchemaWhenNotMSSQL()
    {
        try
        {
            using DbConnection conn = GetConnection("ignoreCatalogs;IGNORE_CATALOGS=TRUE");
            DbCommand stat = conn.CreateCommand();
            PrepareDbAndSetDefaultSchema(stat);
            Execute(stat, "set schema dbo");
            Execute(stat, "create table catalog1.dbo.test(id int primary key, name varchar(255))");
            AssertThrows(ErrorCode.SyntaxError2, stat, "comment on table catalog1..test is 'table comment3'");
            AssertThrows(ErrorCode.SyntaxError2, stat, "create table catalog1..test2(id int primary key, "
                + "name varchar(255))");
            Execute(stat, "comment on table catalog1.dbo.test is 'table comment1'");
            Execute(stat, "insert into test values(1, 'Hello')");
            Execute(stat, "insert into cat.dbo.test values(2, 'Hello2')");
            AssertThrows(ErrorCode.SyntaxError2, stat, "comment on column catalog1...test.id is 'id comment1'");
            AssertThrows(ErrorCode.SyntaxError2, stat, "comment on column catalog1..test..id is 'id comment1'");
            AssertThrows(ErrorCode.SyntaxError2, stat, "comment on column ..test..id is 'id comment1'");
        }
        finally
        {
            DeleteDb("ignoreCatalogs");
        }
    }

    private void CanCommentOn()
    {
        try
        {
            using DbConnection conn = GetConnection("ignoreCatalogs;MODE=MSSQLSERVER;IGNORE_CATALOGS=TRUE;");
            DbCommand stat = conn.CreateCommand();
            PrepareDbAndSetDefaultSchema(stat);
            Execute(stat, "create table catalog1.dbo.test(id int primary key, name varchar(255))");
            Execute(stat, "comment on table catalog1.dbo.test is 'table comment1'");
            Execute(stat, "comment on table dbo.test is 'table comment2'");
            Execute(stat, "comment on table catalog1..test is 'table comment3'");
            Execute(stat, "comment on table test is 'table comment4'");
            Execute(stat, "comment on column catalog1..test.id is 'id comment1'");
            Execute(stat, "comment on column catalog1.dbo.test.id is 'id comment1'");
            Execute(stat, "comment on column dbo.test.id is 'id comment1'");
            Execute(stat, "comment on column test.id is 'id comment1'");
            AssertThrows(ErrorCode.SyntaxError2, stat, "comment on column catalog1...id is 'id comment1'");
            AssertThrows(ErrorCode.SyntaxError2, stat, "comment on column catalog1...test.id is 'id comment1'");
            AssertThrows(ErrorCode.SyntaxError2, stat, "comment on column catalog1..test..id is 'id comment1'");
            AssertThrows(ErrorCode.SyntaxError2, stat, "comment on column ..test..id is 'id comment1'");
            AssertThrows(ErrorCode.SyntaxError2, stat, "comment on column test..id is 'id comment1'");
            AssertThrows(ErrorCode.SyntaxError2, stat, "comment on column .PUBLIC.TEST.ID 'id comment1'");
            AssertThrows(ErrorCode.SyntaxError2, stat, "comment on column .TEST.ID 'id comment1'");
        }
        finally
        {
            DeleteDb("ignoreCatalogs");
        }
    }

    private void CanUseDefaultSchema()
    {
        try
        {
            using DbConnection conn = GetConnection("ignoreCatalogs;MODE=MSSQLSERVER;IGNORE_CATALOGS=TRUE;");
            DbCommand stat = conn.CreateCommand();
            PrepareDbAndSetDefaultSchema(stat);
            Execute(stat, "create table catalog1..test(id int primary key, name varchar(255))");

            Execute(stat, "create table test2(id int primary key, name varchar(255))");
            // expect table already exists
            AssertThrows(ErrorCode.TableOrViewAlreadyExists1, stat,
                "create table catalog2.dbo.test(id int primary key, name varchar(255))");
            Execute(stat, "insert into test values(1, 'Hello')");
            Execute(stat, "insert into test2 values(1, 'Hello')");
        }
        finally
        {
            DeleteDb("ignoreCatalogs");
        }
    }

    private void CanUseSettingInUrl()
    {
        try
        {
            using DbConnection conn = GetConnection("ignoreCatalogs;MODE=MSSQLSERVER;IGNORE_CATALOGS=TRUE;");
            DbCommand stat = conn.CreateCommand();
            PrepareDb(stat);
            Execute(stat, "create table catalog1.dbo.test(id int primary key, name varchar(255))");
            // expect table already exists
            AssertThrows(ErrorCode.TableOrViewAlreadyExists1, stat,
                "create table catalog2.dbo.test(id int primary key, name varchar(255))");
            Execute(stat, "insert into dbo.test values(1, 'Hello')");
        }
        finally
        {
            DeleteDb("ignoreCatalogs");
        }
    }

    private void CanUseSetterSyntax()
    {
        try
        {
            using DbConnection conn = GetConnection("ignoreCatalogs;MODE=MSSQLSERVER;");
            DbCommand stat = conn.CreateCommand();
            PrepareDb(stat);
            Execute(stat, "set IGNORE_CATALOGS=TRUE");
            Execute(stat, "create table catalog1.dbo.test(id int primary key, name varchar(255))");
            // expect table already exists
            AssertThrows(ErrorCode.TableOrViewAlreadyExists1, stat,
                "create table catalog2.dbo.test(id int primary key, name varchar(255))");
            Execute(stat, "insert into dbo.test values(1, 'Hello')");
        }
        finally
        {
            DeleteDb("ignoreCatalogs");
        }
    }

    private void CanCatalogNameEqualSchemaName()
    {
        try
        {
            using DbConnection conn = GetConnection("ignoreCatalogs;MODE=MSSQLSERVER;");
            DbCommand stat = conn.CreateCommand();
            PrepareDb(stat);
            Execute(stat, "set IGNORE_CATALOGS=TRUE");
            Execute(stat, "create table dbo.dbo.test(id int primary key, name varchar(255))");
            // expect object already exists
            AssertThrows(ErrorCode.TableOrViewAlreadyExists1, stat,
                "create table catalog2.dbo.test(id int primary key, name varchar(255))");
            Execute(stat, "insert into dbo.test values(1, 'Hello')");
        }
        finally
        {
            DeleteDb("ignoreCatalogs");
        }
    }

    private void CanYetIdentifyWrongCatalogName()
    {
        try
        {
            using DbConnection conn = GetConnection("ignoreCatalogs;MODE=MSSQLSERVER;");
            DbCommand stat = conn.CreateCommand();
            PrepareDb(stat);
            // works, since catalog name equals database name
            Execute(stat, "create table ignoreCatalogs.dbo.test(id int primary key, name varchar(255))");
            // schema test_x not found error
            AssertThrows(ErrorCode.SchemaNotFound1, stat,
                "create table test_x.dbo.test(id int primary key, name varchar(255))");
            AssertThrows(ErrorCode.DatabaseNotFound1, stat, "comment on column db..test.id is 'id'");
        }
        finally
        {
            DeleteDb("ignoreCatalogs");
        }
    }

    private void CanUseCatalogAtIndexName()
    {
        try
        {
            using DbConnection conn = GetConnection("ignoreCatalogs;MODE=MSSQLSERVER;");
            DbCommand stat = conn.CreateCommand();
            PrepareDb(stat);
            Execute(stat, "set IGNORE_CATALOGS=TRUE");
            Execute(stat, "create table dbo.dbo.test(id int primary key, name varchar(255))");
            Execute(stat, "create index i on dbo.dbo.test(id,name)");
            Execute(stat, "create index dbo.i2 on dbo.dbo.test(id,name)");
            Execute(stat, "create index catalog.dbo.i3 on dbo.dbo.test(id,name)");
            AssertThrows(ErrorCode.SchemaNotFound1, stat,
                "create index dboNotExistent.i4 on dbo.dbo.test(id,name)");
            // expect object already exists
            Execute(stat, "insert into dbo.test values(1, 'Hello')");
        }
        finally
        {
            DeleteDb("ignoreCatalogs");
        }
    }

    private void CanAllCombined()
    {
        try
        {
            using DbConnection conn = GetConnection("ignoreCatalogs;MODE=MSSQLSERVER;IGNORE_CATALOGS=TRUE;");
            DbCommand stat = conn.CreateCommand();
            PrepareDbAndSetDefaultSchema(stat);
            Execute(stat, "create table dbo.test(id int primary key, name varchar(255))");
            Execute(stat, "create table catalog1.dbo.test2(id int primary key, name varchar(255))");
            Execute(stat, "insert into dbo.test values(1, 'Hello')");
            Execute(stat, "insert into dbo.test2 values(1, 'Hello2')");
            Execute(stat, "set ignore_catalogs=false");
            AssertThrows(ErrorCode.SchemaNotFound1, stat,
                "insert into catalog1.dbo.test2 values(2, 'Hello2')");
            Execute(stat, "set ignore_catalogs=true");
            AssertResult("1", stat, "select * from test");
            AssertResult("1", stat, "select * from test2");
            Execute(stat, "alter table xxx.dbo.test add column (a varchar(200))");
            Execute(stat, "alter table xxx..test add column (b varchar(200))");
            Execute(stat, "alter table test add column (c varchar(200))");
            Execute(stat, "drop table xxx.dbo.test");
            Execute(stat, "drop table catalog1.dbo.test2");
            Execute(stat, "drop table if exists xxx.dbo.test");
            Execute(stat, "drop table if exists catalog1.dbo.test2");
            Execute(stat, "set ignore_catalogs=false");
            AssertThrows(ErrorCode.SchemaNotFound1, stat,
                "alter table xxx.dbo.test add column (a varchar(200))");
            AssertThrows(ErrorCode.SchemaNotFound1, stat,
                "alter table xxx..test add column (b varchar(200))");
            AssertThrows(ErrorCode.TableOrViewNotFound1, stat,
                "alter table test add column (c varchar(200))");
            AssertThrows(ErrorCode.SchemaNotFound1, stat,
                "drop table if exists xxx.dbo.test");
            AssertThrows(ErrorCode.SchemaNotFound1, stat,
                "drop table if exists xxx2..test");
            AssertThrows(ErrorCode.TableOrViewNotFound1, stat, "drop table test");
        }
        finally
        {
            DeleteDb("ignoreCatalogs");
        }
    }

    private void PrepareDb(DbCommand stat)
    {
        Execute(stat, "drop all objects");
        Execute(stat, "create schema dbo");
    }

    private void PrepareDbAndSetDefaultSchema(DbCommand stat)
    {
        PrepareDb(stat);
        Execute(stat, "set schema dbo");
    }
}
