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
    private sealed class Target : ResultTarget
    {
        private readonly int columnCount;

        private readonly NullsDistinct nullsDistinct;

        private readonly LocalResult result;

        internal bool HasDuplicates;

        internal Target(int columnCount, NullsDistinct nullsDistinct, LocalResult result)
        {
            this.columnCount = columnCount;
            this.nullsDistinct = nullsDistinct;
            this.result = result;
        }

        public void LimitsWereApplied()
        {
            // Nothing to do
        }

        public long GetRowCount()
        {
            // Not required
            return 0L;
        }

        public void AddRow(params Value?[] values)
        {
            if (HasDuplicates)
            {
                return;
            }

            switch (nullsDistinct)
            {
                case NullsDistinct.Distinct:
                    for (int i = 0; i < columnCount; i++)
                    {
                        if (values[i] is ValueNull)
                        {
                            return;
                        }
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
                        return;
                    }

                    break;
                }

                default:
                    break;
            }

            if (values.Length != columnCount)
            {
                Value?[] copy = new Value?[columnCount];
                Array.Copy(values, copy, System.Math.Min(values.Length, columnCount));
                values = copy;
            }

            long expected = result.GetRowCount() + 1;
            result.AddRow(values);
            if (expected != result.GetRowCount())
            {
                HasDuplicates = true;
                result.Close();
            }
        }
    }

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
        if (query.IsStandardDistinct())
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
        Target target = new(columnCount, nullsDistinct, result);
        _ = query.Run(int.MaxValue, target);
        result.Close();
        return ValueBoolean.Get(!target.HasDuplicates);
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
