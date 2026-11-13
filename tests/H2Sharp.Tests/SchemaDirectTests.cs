/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The direct Schema members SQL rarely reaches: the drop-schema passes (a view its table
 * already took, a dependent living in another schema) and the internal rename collisions that
 * the ALTER commands check before they ever delegate.
 */
using System;
using System.Linq;
using H2Sharp.Command;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using Xunit;

namespace H2Sharp.Tests;

public sealed class SchemaDirectTests
{
    private readonly SessionLocal session;
    private readonly Database database;
    private readonly Schema schema;

    public SchemaDirectTests()
    {
        database = new Database(
            ConnectionInfo.Memory("schema_direct_" + Guid.NewGuid().ToString("N")));
        session = new SessionLocal(database, "sa");
        schema = database.GetSchema("PUBLIC");
    }

    private void Execute(string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        _ = container.ExecuteUpdate(false);
    }

    private long CountObjects(string schemaName)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = '"
            + schemaName + "'"));
        using ResultInterface result = container.ExecuteQuery(10, 0, false);
        _ = result.Next();
        return result.CurrentRow()![0]!.GetLong();
    }

    /// <summary>
    /// The pass works over a snapshot: removing the table removes its dependent view with it,
    /// and when the pass reaches the view it is already gone.
    /// </summary>
    [Fact]
    public void DroppingASchemaSkipsViewsItsTablesAlreadyTook()
    {
        Execute("CREATE SCHEMA S");
        Execute("CREATE TABLE S.T(A INT)");
        Execute("INSERT INTO S.T VALUES (1)");
        Execute("CREATE VIEW S.V AS SELECT * FROM S.T");
        Execute("DROP SCHEMA S CASCADE");

        Assert.Equal(0L, CountObjects("S"));
        Assert.ThrowsAny<H2SharpException>(() => Execute("SELECT * FROM S.T"));
    }

    /// <summary>
    /// A computed column whose expression reads another table makes its table a dependent;
    /// when that dependent lives in another schema the drop is refused.
    /// </summary>
    [Fact]
    public void DroppingASchemaWithAForeignDependentIsRefused()
    {
        Execute("CREATE SCHEMA S1");
        Execute("CREATE SCHEMA S2");
        Execute("CREATE TABLE S1.A1(X INT)");
        Execute("CREATE TABLE S2.B1(Y INT AS (SELECT MAX(X) FROM S1.A1))");

        H2SharpException e = Assert.Throws<H2SharpException>(
            () => Execute("DROP SCHEMA S1 CASCADE"));
        Assert.Equal(Api.ErrorCode.CannotDrop2, e.H2ErrorCode);
    }

    [Fact]
    public void RenamingATableOntoItselfIsAnInternalError()
    {
        Execute("CREATE TABLE T(A INT)");
        Table table = schema.GetTableOrView(null, "T")!;

        Exception same = Assert.Throws<Exception>(() => schema.Rename(table, "T"));
        Assert.Contains("object already exists", same.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenamingATableOntoAnExistingOneIsAnInternalError()
    {
        Execute("CREATE TABLE T(A INT)");
        Execute("CREATE TABLE T2(A INT)");
        Table table = schema.GetTableOrView(null, "T")!;

        Exception taken = Assert.Throws<Exception>(() => schema.Rename(table, "T2"));
        Assert.Contains("object already exists", taken.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenamingATableOfAnotherSchemaIsNotFound()
    {
        Execute("CREATE SCHEMA OTHER");
        Execute("CREATE TABLE OTHER.X(A INT)");
        Table foreign = database.GetSchema("OTHER").GetTableOrView(null, "X")!;

        Exception missing = Assert.Throws<Exception>(() => schema.Rename(foreign, "Y"));
        Assert.Contains("not found", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenamingADomainOntoAnExistingOneIsAnInternalError()
    {
        Execute("CREATE DOMAIN D1 AS INT");
        Execute("CREATE DOMAIN D2 AS INT");
        Domain first = schema.FindDomain("D1")!;

        Exception taken = Assert.Throws<Exception>(() => schema.Rename(first, "D2"));
        Assert.Contains("object already exists", taken.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenamingAConstraintOntoAnExistingOneIsAnInternalError()
    {
        Execute("CREATE TABLE T(A INT, B INT)");
        Execute("ALTER TABLE T ADD CONSTRAINT C1 UNIQUE(A)");
        Execute("ALTER TABLE T ADD CONSTRAINT C2 UNIQUE(B)");
        Constraint first = schema.GetAllConstraints().First(c => c.GetName() == "C1");

        Exception taken = Assert.Throws<Exception>(() => schema.Rename(first, "C2"));
        Assert.Contains("object already exists", taken.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenamingAnIndexOntoAnExistingOneIsAnInternalError()
    {
        Execute("CREATE TABLE T(A INT, B INT)");
        Execute("CREATE INDEX I1 ON T(A)");
        Execute("CREATE INDEX I2 ON T(B)");
        Indexes.Index first = schema.GetAllIndexes().First(i => i.GetName() == "I1");

        Exception taken = Assert.Throws<Exception>(() => schema.Rename(first, "I2"));
        Assert.Contains("object already exists", taken.Message, StringComparison.Ordinal);
    }
}
