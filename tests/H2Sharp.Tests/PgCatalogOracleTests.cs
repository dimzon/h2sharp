/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 227: the PostgreSQL compatibility catalogue. SHOW ALL is a rewrite of
 * PG_CATALOG.PG_SETTINGS in the reference, so both spellings have to answer with the same rows.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class PgCatalogOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void ShowAllReadsPgSettings()
    {
        AssertSameAsReferenceInMode("pgc_show", "PostgreSQL",
            "SHOW ALL",
            "SELECT NAME, SETTING FROM PG_CATALOG.PG_SETTINGS",
            "SHOW ALL");
    }

    [JavaOracleFact]
    public void StaticPgCatalogTablesMatchTheReference()
    {
        AssertSameAsReferenceInMode("pgc_static", "PostgreSQL",
            "SELECT * FROM PG_CATALOG.PG_AM ORDER BY OID",
            "SELECT * FROM PG_CATALOG.PG_DESCRIPTION",
            "SELECT * FROM PG_CATALOG.PG_TABLESPACE",
            "SELECT * FROM PG_CATALOG.PG_GROUP",
            "SELECT * FROM PG_CATALOG.PG_INDEX",
            "SELECT * FROM PG_CATALOG.PG_INHERITS",
            "SELECT * FROM PG_CATALOG.PG_PROC",
            "SELECT * FROM PG_CATALOG.PG_TRIGGER");
    }

    [JavaOracleFact]
    public void PgNamespaceAndDatabaseMatchTheReference()
    {
        AssertSameAsReferenceInMode("pgc_schema", "PostgreSQL",
            "CREATE SCHEMA S",
            // ID and DATDBA are the object ids of the database's users. D-004 has no users,
            // so they differ from the JVM; the remaining columns are compared with the oracle.
            "SELECT NSPNAME FROM PG_CATALOG.PG_NAMESPACE ORDER BY NSPNAME",
            "SELECT DATNAME, ENCODING, DATLASTSYSOID, DATALLOWCONN, DATTABLESPACE "
                + "FROM PG_CATALOG.PG_DATABASE");
    }

    [Fact]
    public void UserIdsFollowTheApprovedNoUserModel()
    {
        // D-004: the port has no user objects. Their ids, which the reference reports here,
        // are fixed by the port's system user (0) and its object-id allocator.
        using H2Connection connection = new(
            "Data Source=pgc_ids;Storage=Memory;MODE=PostgreSQL");
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ID FROM PG_CATALOG.PG_NAMESPACE WHERE NSPNAME = 'PG_CATALOG'";
        Assert.Equal(-1000L, Convert.ToInt64(command.ExecuteScalar()));
        command.CommandText = "SELECT DATDBA FROM PG_CATALOG.PG_DATABASE";
        Assert.Equal(0L, Convert.ToInt64(command.ExecuteScalar()));
    }

    [JavaOracleFact]
    public void PgCatalogDescribesUserTables()
    {
        AssertSameAsReferenceInMode("pgc_tables", "PostgreSQL",
            "CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(20) NOT NULL)",
            "SELECT RELNAME, RELKIND, RELHASINDEX FROM PG_CATALOG.PG_CLASS "
                + "WHERE RELKIND <> 'i' ORDER BY RELNAME",
            "SELECT ATTNAME, ATTNUM, ATTNOTNULL FROM PG_CATALOG.PG_ATTRIBUTE "
                + "WHERE ATTRELID > 0 ORDER BY ATTRELID, ATTNUM",
            "SELECT CONNAME, CONTYPE FROM PG_CATALOG.PG_CONSTRAINT ORDER BY CONNAME");
    }

    [JavaOracleFact]
    public void PgTypeMatchesTheReference()
    {
        AssertSameAsReferenceInMode("pgc_type", "PostgreSQL",
            "SELECT OID, TYPNAME, TYPLEN, TYPELEM FROM PG_CATALOG.PG_TYPE ORDER BY OID, TYPNAME");
    }

    [JavaOracleFact]
    public void PgCatalogIsAbsentOutsidePostgreSqlMode()
    {
        AssertSameAsReference("pgc_regular",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = 'PG_CATALOG'",
            "SELECT * FROM PG_CATALOG.PG_SETTINGS");
    }
}
