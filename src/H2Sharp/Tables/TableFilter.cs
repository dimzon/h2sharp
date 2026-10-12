/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.TableFilter.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A table as a query sees it: under an alias, at a position in FROM, read by a plan.
 *
 * There is one of these per *use* of a table, not per table. "SELECT * FROM TEST T1, TEST T2"
 * has two filters over one table, each with its own alias, its own position and its own cursor,
 * because each is read independently and a column reference has to say which of them it means.
 * This is why a query holds filters and not tables: T1.NAME and T2.NAME are different columns,
 * and the filter is what tells them apart.
 *
 * The constructor asks the session to check the right to read the table unless the caller says
 * the right was already checked. That check is where a statement that names a table it may not
 * read fails, which is before the table would be opened — a check that happened at run time
 * would have half-read the table first.
 *
 * The hash is an object id of the session, not of the database, because it is not an identity
 * among the catalogue's objects: it tells one filter of a query from another, and it must not
 * spend an id a table would need.
 *
 * This file is the foundation of the class: its fields, its constructor, and the members that
 * only read them. Everything that plans or moves — bestPlanItem, the cursor loop, the joins —
 * arrives member by member, each read from the reference before it is written.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Conditions;
using H2Sharp.Result;
using H2Sharp.Values;

namespace H2Sharp.Tables;

/// <summary>
/// A table filter represents a table that is used in a query. There is one such object for
/// every use of a table. For example, for the query "SELECT * FROM TEST T1, TEST T2", this will
/// contain two filter objects, one for table TEST T1 and one for table TEST T2.
/// </summary>
public class TableFilter : ColumnResolver
{
    private const int BeforeFirst = 0;

    private const int Found = 1;

    private const int AfterLast = 2;

    private const int NullRow = 3;

    /// <summary>
    /// Comparator that uses order in FROM clause as a sort key.
    /// </summary>
    public static readonly Comparison<TableFilter> OrderInFromComparator =
        (left, right) => left.orderInFrom.CompareTo(right.orderInFrom);

    /// <summary>
    /// Whether this filter is reached only through another one, which is what makes an outer
    /// join's outer side outer.
    /// </summary>
    protected bool joinOuterIndirect;

    private SessionLocal session;

    private readonly Table table;

    private readonly Command.Query.Select? select;

    private string? alias;

    private readonly IndexHints? indexHints;

    /// <summary>
    /// The index conditions used for direct index lookup (start or end).
    /// </summary>
    private readonly List<IndexCondition> indexConditions = [];

    /// <summary>
    /// Adds an index condition, to be used for direct index lookup.
    /// </summary>
    /// <remarks>
    /// It is only held here. Whether it becomes a lookup or a scan is decided later, when the
    /// plan is chosen — holding it now and using it then is what keeps a condition on an indexed
    /// column from being answered by reading the whole table.
    /// </remarks>
    /// <param name="condition">the condition</param>
    public void AddIndexCondition(IndexCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        indexConditions.Add(condition);
    }

    private readonly int hashCode;

    private readonly int orderInFrom;

    /// <summary>
    /// Creates a filter over a table.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="table">the table</param>
    /// <param name="alias">the alias, or null</param>
    /// <param name="rightsChecked">whether the right to read the table was already checked</param>
    /// <param name="select">the select this filter belongs to, or null</param>
    /// <param name="orderInFrom">the position of this table in the FROM clause</param>
    /// <param name="indexHints">which indexes the caller allowed, or null</param>
    public TableFilter(
        SessionLocal session,
        Table table,
        string? alias,
        bool rightsChecked,
        Command.Query.Select? select,
        int orderInFrom,
        IndexHints? indexHints)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(table);
        this.session = session;
        this.table = table;
        this.alias = alias;
        this.select = select;

        if (!rightsChecked)
        {
            session.CheckReadRight(table);
        }

        hashCode = session.NextObjectId();
        this.orderInFrom = orderInFrom;
        this.indexHints = indexHints;
    }

    /// <summary>
    /// Get the order number (index) of this table filter in the "from" clause of the query.
    /// </summary>
    /// <returns>the index (0, 1, 2,...)</returns>
    public int GetOrderInFrom() => orderInFrom;

    /// <summary>
    /// Get the select statement this table filter belongs to, or null.
    /// </summary>
    /// <returns>the select statement, or null</returns>
    public Command.Query.Select? GetSelect() => select;

    /// <summary>
    /// Get the table.
    /// </summary>
    /// <returns>the table</returns>
    public Table GetTable() => table;

    /// <summary>
    /// Whether this filter has an alias.
    /// </summary>
    /// <returns>true if it has</returns>
    public bool HasAlias() => alias is not null;

    /// <summary>
    /// Get the table alias: the alias, or the table's own name when there is none.
    /// </summary>
    /// <remarks>
    /// The fallback is not a convenience. "SELECT NAME FROM TEST" reads through the same path
    /// as "SELECT T.NAME FROM TEST T": a column that was written without a qualifier still has
    /// to be answered as a column *of this table*, and the table's own name is the only name
    /// that must always mean it.
    /// </remarks>
    /// <returns>the alias</returns>
    public string GetTableAlias()
    {
        if (alias is not null)
        {
            return alias;
        }

        return table.GetTableName();
    }

    /// <summary>
    /// Whether this is the outer side of an outer join, directly or through a nested one.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsJoinOuterIndirect() => joinOuterIndirect;

    /// <inheritdoc/>
    /// <remarks>
    /// The columns of the table, as in the reference. A derived table contributes its own, and
    /// that is the only case where this is answered differently — see <see cref="FindColumn"/>
    /// for why the two go together.
    /// </remarks>
    public Column[] GetColumns() => table.GetColumns();

    /// <summary>
    /// Finds a column by name: a derived column first, then the table's own.
    /// </summary>
    /// <remarks>
    /// Not ported. The derived map is where a view's own names live, and it is filled while the
    /// view is read; without it a name that only exists inside a view would fall through to the
    /// table behind it and be answered with the wrong column.
    /// </remarks>
    /// <param name="name">the name</param>
    /// <returns>the column</returns>
    public Column? FindColumn(string? name)
        => throw new TODOException("TableFilter.findColumn: the derived map is not filled yet");

    /// <summary>
    /// Reads a column out of the row the cursor is on.
    /// </summary>
    /// <remarks>
    /// Not ported. It is the row's own value, and answering it needs the search row the cursor
    /// found and the rules for a rowid — which is <c>ValueBigint.get(currentSearchRow.getKey())</c>
    /// when the column asks for the key rather than a value. Until that is here, a column read
    /// outside a scan would give the same answer as a failed read inside one, which is the
    /// failure this refuses rather than reports.
    /// </remarks>
    /// <param name="column">the column</param>
    /// <returns>the value</returns>
    public Value GetValue(Column column)
        => throw new TODOException("TableFilter.getValue: the cursor's row is not readable yet");

    /// <summary>
    /// Finds the rowid column of the table this filter reads.
    /// </summary>
    /// <remarks>
    /// Not ported. It is <c>table.getRowIdColumn()</c>, and that method is not ported either:
    /// answering without it would need knowing which column is the rowid, and there are three
    /// candidates the reference treats differently depending on the table.
    /// </remarks>
    /// <returns>the column</returns>
    public Column? GetRowIdColumn()
        => throw new TODOException("TableFilter.getRowIdColumn: Table.getRowIdColumn is not ported");

    /// <summary>
    /// Gets the filter this resolver reads from: itself.
    /// </summary>
    /// <returns>this filter</returns>
    public TableFilter? GetTableFilter() => this;

    /// <inheritdoc/>
    public override int GetHashCode() => hashCode;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    private Expression? filterCondition;

    private Expression? joinCondition;

    /// <summary>
    /// Adds a filter condition.
    /// </summary>
    /// <remarks>
    /// The first one is kept as it was written; every later one is ANDed onto it. That is not
    /// an optimisation, it is how the filter answers: there is one condition to check per row,
    /// and two conditions on one row are true together or not at all. A join condition and a
    /// filter condition are kept separately because they are answered at different moments — the
    /// join decides which rows pair up, the filter decides which of the pairs survive.
    /// </remarks>
    /// <param name="condition">the condition</param>
    /// <param name="isJoin">whether this is part of the join</param>
    public void AddFilterCondition(Expression? condition, bool isJoin)
    {
        if (isJoin)
        {
            joinCondition = joinCondition is null
                ? condition
                : new ConditionAndOr(ConditionAndOr.And, joinCondition, condition!);
        }
        else
        {
            filterCondition = filterCondition is null
                ? condition
                : new ConditionAndOr(ConditionAndOr.And, filterCondition, condition!);
        }
    }

    private bool evaluatable;

    /// <summary>
    /// Marks this filter and everything reachable from it as evaluatable or not.
    /// </summary>
    /// <remarks>
    /// The flag goes to the filter itself, to both of its conditions, to the nested join — but
    /// only when this *is* the filter being marked, because a nested join belongs to the join
    /// it sits in and enabling a filter of a joined filter must not switch it on — and then to
    /// the join. That asymmetry is the whole content of this method: a blanket "mark
    /// everything" would enable filters the query never asked to evaluate.
    /// </remarks>
    /// <param name="filter">the filter the marking started from</param>
    /// <param name="b">whether it is evaluatable</param>
    public void SetEvaluatable(TableFilter filter, bool b)
    {
        ArgumentNullException.ThrowIfNull(filter);
        filter.SetEvaluatable(b);
        if (filterCondition is not null)
        {
            filterCondition.SetEvaluatable(filter, b);
        }

        if (joinCondition is not null)
        {
            joinCondition.SetEvaluatable(filter, b);
        }

        if (nestedJoin is not null)
        {
            // don't enable / disable the nested join filters
            // if enabling a filter in a joined filter
            if (this == filter)
            {
                nestedJoin.SetEvaluatable(nestedJoin, b);
            }
        }

        if (join is not null)
        {
            join.SetEvaluatable(filter, b);
        }
    }

    /// <summary>
    /// Sets whether this filter can be evaluated without reading a row.
    /// </summary>
    /// <param name="evaluatable">true when it can</param>
    public void SetEvaluatable(bool evaluatable)
    {
        this.evaluatable = evaluatable;
    }
    /// <remarks>
    /// It is kept apart from the filter condition because the two are answered at different
    /// moments: the join decides which rows pair up, the filter which of the pairs survive. A
    /// caller that wants "the condition" without saying which one would get an answer that is
    /// true for the wrong half of the question.
    /// </remarks>
    /// <returns>the join condition, or null</returns>
    public Expression? GetJoinCondition() => joinCondition;

    /// <summary>
    /// Removes the joined table.
    /// </summary>
    /// <remarks>
    /// It only drops the link. The filter that was joined keeps its own conditions, because
    /// removing a join is not removing what was read through it — a query that forgets which
    /// rows paired up but remembers the pairing would answer from half a plan.
    /// </remarks>
    public void RemoveJoin() => join = null;

    /// <summary>
    /// Removes the join condition.
    /// </summary>
    /// <remarks>
    /// The filter stays joined; only the condition goes. That is for the planner, which tries a
    /// plan, measures it, and takes the condition back when the plan loses — the pairing is
    /// kept so that the next try starts from the same tables.
    /// </remarks>
    public void RemoveJoinCondition() => joinCondition = null;

    /// <summary>
    /// Pushes the condition over the whole join down into the filters that can answer it.
    /// </summary>
    /// <remarks>
    /// Only when this is not the outer side: the null row an outer join produces has no values
    /// to filter by, and a condition pushed into it would unmake rows the join was told to keep.
    /// The walk goes to the nested join and to the join for the same reason the condition was
    /// set on all of them — it constrains rows no single filter sees alone.
    /// </remarks>
    internal void OptimizeFullCondition()
    {
        if (!joinOuter && fullCondition is not null)
        {
            fullCondition.AddFilterConditions(this);
            if (nestedJoin is not null)
            {
                nestedJoin.OptimizeFullCondition();
            }

            if (join is not null)
            {
                join.OptimizeFullCondition();
            }
        }
    }
    /// <returns>the filter condition, or null</returns>
    public Expression? GetFilterCondition() => filterCondition;

    /// <summary>
    /// Forgets the filter condition.
    /// </summary>
    /// <remarks>
    /// It is the planner that does this, when it decides the condition is better answered by the
    /// index than by the filter: a condition left in both places would be asked twice, and a
    /// condition asked twice is asked once too many.
    /// </remarks>
    public void RemoveFilterCondition() => filterCondition = null;

    /// <summary>
    /// Joins another filter to this one.
    /// </summary>
    /// <remarks>
    /// The ON condition is mapped against both sides first, because a name inside it may belong
    /// to either table and reading it against one of them would answer with the other's column.
    /// The filter lands at the end of the join chain rather than here: "A JOIN B JOIN C" pairs
    /// A with B before C is even seen, and putting C anywhere but last would pair it with the
    /// wrong one.
    /// </remarks>
    /// <param name="filter">the joined filter</param>
    /// <param name="outer">whether the join is an outer one</param>
    /// <param name="on">the join condition</param>
    public void AddJoin(TableFilter filter, bool outer, Expression? on)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (on is not null)
        {
            MapColumnsVisitor visitor = new(on);
            Visit(visitor);
            filter.Visit(visitor);
        }

        if (join is null)
        {
            join = filter;
            filter.joinOuter = outer;
            if (outer)
            {
                filter.Visit(new JoinOuterVisitor());
            }

            if (on is not null)
            {
                filter.AddFilter(on);
            }
        }
        else
        {
            join.AddJoin(filter, outer, on);
        }
    }

    /// <summary>
    /// Adds a condition that is answered while joining.
    /// </summary>
    /// <remarks>
    /// It lands on this filter and on every filter it is joined to, because a join condition
    /// names columns of both sides and neither side can answer it alone. A condition that stayed
    /// on one side would be asked of rows it cannot see.
    /// </remarks>
    /// <param name="on">the condition</param>
    public void AddFilter(Expression? on)
    {
        AddFilterCondition(on, true);
        if (join is not null)
        {
            join.AddFilter(on);
        }
    }

    /// <summary>
    /// Something that is done to every filter of a join.
    /// </summary>
    public interface TableFilterVisitor
    {
        /// <summary>
        /// Called for each nested or joined table filter.
        /// </summary>
        /// <param name="f">the filter</param>
        void Accept(TableFilter f);
    }

    /// <summary>
    /// Visits this filter and every filter joined to it.
    /// </summary>
    /// <remarks>
    /// The nested join is visited before the chain moves on, because a filter nested inside a
    /// join belongs to the join it sits in — and visiting it after the chain would hand it to a
    /// query that has already walked past.
    /// </remarks>
    /// <param name="visitor">the visitor</param>
    public void Visit(TableFilterVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        TableFilter f = this;
        do
        {
            visitor.Accept(f);
            TableFilter? n = f.nestedJoin;
            if (n is not null)
            {
                n.Visit(visitor);
            }

            f = f.join!;
        }
        while (f is not null);
    }

    private TableFilter? join;

    private bool joinOuter;

    private TableFilter? nestedJoin;

    private Expression? fullCondition;

    /// <summary>
    /// Sets the condition over the whole join.
    /// </summary>
    /// <remarks>
    /// It lands here and on every filter this one is joined to, because a condition over the
    /// whole join constrains rows no single filter can see alone. A condition that stayed on one
    /// filter would be asked of rows outside its join, which is a different question with a
    /// different answer.
    /// </remarks>
    /// <param name="condition">the condition</param>
    public void SetFullCondition(Expression? condition)
    {
        fullCondition = condition;
        if (join is not null)
        {
            join.SetFullCondition(condition);
        }
    }

    /// <summary>
    /// Sets the filter this one is nested inside.
    /// </summary>
    /// <param name="filter">the filter</param>
    public void SetNestedJoin(TableFilter? filter)
    {
        nestedJoin = filter;
    }

    /// <summary>
    /// Get the filter this one is joined to.
    /// </summary>
    /// <returns>the joined filter, or null</returns>
    public TableFilter? GetJoin() => join;

    /// <summary>
    /// Whether this is the outer side of an outer join.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsJoinOuter() => joinOuter;

    /// <summary>
    /// Maps an ON condition against the columns it names.
    /// </summary>
    /// <summary>
    /// Marks every filter of a join as the outer side of one.
    /// </summary>
    private sealed class JoinOuterVisitor : TableFilterVisitor
    {
        public void Accept(TableFilter f)
        {
            ArgumentNullException.ThrowIfNull(f);
            f.joinOuterIndirect = true;
        }
    }

    private sealed class MapColumnsVisitor : TableFilterVisitor
    {
        private readonly Expression on;

        internal MapColumnsVisitor(Expression on)
        {
            this.on = on;
        }

        public void Accept(TableFilter f)
        {
            ArgumentNullException.ThrowIfNull(f);
            on.MapColumns(f, 0, Expression.MapInitial);
        }
    }
    /// <summary>
    /// Maps the join condition's columns against the filters they name.
    /// </summary>
    /// <remarks>
    /// An outer filter reached from the inside is skipped, because its columns belong to rows
    /// the inner side never sees: mapping them would bind a name to a column of a row that does
    /// not exist yet. The nested join and the join are mapped after, in that order, since a name
    /// inside the condition may belong to either side and both have to be asked.
    /// </remarks>
    /// <param name="resolver">where unqualified names are looked up</param>
    /// <param name="level">the query level</param>
    /// <param name="outer">whether this call comes from outside the join</param>
    public void MapColumns(ColumnResolver resolver, int level, bool outer)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        if (!outer && joinOuter)
        {
            return;
        }

        if (joinCondition is not null)
        {
            joinCondition.MapColumns(resolver, level, Expressions.Expression.MapInitial);
        }

        if (nestedJoin is not null)
        {
            nestedJoin.MapColumns(resolver, level, outer);
        }

        if (join is not null)
        {
            join.MapColumns(resolver, level, outer);
        }
    }

    /// <returns>the session</returns>
    public SessionLocal GetSession() => session;

    /// <summary>
    /// Whether this filter is used in the plan.
    /// </summary>
    /// <remarks>
    /// A filter that no plan uses is a table the query named and never read — which is legal SQL
    /// and a wasted scan, and the planner is the one that decides. Until it does, every filter
    /// is unused, and that is the honest default rather than a guess about the query.
    /// </remarks>
    /// <returns>true if it is used</returns>
    public bool IsUsed() => used;

    /// <summary>
    /// Sets whether this filter is used in the plan.
    /// </summary>
    /// <param name="used">true when the plan uses it</param>
    public void SetUsed(bool used) => this.used = used;

    private bool used;
    /// <param name="s">the session</param>
    public void SetSession(SessionLocal s)
    {
        ArgumentNullException.ThrowIfNull(s);
        session = s;
    }
}
