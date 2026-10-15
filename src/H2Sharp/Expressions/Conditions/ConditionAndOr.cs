/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.ConditionAndOr.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * An 'and' or 'or' condition as in WHERE ID=1 AND NAME=?.
 * Mostly useful for optimisation and preventing stack overflow where generated
 * SQL has tons of conditions.
 *
 * AND and OR are three-valued and their truth tables differ in a way that is easy to get wrong:
 * both are FALSE if any operand decides the answer, and the rest are not read — but AND stops
 * on a FALSE while OR stops on a TRUE, and *either* way it records that it saw a NULL. So "any
 * NULL makes the whole thing unknown" is wrong: FALSE OR NULL is FALSE, because FALSE decided it
 * first. That is why hasNull is remembered rather than returned on the spot, and why the loop
 * returns as soon as a non-NULL operand settles the result.
 *
 * The list is kept rather than a binary tree because generated SQL has "tons of conditions":
 * a generated WHERE with 500 clauses would be 500 nested nodes and could overflow the stack.
 * Sorting by cost before merging is what makes the merge pass find the constants early — a
 * constant condition is what a whole conjunction can be reduced by.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>
/// An 'and' or 'or' condition as in WHERE ID=1 AND NAME=?.
/// Mostly useful for optimisation and preventing stack overflow where generated
/// SQL has tons of conditions.
/// </summary>
public sealed class ConditionAndOr : Condition
{
    /// <summary>The type of an AND condition.</summary>
    public const int And = 0;

    /// <summary>The type of an OR condition.</summary>
    public const int Or = 1;

    private readonly int andOrType;

    /// <summary>
    /// The operands, kept as a list and inserted at the front most of the time.
    /// </summary>
    private readonly List<Expression> expressions;

    /// <summary>
    /// Additional conditions for index only. Not here: the pairwise merges that would add them
    /// (getAdditionalAnd, optimizeOr) need the index conditions and the IN-list conditions,
    /// which are not ported yet. The field exists in the reference and this is where it will go.
    /// </summary>
    public IReadOnlyList<Expression>? Added => null;

    /// <summary>
    /// Creates a condition of three or more operands.
    /// </summary>
    /// <param name="andOrType">the type of this condition</param>
    /// <param name="expr1">the first operand</param>
    /// <param name="expr2">the second operand</param>
    /// <param name="expr3">the third operand</param>
    public ConditionAndOr(int andOrType, Expression expr1, Expression expr2, Expression expr3)
    {
        this.andOrType = andOrType;
        expressions = [expr1, expr2, expr3];
    }

    /// <summary>
    /// Creates a condition of two operands.
    /// </summary>
    /// <remarks>
    /// Not a constructor in the reference, and deliberately not one there either: a two-operand
    /// AND is built by the parser as a three-operand node and then folded, because the fold is
    /// what collapses <c>A AND TRUE</c>. Building it directly here is a convenience for the
    /// optimiser, and it goes through exactly the same fold.
    /// </remarks>
    /// <param name="andOrType">the type of this condition</param>
    /// <param name="expr1">the first operand</param>
    /// <param name="expr2">the second operand</param>
    public ConditionAndOr(int andOrType, Expression expr1, Expression expr2)
        : this(andOrType, new List<Expression> { expr1, expr2 })
    {
    }

    /// <summary>
    /// Creates a condition from a list of operands.
    /// </summary>
    /// <param name="andOrType">the type of this condition</param>
    /// <param name="expressions">the operands</param>
    public ConditionAndOr(int andOrType, List<Expression> expressions)
    {
        this.andOrType = andOrType;
        this.expressions = expressions;
    }

    /// <summary>
    /// Gets the type of this condition: <see cref="And"/> or <see cref="Or"/>.
    /// </summary>
    public int GetAndOrType()
    {
        return andOrType;
    }

    /// <summary>
    /// Adds the expression at the beginning of the list.
    /// </summary>
    /// <param name="e">the expression</param>
    public void AddFirst(Expression e)
    {
        expressions.Insert(0, e);
    }

    /// <inheritdoc/>
    public override bool NeedParentheses()
    {
        return true;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        expressions[0].GetSql(builder, sqlFlags, AutoParentheses);
        for (int i = 1; i < expressions.Count; i++)
        {
            switch (andOrType)
            {
                case And:
                    _ = builder.Append("\n    AND ");
                    break;
                case Or:
                    _ = builder.Append("\n    OR ");
                    break;
                default:
                    throw DbException.GetInternalError("andOrType=" + andOrType);
            }

            expressions[i].GetSql(builder, sqlFlags, AutoParentheses);
        }

        return builder;
    }

    /// <inheritdoc/>
    public override void CreateIndexConditions(SessionLocal session, TableFilter filter)
    {
        // Only AND can turn into an index condition: an OR means a row may match either side.
        if (andOrType != And)
        {
            return;
        }

        foreach (Expression e in expressions)
        {
            e.CreateIndexConditions(session, filter);
        }
    }

    /// <inheritdoc/>
    public override Expression? GetNotIfPossible(SessionLocal session)
    {
        // (NOT (A OR B)): (NOT(A) AND NOT(B))
        // (NOT (A AND B)): (NOT(A) OR NOT(B))
        List<Expression> newList = new(expressions.Count);
        foreach (Expression e in expressions)
        {
            Expression? l = e.GetNotIfPossible(session);
            if (l is null)
            {
                l = new ConditionNot(e);
            }

            newList.Add(l);
        }

        int reversed = andOrType == And ? Or : And;
        return new ConditionAndOr(reversed, newList);
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        bool hasNull = false;
        switch (andOrType)
        {
            case And:
                foreach (Expression e in expressions)
                {
                    Value v = e.GetValue(session);
                    if (v is ValueNull)
                    {
                        hasNull = true;
                    }
                    else if (!v.GetBoolean())
                    {
                        return ValueBoolean.False;
                    }
                }

                return hasNull ? ValueNull.Instance : ValueBoolean.True;

            case Or:
                foreach (Expression e in expressions)
                {
                    Value v = e.GetValue(session);
                    if (v is ValueNull)
                    {
                        hasNull = true;
                    }
                    else if (v.GetBoolean())
                    {
                        return ValueBoolean.True;
                    }
                }

                return hasNull ? ValueNull.Instance : ValueBoolean.False;

            default:
                throw DbException.GetInternalError("type=" + andOrType);
        }
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        // NULL handling: see wikipedia,
        // http://www-cs-students.stanford.edu/~wlam/compsci/sqlnulls

        // first pass, optimize individual sub-expressions
        for (int i = 0; i < expressions.Count; i++)
        {
            expressions[i] = expressions[i].Optimize(session);
        }

        // Cheapest first: a constant is what a whole conjunction can be reduced by, and it is
        // only found early if it sorts first.
        expressions.Sort(static (lhs, rhs) => lhs.GetCost() - rhs.GetCost());

        // second pass, optimize combinations
        for (int i = 0; i < expressions.Count - 1;)
        {
            Expression left = expressions[i];
            Expression right = expressions[i + 1];
            Expression? e = OptimizeIfConstant(session, andOrType, left, right);
            if (e is null)
            {
                i++;
                continue;
            }

            expressions[i] = e;
            expressions.RemoveAt(i + 1);
        }

        return expressions.Count == 1 ? expressions[0] : this;
    }

    /// <summary>
    /// Folds two operands when the pair decides the whole condition.
    /// </summary>
    /// <remarks>
    /// Each side's value is read once: the eager isTrue/isFalse/isUnknown triple this
    /// replaced read every constant twice, and a second read of a value is never free.
    /// <para>
    /// NULL does not decide: FALSE AND NULL is FALSE only because FALSE dominates, while
    /// TRUE AND NULL is UNKNOWN, and UNKNOWN is its own expression rather than FALSE. A
    /// NULL next to a non-constant folds nothing at all. A TRUE next to an expression
    /// casts it to boolean instead of passing it through, which is where a non-boolean
    /// would otherwise leak into a condition.
    /// </para>
    /// <para>
    /// The reference also merges comparable pairs here (getAdditionalAnd, optimizeOr), which
    /// is where "A=1 AND B=1 AND A=B" loses a comparison. Those need index conditions and the
    /// IN-list conditions, which are not ported yet; what is here is the fold, and the doc
    /// says so rather than leaving the absence to be noticed as a slow query.
    /// </para>
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="andOrType">the type of the condition</param>
    /// <param name="left">the left operand</param>
    /// <param name="right">the right operand</param>
    /// <returns>the folded expression, or null</returns>
    public static Expression? OptimizeIfConstant(SessionLocal session, int andOrType, Expression left, Expression right)
    {
        if (!left.IsConstant())
        {
            if (!right.IsConstant())
            {
                return null;
            }

            return OptimizeConstant(session, andOrType, right.GetValue(session), left);
        }

        Value l = left.GetValue(session);
        if (!right.IsConstant())
        {
            return OptimizeConstant(session, andOrType, l, right);
        }

        Value r = right.GetValue(session);
        switch (andOrType)
        {
            case And:
                if (l.IsFalse() || r.IsFalse())
                {
                    return ValueExpression.False;
                }

                if (l is ValueNull || r is ValueNull)
                {
                    return TypedValueExpression.Unknown;
                }

                return ValueExpression.True;

            case Or:
                if (l.IsTrue() || r.IsTrue())
                {
                    return ValueExpression.True;
                }

                if (l is ValueNull || r is ValueNull)
                {
                    return TypedValueExpression.Unknown;
                }

                return ValueExpression.False;

            default:
                throw DbException.GetInternalError("type=" + andOrType);
        }
    }

    private static Expression? OptimizeConstant(SessionLocal session, int andOrType, Value l, Expression right)
    {
        if (l is not ValueNull)
        {
            switch (andOrType)
            {
                case And:
                    return l.GetBoolean() ? CastToBoolean(session, right) : ValueExpression.False;

                case Or:
                    return l.GetBoolean() ? ValueExpression.True : CastToBoolean(session, right);

                default:
                    throw DbException.GetInternalError("type=" + andOrType);
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool b)
    {
        foreach (Expression e in expressions)
        {
            e.SetEvaluatable(tableFilter, b);
        }
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        foreach (Expression e in expressions)
        {
            e.UpdateAggregate(session, stage);
        }
    }

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        foreach (Expression e in expressions)
        {
            e.MapColumns(resolver, level, state);
        }
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        foreach (Expression e in expressions)
        {
            if (!e.IsEverything(visitor))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        int cost = 0;
        foreach (Expression e in expressions)
        {
            cost += e.GetCost();
        }

        return cost;
    }

    /// <inheritdoc/>
    public override int GetSubexpressionCount()
    {
        return expressions.Count;
    }

    /// <inheritdoc/>
    public override Expression? GetSubexpression(int index)
    {
        return 0 <= index && index < expressions.Count ? expressions[index] : throw new IndexOutOfRangeException();
    }
}
