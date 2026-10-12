/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What an index can serve, checked where the answers are easy to confuse.
 *
 * A column that is not in the index is -1, not an exception: it is the answer the planner asks
 * for, and an index that does not contain the column is an index that cannot serve the condition
 * on it. And an IN over two columns needs those two in that order — an index over the same two
 * in the other order is a different key, and it would answer rows the condition never asked for.
 */
using System;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Tables;
using DbIndex = H2Sharp.Indexes.Index;
using Xunit;

namespace H2Sharp.Tests;

public sealed class IndexPlanTests
{
    private static SessionLocal NewSession()
    {
        return new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:index_plan_tests")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = Values.CompareMode.GetInstance(null, 0),
        };
    }

    /// <summary>
    /// An index with the columns it was given, for the two methods under test.
    /// </summary>
    /// <remarks>
    /// A table builds its primary index without columns in this port, which is a gap of
    /// <c>MVTable</c> and not of the two methods here. This subclass holds the columns the way
    /// the reference's callers hand them over, so that what is tested is the lookup and the IN
    /// check rather than the table's construction.
    /// </remarks>
    private sealed class ColumnsIndex : DbIndex
    {
        internal ColumnsIndex(Tables.Table? table, int id, string name, Tables.IndexColumn[] columns)
            : base(table, id, name, columns, 0, null)
        {
        }

        public override void Close(SessionLocal session)
        {
        }
    }

    [Fact]
    public void AColumnNotInTheIndexIsMinusOne()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Tables.Column id = new("ID", Values.TypeInfo.TypeInteger, null, 0);
            Tables.Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                3, "T", [id], store, persistData: false);

            // A column only becomes the column when it belongs to the table: Equals is on the
            // table and the name, and a column created before its table has no table yet.
            id.SetTable(table, 0);

            DbIndex index = new ColumnsIndex(table, 7, "I", [new Tables.IndexColumn(id, ascending: true)]);

            // The widened column is still found: Equals is on the table and the name, so a
            // column that was created and later changed type is the same column -- but only when
            // it belongs to the same table. A column without a table is no column at all, and
            // Equals says so rather than comparing names in the air.
            Assert.Equal(0, index.GetColumnIndex(new Tables.Column("ID", Values.TypeInfo.TypeBigint, table, 0)));
            Assert.Equal(-1, index.GetColumnIndex(new Tables.Column("NOPE", Values.TypeInfo.TypeInteger, table, 7)));
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void AnEmptyPrepareWalksNowhere()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Tables.Column id = new("ID", Values.TypeInfo.TypeInteger, null, 0);
            Tables.Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                3, "T", [id], store, persistData: false);
            id.SetTable(table, 0);

            // A cursor prepared with no conditions has no bounds and no list: there is nothing
            // to walk, so Next answers false instead of starting a scan it was never given.
            // Previous is not a walk at all -- the reference throws an internal error, because
            // bounds prepared forward cannot be walked backwards.
            Tables.IndexCursor cursor = new();
            cursor.Prepare(session, []);
            Assert.False(cursor.IsAlwaysFalse());
            Assert.False(cursor.Next());
            Assert.Null(cursor.GetStart());
            Assert.Null(cursor.GetEnd());
            Assert.ThrowsAny<Exception>(() => cursor.Previous());
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void AnAlwaysFalseConditionEmptiesTheResult()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Tables.Column id = new("ID", Values.TypeInfo.TypeInteger, null, 0);
            Tables.Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                3, "T", [id], store, persistData: false);
            id.SetTable(table, 0);

            Tables.TableFilter filter = new(session, table, "T", rightsChecked: true, null, 0, null);
            var column = new H2Sharp.Expressions.ExpressionColumn(
                id, filter, 0);
            var condition = H2Sharp.Indexes.IndexCondition.Get(
                H2Sharp.Expressions.Conditions.Comparison.False,
                column,
                H2Sharp.Expressions.ValueExpression.Get(H2Sharp.Values.ValueBoolean.Get(false)));

            Tables.IndexCursor cursor = new();
            cursor.Prepare(session, [condition]);
            Assert.True(cursor.IsAlwaysFalse());
            Assert.False(cursor.Next());
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void AnInOverTwoColumnsNeedsThoseTwoInThatOrder()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Tables.Column id = new("ID", Values.TypeInfo.TypeInteger, null, 0);
            Tables.Column name = new("NAME", Values.TypeInfo.TypeVarchar, null, 1);
            Tables.Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                3, "T", [id, name], store, persistData: false);

            DbIndex single = new ColumnsIndex(table, 7, "I1", [new Tables.IndexColumn(id, ascending: true)]);
            DbIndex pair = new ColumnsIndex(
                table, 8, "I2",
                [new Tables.IndexColumn(id, ascending: true), new Tables.IndexColumn(name, ascending: true)]);

            Assert.True(Tables.IndexCursor.CanUseIndexForIn(single, [id]));
            Assert.False(Tables.IndexCursor.CanUseIndexForIn(single, [name]));
            Assert.False(Tables.IndexCursor.CanUseIndexForIn(pair, [name, id]));
            Assert.False(Tables.IndexCursor.CanUseIndexForIn(null, [id]));
            Assert.False(Tables.IndexCursor.CanUseIndexForIn(single, null));
        }
        finally
        {
            store.Dispose();
        }
    }
}
