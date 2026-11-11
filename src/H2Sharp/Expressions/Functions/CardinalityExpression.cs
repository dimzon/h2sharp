/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.CardinalityExpression.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * getValue()'s JSON branch counts the elements of a JSONArray, as the reference does after
 * its Value.convertToAnyJson: the switch has already told us the value is JSON, and
 * convertToAnyJson is the identity for a JSON value, so the port reads its decomposition
 * directly.
 */
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// Cardinality expression.
/// </summary>
public sealed class CardinalityExpression : Function1
{
    private readonly bool max;

    /// <summary>
    /// Creates new instance of cardinality expression.
    /// </summary>
    /// <param name="arg">argument</param>
    /// <param name="max">false for CARDINALITY, true for ARRAY_MAX_CARDINALITY</param>
    public CardinalityExpression(Expression arg, bool max)
        : base(arg)
    {
        this.max = max;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        int result;
        if (max)
        {
            TypeInfo t = arg.GetTypeInfo();
            if (t.GetValueType() == Value.Array)
            {
                result = MathUtils.ConvertLongToInt(t.GetPrecision());
            }
            else
            {
                throw DbException.GetInvalidValueException("array", arg.GetValue(session).GetTraceSql());
            }
        }
        else
        {
            Value v = arg.GetValue(session);
            if (v == ValueNull.Instance)
            {
                return ValueNull.Instance;
            }

            switch (v.GetValueType())
            {
                case Value.Json:
                {
                    Util.Json.JsonValue decomposition = ((ValueJson)v).GetDecomposition();
                    if (decomposition is Util.Json.JsonArray array)
                    {
                        result = array.Length();
                    }
                    else
                    {
                        return ValueNull.Instance;
                    }

                    break;
                }

                case Value.Array:
                    result = ((ValueArray)v).GetList().Length;
                    break;
                default:
                    throw DbException.GetInvalidValueException("array", v.GetTraceSql());
            }
        }

        return ValueInteger.Get(result);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        arg = arg.Optimize(session);
        type = TypeInfo.TypeInteger;
        if (arg.IsConstant())
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        return this;
    }

    /// <inheritdoc/>
    public override string GetName()
    {
        return max ? "ARRAY_MAX_CARDINALITY" : "CARDINALITY";
    }
}
