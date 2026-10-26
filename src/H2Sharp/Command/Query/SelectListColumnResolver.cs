/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.query.SelectListColumnResolver, H2 2.5.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Command.Query;

/// <summary>
/// Resolves select-list aliases in HAVING, as the reference does.
/// </summary>
public sealed class SelectListColumnResolver : ColumnResolver
{
    private readonly Select select;

    private readonly Expression[] expressions;

    private readonly Column[] columns;

    /// <summary>Creates the resolver for a select.</summary>
    /// <param name="select">the select</param>
    public SelectListColumnResolver(Select select)
    {
        this.select = select;
        int columnCount = select.GetColumnCount();
        columns = new Column[columnCount];
        expressions = new Expression[columnCount];
        Expression[] columnList = select.GetExpressions();
        SessionLocal session = select.GetSession();
        for (int i = 0; i < columnCount; i++)
        {
            Expression expr = columnList[i];
            columns[i] = new Column(expr.GetAlias(session, i), TypeInfo.TypeNull, null, i);
            expressions[i] = expr.GetNonAliasExpression();
        }
    }

    /// <inheritdoc/>
    public Column[] GetColumns() => columns;

    /// <inheritdoc/>
    public Column? FindColumn(string name)
    {
        Database db = select.GetSession().GetDatabase();
        foreach (Column column in columns)
        {
            if (db.EqualsIdentifiers(column.GetName(), name))
            {
                return column;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public Select GetSelect() => select;

    /// <inheritdoc/>
    public Value? GetValue(Column column) => null;

    /// <inheritdoc/>
    public Expression Optimize(ExpressionColumn expressionColumn, Column column)
    {
        Expression expression = expressions[column.GetColumnId()];
        // H2's group validation rejects a HAVING reference to a select-list alias unless the
        // aliased expression is aggregate: a scalar alias is not considered grouped even when
        // GROUP BY names the same alias.
        if (!ContainsAggregate(expression))
        {
            throw Message.DbException.Get(Api.ErrorCode.MustGroupByColumn1,
                expressionColumn.GetSql(Util.HasSql.TraceSqlFlags));
        }

        return expression;
    }

    private static bool ContainsAggregate(Expression expression)
    {
        if (expression is Expressions.Analysis.DataAnalysisOperation operation && operation.IsAggregate())
        {
            return true;
        }

        for (int i = 0; i < expression.GetSubexpressionCount(); i++)
        {
            if (ContainsAggregate(expression.GetSubexpression(i)!))
            {
                return true;
            }
        }

        return false;
    }
}
