/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.function.table.TableFunction. */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Result;

namespace H2Sharp.Expressions.Functions.Table;

/// <summary>A table-valued function.</summary>
public abstract class TableFunction : IExpressionWithVariableParameters, INamedExpression
{
    /// <summary>Function arguments.</summary>
    protected Expression[] args;
    private int argsCount;
    /// <summary>Creates a function.</summary>
    protected TableFunction(Expression[] args) => this.args = args;
    /// <inheritdoc/>
    public void AddParameter(Expression param)
    {
        if (argsCount >= args.Length) Array.Resize(ref args, args.Length * 2);
        args[argsCount++] = param;
    }
    /// <inheritdoc/>
    public void DoneWithParameters() { if (args.Length != argsCount) Array.Resize(ref args, argsCount); }
    /// <summary>Evaluates the function.</summary>
    public abstract ResultInterface GetValue(SessionLocal session);
    /// <summary>Returns an empty result with column metadata.</summary>
    public abstract ResultInterface GetValueTemplate(SessionLocal session);
    /// <summary>Optimizes arguments.</summary>
    public virtual void Optimize(SessionLocal session)
    { for (int i = 0; i < args.Length; i++) args[i] = args[i].Optimize(session); }
    /// <summary>Whether results depend only on the arguments.</summary>
    public abstract bool IsDeterministic();
    /// <inheritdoc/>
    public abstract string GetName();
    /// <summary>Renders the function call.</summary>
    public virtual StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => Expression.WriteExpressions(builder.Append(GetName()).Append('('), args, sqlFlags).Append(')');
}
