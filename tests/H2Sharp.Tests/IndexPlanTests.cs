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

        /// <summary>
        /// Reads through to the base row factories, which refuse until the database path lands.
        /// </summary>
        internal H2Sharp.Result.RowFactory ProbeRowFactory() => GetRowFactory();

        /// <summary>
        /// Reads through to the base unique factory, same refusal.
        /// </summary>
        internal H2Sharp.Result.RowFactory ProbeUniqueRowFactory() => GetUniqueRowFactory();

        /// <summary>
        /// Nothing is stored, so there is nothing to rebuild after opening.
        /// </summary>
        public override bool NeedRebuild() => false;

        /// <summary>
        /// A test double prices like a scan of nothing: the methods under test here are the
        /// lookup and the mask check, and a cost is not one of them.
        /// </summary>
        public override double GetCost(
            SessionLocal session,
            int[]? masks,
            Tables.TableFilter[]? filters,
            int filter,
            Result.SortOrder? sortOrder,
            H2Sharp.Command.Query.AllColumnsForPlan? allColumnsSet,
            bool isSelectCommand) => 0;
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
    public void AnIndexWithoutADatabaseRefusesARowFactory()
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

            ColumnsIndex index = new(table, 7, "I", [new Tables.IndexColumn(id, ascending: true)]);

            // A loud refusal at the access, not a null that fails later far from the cause:
            // the factories need the database path, which is not ported.
            Assert.Throws<H2Sharp.TODOException>(() => index.ProbeRowFactory());
            Assert.Throws<H2Sharp.TODOException>(() => index.ProbeUniqueRowFactory());
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

    /// <summary>
    /// An index with one unique leading column, like the delegate the reference wraps a primary
    /// key in: an equality on all of a unique key costs 3 rows, because a lookup that stops at
    /// the first match reads that much and no more.
    /// </summary>
    private sealed class UniqueIndex : DbIndex
    {
        internal UniqueIndex(Tables.Table? table, int id, string name, Tables.IndexColumn[] columns)
            : base(table, id, name, columns, 1, null)
        {
        }

        public override void Close(SessionLocal session)
        {
        }

        /// <summary>
        /// Nothing is stored, so there is nothing to rebuild after opening.
        /// </summary>
        public override bool NeedRebuild() => false;

        public override double GetCost(
            SessionLocal session,
            int[]? masks,
            Tables.TableFilter[]? filters,
            int filter,
            Result.SortOrder? sortOrder,
            H2Sharp.Command.Query.AllColumnsForPlan? allColumnsSet,
            bool isSelectCommand)
        {
            // Row count zero, as on an empty table: GetCostRangeIndex adds the offset itself,
            // and passing anything else would price a table that was never built.
            return 10 * GetCostRangeIndex(
                masks, 0, filters, filter, sortOrder,
                isScanIndex: true, allColumnsSet, isSelectCommand);
        }
    }

    [Fact]
    public void AUniqueLookupCosts230AndAScanCosts10200()
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

            DbIndex index = new UniqueIndex(
                table, 7, "I", [new Tables.IndexColumn(id, ascending: true)]);

            // The mask says EQUALITY on column 0, which is what a WHERE ID = ? becomes by the
            // time the planner prices it. Neither number below is hand-computed: both are what
            // the reference answers for the same table, the same mask and the same row count,
            // probed on the JVM rather than derived here. A planner that priced them close
            // would pick a scan where a lookup would do.
            int[] masks = new int[1];
            masks[0] = H2Sharp.Indexes.IndexCondition.Equality;
            Assert.Equal(230, index.GetCost(session, masks, null, 0, null, null, isSelectCommand: true));
            Assert.Equal(10200, index.GetCost(session, null, null, 0, null, null, isSelectCommand: true));
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
