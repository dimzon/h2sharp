/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The table's own behaviour: rows in, rows out, and the keys it hands them.
 *
 * The test drives the table the way a statement does — through a session — and never takes a
 * transaction itself. That is not a convenience: the transaction belongs to the session, and a
 * caller that opened its own would read and write on a transaction the statement cannot see,
 * which is a difference no assertion here would catch and a real caller would feel.
 *
 * The corpus is small and every assertion is a rule:
 *
 * * **Keys are handed out in order and never twice.** Adding rows and reading them back in key
 *   order shows both halves: a counter that went backwards would come back out of order, and one
 *   that reused a number would lose a row.
 * * **A row comes back as the values that went in** — not merely "equal", because equal would
 *   pass with the values swapped between two columns of the same type.
 * * **Removing a row removes it and only it**, and removing one that is not there fails with
 *   the reference's error rather than quietly doing nothing.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using H2Sharp.Engine;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Db;
using H2Sharp.MvStore.Tx;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class MVTableTests : IDisposable
{
    private readonly MVStore store = MVStore.Open(null!);

    private readonly TransactionStore transactions;

    private readonly SessionLocal session;

    public MVTableTests()
    {
        transactions = new TransactionStore(store);
        transactions.Init();
        session = new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:table_tests")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
    }

    public void Dispose()
    {
        transactions.Dispose();
        store.Close();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RowsComeBackAsTheValuesThatWentIn()
    {
        MVTable table = NewTable();
        long first = table.AddRow(session, Row(1, "Ada"));
        long second = table.AddRow(session, Row(2, "Grace"));

        Assert.True(second > first);

        SearchRow? a = table.GetRow(session, first);
        Assert.NotNull(a);
        Assert.Equal(ValueInteger.Get(1), a.GetValue(0));
        Assert.Equal(ValueVarchar.Get("Ada"), a.GetValue(1));

        SearchRow? b = table.GetRow(session, second);
        Assert.NotNull(b);
        Assert.Equal(ValueInteger.Get(2), b.GetValue(0));
        Assert.Equal(ValueVarchar.Get("Grace"), b.GetValue(1));
    }

    [Fact]
    public void RowsComeBackInKeyOrder()
    {
        MVTable table = NewTable();
        foreach (int i in new[] { 5, 3, 9, 1, 7 })
        {
            table.AddRow(session, Row(i, "v" + i));
        }

        long[] keys = [.. table.Scan(session).Select(r => r.GetKey())];

        Assert.Equal(keys.OrderBy(k => k), keys);
        Assert.Equal(5, table.GetRowCountApproximation(session));
    }

    [Fact]
    public void RemovingARowRemovesOnlyIt()
    {
        MVTable table = NewTable();
        long keep = table.AddRow(session, Row(1, "keep"));
        long drop = table.AddRow(session, Row(2, "drop"));

        table.RemoveRow(session, drop);

        Assert.NotNull(table.GetRow(session, keep));
        Assert.Null(table.GetRow(session, drop));
        Assert.Equal(1, table.GetRowCountApproximation(session));
    }

    [Fact]
    public void RemovingARowThatIsNotThereFails()
    {
        MVTable table = NewTable();
        table.AddRow(session, Row(1, "only"));

        H2SharpException ex = Assert.Throws<H2SharpException>(() => table.RemoveRow(session, 999));
        Assert.Equal(Api.ErrorCode.RowNotFoundWhenDeleting1, ex.H2ErrorCode);
    }

    [Fact]
    public void UpdatingReplacesTheValues()
    {
        MVTable table = NewTable();
        long key = table.AddRow(session, Row(1, "before"));
        SearchRow before = table.GetRow(session, key)!;

        Row after = Row(1, "after");
        after.SetKey(key);
        table.UpdateRow(session, before, after);

        SearchRow? read = table.GetRow(session, key);
        Assert.NotNull(read);
        Assert.Equal(ValueVarchar.Get("after"), read.GetValue(1));
    }

    [Fact]
    public void TheTableDescribesItselfTheWayItWasCreated()
    {
        MVTable table = NewTable();

        Assert.Equal("PERSON", table.GetTableName());
        Assert.Equal(2, table.GetColumnCount());
        Assert.Equal(["ID", "NAME"], table.GetColumns().Select(c => c.GetName()));

        // A table built without a key constraint has no primary key to answer with: the
        // reference's getPrimaryKey throws INDEX_NOT_FOUND_1 when findPrimaryKey is null, and
        // the rows live in the scan index.
        Assert.Null(table.FindPrimaryKey());
        Assert.NotNull(table.GetScanIndex(session));
        Assert.Contains("CREATE TABLE", table.GetCreateSql(), StringComparison.Ordinal);

        // "CHARACTER VARYING", not "VARCHAR": the name table is the reference's, and the
        // reference prints the SQL-standard spelling. A test that looked for "VARCHAR" here
        // would fail on a correct implementation.
        Assert.Contains("CHARACTER VARYING", table.GetCreateSql(), StringComparison.Ordinal);
    }

    [Fact]
    public void ATemplateRowHasRoomForEveryColumn()
    {
        MVTable table = NewTable();

        // A template is not a row: every value in it is null, and it exists so that a search
        // has something to fill in. What is asserted is the room, not the contents -- a template
        // with fewer slots than columns would drop the values a search wrote past its end.
        Row template = table.GetTemplateRow();
        Assert.Equal(2, template.GetColumnCount());
        Assert.Null(template.GetValue(0));
        Assert.Null(template.GetValue(1));
    }

    private static Row Row(int id, string name)
        => new DefaultRow([ValueInteger.Get(id), ValueVarchar.Get(name)]);

    private MVTable NewTable()
    {
        Column id = new("ID", TypeInfo.TypeInteger, null, 0);
        Column name = new("NAME", TypeInfo.TypeVarchar, null, 1);
        MVTable table = new(session.GetDatabase(), null, 1, "PERSON", [id, name], persistIndexes: false, persistData: false);
        foreach (Column column in table.GetColumns())
        {
            column.SetTable(table, column.GetColumnId());
        }

        return table;
    }
}
