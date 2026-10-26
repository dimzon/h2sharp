/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.index.IndexCondition.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * One condition that can potentially use an index.
 *
 * It is not an expression, although there is generally one expression behind each index
 * condition. An expression says what is true about a row; an index condition says what the
 * *index* can do with it — whether the column it names is the first of the index, whether the
 * comparison bounds the scan from below, from above, or from both sides. That is why the
 * comparison type is a bit of a mask here and an answer there.
 *
 * The masks are the whole reason this is a class and not a pair of values. EQUALITY, START and
 * END compose: a BETWEEN is START|END, and ALWAYS_FALSE is a bit rather than a special case
 * because a condition that is always false is still a condition on the index's columns, and the
 * planner treats it as a range of nothing.
 *
 * The simple readers are real. The members that evaluate — the current value, the list, the
 * result, the mask over a list of conditions, whether a compound list fits an index — refuse,
 * because they need the session to read and the index to compare against.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Conditions;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Indexes;

/// <summary>
/// An index condition object is made for each condition that can potentially use an index. This
/// class does not extend expression, but in general there is one expression that maps to each
/// index condition.
/// </summary>
public class IndexCondition
{
    /// <summary>
    /// A bit of a search mask meaning 'equal'.
    /// </summary>
    public const int Equality = 1;

    /// <summary>
    /// A bit of a search mask meaning 'larger or equal'.
    /// </summary>
    public const int Start = 2;

    /// <summary>
    /// A bit of a search mask meaning 'smaller or equal'.
    /// </summary>
    public const int End = 4;

    /// <summary>
    /// A search mask meaning 'between'.
    /// </summary>
    public const int Range = Start | End;

    /// <summary>
    /// A bit of a search mask meaning 'the condition is always false'.
    /// </summary>
    public const int AlwaysFalse = 8;

    /// <summary>
    /// A bit of a search mask meaning 'spatial intersection'.
    /// </summary>
    public const int SpatialIntersects = 16;

    private readonly Column? column;

    private readonly Column[]? columns;

    private readonly bool compoundColumns;

    /// <summary>
    /// see constants in <see cref="Comparison"/>
    /// </summary>
    private readonly int compareType;

    private readonly Expression? expression;

    private readonly IReadOnlyList<Expression>? expressionList;

    private readonly Command.Query.Query? expressionQuery;

    /// <summary>
    /// Creates an index condition.
    /// </summary>
    /// <param name="compareType">the comparison type, see constants in <see cref="Comparison"/></param>
    /// <param name="column">the column, or null for a compound list</param>
    /// <param name="columns">the columns, or null for a single column</param>
    /// <param name="expression">the expression</param>
    /// <param name="list">the expression list, or null</param>
    /// <param name="query">the query, or null</param>
    protected IndexCondition(
        int compareType,
        ExpressionColumn? column,
        Column[]? columns,
        Expression? expression,
        IReadOnlyList<Expression>? list,
        Command.Query.Query? query)
    {
        this.compareType = compareType;
        if (column is not null)
        {
            this.column = column.GetColumn();
            this.columns = null;
            compoundColumns = false;
        }
        else if (columns is not null)
        {
            this.column = null;
            this.columns = columns;
            compoundColumns = true;
        }
        else
        {
            this.column = null;
            this.columns = null;
            compoundColumns = false;
        }

        this.expression = expression;
        expressionList = list;
        expressionQuery = query;
    }

    /// <summary>
    /// Create an index condition with the given parameters.
    /// </summary>
    /// <param name="compareType">the comparison type, see constants in <see cref="Comparison"/></param>
    /// <param name="column">the column</param>
    /// <param name="expression">the expression</param>
    /// <returns>the index condition</returns>
    public static IndexCondition Get(int compareType, ExpressionColumn column, Expression expression)
    {
        ArgumentNullException.ThrowIfNull(column);
        return new IndexCondition(compareType, column, null, expression, null, null);
    }

    /// <summary>
    /// Create an index condition with the compare type IN_LIST and with the given parameters.
    /// </summary>
    /// <param name="column">the column</param>
    /// <param name="list">the expression list</param>
    /// <returns>the index condition</returns>
    public static IndexCondition GetInList(ExpressionColumn column, IReadOnlyList<Expression> list)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(list);
        return new IndexCondition(Comparison.InList, column, null, null, list, null);
    }

    /// <summary>
    /// Create a compound index condition with the compare type IN_LIST and the given parameters.
    /// </summary>
    /// <param name="columns">the columns</param>
    /// <param name="list">the expression list</param>
    /// <returns>the index condition</returns>
    public static IndexCondition GetCompoundInList(Column[] columns, List<Expression> list)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(list);
        return new IndexCondition(Comparison.InList, null, columns, null, list, null);
    }

    /// <summary>
    /// Create an index condition with the compare type IN_ARRAY and the given parameters.
    /// </summary>
    /// <param name="column">the column</param>
    /// <param name="array">the array expression</param>
    /// <returns>the index condition</returns>
    public static IndexCondition GetInArray(ExpressionColumn column, Expression array)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(array);
        return new IndexCondition(Comparison.InArray, column, null, array, null, null);
    }

    /// <summary>
    /// Create an index condition with the compare type IN_QUERY and the given parameters.
    /// </summary>
    /// <param name="column">the column</param>
    /// <param name="query">the query</param>
    /// <returns>the index condition</returns>
    public static IndexCondition GetInQuery(ExpressionColumn column, Command.Query.Query query)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(query);
        return new IndexCondition(Comparison.InQuery, column, null, null, null, query);
    }

    /// <summary>
    /// Gets the current value of this condition's expression.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the value</returns>
    public Value? GetCurrentValue(SessionLocal session)
        => expression!.GetValue(session);

    /// <summary>
    /// Gets the current values of this condition's expression list.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="sortTypes">the sort types</param>
    /// <returns>the values</returns>
    public Value?[] GetCurrentValueList(SessionLocal session, int[] sortTypes)
    {
        IComparer<Value> comparator;
        if (compoundColumns)
        {
            // The row-IN path needs the multi-column convert, which is not ported; a scalar IN
            // list never takes this branch.
            throw new TODOException("IndexCondition.getCurrentValueList: compound columns are not ported");
        }

        comparator = session.GetValueComparer();
        if ((sortTypes[0] & Result.SortOrder.Descending) != 0)
        {
            IComparer<Value> forward = comparator;
            comparator = Comparer<Value>.Create((o1, o2) => forward.Compare(o2, o1));
        }

        SortedSet<Value> valueSet = new(comparator);
        if (compareType == Comparison.InList)
        {
            Column col = column!;
            foreach (Expression e in expressionList!)
            {
                Value v = e.GetValue(session);
                v = col.Convert(session, v);
                _ = valueSet.Add(v);
            }
        }
        else if (compareType == Comparison.InArray)
        {
            Value value = expression!.GetValue(session);
            if (value is ValueArray array)
            {
                foreach (Value element in array.GetList()) valueSet.Add(element);
            }
        }
        else throw Message.DbException.GetInternalError("compareType = " + compareType);

        return [.. valueSet];
    }

    /// <summary>
    /// Gets the current result of this condition's query.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="sortTypes">the sort types</param>
    /// <returns>the result</returns>
    public ResultInterface? GetCurrentResult(SessionLocal session, int[] sortTypes)
    {
        expressionQuery!.SetInPredicateResultSortTypes(sortTypes);
        return expressionQuery.Run(0);
    }

    /// <summary>
    /// Gets the SQL of this condition.
    /// </summary>
    /// <param name="sqlFlags">the SQL flags</param>
    /// <returns>the SQL</returns>
    public string GetSql(int sqlFlags)
    {
        if (compareType == Comparison.False)
        {
            return "FALSE";
        }

        StringBuilder builder = new();
        builder = compoundColumns
            ? BuildSql(sqlFlags, builder)
            : BuildSql(sqlFlags, column!, builder);
        return builder.ToString();
    }

    private StringBuilder BuildSql(int sqlFlags, StringBuilder builder)
    {
        if (compareType == Comparison.InList)
        {
            _ = builder.Append(" IN(");
            for (int i = 0, size = expressionList!.Count; i < size; i++)
            {
                if (i > 0)
                {
                    _ = builder.Append(", ");
                }

                _ = expressionList[i].GetSql(builder, sqlFlags);
            }

            return builder.Append(')');
        }

        throw Message.DbException.GetInternalError(
            "Multiple columns can only be used with compound IN lists.");
    }

    private StringBuilder BuildSql(int sqlFlags, Column column, StringBuilder builder)
    {
        _ = column.GetSql(builder, sqlFlags);
        switch (compareType)
        {
            case Comparison.Equal:
                _ = builder.Append(" = ");
                break;
            case Comparison.EqualNullSafe:
                _ = builder.Append(expression!.IsNullConstant()
                    || (column.GetTypeInfo().GetValueType() == Values.Value.Boolean && expression.IsConstant())
                        ? " IS "
                        : " IS NOT DISTINCT FROM ");
                break;
            case Comparison.BiggerEqual:
                _ = builder.Append(" >= ");
                break;
            case Comparison.Bigger:
                _ = builder.Append(" > ");
                break;
            case Comparison.SmallerEqual:
                _ = builder.Append(" <= ");
                break;
            case Comparison.Smaller:
                _ = builder.Append(" < ");
                break;
            case Comparison.InList:
                _ = Expression.WriteExpressions(builder.Append(" IN("), [.. expressionList!], sqlFlags).Append(')');
                break;
            case Comparison.InArray:
                return expression!.GetSql(builder.Append(" = ANY("), sqlFlags, Expression.AutoParentheses)
                    .Append(')');
            case Comparison.InQuery:
                _ = builder.Append(" IN(");
                _ = expressionQuery!.GetPlanSql(builder, sqlFlags);
                _ = builder.Append(')');
                break;
            case Comparison.SpatialIntersects:
                _ = builder.Append(" && ");
                break;
            default:
                throw Message.DbException.GetInternalError("type=" + compareType);
        }

        if (expression is not null)
        {
            _ = expression.GetSql(builder, sqlFlags, Expression.AutoParentheses);
        }

        return builder;
    }

    /// <summary>
    /// Gets the search mask for the given conditions.
    /// </summary>
    /// <remarks>
    /// An instance member, as in the reference: the mask answers what *this* condition
    /// contributes to the lookup, read against the conditions around it. A static one would
    /// answer about a list with no condition of its own, which is a mask of nothing.
    /// Table types are ints here rather than the reference's enum, so "a regular table" is
    /// the <c>TABLE</c> constant, not an enum member — the comparison means the same.
    /// </remarks>
    /// <param name="indexConditions">the conditions</param>
    /// <returns>the mask</returns>
    public int GetMask(List<IndexCondition> indexConditions)
    {
        switch (compareType)
        {
            case Comparison.False:
                return AlwaysFalse;
            case Comparison.Equal:
            case Comparison.EqualNullSafe:
                return Equality;
            case Comparison.InList:
            case Comparison.InArray:
            case Comparison.InQuery:
                if (indexConditions.Count > 1)
                {
                    if (IsCompoundColumns())
                    {
                        Column[]? columns = GetColumns();
                        for (int i = columns!.Length; --i >= 0;)
                        {
                            if (Tables.Table.TABLE != columns[i].GetTable()?.GetTableType())
                            {
                                return 0;
                            }
                        }
                    }
                    else if (Tables.Table.TABLE != GetColumn()?.GetTable()?.GetTableType())
                    {
                        // if combined with other conditions,
                        // IN(..) can only be used for regular tables
                        // test case:
                        // create table test(a int, b int, primary key(id, name));
                        // create unique index c on test(b, a);
                        // insert into test values(1, 10), (2, 20);
                        // select * from (select * from test)
                        // where a=1 and b in(10, 20);
                        return 0;
                    }
                }
                return Equality;
            case Comparison.BiggerEqual:
            case Comparison.Bigger:
                return Start;
            case Comparison.SmallerEqual:
            case Comparison.Smaller:
                return End;
            case Comparison.SpatialIntersects:
                return SpatialIntersects;
            default:
                throw Message.DbException.GetInternalError("type=" + compareType);
        }
    }

    /// <summary>
    /// Whether this condition is always false.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsAlwaysFalse() => compareType == Comparison.False;

    /// <summary>
    /// Whether this condition bounds the scan from below.
    /// </summary>
    /// <returns>true if it does</returns>
    public bool IsStart()
    {
        return compareType switch
        {
            Comparison.Equal or Comparison.BiggerEqual or Comparison.Bigger => true,
            _ => false,
        };
    }

    /// <summary>
    /// Whether this condition bounds the scan from above.
    /// </summary>
    /// <returns>true if it does</returns>
    public bool IsEnd()
    {
        return compareType switch
        {
            Comparison.Equal or Comparison.SmallerEqual or Comparison.Smaller => true,
            _ => false,
        };
    }

    /// <summary>
    /// Whether this condition is a spatial intersection.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsSpatialIntersects() => compareType == Comparison.SpatialIntersects;

    /// <summary>
    /// Gets the comparison type.
    /// </summary>
    /// <returns>the comparison type</returns>
    public int GetCompareType() => compareType;

    /// <summary>
    /// Gets the referenced column.
    /// </summary>
    /// <returns>the column, or null for a compound list</returns>
    public Column? GetColumn() => column;

    /// <summary>
    /// Gets the referenced columns.
    /// </summary>
    /// <returns>the columns, or null for a single column</returns>
    public Column[]? GetColumns() => columns;

    /// <summary>
    /// Whether this condition covers several columns.
    /// </summary>
    /// <returns>true if it does</returns>
    public bool IsCompoundColumns() => compoundColumns;

    /// <summary>
    /// Gets the expression.
    /// </summary>
    /// <returns>the expression, or null</returns>
    public Expression? GetExpression() => expression;

    /// <summary>
    /// Gets the expression list.
    /// </summary>
    /// <returns>the list, or null</returns>
    public IReadOnlyList<Expression>? GetExpressionList() => expressionList;

    /// <summary>
    /// Gets the expression query.
    /// </summary>
    /// <returns>the query, or null</returns>
    public Command.Query.Query? GetExpressionQuery() => expressionQuery;

    /// <summary>
    /// Whether this condition can be evaluated without reading a row.
    /// </summary>
    /// <returns>true if it can</returns>
    public bool IsEvaluatable()
    {
        if (expression is not null)
        {
            return expression.IsEverything(ExpressionVisitor.EvaluableVisitor);
        }

        if (expressionList is not null)
        {
            foreach (Expression e in expressionList)
            {
                if (!e.IsEverything(ExpressionVisitor.EvaluableVisitor))
                {
                    return false;
                }
            }

            return true;
        }

        return expressionQuery is null || expressionQuery.IsEverything(ExpressionVisitor.EvaluableVisitor);
    }

    /// <summary>
    /// Clones this condition with the columns of the given index.
    /// </summary>
    /// <remarks>
    /// Not ported. It reorders a compound condition's columns to the order the index keeps
    /// them in, which needs ValueRow.cloneWithOrder and ExpressionList.cloneWithOrder -- and
    /// neither reordering exists here. Until they do, a compound condition whose columns are
    /// out of order cannot be fixed, only dropped.
    /// </remarks>
    /// <param name="index">the index</param>
    /// <returns>the fixed condition, or null when it cannot be fixed</returns>
    public IndexCondition? CloneWithIndexColumns(Index index)
        => throw new TODOException("IndexCondition.cloneWithIndexColumns: reordering rows is not ported");


}
