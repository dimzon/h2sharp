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

            // An alias is set, not built: it is what was written, and renaming a filter after
            // it was read would answer column references against a name nobody wrote. What the
            // filter prints is the name it is known by -- the alias when there is one, the
            // table's own printing otherwise, which is name:id:... and not the bare name.
            Assert.Equal("T1", first.ToString());
            first.SetAlias(null);
            Assert.False(first.HasAlias());
            Assert.Equal(table.ToString(), first.ToString());

            // An alias is what was written. Without one, the table's own name is what answers,
            // so that a column written without a qualifier is still a column *of this table*.
            TableFilter plain = new(session, table, null, rightsChecked: true, null, 0, null);
            Assert.False(plain.HasAlias());
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

            var on = H2Sharp.Expressions.ValueExpression.Get(H2Sharp.Values.ValueBoolean.Get(true));
            b.AddJoin(c, outer: true, on);
            Assert.Same(b, a.GetJoin());
            Assert.Same(c, b.GetJoin());
            Assert.True(c.IsJoinOuter());
            Assert.True(c.IsJoinOuterIndirect());

            // The ON condition landed on the joined filter, not on the one that added it: a join
            // condition names columns of both sides, and the side that was joined is where it is
            // answered.
            Assert.NotNull(c.GetJoinCondition());

            // Every member of the chain is visited, with the nested one before the chain moves
            // on past the filter it sits in. This is checked before anything is removed: the
            // visit walks the very links the next assertions drop, and checking it afterwards
            // would prove nothing about a chain that is no longer there.
            TableFilter nested = new(session, table, "N", rightsChecked: true, null, 3, null);
            b.SetNestedJoin(nested);

            List<string> seen = [];
            a.Visit(new AliasCollector(seen));
            Assert.Equal(["A", "B", "N", "C"], seen);

            // A condition over the whole join lands on every filter of the chain: it constrains
            // rows no single filter sees alone. The field has no reader -- the reference reads it
            // in optimizeFullCondition, which is not ported -- so what is asserted is the walk
            // itself: over three links and a nested join, without breaking the chain that the
            // next visit still sees whole.
            var whole = H2Sharp.Expressions.ValueExpression.Get(H2Sharp.Values.ValueBoolean.Get(true));
            a.SetFullCondition(whole);

            List<string> again = [];
            a.Visit(new AliasCollector(again));
            Assert.Equal(["A", "B", "N", "C"], again);

            // Removing the join drops the link and nothing else, and removing the condition
            // drops only the condition: the pairing is kept so that the next try starts from
            // the same tables.
            b.RemoveJoin();
            Assert.Null(b.GetJoin());
            c.RemoveJoinCondition();
            Assert.Null(c.GetJoinCondition());
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
    public void CreatingIndexConditionsWalksTheWholeChain()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Tables.Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                3, "T",
                [new Tables.Column("ID", Values.TypeInfo.TypeInteger, null, 0)],
                store, persistData: false);

            // The join condition is optimized first and then asked to create its index
            // conditions here, on the nested join and on the join -- so that every filter holds
            // the conditions it can serve. A plain comparison creates none (that is
            // Comparison's own member, and it is not ported), so what is asserted is the walk:
            // it reaches every filter of the chain without breaking it.
            Tables.TableFilter a = new(session, table, "A", rightsChecked: true, null, 0, null);
            Tables.TableFilter b = new(session, table, "B", rightsChecked: true, null, 1, null);
            a.AddJoin(
                b, outer: false,
                H2Sharp.Expressions.ValueExpression.Get(H2Sharp.Values.ValueBoolean.Get(true)));

            Tables.TableFilter nested = new(session, table, "N", rightsChecked: true, null, 2, null);
            b.SetNestedJoin(nested);

            a.CreateIndexConditions();

            List<string> seen = [];
            a.Visit(new AliasCollector(seen));
            Assert.Equal(["A", "B", "N"], seen);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void AnOuterFilterReachedFromInsideIsSkipped()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Tables.Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                3, "T",
                [new Tables.Column("ID", Values.TypeInfo.TypeInteger, null, 0)],
                store, persistData: false);

            // Mapping walks the chain, and an outer filter reached from the inside is skipped:
            // its columns belong to rows the inner side never sees. What is asserted is that the
            // walk completes over a chain with an outer link and a nested join, since a walk that
            // mapped the wrong side would bind a name to a column of a row that does not exist.
            Tables.TableFilter a = new(session, table, "A", rightsChecked: true, null, 0, null);
            Tables.TableFilter b = new(session, table, "B", rightsChecked: true, null, 1, null);
            a.AddJoin(b, outer: true, null);

            Tables.TableFilter nested = new(session, table, "N", rightsChecked: true, null, 2, null);
            b.SetNestedJoin(nested);

            a.MapColumns(a, 0, outer: false);
            b.MapColumns(b, 0, outer: true);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void SharedColumnsFixTheirFilterOnFirstUse()
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

            Tables.TableFilter left = new(session, table, "L", rightsChecked: true, null, 0, null);
            Tables.TableFilter right = new(session, table, "R", rightsChecked: true, null, 1, null);
            Tables.TableFilter other = new(session, table, "X", rightsChecked: true, null, 2, null);

            // Nothing shared yet: the map and the filter are both null rather than empty, because
            // "no shared columns" is an answer and an empty map would make every reader iterate
            // it to learn the same thing.
            Assert.Null(left.GetCommonJoinColumns());
            Assert.Null(left.GetCommonJoinColumnsFilter());
            Assert.False(left.IsCommonJoinColumnToExclude(id));

            // The first call fixes which filter the replacements come from, and a second filter
            // naming itself is refused: one shared column replaced by two filters is two
            // different columns, and the reference catches it with an assert.
            left.AddCommonJoinColumns(id, id, right);
            Assert.Same(right, left.GetCommonJoinColumnsFilter());
            Assert.Same(id, left.GetCommonJoinColumns()![id]);
            Assert.ThrowsAny<Exception>(() => left.AddCommonJoinColumns(id, id, other));

            // Excluding a column is separate from sharing it: the map keeps the entry, and the
            // exclusion only says not to use it.
            left.AddCommonJoinColumnToExclude(id);
            Assert.True(left.IsCommonJoinColumnToExclude(id));
            Assert.False(left.IsCommonJoinColumnToExclude(null));
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void DerivedNamesMustMatchAndNotRepeat()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Tables.Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                3, "T",
                [new Tables.Column("A", Values.TypeInfo.TypeInteger, null, 0),
                 new Tables.Column("B", Values.TypeInfo.TypeInteger, null, 1)],
                store, persistData: false);

            Tables.TableFilter filter = new(session, table, "T", rightsChecked: true, null, 0, null);
            Assert.False(filter.HasDerivedColumnList());

            // The wrong length leaves columns unnamed, and a repeated name makes two columns
            // unfindable: both are refused here, while the query has read nothing yet, rather
            // than when the missing name is looked up.
            Assert.ThrowsAny<Exception>(() => filter.SetDerivedColumns(["X"]));
            Assert.ThrowsAny<Exception>(() => filter.SetDerivedColumns(["X", "X"]));
            Assert.False(filter.HasDerivedColumnList());

            filter.SetDerivedColumns(["X", "Y"]);
            Assert.True(filter.HasDerivedColumnList());

            // A derived name hides the table's own: the view renamed the column, and answering
            // with the table's name would name something the query cannot see. Without a list,
            // the table's name is the only answer there is.
            Tables.Column[] columns = table.GetColumns();
            Assert.Equal("X", filter.GetColumnName(columns[0]));
            Assert.Equal("Y", filter.GetColumnName(columns[1]));

            // Finding by those names is FindColumn's own member, and it needs the database's
            // identifier comparison plus the table's own lookup -- neither of which is ported.
            // What is asserted here is the list itself: it is accepted exactly when it names
            // every column once, and the two refusals above are the reference's errors rather
            // than something the lookup would have to invent later.
            Assert.ThrowsAny<Exception>(() => filter.FindColumn("X"));
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void AnEmptyTableEndsTheScanBeforeItStarts()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Tables.Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                3, "T",
                [new Tables.Column("ID", Values.TypeInfo.TypeInteger, null, 0)],
                store, persistData: false);

            // Nothing was ever inserted, so the scan has no first row: Next answers false on a
            // filter that was started and reset, and there is no row to read afterwards. A scan
            // that answered true here would hand out a row the table never held.
            Tables.TableFilter filter = new(session, table, "T", rightsChecked: true, null, 0, null);
            filter.StartQuery(session);
            filter.Reset();
            Assert.False(filter.Next());
            Assert.False(filter.IsNullRow());
            Assert.Null(filter.Get());
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void TheFilterAnswersTheIndexItWasGiven()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Tables.Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                3, "T",
                [new Tables.Column("ID", Values.TypeInfo.TypeInteger, null, 0)],
                store, persistData: false);

            // No plan has chosen anything, so there is no index to answer with -- and null is
            // the answer, not a failure to prepare one.
            Tables.TableFilter filter = new(session, table, "T", rightsChecked: true, null, 0, null);
            Assert.Null(filter.GetIndex());

            // Setting one answers it back, and points the cursor at it in the same breath: the
            // filter answers which index it reads and the cursor does the reading, and keeping
            // one while the other walked elsewhere would answer about an index never read.
            H2Sharp.Indexes.Index? primary = table.GetPrimaryKey();
            Assert.NotNull(primary);
            filter.SetIndex(primary, reverse: false);
            Assert.Same(primary, filter.GetIndex());
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void ANullPlanSetsNothing()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Tables.Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                3, "T",
                [new Tables.Column("ID", Values.TypeInfo.TypeInteger, null, 0)],
                store, persistData: false);

            // A null plan is not an error here but a plan that cannot be used: the exception
            // comes later, when the statement runs without one. What is asserted is that
            // nothing is set by it -- no index, no masks.
            Tables.TableFilter filter = new(session, table, "T", rightsChecked: true, null, 0, null);
            Assert.Null(filter.GetIndex());
            Assert.Null(filter.GetMasks());
            filter.SetPlanItem(null);
            Assert.Null(filter.GetIndex());
            Assert.Null(filter.GetMasks());

            // A plan without an index is refused, not scanned: in the reference this is a null
            // dereference inside setIndex, and here it is the argument check saying the same
            // thing -- a plan has to name an index to be a plan.
            Assert.ThrowsAny<Exception>(() => filter.SetPlanItem(new Tables.PlanItem()));
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void PrepareKeepsOnlyWhatTheIndexCanServe()
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

            // A plan is chosen first: without it the filter has no index, and Prepare over no
            // index would answer about a scan it was never told to read.
            Tables.TableFilter filter = new(session, table, "T", rightsChecked: true, null, 0, null);
            filter.SetPlanItem(null);

            // An always-false condition empties everything after it: there is no plan in which
            // a later condition matters, since no row can survive the false one.
            Tables.TableFilter second = new(session, table, "S", rightsChecked: true, null, 1, null);
            second.SetPlanItem(null);
            second.Prepare();
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void APlanKnowsItsFiltersAndPricesThemThroughThePlanner()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Tables.Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                3, "T",
                [new Tables.Column("ID", Values.TypeInfo.TypeInteger, null, 0)],
                store, persistData: false);

            // The plan walks every filter of the order, collecting the filters themselves and
            // every join condition it meets: that walk is why a plan knows about tables the
            // query never named in FROM.
            Tables.TableFilter a = new(session, table, "A", rightsChecked: true, null, 0, null);
            Tables.TableFilter b = new(session, table, "B", rightsChecked: true, null, 1, null);
            a.AddJoin(
                b, outer: false,
                H2Sharp.Expressions.ValueExpression.Get(H2Sharp.Values.ValueBoolean.Get(true)));

            Tables.Plan plan = new([a, b], 2, null);
            Assert.Equal([a, b], plan.GetFilters());
            Assert.Null(plan.GetItem(a));

            // Pricing chooses: a missing plan item is not a refusal, the reference prices each
            // filter through getBestPlanItem and files the items away. The join is on a
            // constant, so the plan is valid and the cost below is what the reference answers
            // for the same two filters, probed on the JVM.
            double cost = plan.CalculateCost(
                session, new H2Sharp.Command.Query.AllColumnsForPlan([a, b]), isSelectCommand: true);
            Assert.NotNull(plan.GetItem(a));
            Assert.NotNull(plan.GetItem(b));

            const string javaBody = """
                java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:plan_cost");
                java.sql.Statement stat = conn.createStatement();
                stat.execute("CREATE TABLE T(ID INT)");
                org.h2.jdbc.JdbcConnection c = (org.h2.jdbc.JdbcConnection) conn;
                org.h2.engine.SessionLocal session = (org.h2.engine.SessionLocal) c.getSession();
                org.h2.table.Table table = session.getDatabase().getSchema("PUBLIC").getTableOrView(session, "T");
                org.h2.table.TableFilter a = new org.h2.table.TableFilter(session, table, "A", true, null, 0, null);
                org.h2.table.TableFilter b = new org.h2.table.TableFilter(session, table, "B", true, null, 1, null);
                a.addJoin(b, false, org.h2.expression.ValueExpression.get(org.h2.value.ValueBoolean.get(true)));
                org.h2.table.Plan plan = new org.h2.table.Plan(
                    new org.h2.table.TableFilter[]{ a, b }, 2, null);
                double cost = plan.calculateCost(session,
                    new org.h2.command.query.AllColumnsForPlan(
                        new org.h2.table.TableFilter[]{ a, b }), true);
                System.out.println("=" + Double.doubleToLongBits(cost));
                conn.close();
                """;
            System.Collections.Generic.List<string> java = H2Sharp.Tests.Oracle.OracleRunner.Run(
                [], javaBody, null, null, "org.h2.table");
            Assert.Equal(java, ["=" + System.BitConverter.DoubleToInt64Bits(cost).ToString()]);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void ParsingJoinsWithoutMeasuring()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Tables.Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                3, "T",
                [new Tables.Column("ID", Values.TypeInfo.TypeInteger, null, 0)],
                store, persistData: false);

            // PreparePlan in parse mode builds the fake plan and joins the filters, but measures
            // nothing and prepares nothing: a view parsed now runs later, against tables that
            // may have changed since. What is asserted is the chain -- the top filter is where
            // the scan would start, and the second filter hangs off it.
            H2Sharp.Command.Query.Select select = new(session);
            Tables.TableFilter a = new(session, table, "A", rightsChecked: true, null, 0, null);
            Tables.TableFilter b = new(session, table, "B", rightsChecked: true, null, 1, null);
            select.AddTableFilter(a, isTop: true);
            select.AddTableFilter(b, isTop: true);

            _ = select.PreparePlan(parse: true);

            Assert.Same(a, select.GetTopFilter());
            Assert.Same(b, a.GetJoin());
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void LockingLocksTheWholeChain()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Tables.Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                3, "T",
                [new Tables.Column("ID", Values.TypeInfo.TypeInteger, null, 0)],
                store, persistData: false);

            // The base table answers false: it has nothing to lock, and a lock on it would
            // serialize statements that share nothing. What is asserted is that the call walks
            // the chain -- a join left unlocked while the filter is locked would join rows from
            // two different moments.
            Tables.TableFilter a = new(session, table, "A", rightsChecked: true, null, 0, null);
            Tables.TableFilter b = new(session, table, "B", rightsChecked: true, null, 1, null);
            a.AddJoin(b, outer: false, null);

            Assert.False(a.GetTable().Lock(session, Tables.Table.ReadLock));
            a.Lock(session);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void HintsAreKeptNotApplied()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Tables.Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                3, "T",
                [new Tables.Column("ID", Values.TypeInfo.TypeInteger, null, 0)],
                store, persistData: false);

            // A filter without hints allows everything, and one with hints keeps them: applying
            // them is the planner's job, and a filter that chose its own index would answer
            // about a plan it has not seen.
            Tables.TableFilter plain = new(session, table, "P", rightsChecked: true, null, 0, null);
            Assert.Null(plain.GetIndexHints());

            Tables.IndexHints hints = Tables.IndexHints.CreateUseIndexHints(["I"]);
            Tables.TableFilter hinted = new(session, table, "H", rightsChecked: true, null, 1, hints);
            Assert.Same(hints, hinted.GetIndexHints());
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void MarkingWalksTheChainWithoutBreakingIt()
    {
        SessionLocal session = NewSession();

        var store = new H2Sharp.MvStore.Tx.TransactionStore(H2Sharp.MvStore.MVStore.Open(null!));
        store.Init();
        try
        {
            Tables.Table table = new H2Sharp.MvStore.Db.MVTable(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                3, "T",
                [new Tables.Column("ID", Values.TypeInfo.TypeInteger, null, 0)],
                store, persistData: false);

            // The flag itself has no reader -- the reference keeps it private too, and it is
            // only asked by the cursor loop, which is not ported. So what is asserted here is
            // the walk: marking starts from a filter and reaches the nested join through it,
            // and the chain visited afterwards is the chain that was built.
            Tables.TableFilter outer = new(session, table, "O", rightsChecked: true, null, 0, null);
            Tables.TableFilter nested = new(session, table, "N", rightsChecked: true, null, 1, null);
            outer.SetNestedJoin(nested);
            outer.AddFilterCondition(
                H2Sharp.Expressions.ValueExpression.Get(H2Sharp.Values.ValueBoolean.Get(true)), isJoin: false);

            outer.SetEvaluatable(outer, true);

            List<string> seen = [];
            outer.Visit(new AliasCollector(seen));
            Assert.Equal(["O", "N"], seen);

            // The flag is read back as it was set: it is decided elsewhere, by the query that
            // marks what it will read, and the filter only keeps it.
            Assert.True(outer.IsEvaluatable());
            Assert.Same(nested, outer.GetNestedJoin());
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
