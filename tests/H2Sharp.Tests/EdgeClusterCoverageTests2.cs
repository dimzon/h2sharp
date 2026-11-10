/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 120: the next cheap cluster — the boolean aggregates, the bit-set helper,
 * the UUID self-comparison, the database's data-handler members, the column resolver
 * defaults, the compatibility-mode function refusal, the delegated cast provider,
 * the rename commands' refusals, the row-number visitor, the session control
 * functions, the generated-column resolver, and the JSON scanner errors.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Command;
using H2Sharp.Command.Ddl;
using H2Sharp.Command.Dml;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Aggregate;
using H2Sharp.Expressions.Functions;
using H2Sharp.Constraints;
using H2Sharp.Message;
using H2Sharp.Modes;
using H2Sharp.MvStore.Tx;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Store;
using H2Sharp.Store.FileSystem;
using H2Sharp.Tables;
using H2Sharp.Util.Json;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class EdgeClusterCoverageTests2
{
    /// <summary>A resolver that answers only the abstract member.</summary>
    private sealed class TestColumnResolver : ColumnResolver
    {
        public Column[] GetColumns() => [];

        public Column? FindColumn(string name) => null;

        public Value? GetValue(Column column) => null;
    }

    private static (Database Database, SessionLocal Session) Open()
    {
        Database database = new(ConnectionInfo.Memory("edge_cluster2_" + Guid.NewGuid().ToString("N")));
        SessionLocal session = new(database, "sa");
        return (database, session);
    }

    [Fact]
    public void AggregateAndBitSetEdgesAnswer()
    {
        (Database database, SessionLocal session) = Open();

        AggregateDataDefault every = new(AggregateType.Every, TypeInfo.TypeBoolean);
        every.Add(session, ValueBoolean.True);
        every.Add(session, ValueBoolean.False);
        Assert.False(every.GetValue(session).GetBoolean());
        Assert.Throws<Exception>(() => new AggregateDataDefault((AggregateType)999, TypeInfo.TypeBoolean)
            .Add(session, ValueBoolean.True));

        long[] flipped = BitSetHelper.Flip([0L, 0L], 5);
        Assert.Single(flipped);
        Assert.Equal(0, BitSetHelper.Length([]));

        database.Close();
    }

    [Fact]
    public void UuidSelfComparisonAnswersZero()
    {
        ValueUuid uuid = ValueUuid.Get(Guid.NewGuid());
        Assert.Equal(0, uuid.CompareTypeSafe(uuid, null!, null));
    }

    [Fact]
    public void DatabaseDataHandlerAnswers()
    {
        (Database database, SessionLocal session) = Open();
        database.CheckPowerOff();
        Assert.Same(database, database.GetLobSyncObject());
        Assert.Null(database.GetLobFileListCache());
        database.OpenFile("probe", "rw", false).Close();
        database.Close();
    }

    [Fact]
    public void ColumnResolverDefaultsAnswer()
    {
        ColumnResolver resolver = new TestColumnResolver();
        Assert.Empty(resolver.GetColumns());
        Assert.Null(resolver.FindColumn("X"));
        Assert.False(resolver.HasDerivedColumnList());
        Assert.Null(resolver.GetSystemColumns());
        Assert.Null(resolver.GetRowIdColumn());
        Assert.Null(resolver.GetSchemaName());
        Assert.Throws<TODOException>(() => resolver.IncrementQueryLevel(1));
    }

    [Fact]
    public void ModeFunctionAndCastProviderAnswer()
    {
        Assert.Throws<TODOException>(() => ModeFunction.GetFunction(Mode.GetInstance("MYSQL")!, "X"));
        Assert.Null(ModeFunction.GetFunction(Mode.GetRegular(), "X"));

        DelegatedCastDataProvider provider = new(() => DateTimeOffset.Now);
        Assert.NotNull(provider.CurrentTimestamp());
        Assert.NotNull(provider.CurrentTimeZone());
        Assert.False(provider.ZeroBasedEnums());
        Assert.NotNull(provider.GetMode());
    }

    [Fact]
    public void RenameCommandsRefuseTheirEdges()
    {
        (Database database, SessionLocal session) = Open();
        Schema schema = database.GetSchema("PUBLIC");
        session.PrepareCommand("CREATE TABLE T(ID INT CONSTRAINT C1 CHECK(ID > 0))").ExecuteUpdate(false);

        session.PrepareCommand("CREATE SCHEMA S1").ExecuteUpdate(false);
        Schema userSchema = database.GetSchema("S1");

        AlterSchemaRename renameSchema = new(session);
        renameSchema.SetOldSchema(userSchema);
        renameSchema.SetNewName("S1");
        Assert.Throws<H2SharpException>(() => renameSchema.Update());

        AlterSchemaRename renameSystem = new(session);
        renameSystem.SetOldSchema(database.GetSchema(Database.InformationSchemaName));
        renameSystem.SetNewName("OTHER");
        Assert.Throws<H2SharpException>(() => renameSystem.Update());

        Assert.Equal(CommandInterface.AlterSchemaRename, renameSchema.GetCommandType());

        AlterTableRenameConstraint rename = new(session, schema);
        rename.SetConstraintName("Missing");
        rename.SetNewConstraintName("C2");
        Table table = schema.GetTableOrView(session, "T");
        Assert.Throws<H2SharpException>(() => rename.Update(table));

        rename.SetConstraintName("C1");
        rename.SetNewConstraintName("C1");
        Assert.Throws<H2SharpException>(() => rename.Update(table));
        Assert.Equal(CommandInterface.AlterTableRenameConstraint, rename.GetCommandType());

        AlterTableSet set = new(session, schema, CommandInterface.AlterTableSetReferentialIntegrity, true);
        set.SetTableName("Missing");
        Assert.Throws<H2SharpException>(() => set.Update());

        AlterTableSet bogus = new(session, schema, 999, true);
        bogus.SetTableName("T");
        Assert.Throws<Exception>(() => bogus.Update());
        Assert.Equal(999, bogus.GetCommandType());

        database.Close();
    }

    [Fact]
    public void RownumAndSessionControlAnswer()
    {
        (Database database, SessionLocal session) = Open();

        Rownum rownum = new(new NoOperation(session));
        Assert.True(rownum.IsEverything(ExpressionVisitor.GetDecrementQueryLevelVisitor([], 1)));

        SessionLocal target = new(database, "sa");
        int targetId = target.GetId();
        Assert.Same(ValueNull.Instance, new SessionControlFunction(
            ValueExpression.Get(ValueNull.Instance), SessionControlFunction.AbortSession).GetValue(session));
        Assert.Same(ValueBoolean.True, new SessionControlFunction(
            ValueExpression.Get(ValueInteger.Get(targetId)), SessionControlFunction.AbortSession).GetValue(session));

        SessionLocal cancellable = new(database, "sa");
        Assert.Same(ValueBoolean.True, new SessionControlFunction(
            ValueExpression.Get(ValueInteger.Get(cancellable.GetId())), SessionControlFunction.CancelSession)
            .GetValue(session));
        Assert.Throws<Exception>(() => new SessionControlFunction(
            ValueExpression.Get(ValueInteger.Get(cancellable.GetId())), 99).GetValue(session));

        SetSessionCharacteristics characteristics = new(session, IsolationLevel.ReadCommitted);
        Assert.Null(characteristics.QueryMeta());
        Assert.Equal(CommandInterface.Set, characteristics.GetCommandType());
        characteristics.Update();

        database.Close();
    }

    [Fact]
    public void GeneratedColumnResolverAnswers()
    {
        (Database database, SessionLocal session) = Open();
        session.PrepareCommand("CREATE TABLE T(ID INT, B INT AS ID + 1)").ExecuteUpdate(false);
        Table table = database.GetSchema("PUBLIC").GetTableOrView(session, "T");

        GeneratedColumnResolver resolver = new(table);
        Column column = table.GetColumns()[0];
        Assert.Null(resolver.GetValue(column));

        DefaultRow row = new(2);
        row.SetKey(7);
        resolver.Set(row);
        Column rowId = new("X", TypeInfo.TypeInteger, null, -1);
        Assert.Equal(7L, resolver.GetValue(rowId)!.GetLong());

        DomainColumnResolver domain = new(TypeInfo.TypeInteger);
        Assert.Single(domain.GetColumns());
        Assert.Null(domain.FindColumn("x"));

        database.Close();
    }

    [Fact]
    public void JsonScannerErrorsRefuse()
    {
        {
            JsonStringTarget target = new();
            _ = JsonStringSource.Parse("[1.5E1]", target);
        }
        Assert.NotNull(Record.Exception(() =>
        {
            JsonStringTarget target = new();
            JsonStringSource.Parse("[\"\\u\"]", target);
        }));
        Assert.NotNull(Record.Exception(() =>
        {
            JsonStringTarget target = new();
            JsonStringSource.Parse("[\"\\uD800\\n\"]", target);
        }));
    }
}
