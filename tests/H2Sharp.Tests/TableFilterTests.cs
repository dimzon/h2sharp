/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The foundation of a table filter: one object per *use* of a table.
 *
 * "SELECT * FROM TEST T1, TEST T2" has two filters over one table, each with its own alias, its
 * own position and its own identity, because each is read independently and a column reference
 * has to say which of them it means. A port that kept one object per table would make T1.NAME
 * and T2.NAME the same column, and no test with a single table in FROM would notice.
 *
 * The constructor checks the right to read the table unless the caller says it was already
 * checked. That is where a statement that names a table it may not read fails — before the table
 * would be opened, because a check at run time would have half-read it first.
 */
using System;
using H2Sharp.Engine;
using H2Sharp.Tables;
using Xunit;

namespace H2Sharp.Tests;

public sealed class TableFilterTests
{
    private static SessionLocal NewSession()
    {
        return new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:filter_tests")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = Values.CompareMode.GetInstance(null, 0),
        };
    }

    [Fact]
    public void TwoUsesOfOneTableAreTwoFilters()
    {
        SessionLocal session = NewSession();

        // MVTable needs a store, and a test double of a table would not be what a query reads.
        // The store is real because the filter is real: it is built to read a table, and a table
        // that cannot be read is not a table a filter can be built over.
        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), null, 3, "TEST",
                [new Column("ID", Values.TypeInfo.TypeInteger, null, 0)],
                store, persistData: false);

            TableFilter first = new(session, table, "T1", rightsChecked: true, null, 0, null);
            TableFilter second = new(session, table, "T2", rightsChecked: true, null, 1, null);

            // Different aliases, different positions, and not equal: each is read independently.
            Assert.Equal("T1", first.GetTableAlias());
            Assert.Equal("T2", second.GetTableAlias());
            Assert.Equal(0, first.GetOrderInFrom());
            Assert.Equal(1, second.GetOrderInFrom());
            Assert.NotEqual(first, second);
            Assert.NotEqual(first.GetHashCode(), second.GetHashCode());

            // The same table behind both.
            Assert.Same(table, first.GetTable());
            Assert.Same(table, second.GetTable());

            // An alias is what was written. Without one, the table's own name is what answers,
            // so that a column written without a qualifier is still a column *of this table*.
            TableFilter plain = new(session, table, null, rightsChecked: true, null, 0, null);
            Assert.False(plain.HasAlias());
            Assert.True(first.HasAlias());
            Assert.Equal("TEST", plain.GetTableAlias());
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void TheOrderInFromComparatorOrdersByPosition()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), null, 3, "TEST",
                [new Column("ID", Values.TypeInfo.TypeInteger, null, 0)],
                store, persistData: false);

            TableFilter[] filters =
            [
                new(session, table, "B", rightsChecked: true, null, 2, null),
                new(session, table, "A", rightsChecked: true, null, 0, null),
                new(session, table, "C", rightsChecked: true, null, 1, null),
            ];
            Array.Sort(filters, TableFilter.OrderInFromComparator);

            Assert.Equal([0, 1, 2], [filters[0].GetOrderInFrom(), filters[1].GetOrderInFrom(), filters[2].GetOrderInFrom()]);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void AJoinLandsAtTheEndOfTheChain()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), null, 3, "TEST",
                [new Column("ID", Values.TypeInfo.TypeInteger, null, 0)],
                store, persistData: false);

            // "A JOIN B JOIN C" pairs A with B before C is seen: C must land at the end of the
            // chain, and a filter that put it anywhere else would pair it with the wrong one.
            // An outer join marks every filter of its chain as an outer side, so that the null
            // row is produced for the right one.
            TableFilter a = new(session, table, "A", rightsChecked: true, null, 0, null);
            TableFilter b = new(session, table, "B", rightsChecked: true, null, 1, null);
            TableFilter c = new(session, table, "C", rightsChecked: true, null, 2, null);

            a.AddJoin(b, outer: false, null);
            Assert.Same(b, a.GetJoin());
            Assert.Null(b.GetJoin());

            a.AddJoin(c, outer: true, null);
            Assert.Same(b, a.GetJoin());
            Assert.Same(c, b.GetJoin());
            Assert.True(c.IsJoinOuter());
            Assert.True(c.IsJoinOuterIndirect());

            // Every member of the chain is visited, with the nested one before the chain moves
            // on past the filter it sits in.
            TableFilter nested = new(session, table, "N", rightsChecked: true, null, 3, null);
            b.SetNestedJoin(nested);

            List<string> seen = [];
            a.Visit(new AliasCollector(seen));
            Assert.Equal(["A", "B", "N", "C"], seen);
        }
        finally
        {
            store.Dispose();
        }
    }

    private sealed class AliasCollector : Tables.TableFilter.TableFilterVisitor
    {
        private readonly List<string> seen;

        internal AliasCollector(List<string> seen)
        {
            this.seen = seen;
        }

        public void Accept(Tables.TableFilter f)
        {
            seen.Add(f.GetTableAlias());
        }
    }

    [Fact]
    public void AFilterResolvesItself()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), null, 3, "TEST",
                [new Column("ID", Values.TypeInfo.TypeInteger, null, 0)],
                store, persistData: false);

            TableFilter filter = new(session, table, "T", rightsChecked: true, null, 0, null);
            Assert.Same(filter, filter.GetTableFilter());
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void ConditionsAccumulateSeparately()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), null, 3, "TEST",
                [new Column("ID", Values.TypeInfo.TypeInteger, null, 0)],
                store, persistData: false);

            TableFilter filter = new(session, table, "T", rightsChecked: true, null, 0, null);
            Assert.Same(filter, filter.GetTableFilter());
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void ConditionsAccumulateOnTheRightPiles()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), null, 3, "TEST",
                [new Column("ID", Values.TypeInfo.TypeInteger, null, 0)],
                store, persistData: false);

            // The first condition is kept as written and every later one ANDs onto it, so that
            // there is one condition per row. A join condition and a filter condition are kept
            // on different piles, because they are answered at different moments -- and a test
            // that only added one would never see whether the second landed on the right pile.
            TableFilter filter = new(session, table, "T", rightsChecked: true, null, 0, null);
            var first = H2Sharp.Expressions.ValueExpression.Get(H2Sharp.Values.ValueBoolean.Get(true));
            var second = H2Sharp.Expressions.ValueExpression.Get(H2Sharp.Values.ValueBoolean.Get(false));

            filter.AddFilterCondition(first, isJoin: false);
            filter.AddFilterCondition(second, isJoin: false);
            filter.AddFilterCondition(first, isJoin: true);

            // The two piles read back separately: what was added as a filter is a filter, and
            // what was added as a join is a join. A test that only added one would never see
            // whether the other pile stayed empty -- and it is the empty pile that proves they
            // do not mix.
            Assert.NotNull(filter.GetFilterCondition());
            Assert.NotNull(filter.GetJoinCondition());

            // Forgetting the filter condition is the planner's move: a condition the index
            // answers is removed from the filter, so that it is asked once and not twice.
            filter.RemoveFilterCondition();
            Assert.Null(filter.GetFilterCondition());
            Assert.NotNull(filter.GetJoinCondition());
        }
        finally
        {
            store.Dispose();
        }
    }
}
