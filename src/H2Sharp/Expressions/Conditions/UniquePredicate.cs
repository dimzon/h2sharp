/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.UniquePredicate.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>
/// Unique predicate as in UNIQUE(SELECT ...).
/// </summary>
public sealed class UniquePredicate : PredicateWithSubquery
{
    private readonly NullsDistinct nullsDistinct;

    /// <summary>
    /// Creates a unique predicate.
    /// </summary>
    /// <param name="query">the subquery</param>
    /// <param name="nullsDistinct">how NULL rows count toward duplicates</param>
    public UniquePredicate(Query query, NullsDistinct nullsDistinct)
        : base(query)
    {
        this.nullsDistinct = nullsDistinct;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        base.Optimize(session);
        if (query.IsAnyDistinct())
        {
            return ValueExpression.True;
        }

        return this;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        query.SetSession(session);
        int columnCount = query.GetColumnCount();
        LocalResult result = new(session, query.GetExpressions(), columnCount, columnCount);
        result.SetDistinct();
        bool hasDuplicates = false;
        ResultInterface rows = query.Run(int.MaxValue);
        try
        {
            while (!hasDuplicates && rows.Next())
            {
                Value?[] values = rows.CurrentRow();
                if (values.Length != columnCount)
                {
                    Value?[] copy = new Value?[columnCount];
                    Array.Copy(values, copy, System.Math.Min(values.Length, columnCount));
                    values = copy;
                }

                switch (nullsDistinct)
                {
                    case NullsDistinct.Distinct:
                        bool hasNull = false;
                        for (int i = 0; i < columnCount; i++)
                        {
                            if (values[i] is ValueNull)
                            {
                                hasNull = true;
                                break;
                            }
                        }

                        if (hasNull)
                        {
                            continue;
                        }

                        break;
                    case NullsDistinct.AllDistinct:
                    {
                        bool allNull = true;
                        for (int i = 0; i < columnCount; i++)
                        {
                            if (values[i] is not ValueNull)
                            {
                                allNull = false;
                                break;
                            }
                        }

                        if (allNull)
                        {
                            continue;
                        }

                        break;
                    }

                    default:
                        break;
                }

                long expected = result.GetRowCount() + 1;
                result.AddRow(values);
                if (expected != result.GetRowCount())
                {
                    hasDuplicates = true;
                }
            }
        }
        finally
        {
            rows.Close();
            result.Close();
        }

        return ValueBoolean.Get(!hasDuplicates);
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        builder.Append("UNIQUE");
        if (nullsDistinct != NullsDistinct.Distinct)
        {
            NullsDistinctSql.GetSql(builder.Append(' '), nullsDistinct, 0);
        }

        return base.GetUnenclosedSql(builder, sqlFlags);
    }
}
