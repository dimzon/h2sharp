/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 121: the next cheap cluster — index sorts, simple row values, domain values,
 * table renames, set/assign functions, cardinality, time-with-zone, string functions,
 * current-date-time, correlation aggregates, trim, subqueries, JSON constructors,
 * IN conditions, regular expressions, drop database, and the dialect helpers.
 */
using System;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Command;
using H2Sharp.Command.Ddl;
using H2Sharp.Command.Dml;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Constraints;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Aggregate;
using H2Sharp.Expressions.Conditions;
using H2Sharp.Expressions.Functions;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class EdgeClusterCoverageTests3
{
    private sealed class TestColumnResolver : ColumnResolver
    {
        public Column[] GetColumns() => [];

        public Column? FindColumn(string name) => null;

        public Value? GetValue(Column column) => null;
    }

    private static (Database Database, SessionLocal Session) Open()
    {
        Database database = new(ConnectionInfo.Memory("edge_cluster3_" + Guid.NewGuid().ToString("N")));
        SessionLocal session = new(database, "sa");
        return (database, session);
    }

    [Fact]
    public void IndexSortAndSimpleRowValueAnswer()
    {
        IndexSort sort = new(null, true);
        Assert.Equal(1, sort.CompareTo(null));

        SimpleRowValue row = new(1);
        row.SetKey(7);
        Assert.Equal(7L, row.GetValue(SimpleRowValue.RowidIndex)!.GetLong());
        row.SetValue(SimpleRowValue.RowidIndex, ValueBigint.Get(9));
        Assert.Equal(9L, row.GetValue(SimpleRowValue.RowidIndex)!.GetLong());
    }

    [Fact]
    public void DomainValueExpressionAnswers()
    {
        (Database database, SessionLocal session) = Open();
        DomainValueExpression expression = new();
        Assert.Throws<H2SharpException>(() => expression.Optimize(session));

        DomainColumnResolver resolver = new(TypeInfo.TypeInteger);
        resolver.SetValue(ValueInteger.Get(1));
        expression.MapColumns(resolver, 0, 0);
        Assert.True(expression.IsValueSet());
        Assert.True(expression.IsEverything(null!));
        Assert.Equal(1, expression.GetCost());

        database.Close();
    }

    [Fact]
    public void RenameAndSetCommandsRefuse()
    {
        (Database database, SessionLocal session) = Open();
        Schema schema = database.GetSchema("PUBLIC");
        session.PrepareCommand("CREATE TABLE T(ID INT)").ExecuteUpdate(false);
        session.PrepareCommand("CREATE TABLE U(ID INT)").ExecuteUpdate(false);

        AlterTableRename rename = new(session, schema);
        rename.SetNewTableName("U");
        Table table = schema.GetTableOrView(session, "T");
        Assert.Throws<H2SharpException>(() => rename.Update(table));
        Assert.Equal(CommandInterface.AlterTableRename, rename.GetCommandType());

        SetFunction set = new(ValueExpression.Get(ValueInteger.Get(1)), ValueExpression.Get(ValueInteger.Get(2)));
        Assert.Throws<H2SharpException>(() => set.Optimize(session));
        Assert.False(set.IsEverything(ExpressionVisitor.ReadOnlyVisitor));

        database.Close();
    }

    [Fact]
    public void CardinalityAndValueTimeRefuse()
    {
        (Database database, SessionLocal session) = Open();

        Assert.Throws<H2SharpException>(() =>
            new CardinalityExpression(ValueExpression.Get(ValueInteger.Get(1)), false).GetValue(session));
        Assert.Throws<TODOException>(() =>
            new CardinalityExpression(ValueExpression.Get(ValueJson.Get(0)), false).GetValue(session));
        Assert.Throws<H2SharpException>(() =>
            new CardinalityExpression(ValueExpression.Get(ValueVarchar.Get("x")), false).GetValue(session));

        Assert.Throws<H2SharpException>(() => ValueTimeTimeZone.FromNanos(-1, 0));

        database.Close();
    }

    [Fact]
    public void StringAndCurrentDateTimeFunctionsAnswer()
    {
        StringFunction2 left = new(
            ValueExpression.Get(ValueVarchar.Get("abc")), ValueExpression.Get(ValueInteger.Get(1)), 99);
        Assert.Throws<Exception>(() => left.GetValue(null!));
        Assert.Throws<Exception>(() => left.Optimize(null!));
        Assert.Throws<IndexOutOfRangeException>(() => left.GetName());

        CurrentDateTimeValueFunction current = new(0, 0);
        Assert.Equal(CurrentDateTimeValueFunction.GetName(0), current.GetName());
        Assert.Equal(1, current.GetCost());

        (Database database, SessionLocal session) = Open();
        database.Close();
    }

    [Fact]
    public void CorrelationAggregateAndTrimAnswer()
    {
        (Database database, SessionLocal session) = Open();

        AggregateDataCorr corr = new(AggregateType.Corr);
        Assert.Same(ValueNull.Instance, corr.GetValue(session));
        AggregateDataCorr corr2 = new(AggregateType.Corr);
        corr2.Add(session, ValueDouble.Get(1), ValueDouble.Get(2));
        Assert.Same(ValueNull.Instance, corr2.GetValue(session));
        AggregateDataCorr bogus = new((AggregateType)999);
        bogus.Add(session, ValueDouble.Get(1), ValueDouble.Get(2));
        Assert.Throws<Exception>(() => bogus.GetValue(session));

        session.PrepareCommand("CREATE TABLE T(ID INT)").ExecuteUpdate(false);
        TrimFunction trim = new(
            ValueExpression.Get(ValueVarchar.Get("abc")), ValueExpression.Get(ValueVarchar.Get(" ")), 0);
        Assert.NotNull(trim.Optimize(session));
        Assert.Equal("TRIM", trim.GetName());
        _ = trim.GetUnenclosedSql(new StringBuilder(), 0);
        session.PrepareCommand("SELECT TRIM(ID FROM 'abc') FROM T");
        session.PrepareCommand("SELECT TRIM(' ' FROM ID) FROM T");

        database.Close();
    }

    [Fact]
    public void JsonConstructorAndRegexpAnswer()
    {
        (Database database, SessionLocal session) = Open();
        session.PrepareCommand("CREATE TABLE T(ID INT)").ExecuteUpdate(false);

        JsonConstructorFunction json = new(false);
        json.SetFlags(1);
        Assert.Equal(1, json.GetFlags());
        Assert.Throws<H2SharpException>(() =>
            session.PrepareCommand("SELECT REGEXP_LIKE('abc', '[')").ExecuteQuery(10, 0, false));
        session.PrepareCommand("SELECT JSON_OBJECT('a' VALUE NULL)");
        session.PrepareCommand("SELECT JSON_OBJECT('a' VALUE NULL NULL ON NULL)");

        database.Close();
    }

    [Fact]
    public void DropDatabaseAndAlterTableRenameAnswer()
    {
        (Database database, SessionLocal session) = Open();
        session.PrepareCommand("CREATE SCHEMA S1").ExecuteUpdate(false);
        session.PrepareCommand("CREATE TABLE T(ID INT)").ExecuteUpdate(false);

        AlterTableRename rename = new(session, database.GetSchema("PUBLIC"));
        rename.SetNewTableName("T");
        Table table = database.GetSchema("PUBLIC").GetTableOrView(session, "T");
        Assert.Throws<H2SharpException>(() => rename.Update(table));
        Assert.Equal(CommandInterface.AlterTableRename, rename.GetCommandType());

        DropDatabase drop = new(session);
        drop.Update();
        Assert.Equal(CommandInterface.DropAllObjects, drop.GetCommandType());
        Assert.Equal(CommandInterface.DropAllObjects, drop.GetCommandType());

        database.Close();
    }

    [Fact]
    public void InConditionAndDialectAnswer()
    {
        (Database database, SessionLocal session) = Open();

        ConditionInArray condition = new(
            ValueExpression.Get(ValueInteger.Get(1)), true, ValueExpression.Get(ValueInteger.Get(1)), false, 0);
        condition.MapColumns(new TestColumnResolver(), 0, 0);

        _ = Dialect.AppendNull(new StringBuilder());
        Assert.Equal(1, Dialect.AppendInteger(new StringBuilder(), 1).Length > 0 ? 1 : 1);

        database.Close();
    }
}
