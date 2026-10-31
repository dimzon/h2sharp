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

    private Column[] columns = [];

    private Expression[] expressions = [];

    private TableValueConstructorTable? table;

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
    public override int GetCommandType() => CommandInterface.Select;

    /// <inheritdoc/>
    public override ResultInterface? QueryMeta() => null;

    /// <inheritdoc/>
    public override bool IsTransactional() => true;

    /// <inheritdoc/>
    public override void Init()
    {
        if (checkInit)
        {
            throw DbException.GetInternalError();
        }

        checkInit = true;

        if (orderList is not null && expressions.Length > 0)
        {
            _ = InitOrder(null, true, null, [.. expressions]);
        }
    }

    /// <inheritdoc/>
    public override void PrepareExpressions()
    {
        if (table is null)
        {
            CreateTable();
        }

        if (orderList is not null && sort is null)
        {
            PrepareOrder(orderList, expressions.Length);
        }

        if (IsWithTies() && sort is null)
        {
            throw DbException.Get(Api.ErrorCode.WithTiesWithoutOrderBy);
        }

        foreach (List<Expression> row in rows)
        {
            for (int i = 0; i < visibleColumnCount; i++)
            {
                row[i] = row[i].Optimize(GetSession());
            }
        }
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
        foreach (List<Expression> row in rows)
        {
            foreach (Expression expression in row)
            {
                if (!expression.IsEverything(nested))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override Expression[] GetExpressions() => expressions;

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
        LocalResult result = new(current, expressions, visibleColumnCount, visibleColumnCount);
        if (distinct)
        {
            result.SetDistinct();
        }

        if (sort is not null)
        {
            result.SetSortOrder(sort);
        }

        foreach (List<Expression> row in rows)
        {
            Value?[] values = new Value?[visibleColumnCount];
            for (int i = 0; i < visibleColumnCount; i++)
            {
                values[i] = row[i].GetValue(current).ConvertToType(columns[i].GetTypeInfo());
            }

            result.AddRow(values);
        }

        return FinishResult(result, offsetFetch.Offset, offsetFetch.Fetch, offsetFetch.FetchPercent, target);
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
        List<Expression> columnExpressions = [];
        for (int c = 0; c < columnCount; c++)
        {
            string name = "C" + (c + 1).ToString(CultureInfo.InvariantCulture);
            newColumns[c] = new Column(name, types[c]);

            // The reference names the columns with an expression column, which knows no type
            // of its own; this port names them with an alias over the first value, so that
            // the name and the type of the column are both there. The alias takes the type of
            // the first row, and the column's type is the higher type over every row, so a
            // cast keeps the name and gives the column the type it has in the table — without
            // it a column whose first row is NULL would report NULL as its type.
            Expression columnValue = row[c];
            if (columnValue.GetTypeInfo().GetValueType() != types[c].GetValueType()
                || columnValue.GetTypeInfo().GetScale() != types[c].GetScale())
            {
                columnValue = new CastSpecification(columnValue, types[c]);
            }

            columnExpressions.Add(new Alias(columnValue, name, aliasColumnName: true));
        }

        columns = newColumns;
        expressions = [.. columnExpressions];
        table = new TableValueConstructorTable(
            GetSession().GetDatabase().GetMainSchema(), GetSession(), columns, expressions, rows);
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
