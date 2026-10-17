/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.analysis.WindowFrameBound.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;

using H2Sharp.Engine;
using H2Sharp.Tables;

namespace H2Sharp.Expressions.Analysis;

/// <summary>
/// Window frame bound.
/// </summary>
public class WindowFrameBound
{
    private readonly WindowFrameBoundType type;

    private Expression? value;

    private bool isVariable;

    private int expressionIndex = -1;

    /// <summary>
    /// Creates new instance of window frame bound.
    /// </summary>
    /// <param name="type">bound type</param>
    /// <param name="value">bound value, if any</param>
    public WindowFrameBound(WindowFrameBoundType type, Expression? value)
    {
        this.type = type;
        this.value = type is WindowFrameBoundType.Preceding or WindowFrameBoundType.Following
            ? value
            : null;
    }

    /// <summary>
    /// Returns the type.
    /// </summary>
    /// <remarks>
    /// Named <c>GetBoundType</c> rather than <c>GetType</c>: the reference's <c>getType()</c>
    /// cannot keep its name here, because <see cref="object.GetType()"/> is not virtual and must
    /// keep returning <see cref="System.Type"/>.
    /// </remarks>
    /// <returns>the type</returns>
    public WindowFrameBoundType GetBoundType() => type;

    /// <summary>
    /// Returns the value.
    /// </summary>
    /// <returns>the value</returns>
    public Expression? GetValue() => value;

    /// <summary>
    /// Returns whether bound is defined as n PRECEDING or n FOLLOWING.
    /// </summary>
    /// <returns>whether bound is defined as n PRECEDING or n FOLLOWING</returns>
    public bool IsParameterized()
        => type is WindowFrameBoundType.Preceding or WindowFrameBoundType.Following;

    /// <summary>
    /// Returns whether bound is defined with a variable. This method may be used only after
    /// <see cref="Optimize"/> invocation.
    /// </summary>
    /// <returns>whether bound is defined with a variable</returns>
    public bool IsVariable() => isVariable;

    /// <summary>
    /// Returns the index of preserved expression.
    /// </summary>
    /// <returns>the index of preserved expression, or -1</returns>
    public int GetExpressionIndex() => expressionIndex;

    /// <summary>
    /// Sets the index of preserved expression.
    /// </summary>
    /// <param name="expressionIndex">the index to set</param>
    internal void SetExpressionIndex(int expressionIndex) => this.expressionIndex = expressionIndex;

    /// <summary>
    /// Maps the columns of the resolver to expression columns.
    /// </summary>
    /// <param name="resolver">the column resolver</param>
    /// <param name="level">the subquery nesting level</param>
    /// <param name="state">current state for nesting checks</param>
    internal void MapColumns(ColumnResolver resolver, int level, int state)
    {
        value?.MapColumns(resolver, level, state);
    }

    /// <summary>
    /// Tries to optimize the bound expression.
    /// </summary>
    /// <param name="session">the session</param>
    internal void Optimize(SessionLocal session)
    {
        if (value is not null)
        {
            value = value.Optimize(session);
            if (!value.IsConstant())
            {
                isVariable = true;
            }
        }
    }

    /// <summary>
    /// Updates an aggregate value.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="stage">select stage</param>
    internal void UpdateAggregate(SessionLocal session, int stage)
    {
        value?.UpdateAggregate(session, stage);
    }

    /// <summary>
    /// Appends the SQL representation to the specified builder.
    /// </summary>
    /// <param name="builder">string builder</param>
    /// <param name="following">if false return SQL for starting clause, if true return SQL for following clause</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the specified builder</returns>
    public StringBuilder GetSql(StringBuilder builder, bool following, int sqlFlags)
    {
        if (type is WindowFrameBoundType.Preceding or WindowFrameBoundType.Following)
        {
            value!.GetUnenclosedSql(builder, sqlFlags).Append(' ');
        }

        return builder.Append(type.GetSql());
    }
}
