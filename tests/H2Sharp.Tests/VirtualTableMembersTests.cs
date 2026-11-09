/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 101: the virtual tables a statement builds but SQL hides — SYSTEM_RANGE with
 * its step arithmetic and accessors, and the OLD/NEW/FINAL delta table with its column
 * and index members.
 */
using System;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Command;
using H2Sharp.Command.Dml;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.MvStore.Db;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class VirtualTableMembersTests
{
    private static (Database Database, SessionLocal Session, Schema Schema) NewDatabase(string name)
    {
        Database database = new(ConnectionInfo.Memory(name + "_" + Guid.NewGuid().ToString("N")));
        SessionLocal session = new(database, "sa");
        return (database, session, database.GetSchema("PUBLIC"));
    }

    private static RangeTable Range(Schema schema, long min, long max, long? step = null)
    {
        Expression minExpression = ValueExpression.Get(ValueBigint.Get(min));
        Expression maxExpression = ValueExpression.Get(ValueBigint.Get(max));
        return step is null
            ? new RangeTable(schema, minExpression, maxExpression)
            : new RangeTable(schema, minExpression, maxExpression, ValueExpression.Get(ValueBigint.Get(step.Value)));
    }

    [Fact]
    public void TheRangeTableExposesItsSingleColumnAndStepArithmetic()
    {
        var (database, session, schema) = NewDatabase("range_direct");

        RangeTable ascending = Range(schema, 1, 10, 3);
        Assert.Equal(RangeTable.Name, ascending.GetTableName());
        _ = ascending.GetTableType();
        Assert.Equal(1, ascending.GetColumnCount());
        Assert.Equal("X", ascending.GetColumn(0).GetName());
        Assert.Single(ascending.GetColumns());
        Assert.NotNull(ascending.GetScanIndex(session));
        Assert.Single(ascending.GetIndexes());
        Assert.Same(ascending.GetScanIndex(session), ascending.GetIndexFor(session, ascending.GetTemplateRow()));
        Assert.True(ascending.CanGetRowCount(session));
        Assert.Equal(1, ascending.GetMin(session));
        Assert.Equal(10, ascending.GetMax(session));
        Assert.Equal(3, ascending.GetStep(session));
        Assert.Equal(4, ascending.GetRowCount(session));
        Assert.Equal(100, ascending.GetRowCountApproximation(session));
        Assert.Equal(0, ascending.GetMaxDataModificationId());
        Assert.Null(ascending.GetCreateSql());
        Assert.Equal(RangeTable.Name, ascending.GetSql(HasSql.NoCasts));
        Assert.Equal("SYSTEM_RANGE(1, 10, 3)", ascending.GetSql(new StringBuilder(), HasSql.NoCasts).ToString());
        Assert.Equal(0, ascending.GetId());
        Assert.False(ascending.IsClosed());
        Assert.Same(schema, ascending.GetSchema());

        // A default step is one, and a range that runs the wrong way is empty.
        RangeTable unit = Range(schema, 1, 5);
        Assert.Equal(1, unit.GetStep(session));
        Assert.Equal(5, unit.GetRowCount(session));
        Assert.Equal(0, Range(schema, 10, 1, 1).GetRowCount(session));
        Assert.Equal(0, Range(schema, 1, 10, -1).GetRowCount(session));

        // The step of zero has no rows to count.
        RangeTable zero = Range(schema, 1, 10, 0);
        Assert.Equal(
            ErrorCode.StepSizeMustNotBeZero,
            Assert.Throws<H2SharpException>(() => zero.GetRowCount(session)).H2ErrorCode);

        Assert.Equal(
            ErrorCode.FeatureNotSupported1,
            Assert.Throws<H2SharpException>(() => ascending.Truncate(session)).H2ErrorCode);
        ascending.Close();
        ascending.RemoveChildrenAndResources(session);
        database.Close();
    }

    [Fact]
    public void TheDeltaTableExposesTheChangedRowsColumns()
    {
        var (database, session, schema) = NewDatabase("delta_direct");
        CommandContainer create = Assert.IsType<CommandContainer>(session.PrepareCommand(
            "CREATE TABLE D(ID INT PRIMARY KEY, VAL INT)"));
        _ = create.ExecuteUpdate(false);
        MVTable table = Assert.IsType<MVTable>(schema.GetTableOrView(session, "D"));

        Insert insert = new(session);
        insert.SetTable(table);
        DataChangeDeltaTable delta = new(schema, insert, DataChangeDeltaTable.ResultOption.FINAL);

        _ = delta.GetTableType();
        Assert.Equal(2, delta.GetColumnCount());
        Assert.Equal("ID", delta.GetColumn(0).GetName());
        Assert.Equal(2, delta.GetColumns().Length);
        Assert.Single(delta.GetIndexes());
        Assert.NotNull(delta.GetIndexFor(session, delta.GetTemplateRow()));
        Assert.False(delta.CanGetRowCount(session));
        Assert.Equal(long.MaxValue, delta.GetRowCount(session));
        Assert.Equal(long.MaxValue, delta.GetRowCountApproximation(session));
        Assert.Null(delta.GetCreateSql());
        Assert.Equal(0, delta.GetId());
        Assert.False(delta.IsClosed());
        Assert.Same(schema, delta.GetSchema());
        _ = delta.GetSql(HasSql.NoCasts);
        _ = delta.GetSql(new StringBuilder(), HasSql.NoCasts);

        Assert.Equal(
            ErrorCode.FeatureNotSupported1,
            Assert.Throws<H2SharpException>(() => delta.Truncate(session)).H2ErrorCode);
        delta.Close();
        delta.RemoveChildrenAndResources(session);
        database.Close();
    }
}
