/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.ArrayElementReference. */
using System.Globalization;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>One-based array element reference.</summary>
public sealed class ArrayElementReference : Operation2
{
    /// <summary>Creates an element reference.</summary>
    public ArrayElementReference(Expression left, Expression right) : base(left, right) { }
    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
        => right.GetUnenclosedSql(left.GetSql(builder, sqlFlags, AutoParentheses).Append('['), sqlFlags).Append(']');
    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value l = left.GetValue(session), r = right.GetValue(session);
        if (l is not ValueNull && r is not ValueNull)
        {
            int element = r.GetInt();
            if (left.GetTypeInfo().GetValueType() == Value.Array)
            {
                Value[] list = ((ValueArray)l).GetList();
                if (element >= 1 && element <= list.Length) return list[element - 1];
                throw DbException.Get(Api.ErrorCode.ArrayElementError2,
                    element.ToString(CultureInfo.InvariantCulture), "1.." + list.Length.ToString(CultureInfo.InvariantCulture));
            }

            Util.Json.JsonValue value = ((ValueJson)l.ConvertToType(TypeInfo.TypeJson, session)).GetDecomposition();
            if (value is Util.Json.JsonArray array)
            {
                Util.Json.JsonValue? jsonValue = array.GetElement(element - 1);
                if (jsonValue is not null) return ValueJson.FromJson(jsonValue);
            }
        }
        return ValueNull.Instance;
    }
    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        right = right.Optimize(session);
        TypeInfo leftType = left.GetTypeInfo();
        switch (leftType.GetValueType())
        {
            case Value.Null: return ValueExpression.Null;
            case Value.Json:
                type = TypeInfo.TypeJson;
                if (left.IsConstant() && right.IsConstant()) return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
                break;
            case Value.Array:
                type = (TypeInfo)leftType.GetExtTypeInfo()!;
                if (left.IsConstant() && right.IsConstant()) return TypedValueExpression.Get(GetValue(session), type);
                break;
            default: throw MvStore.Db.Store.GetInvalidExpressionTypeException("Array", left);
        }
        return this;
    }
}
