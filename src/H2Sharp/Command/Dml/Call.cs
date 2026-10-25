/*
 * Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.command.dml.Call, H2 2.5.
 * Copyright 2026 H2Sharp contributors.
 */
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Functions.Table;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Command.Dml;

/// <summary>CALL expression or table-valued function.</summary>
public sealed class Call : Prepared
{
    private Expression expression = null!;
    private TableFunction? tableFunction;
    private Expression[] expressions = [];

    public Call(SessionLocal session) : base(session) { }
    public override ResultInterface QueryMeta()
    {
        int count = expressions.Length;
        LocalResult result = new(GetSession(), expressions, count, count);
        result.Done();
        return result;
    }
    public override long Update()
    {
        if (tableFunction is not null) return base.Update();
        Value value = expression.GetValue(GetSession());
        return value.GetValueType() is Value.Unknown or Value.Null ? 0 : value.GetInt();
    }
    // Java query(long) is Run(long) in this Prepared hierarchy.
    public override ResultInterface Run(long maxRows)
    {
        SetCurrentRowNumber(1);
        if (tableFunction is not null) return tableFunction.GetValue(GetSession());
        LocalResult result = new(GetSession(), expressions, 1, 1);
        result.AddRow(expression.GetValue(GetSession()));
        result.Done();
        return result;
    }
    public override void Prepare()
    {
        if (tableFunction is not null)
        {
            SetPrepareAlways(true);
            tableFunction.Optimize(GetSession());
            ResultInterface result = tableFunction.GetValueTemplate(GetSession());
            int count = result.GetVisibleColumnCount();
            expressions = new Expression[count];
            for (int i = 0; i < count; i++)
            {
                string name = result.GetColumnName(i);
                string alias = result.GetAlias(i);
                Expression column = new ExpressionColumn(GetSession().GetDatabase(), new Column(name, result.GetColumnType(i)));
                if (alias != name) column = new Alias(column, alias, false);
                expressions[i] = column;
            }
        }
        else expressions = [expression = expression.Optimize(GetSession())];
    }
    public void SetExpression(Expression expression) => this.expression = expression;
    public void SetTableFunction(TableFunction tableFunction) => this.tableFunction = tableFunction;
    public override bool IsQuery() => true;
    public override bool IsTransactional() => true;
    public override bool IsReadOnly() => tableFunction is null && expression.IsEverything(ExpressionVisitor.ReadOnlyVisitor);
    public override int GetCommandType() => CommandInterface.Call;
    public override bool IsCacheable() => tableFunction is null;
}
