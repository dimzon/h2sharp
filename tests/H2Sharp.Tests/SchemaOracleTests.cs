/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The schema object: DROP SCHEMA CASCADE removing every kind of child it can hold, the
 * renames of a table, an index, a domain and a constraint that the schema bookkeeping
 * follows, and the local temporary tables' own name spaces.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class SchemaOracleTests
{
    [JavaOracleFact]
    public void DroppingASchemaCascadesLikeJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE SCHEMA S2",
            "CREATE TABLE X(ID INT)",
            "INSERT INTO X VALUES (1), (2)",
            "CREATE TABLE S2.T(ID INT PRIMARY KEY)",
            "CREATE TABLE S2.T2(A INT, B INT, CONSTRAINT U UNIQUE(A, B))",
            "CREATE INDEX S2.I ON S2.T2(B)",
            "CREATE VIEW S2.V AS SELECT * FROM S2.T",
            "CREATE MATERIALIZED VIEW S2.MV AS SELECT ID FROM X",
            "CREATE SEQUENCE S2.SEQ",
            "CREATE DOMAIN S2.D AS INT",
            "CREATE DOMAIN S2.D3 AS INT CHECK (VALUE > 0)",
            "CREATE CONSTANT S2.C VALUE 1",
            "CREATE SYNONYM S2.SYN FOR X",
            "SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = 'S2'",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'S2'",
            // The synonym resolves to the table of another schema, and the materialized view
            // to the table behind it; the domain's check owns a generated constraint name.
            "SELECT ID FROM S2.SYN ORDER BY ID",
            "SELECT ID FROM S2.MV ORDER BY ID",
            "REFRESH MATERIALIZED VIEW S2.MV",
            "SELECT CONSTRAINT_NAME FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS "
                + "WHERE TABLE_SCHEMA = 'S2' AND TABLE_NAME = 'D3' ORDER BY CONSTRAINT_NAME",
            "DROP SCHEMA S2 CASCADE",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = 'S2'",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'S2'",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SEQUENCES WHERE SEQUENCE_SCHEMA = 'S2'",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.DOMAINS WHERE DOMAIN_SCHEMA = 'S2'",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.INDEXES WHERE INDEX_SCHEMA = 'S2'",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.CONSTRAINTS WHERE CONSTRAINT_SCHEMA = 'S2'",
            "DROP SCHEMA IF EXISTS S2 CASCADE",
            "CREATE SCHEMA IF NOT EXISTS S3",
            "CREATE SCHEMA IF NOT EXISTS S3",
            "DROP SCHEMA S3",
        ]);
    }

    [JavaOracleFact]
    public void RenamesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE SCHEMA S4",
            "CREATE TABLE S4.T(ID INT, A INT, B INT, CONSTRAINT U UNIQUE(A, B))",
            "CREATE INDEX S4.I ON S4.T(B)",
            "CREATE DOMAIN S4.D AS INT",
            "CREATE TABLE S4.TD(X S4.D)",
            "ALTER TABLE S4.T RENAME TO S4.T2",
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'S4' ORDER BY TABLE_NAME",
            "ALTER INDEX S4.I RENAME TO S4.I2",
            "SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE INDEX_SCHEMA = 'S4' ORDER BY INDEX_NAME",
            "ALTER DOMAIN S4.D RENAME TO S4.D2",
            "SELECT DOMAIN_NAME FROM INFORMATION_SCHEMA.DOMAINS WHERE DOMAIN_SCHEMA = 'S4'",
            "ALTER TABLE S4.T2 RENAME CONSTRAINT U TO U2",
            "SELECT CONSTRAINT_NAME FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS "
                + "WHERE TABLE_SCHEMA = 'S4' AND TABLE_NAME = 'T2' ORDER BY CONSTRAINT_NAME",
            "ALTER TABLE S4.T2 RENAME COLUMN B TO C",
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS "
                + "WHERE TABLE_SCHEMA = 'S4' AND TABLE_NAME = 'T2' ORDER BY ORDINAL_POSITION",
            "DROP SCHEMA S4 CASCADE",
        ]);
    }

    [Fact]
    public void TheSchemaObjectAnswersItsIdentityAndSql()
    {
        H2Sharp.Engine.Database database = new(
            H2Sharp.Engine.ConnectionInfo.Memory("schema_surface"));
        H2Sharp.Engine.SessionLocal session = new(database, "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = H2Sharp.Values.CompareMode.GetInstance(null, 0),
        };
        _ = Assert.IsType<H2Sharp.Command.CommandContainer>(
            session.PrepareCommand("CREATE SCHEMA S5")).GetPreparedCommand().Update();
        H2Sharp.Schemas.Schema schema = database.GetSchema("S5")!;

        Assert.Equal("S5", schema.GetSchemaName());
        Assert.False(schema.IsSystem());
        Assert.True(schema.CanDrop());
        Assert.True(schema.IsEmpty());
        Assert.Equal("CREATE SCHEMA IF NOT EXISTS \"S5\" AUTHORIZATION \"SA\"", schema.GetCreateSql());

        // A reserved name is held until it is freed; a null name is simply ignored.
        schema.ReserveUniqueName("TMP");
        schema.ReserveUniqueName(null);
        schema.FreeUniqueName("TMP");
        schema.FreeUniqueName(null);

        _ = Assert.IsType<H2Sharp.Command.CommandContainer>(
            session.PrepareCommand("CREATE TABLE S5.T(ID INT)")).GetPreparedCommand().Update();
        Assert.False(schema.IsEmpty());
        Assert.Same(schema, database.GetSchema("S5"));
        H2Sharp.Schemas.Schema system = database.GetSchema(H2Sharp.Engine.Database.InformationSchemaName)!;
        Assert.True(system.IsSystem());
        Assert.False(system.CanDrop());
        Assert.Null(system.GetCreateSql());
    }

    [JavaOracleFact]
    public void LocalTemporaryTablesKeepTheirOwnNamesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE LOCAL TEMPORARY TABLE TT(A INT, B INT, CONSTRAINT UT UNIQUE(A))",
            "CREATE INDEX TI ON TT(B)",
            "CREATE LOCAL TEMPORARY TABLE TT2(A INT, CONSTRAINT UT UNIQUE(A))",
            "CREATE INDEX TI ON TT2(A)",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'TT2'",
            "DROP TABLE TT2",
            "DROP TABLE TT",
        ]);
    }
}
