/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.query.TableValueConstructor.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The table value constructor: the rows a VALUES clause names.
 */
using System.Collections.Generic;
using System.Globalization;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Functions;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Command.Query;

/// <summary>
/// The table value constructor: a query whose rows are the rows a VALUES clause names.
/// </summary>
/// <remarks>
/// The rows are kept as expressions rather than as values, and that is the reference's rule
/// with a reason behind it: a row may name a parameter, and a parameter's value is not known
/// until the statement runs, so a statement prepared once and run twice must evaluate its rows
/// twice.
/// </remarks>
public sealed class TableValueConstructor : Query
{
    private readonly List<List<Expression>> rows;

    private readonly int visibleColumnCount;

    private int resultColumnCount;

    private Column[] columns = [];

    private List<Expression> expressions = [];

    private Expression[] expressionArray = [];

    private TableValueConstructorTable? table;

    private TableValueColumnResolver? columnResolver;

    private double cost;

    /// <summary>
    /// Creates a table value constructor over the given rows.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="rows">the rows</param>
    public TableValueConstructor(SessionLocal session, List<List<Expression>> rows)
        : base(session, string.Empty)
    {
        this.rows = rows;
        visibleColumnCount = rows[0].Count;
        if (visibleColumnCount > Engine.Constants.MaxColumns)
        {
            throw DbException.Get(
                ErrorCode.TooManyColumns1,
                Engine.Constants.MaxColumns.ToString(CultureInfo.InvariantCulture));
        }

        foreach (List<Expression> row in rows)
        {
            foreach (Expression column in row)
            {
                if (!column.IsConstant())
                {
                    // The rows are not all constants, so the columns are not known yet; they
                    // are worked out when the expressions are prepared.
                    return;
                }
            }
        }

        CreateTable();
    }

    /// <summary>
    /// The columns of the table, the names they have and the types they hold.
    /// </summary>
    /// <returns>the columns</returns>
    public Column[] GetColumns() => columns;

    /// <summary>
    /// The table behind the rows.
    /// </summary>
    /// <returns>the table</returns>
    public TableValueConstructorTable GetTable()
        => table ?? throw new TODOException("TableValueConstructor.getTable: the table is not built yet");

    /// <inheritdoc/>
    public override Tables.Table ToTable(string alias, Tables.Column[]? columnTemplates,
        List<Parameter> parameters, bool forCreateView, Query? topQuery)
    {
        // Reference: an unordered constructor with no offset or fetch is the table it already
        // built, not a derived table over itself.
        if (!HasOrder() && offsetExpr is null && fetchExpr is null && table is not null)
        {
            return table;
        }

        return base.ToTable(alias, columnTemplates, parameters, forCreateView, topQuery);
    }

    /// <inheritdoc/>
    public override bool IsConstantQuery()
    {
        if (!base.IsConstantQuery())
        {
            return false;
        }

        foreach (List<Expression> row in rows)
        {
            for (int i = 0; i < visibleColumnCount; i++)
            {
                if (!row[i].IsConstant())
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override Expression? GetIfSingleRow()
    {
        if (offsetExpr is not null || fetchExpr is not null || rows.Count != 1)
        {
            return null;
        }

        List<Expression> row = rows[0];
        if (visibleColumnCount == 1)
        {
            return row[0];
        }

        Expression[] array = new Expression[visibleColumnCount];
        for (int i = 0; i < visibleColumnCount; i++)
        {
            array[i] = row[i];
        }

        return new ExpressionList(array, false);
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.Select;

    /// <inheritdoc/>
    public override ResultInterface? QueryMeta() => null;

    /// <inheritdoc/>
    public override bool IsTransactional() => true;

    /// <summary>Sets the FOR UPDATE clause: a VALUES query is read-only.</summary>
    /// <param name="forUpdate">the FOR UPDATE clause</param>
    public override void SetForUpdate(ForUpdate forUpdate)
        => throw DbException.Get(ErrorCode.ResultSetReadonly);

    /// <inheritdoc/>
    public override void Init()
    {
        if (checkInit)
        {
            throw DbException.GetInternalError();
        }

        checkInit = true;

        if (IsWithTies() && !HasOrder())
        {
            throw DbException.Get(Api.ErrorCode.WithTiesWithoutOrderBy);
        }
    }

    /// <inheritdoc/>
    public override void PrepareExpressions()
    {
        if (columnResolver is null)
        {
            CreateTable();
        }

        if (orderList is not null)
        {
            // The reference resolves ORDER BY against the expression columns C1..Cn with
            // mustBeInResult = false: an expression the rows do not show becomes a hidden
            // result column that is evaluated per row, not a refusal.
            List<string> expressionSQL = new(expressions.Count);
            foreach (Expression e in expressions)
            {
                expressionSQL.Add(e.GetSql(HasSql.DefaultSqlFlags, Expression.WithoutParentheses));
            }

            if (InitOrder(expressionSQL, mustBeInResult: false, filters: null, expressions))
            {
                PrepareOrder(orderList!, expressions.Count);
            }
        }

        resultColumnCount = expressions.Count;
        for (int i = 0; i < resultColumnCount; i++)
        {
            expressions[i].MapColumns(columnResolver!, 0, Expression.MapInitial);
        }

        for (int i = visibleColumnCount; i < resultColumnCount; i++)
        {
            expressions[i] = expressions[i].Optimize(GetSession());
        }

        if (sort is not null)
        {
            CleanupOrder(expressions);
        }

        expressionArray = [.. expressions];
    }

    /// <inheritdoc/>
    public override void PreparePlan(SessionLocal session, TableFilter? targetTableFilter)
    {
        double total = 0;
        int columnCount = visibleColumnCount;
        foreach (List<Expression> row in rows)
        {
            for (int i = 0; i < columnCount; i++)
            {
                total += row[i].GetCost();
            }
        }

        cost = total + rows.Count;
    }

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, bool outer)
    {
        int columnCount = visibleColumnCount;
        foreach (List<Expression> row in rows)
        {
            for (int i = 0; i < columnCount; i++)
            {
                row[i].MapColumns(resolver, level, Expression.MapInitial);
            }
        }
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
        foreach (List<Expression> row in rows)
        {
            foreach (Expression expression in row)
            {
                expression.SetEvaluatable(tableFilter, value);
            }
        }
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        foreach (List<Expression> row in rows)
        {
            foreach (Expression expression in row)
            {
                expression.UpdateAggregate(session, stage);
            }
        }
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        ExpressionVisitor nested = visitor.IncrementQueryLevel(1);
        foreach (Expression expression in expressionArray)
        {
            if (!expression.IsEverything(nested))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override Expression[] GetExpressions()
        => expressionArray.Length > 0 ? expressionArray : [.. expressions];

    /// <inheritdoc/>
    public override int GetColumnCount() => visibleColumnCount;

    /// <inheritdoc/>
    public override HashSet<Table> GetTables() => [GetTable()];

    /// <inheritdoc/>
    public override long GetCost() => (long)cost;

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session, List<TableFilter> filters, long limit)
        => rows.Count;

    /// <inheritdoc/>
    public override ResultInterface? Run(long limit, ResultTarget? target)
    {
        SessionLocal current = GetSession();
        OffsetFetch offsetFetch = GetOffsetFetch(limit);
        int visible = visibleColumnCount, resultWidth = resultColumnCount;
        LocalResult result = new(current, expressionArray, visible, resultWidth);
        if (distinct)
        {
            result.SetDistinct();
        }

        if (sort is not null)
        {
            result.SetSortOrder(sort);
        }

        if (visible == resultWidth)
        {
            GetVisibleResult(current, result, columns, rows);
        }
        else
        {
            // Hidden ORDER BY columns: the visible values are read from the rows, and the
            // hidden expressions read them back through the column resolver.
            foreach (List<Expression> row in rows)
            {
                Value?[] values = new Value?[resultWidth];
                for (int i = 0; i < visible; i++)
                {
                    values[i] = row[i].GetValue(current).ConvertToType(columns[i].GetTypeInfo());
                }

                columnResolver!.CurrentRow = values;
                for (int i = visible; i < resultWidth; i++)
                {
                    values[i] = expressionArray[i].GetValue(current);
                }

                result.AddRow(values);
            }

            columnResolver!.CurrentRow = null;
        }

        return FinishResult(result, offsetFetch.Offset, offsetFetch.Fetch, offsetFetch.FetchPercent, target);
    }

    /// <summary>
    /// Appends the visible columns of all rows to the result, as the reference's
    /// getVisibleResult does.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="result">the result</param>
    /// <param name="columns">the columns</param>
    /// <param name="rows">the rows</param>
    public static void GetVisibleResult(
        SessionLocal session, ResultTarget result, Column[] columns, List<List<Expression>> rows)
    {
        int count = columns.Length;
        foreach (List<Expression> row in rows)
        {
            Value?[] values = new Value?[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = row[i].GetValue(session).ConvertToType(columns[i].GetTypeInfo());
            }

            result.AddRow(values);
        }
    }

    /// <inheritdoc/>
    public override void AddGlobalCondition(Parameter param, int columnId, int comparisonType)
    {
        // The values are fixed; there is nothing an index condition can narrow.
    }

    /// <inheritdoc/>
    public override bool AllowGlobalConditions() => false;

    /// <summary>
    /// Works out the columns of the table: their names and the types the rows hold.
    /// </summary>
    /// <remarks>
    /// The names are the default ones, C1, C2 and so on, and the type of a column is the
    /// type every row's value in it fits into.
    /// </remarks>
    private void CreateTable()
    {
        int rowCount = rows.Count;
        List<Expression> row = rows[0];
        int columnCount = row.Count;
        TypeInfo[] types = new TypeInfo[columnCount];
        for (int c = 0; c < columnCount; c++)
        {
            Expression e = row[c].Optimize(GetSession());
            row[c] = e;
            TypeInfo type = e.GetTypeInfo();
            if (type.GetValueType() == Value.Unknown)
            {
                type = TypeInfo.TypeVarchar;
            }

            types[c] = type;
        }

        for (int r = 1; r < rowCount; r++)
        {
            row = rows[r];
            for (int c = 0; c < columnCount; c++)
            {
                Expression e = row[c].Optimize(GetSession());
                row[c] = e;
                types[c] = TypeInfo.GetHigherType(types[c], e.GetTypeInfo());
            }
        }

        Column[] newColumns = new Column[columnCount];
        for (int c = 0; c < columnCount; c++)
        {
            newColumns[c] = new Column(
                "C" + (c + 1).ToString(CultureInfo.InvariantCulture), types[c]);
        }

        columns = newColumns;
        List<Expression> columnExpressions = new(columnCount);
        for (int c = 0; c < columnCount; c++)
        {
            // The reference names the columns with an expression column; mapColumns binds it
            // to the table's column and the column resolver reads the value from the row.
            columnExpressions.Add(new ExpressionColumn(newColumns[c].GetName(), null, null, 0));
        }

        expressions = columnExpressions;
        table = new TableValueConstructorTable(
            GetSession().GetDatabase().GetMainSchema(), GetSession(), columns, rows);
        columnResolver = new TableValueColumnResolver(this, table);
    }

    /// <summary>
    /// Resolves the expression columns of the constructor to the values of the row being
    /// evaluated, as the reference's TableValueColumnResolver does.
    /// </summary>
    private sealed class TableValueColumnResolver : ColumnResolver
    {
        private readonly TableValueConstructor owner;

        private readonly TableValueConstructorTable table;

        internal TableValueColumnResolver(TableValueConstructor owner, TableValueConstructorTable table)
        {
            this.owner = owner;
            this.table = table;
        }

        /// <summary>The values of the row being evaluated, or null.</summary>
        internal Value?[]? CurrentRow { get; set; }

        /// <inheritdoc/>
        public Column[] GetColumns() => table.GetColumns();

        /// <inheritdoc/>
        public Column? FindColumn(string name) => table.FindColumn(name);

        /// <inheritdoc/>
        public Value? GetValue(Column column)
            => CurrentRow is null ? null : CurrentRow[column.GetColumnId()];

        /// <inheritdoc/>
        public Expression Optimize(ExpressionColumn expressionColumn, Column column)
            => owner.expressions[column.GetColumnId()];
    }

    /// <inheritdoc/>
    public override System.Text.StringBuilder GetPlanSql(System.Text.StringBuilder builder, int sqlFlags)
    {
        WriteWithList(builder, sqlFlags);
        // H2 getPlanSQL -> getSQL -> appendEndOfQueryToSQL.
        builder.Append("VALUES ");
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0) builder.Append(", ");
            Expression.WriteExpressions(builder.Append('('), [.. rows[i]], sqlFlags).Append(')');
        }
        AppendEndOfQueryToSql(builder, sqlFlags, GetExpressions());
        return builder;
    }
}
