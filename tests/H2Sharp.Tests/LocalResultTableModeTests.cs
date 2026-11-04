/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The table-shaped local result: the one the data-change delta tables use, where rows keep
 * the table's key in a hidden last column.
 */
using System;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class LocalResultTableModeTests
{
    private static SessionLocal NewSession()
        => new(new Database(ConnectionInfo.Memory("local_result_table_" + Guid.NewGuid().ToString("N"))), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };

    [Fact]
    public void ForTableCarriesTheHiddenKeyColumn()
    {
        SessionLocal session = NewSession();
        CommandContainer create = Assert.IsType<CommandContainer>(
            session.PrepareCommand("CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR)"));
        _ = create.GetPreparedCommand().Update();
        Table table = session.GetDatabase()!.GetSchema("PUBLIC")!.GetTableOrView(session, "T")!;

        LocalResult result = LocalResult.ForTable(session, table);
        Assert.Equal(2, result.GetVisibleColumnCount());
        Assert.Equal("ID", result.GetColumnName(0));
        Assert.Equal("NAME", result.GetColumnName(1));
        Assert.Equal("T", result.GetTableName(0));

        Row row = Row.Get([ValueInteger.Get(1), ValueVarchar.Get("a")!], SearchRow.MemoryCalculate);
        row.SetKey(77);
        result.AddRowForTable(row);
        result.Done();

        Assert.Equal(1, result.GetRowCount());
        Assert.True(result.Next());
        Row back = result.CurrentRowForTable();
        Assert.Equal(77, back.GetKey());
        Assert.Equal(1, back.GetValue(0)!.GetInt());
        Assert.Equal("a", back.GetValue(1)!.GetString());
        Assert.False(result.Next());
        Assert.True(result.IsAfterLast());
    }

    [Fact]
    public void DeltaTableModeRefusesAShallowCopyOfLobRows()
    {
        SessionLocal session = NewSession();
        LocalResult result = new(
            session, [ValueExpression.Get(ValueInteger.Get(0))], 1, 1);
        result.SetForDataChangeDeltaTable();
        // A delta table's row carries the value the statement wrote, so the LOB is kept as
        // read instead of being cloned to outlive the statement; a shallow copy then cannot
        // share the rows and is refused, which is the reference's answer too.
        result.AddRow(ValueClob.CreateSmall("clob value"));
        result.Done();
        Assert.Null(result.CreateShallowCopy(session));

        // Without the delta-table mode the row's LOB is copied for the result, and a small
        // in-memory LOB is its own copy - that is what the reference's copyToResult answers
        // for it - so the result holds no LOB that would outlive the statement and the
        // shallow copy is allowed.
        LocalResult plain = new(
            session, [ValueExpression.Get(ValueInteger.Get(0))], 1, 1);
        plain.AddRow(ValueClob.CreateSmall("clob value"));
        plain.Done();
        Assert.NotNull(plain.CreateShallowCopy(session));
    }

    [Fact]
    public void PlainResultShallowCopySharesRows()
    {
        SessionLocal session = NewSession();
        LocalResult result = new(
            session, [ValueExpression.Get(ValueInteger.Get(0))], 1, 1);
        result.AddRow(ValueInteger.Get(5));
        result.AddRow(ValueInteger.Get(6));
        result.Done();

        LocalResult copy = Assert.IsType<LocalResult>(result.CreateShallowCopy(session));
        Assert.Equal(2, copy.GetRowCount());
        Assert.Equal(-1, copy.GetRowId());

        // The row arrays are shared: a value replaced in the original's first row is the
        // value the copy answers with. The count is a field copied at creation, so the copy
        // keeps the count it was made with - the reference does the same.
        Assert.True(result.Next());
        result.CurrentRow()[0] = ValueInteger.Get(42);
        Assert.True(copy.Next());
        Assert.Equal(42, copy.CurrentRow()[0]!.GetInt());
        Assert.Equal(0, result.GetRowId());
    }

    [Fact]
    public void ResultSurfaceAnswersTheTrivialQuestions()
    {
        // The parameterless result: no session, so every row fits in memory by definition.
        LocalResult bare = new();
        bare.SetMaxMemoryRows(10);
        Assert.Equal(0, bare.GetRowCount());
        Assert.False(bare.IsLazy());
        Assert.False(bare.NeedToClose());
        Assert.False(bare.IsClosed());

        SessionLocal session = NewSession();
        LocalResult result = new(
            session, [ValueExpression.Get(ValueInteger.Get(0))], 1, 1);
        result.AddRow(ValueInteger.Get(1));
        result.Done();

        Assert.Null(result.GetSchemaName(0));
        Assert.False(result.IsIdentity(0));
        Assert.Equal(0, result.GetFetchSize());
        result.SetFetchSize(100);
        Assert.Equal(0, result.GetFetchSize());
        Assert.Contains("columns: 1", result.ToString(), StringComparison.Ordinal);

        // Removing a row from a result that is not DISTINCT is the reference's internal
        // error: the call has no meaning there.
        Assert.Throws<Exception>(() => result.RemoveDistinct([ValueInteger.Get(1)]));
    }
}
