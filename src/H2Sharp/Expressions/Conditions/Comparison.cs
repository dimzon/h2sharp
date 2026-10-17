/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.Comparison.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Example comparison expressions are ID=1, NAME=NAME, NAME IS NULL.
 *
 * The whole class is about the three-valued answer and about when the right side may be
 * skipped, and both are visible in the SQL it prints as well as in what it returns.
 *
 * NULL is not a value that compares: `= NULL` is unknown, not false, and that is why the
 * compare helpers answer with a Value and why `(compareType & ~1) != EQUAL_NULL_SAFE` appears
 * wherever NULL must not short-circuit. That expression is the reference's way of asking "is
 * this one of the two null-safe comparisons", since the null-safe types are adjacent to the
 * ordinary ones: EQUAL/NOT_EQUAL and EQUAL_NULL_SAFE/NOT_EQUAL_NULL_SAFE differ in exactly one
 * bit.
 *
 * The optimization that matters is "do not evaluate right if not necessary". If the left side
 * is NULL and the comparison is not null-safe, the answer is unknown whatever the right side
 * is — so the right side is never read. That is observable: a right side with a side effect, or
 * one that fails, does not fail; and for the common `WHERE column = @p` it is the difference
 * between reading a parameter and not.
 *
 * The swapping in optimize is the other half: `1 = ID` becomes `ID = 1` with the comparison
 * reversed, so that the left side is the column. That is what lets the optimiser treat every
 * comparison the same way regardless of how the user wrote it, and it is why getReversedCompareType
 * is a table rather than an arithmetic trick — "<" becomes ">", but "IS NOT DISTINCT FROM"
 * reverses to itself.
 */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>
/// Example comparison expressions are ID=1, NAME=NAME, NAME IS NULL.
/// </summary>
public sealed class Comparison : Condition
{
    /// <summary>
    /// The comparison type meaning = as in ID=1.
    /// </summary>
    public const int Equal = 0;

    /// <summary>
    /// The comparison type meaning &lt;&gt; as in ID&lt;&gt;1.
    /// </summary>
    public const int NotEqual = 1;

    /// <summary>
    /// The comparison type meaning &lt; as in ID&lt;1.
    /// </summary>
    public const int Smaller = 2;

    /// <summary>
    /// The comparison type meaning &gt; as in ID&gt;1.
    /// </summary>
    public const int Bigger = 3;

    /// <summary>
    /// The comparison type meaning &lt;= as in ID&lt;=1.
    /// </summary>
    public const int SmallerEqual = 4;

    /// <summary>
    /// The comparison type meaning &gt;= as in ID&gt;=1.
    /// </summary>
    public const int BiggerEqual = 5;

    /// <summary>
    /// The comparison type meaning ID IS NOT DISTINCT FROM 1.
    /// </summary>
    public const int EqualNullSafe = 6;

    /// <summary>
    /// The comparison type meaning ID IS DISTINCT FROM 1.
    /// </summary>
    public const int NotEqualNullSafe = 7;

    /// <summary>
    /// This is a comparison type that is only used for spatial index
    /// conditions (operator "&amp;&amp;").
    /// </summary>
    public const int SpatialIntersects = 8;

    /// <summary>
    /// This is a pseudo comparison type that is only used for index conditions.
    /// It means the comparison will always yield FALSE. Example: 1=0.
    /// </summary>
    public const int False = 9;

    /// <summary>
    /// This is a pseudo comparison type that is only used for index conditions.
    /// It means equals any value of a list. Example: IN(1, 2, 3).
    /// </summary>
    public const int InList = 10;

    /// <summary>
    /// This is a pseudo comparison type that is only used for index conditions.
    /// It means equals any value of a ARRAY. Example: ARRAY[1, 2, 3].
    /// </summary>
    public const int InArray = 11;

    /// <summary>
    /// This is a pseudo comparison type that is only used for index conditions.
    /// It means equals any value of a list. Example: IN(SELECT ...).
    /// </summary>
    public const int InQuery = 12;

    /// <summary>
    /// The operator for each comparison type, in the order of the constants above.
    /// </summary>
    internal static readonly string[] CompareTypes =
    [
        "=", "<>", "<", ">", "<=", ">=",
        "IS NOT DISTINCT FROM", "IS DISTINCT FROM",
        "&&",
    ];

    private int compareType;

    private Expression left;

    private Expression right;

    private readonly bool whenOperand;

    /// <summary>
    /// Creates a comparison.
    /// </summary>
    /// <param name="compareType">the comparison type</param>
    /// <param name="left">the left hand side</param>
    /// <param name="right">the right hand side</param>
    /// <param name="whenOperand">whether this is the when operand of a simple case</param>
    public Comparison(int compareType, Expression left, Expression right, bool whenOperand)
    {
        this.left = left;
        this.right = right;
        this.compareType = compareType;
        this.whenOperand = whenOperand;
    }

    /// <inheritdoc/>
    public override bool NeedParentheses()
    {
        return true;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        return GetWhenSql(left.GetSql(builder, sqlFlags, AutoParentheses), sqlFlags);
    }

    /// <inheritdoc/>
    public override StringBuilder GetWhenSql(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append(' ').Append(CompareTypes[compareType]).Append(' ');
        return right.GetSql(builder, sqlFlags, AutoParentheses);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        right = right.Optimize(session);
        TypeInfo.CheckComparable(left.GetTypeInfo(), right.GetTypeInfo());

        if (whenOperand)
        {
            return this;
        }

        if (right is ExpressionColumn && (left.IsConstant() || left is Parameter))
        {
            Expression temp = left;
            left = right;
            right = temp;
            compareType = GetReversedCompareType(compareType);
        }

        if (left.IsConstant() && right.IsConstant())
        {
            return ValueExpression.Get(GetValue(session));
        }

        return this;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value l = left.GetValue(session);

        // Optimization: do not evaluate right if not necessary
        if (l is ValueNull && (compareType & ~1) != EqualNullSafe)
        {
            return ValueNull.Instance;
        }

        return Compare(session, l, right.GetValue(session), compareType);
    }

    /// <inheritdoc/>
    public override bool GetWhenValue(SessionLocal session, Value left)
    {
        if (!whenOperand)
        {
            return base.GetWhenValue(session, left);
        }

        // Optimization: do not evaluate right if not necessary
        if (left is ValueNull && (compareType & ~1) != EqualNullSafe)
        {
            return false;
        }

        return Compare(session, left, right.GetValue(session), compareType).IsTrue();
    }

    /// <summary>
    /// Compare two values.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="l">the first value</param>
    /// <param name="r">the second value</param>
    /// <param name="compareType">the compare type</param>
    /// <returns>result of comparison, either TRUE, FALSE, or NULL</returns>
    public static Value Compare(SessionLocal session, Value l, Value r, int compareType)
    {
        switch (compareType)
        {
            case Equal:
                return FromCompare(session.CompareWithNull(l, r, true), 0);

            case NotEqual:
                return FromCompare(session.CompareWithNull(l, r, true), 1);

            case Smaller:
                return FromCompare(session.CompareWithNull(l, r, false), 2);

            case SmallerEqual:
                return FromCompare(session.CompareWithNull(l, r, false), 4);

            case Bigger:
                return FromCompare(session.CompareWithNull(l, r, false), 3);

            case BiggerEqual:
                return FromCompare(session.CompareWithNull(l, r, false), 5);

            case EqualNullSafe:
                return ValueBoolean.Get(session.AreEqual(l, r));

            case NotEqualNullSafe:
                return ValueBoolean.Get(!session.AreEqual(l, r));

            default:
                throw new ArgumentOutOfRangeException(nameof(compareType));
        }
    }

    /// <summary>
    /// Turns the answer of a three-valued comparison into a value. The sentinel is
    /// <see cref="int.MinValue"/>: it is not a comparison result any comparison can produce,
    /// so it cannot be confused with "less than everything".
    /// </summary>
    private static Value FromCompare(int cmp, int compareType)
    {
        bool unknown = cmp == int.MinValue;
        bool less = cmp < 0;
        bool equal = cmp == 0;

        bool result = compareType switch
        {
            0 => equal,          // =
            1 => !equal,         // <>
            2 => less,           // <
            3 => !less && !equal, // >
            4 => less || equal,   // <=
            _ => !less,          // >=
        };

        return unknown ? ValueNull.Instance : ValueBoolean.Get(result);
    }

    /// <summary>
    /// Gets the comparison type of the reversed comparison, which is how "1 &lt; ID" becomes
    /// "ID &gt; 1".
    /// </summary>
    /// <param name="compareType">the comparison type</param>
    /// <returns>the reversed comparison type</returns>
    public static int GetReversedCompareType(int compareType)
    {
        return compareType switch
        {
            Equal => Equal,
            NotEqual => NotEqual,
            Smaller => Bigger,
            Bigger => Smaller,
            SmallerEqual => BiggerEqual,
            BiggerEqual => SmallerEqual,
            EqualNullSafe => EqualNullSafe,
            NotEqualNullSafe => NotEqualNullSafe,
            SpatialIntersects => SpatialIntersects,
            _ => throw new ArgumentOutOfRangeException(nameof(compareType)),
        };
    }

    /// <inheritdoc/>
    public override void CreateIndexConditions(SessionLocal session, TableFilter filter)
    {
        if (!whenOperand)
        {
            CreateIndexConditions(filter, left, right, compareType);
        }
    }

    /// <summary>
    /// Whether a comparison between the column type and the higher type can use an index.
    /// </summary>
    /// <remarks>
    /// A numeric integer column against a wider numeric is unsafe: the index holds integers
    /// and the condition may match fractions between them. The range check is the reference's
    /// numeric-type test written out, because the type catalog it reads does not exist here.
    /// </remarks>
    /// <param name="colType">the column type</param>
    /// <param name="higherType">the higher type</param>
    /// <returns>true when the index can serve the comparison</returns>
    private static bool IsSafeIndexCondition(TypeInfo colType, TypeInfo higherType)
    {
        if (!TypeInfo.HaveSameOrdering(colType, higherType))
        {
            return false;
        }

        int cType = colType.GetValueType(), hType = higherType.GetValueType();
        if (cType == hType)
        {
            return true;
        }

        if (cType is >= Value.Tinyint and <= Value.Decfloat
            && hType is >= Value.Tinyint and <= Value.Decfloat)
        {
            bool cIsInt = cType is Value.Tinyint or Value.Smallint or Value.Integer or Value.Bigint;
            bool hIsInt = hType is Value.Tinyint or Value.Smallint or Value.Integer or Value.Bigint;
            if (cIsInt && !hIsInt)
            {
                return false;
            }
        }

        return true;
    }

    internal static void CreateIndexConditions(TableFilter filter, Expression left, Expression right, int compareType)
    {
        if (compareType == NotEqual || compareType == NotEqualNullSafe)
        {
            return;
        }

        if (!filter.GetTable()!.IsQueryComparable())
        {
            return;
        }

        if (compareType != SpatialIntersects)
        {
            bool lIsList = left is ExpressionList, rIsList = right is ExpressionList;
            if (lIsList)
            {
                if (rIsList)
                {
                    CreateIndexConditions(filter, (ExpressionList)left, (ExpressionList)right, compareType);
                }
                else if (right is ValueExpression)
                {
                    CreateIndexConditions(filter, (ExpressionList)left, (ValueExpression)right, compareType);
                }
            }
            else if (rIsList && left is ValueExpression)
            {
                CreateIndexConditions(filter, (ExpressionList)right, (ValueExpression)left,
                    GetReversedCompareType(compareType));
                return;
            }
        }

        ExpressionColumn? l = null;
        if (left is ExpressionColumn leftColumn)
        {
            l = leftColumn;
            if (!ReferenceEquals(filter, l.GetTableFilter()))
            {
                l = null;
            }
        }

        ExpressionColumn? r = null;
        if (right is ExpressionColumn rightColumn)
        {
            r = rightColumn;
            if (!ReferenceEquals(filter, r.GetTableFilter()))
            {
                r = null;
            }
        }

        // one side must be from the current filter
        if ((l is null) == (r is null))
        {
            return;
        }

        if (l is null)
        {
            if (!left.IsEverything(ExpressionVisitor.GetNotFromResolverVisitor(filter)))
            {
                return;
            }
        }
        else
        {
            // r is null
            if (!right.IsEverything(ExpressionVisitor.GetNotFromResolverVisitor(filter)))
            {
                return;
            }
        }

        switch (compareType)
        {
            case Equal:
            case EqualNullSafe:
            case Bigger:
            case BiggerEqual:
            case SmallerEqual:
            case Smaller:
            case SpatialIntersects:
                if (l is not null)
                {
                    TypeInfo colType = l.GetTypeInfo();
                    TypeInfo higherType = TypeInfo.GetHigherType(colType, right.GetTypeInfo());
                    if (IsSafeIndexCondition(colType, higherType))
                    {
                        filter.AddIndexCondition(IndexCondition.Get(compareType, l, right));
                    }
                }
                else
                {
                    TypeInfo colType = r!.GetTypeInfo();
                    TypeInfo higherType = TypeInfo.GetHigherType(colType, left.GetTypeInfo());
                    if (IsSafeIndexCondition(colType, higherType))
                    {
                        filter.AddIndexCondition(IndexCondition.Get(GetReversedCompareType(compareType), r, left));
                    }
                }

                break;
            default:
                throw DbException.GetInternalError("type=" + compareType);
        }
    }

    private static void CreateIndexConditions(TableFilter filter, ExpressionList left, ExpressionList right,
        int compareType)
    {
        int c = left.GetSubexpressionCount();
        if (c == 0 || c != right.GetSubexpressionCount())
        {
            return;
        }

        if (compareType != Equal && compareType != EqualNullSafe)
        {
            if (c > 1)
            {
                if (compareType == Bigger)
                {
                    compareType = BiggerEqual;
                }
                else if (compareType == Smaller)
                {
                    compareType = SmallerEqual;
                }
            }

            c = 1;
        }

        for (int i = 0; i < c; i++)
        {
            CreateIndexConditions(filter, left.GetSubexpression(i), right.GetSubexpression(i), compareType);
        }
    }

    private static void CreateIndexConditions(TableFilter filter, ExpressionList left, ValueExpression right,
        int compareType)
    {
        int c = left.GetSubexpressionCount();
        if (c == 0)
        {
            return;
        }
        else if (c == 1)
        {
            CreateIndexConditions(filter, left.GetSubexpression(0), right, compareType);
        }
        else if (c > 1)
        {
            Value v = right.GetValue(null!);
            if (v.GetValueType() == Value.Row)
            {
                Value[] values = ((ValueRow)v).GetList();
                if (c != values.Length)
                {
                    return;
                }

                if (compareType != Equal && compareType != EqualNullSafe)
                {
                    if (compareType == Bigger)
                    {
                        compareType = BiggerEqual;
                    }
                    else if (compareType == Smaller)
                    {
                        compareType = SmallerEqual;
                    }

                    c = 1;
                }

                for (int i = 0; i < c; i++)
                {
                    CreateIndexConditions(filter, left.GetSubexpression(i), ValueExpression.Get(values[i]),
                        compareType);
                }
            }
        }
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
        left.SetEvaluatable(tableFilter, value);
        right.SetEvaluatable(tableFilter, value);
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        left.UpdateAggregate(session, stage);
        right.UpdateAggregate(session, stage);
    }

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        left.MapColumns(resolver, level, state);
        right.MapColumns(resolver, level, state);
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        return left.IsEverything(visitor) && right.IsEverything(visitor);
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        return left.GetCost() + 1 + right.GetCost();
    }

    /// <inheritdoc/>
    public override int GetSubexpressionCount()
    {
        return 2;
    }

    /// <inheritdoc/>
    public override Expression? GetSubexpression(int index)
    {
        return index switch
        {
            0 => left,
            1 => right,
            _ => throw new IndexOutOfRangeException(),
        };
    }

    /// <inheritdoc/>
    public override bool IsWhenConditionOperand()
    {
        return whenOperand;
    }
}
