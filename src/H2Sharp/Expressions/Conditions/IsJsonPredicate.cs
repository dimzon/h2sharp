/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.IsJsonPredicate.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Util.Json;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>
/// IS JSON predicate.
/// </summary>
public sealed class IsJsonPredicate : Condition
{
    private Expression left;

    private readonly bool not;

    private readonly bool whenOperand;

    private readonly bool withUniqueKeys;

    private readonly JsonItemType itemType;

    /// <summary>
    /// Creates an IS JSON predicate.
    /// </summary>
    /// <param name="left">the expression to test</param>
    /// <param name="not">whether this is IS NOT JSON</param>
    /// <param name="whenOperand">whether this is the when operand of the simple case</param>
    /// <param name="withUniqueKeys">whether object keys must be unique</param>
    /// <param name="itemType">the required kind of JSON value</param>
    public IsJsonPredicate(Expression left, bool not, bool whenOperand, bool withUniqueKeys, JsonItemType itemType)
    {
        this.left = left;
        this.whenOperand = whenOperand;
        this.not = not;
        this.withUniqueKeys = withUniqueKeys;
        this.itemType = itemType;
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
        builder.Append(" IS");
        if (not)
        {
            builder.Append(" NOT");
        }

        builder.Append(" JSON");
        switch (itemType)
        {
            case JsonItemType.Value:
                break;
            case JsonItemType.Array:
                builder.Append(" ARRAY");
                break;
            case JsonItemType.Object:
                builder.Append(" OBJECT");
                break;
            case JsonItemType.Scalar:
                builder.Append(" SCALAR");
                break;
            default:
                throw DbException.GetInternalError("itemType=" + itemType);
        }

        if (withUniqueKeys)
        {
            builder.Append(" WITH UNIQUE KEYS");
        }

        return builder;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        if (!whenOperand && left.IsConstant())
        {
            return ValueExpression.Get(GetValue(session));
        }

        return this;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value value = left.GetValue(session);
        if (value is ValueNull)
        {
            return ValueNull.Instance;
        }

        return ValueBoolean.Get(Answer(value));
    }

    /// <inheritdoc/>
    public override bool GetWhenValue(SessionLocal session, Value leftValue)
    {
        if (!whenOperand)
        {
            return base.GetWhenValue(session, leftValue);
        }

        if (leftValue is ValueNull)
        {
            return false;
        }

        return Answer(leftValue);
    }

    private bool Answer(Value value)
    {
        bool result;
        switch (value.GetValueType())
        {
            case Value.Varbinary:
            case Value.Binary:
            case Value.Blob:
            {
                byte[] bytes = value.GetBytesNoCopy()!;
                JsonValidationTarget target = withUniqueKeys
                    ? new JsonValidationTargetWithUniqueKeys()
                    : new JsonValidationTargetWithoutUniqueKeys();
                try
                {
                    result = itemType.Includes(JsonBytesSource.Parse(bytes, target)) ^ not;
                }
                catch (Exception)
                {
                    result = not;
                }

                break;
            }

            case Value.Json:
            {
                JsonItemType valueItemType = ((ValueJson)value).GetItemType();
                if (!itemType.Includes(valueItemType))
                {
                    result = not;
                    break;
                }

                if (!withUniqueKeys || valueItemType == JsonItemType.Scalar)
                {
                    result = !not;
                    break;
                }

                goto case Value.Varchar;
            }

            case Value.Varchar:
            case Value.VarcharIgnorecase:
            case Value.Char:
            case Value.Clob:
            {
                string text = value.GetString()!;
                JsonValidationTarget target = withUniqueKeys
                    ? new JsonValidationTargetWithUniqueKeys()
                    : new JsonValidationTargetWithoutUniqueKeys();
                try
                {
                    result = itemType.Includes(JsonStringSource.Parse(text, target)) ^ not;
                }
                catch (Exception)
                {
                    result = not;
                }

                break;
            }

            default:
                result = not;
                break;
        }

        return result;
    }

    /// <inheritdoc/>
    public override bool IsWhenConditionOperand()
    {
        return whenOperand;
    }

    /// <inheritdoc/>
    public override Expression? GetNotIfPossible(SessionLocal session)
    {
        if (whenOperand)
        {
            return null;
        }

        return new IsJsonPredicate(left, !not, false, withUniqueKeys, itemType);
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool b)
    {
        left.SetEvaluatable(tableFilter, b);
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        left.UpdateAggregate(session, stage);
    }

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        left.MapColumns(resolver, level, state);
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        return left.IsEverything(visitor);
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        int cost = left.GetCost();
        if (left.GetTypeInfo().GetValueType() == Value.Json
            && (!withUniqueKeys || itemType == JsonItemType.Scalar))
        {
            cost++;
        }
        else
        {
            cost += 10;
        }

        return cost;
    }

    /// <inheritdoc/>
    public override int GetSubexpressionCount()
    {
        return 1;
    }

    /// <inheritdoc/>
    public override Expression? GetSubexpression(int index)
    {
        if (index == 0)
        {
            return left;
        }

        throw new IndexOutOfRangeException();
    }
}
