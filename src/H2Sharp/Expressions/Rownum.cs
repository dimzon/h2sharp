/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.Rownum.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What is left out, and why:
 *
 * The reference falls back to the enclosing prepared when there is no current select;
 * this port only threads the current select through expression readers, so outside one
 * ROWNUM is a syntax error rather than a lookup miss. The contexts that can reach the
 * reader without a select are the ones where the reference would also refuse or where
 * the port does not build names yet.
 */
using System.Text;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>
/// Represents the ROWNUM function.
/// </summary>
public sealed class Rownum : Expression
{
    private readonly Prepared prepared;

    private bool singleRow;

    /// <summary>
    /// Creates a ROWNUM over the running prepared statement.
    /// </summary>
    /// <param name="prepared">the prepared statement, not null</param>
    public Rownum(Prepared prepared)
    {
        this.prepared = prepared ?? throw DbException.GetInternalError();
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
        => ValueBigint.Get(prepared.GetCurrentRowNumber());

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeBigint;

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
        => builder.Append("ROWNUM()");

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
        => singleRow ? ValueExpression.Get(ValueBigint.Get(1L)) : this;

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        switch (visitor.GetVisitorType())
        {
            case ExpressionVisitor.QueryComparable:
            case ExpressionVisitor.OptimizableAggregate:
            case ExpressionVisitor.Deterministic:
            case ExpressionVisitor.Independent:
            case ExpressionVisitor.Evaluatable:
                return false;
            case ExpressionVisitor.DecrementQueryLevel:
                if (visitor.GetQueryLevel() > 0)
                {
                    singleRow = true;
                }

                break;
            default:
                break;
        }

        return true;
    }

    /// <inheritdoc/>
    public override int GetCost() => 0;
}
