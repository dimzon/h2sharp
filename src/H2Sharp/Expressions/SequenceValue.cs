/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.SequenceValue.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A sequence used in a statement: NEXT VALUE FOR and CURRENT VALUE FOR.
 */
using System.Text;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Schemas;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>
/// A sequence used in a statement: the next value of it, or the current one.
/// </summary>
public sealed class SequenceValue : Expression
{
    private readonly Sequence sequence;

    private readonly bool current;

    private readonly Prepared? prepared;

    /// <summary>
    /// Creates the next value of a sequence.
    /// </summary>
    /// <param name="sequence">the sequence</param>
    /// <param name="ownerCommand">the statement that asks, or null</param>
    public SequenceValue(Sequence sequence, Prepared? ownerCommand)
    {
        this.sequence = sequence;
        current = false;
        prepared = ownerCommand;
    }

    /// <summary>
    /// Creates the current value of a sequence.
    /// </summary>
    /// <param name="sequence">the sequence</param>
    public SequenceValue(Sequence sequence)
    {
        this.sequence = sequence;
        current = true;
        prepared = null;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
        => current
            ? session.GetCurrentValueFor(sequence)
            : session.GetNextValueFor(sequence, prepared);

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => sequence.GetDataType();

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append(current ? "CURRENT" : "NEXT").Append(" VALUE FOR ");
        return sequence.GetSql(builder, sqlFlags);
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        switch (visitor.GetVisitorType())
        {
            case ExpressionVisitor.Deterministic:
            case ExpressionVisitor.Independent:
            case ExpressionVisitor.QueryComparable:
                return false;
            case ExpressionVisitor.ReadOnly:
                return current;
            case ExpressionVisitor.GetDependencies:
                visitor.AddDependency(sequence);
                return true;
            default:
                return true;
        }
    }

    /// <inheritdoc/>
    public override int GetCost() => 1;
}
