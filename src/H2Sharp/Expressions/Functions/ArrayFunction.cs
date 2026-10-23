/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.function.ArrayFunction. */
using System;
using System.Globalization;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>TRIM_ARRAY, ARRAY_CONTAINS, ARRAY_SLICE.</summary>
public sealed class ArrayFunction : FunctionN
{
    /// <summary>TRIM_ARRAY.</summary>
    public const int TrimArray = 0;
    /// <summary>ARRAY_CONTAINS.</summary>
    public const int ArrayContains = 1;
    /// <summary>ARRAY_SLICE.</summary>
    public const int ArraySlice = 2;
    private static readonly string[] Names = ["TRIM_ARRAY", "ARRAY_CONTAINS", "ARRAY_SLICE"];
    private readonly int function;
    /// <summary>Creates an array function.</summary>
    public ArrayFunction(Expression arg1, Expression arg2, Expression? arg3, int function)
        : base(arg3 is null ? [arg1, arg2] : [arg1, arg2, arg3]) => this.function = function;
    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value v1 = args[0].GetValue(session), v2 = args[1].GetValue(session);
        switch (function)
        {
            case TrimArray:
                if (v2 is ValueNull) return ValueNull.Instance;
                int trim = v2.GetInt();
                if (trim < 0) throw DbException.Get(Api.ErrorCode.ArrayElementError2,
                    trim.ToString(CultureInfo.InvariantCulture), "0..CARDINALITY(array)");
                if (v1 is ValueNull) return v1;
                ValueArray array = v1.ConvertToAnyArray(session);
                Value[] elements = array.GetList();
                if (trim > elements.Length) throw DbException.Get(Api.ErrorCode.ArrayElementError2,
                    trim.ToString(CultureInfo.InvariantCulture), "0.." + elements.Length.ToString(CultureInfo.InvariantCulture));
                return trim == 0 ? array : ValueArray.Get(array.GetComponentType(), elements[..(elements.Length - trim)], session);
            case ArrayContains:
                if (v1.GetValueType() is Value.Array or Value.Row)
                {
                    foreach (Value value in ((ValueCollectionBase)v1).GetList())
                        if (session.AreEqual(value, v2)) return ValueBoolean.True;
                    return ValueBoolean.False;
                }
                return ValueNull.Instance;
            case ArraySlice:
                if (v1 is ValueNull || v2 is ValueNull) return ValueNull.Instance;
                Value v3 = args[2].GetValue(session);
                if (v3 is ValueNull) return ValueNull.Instance;
                ValueArray slice = v1.ConvertToAnyArray(session);
                int index1 = unchecked(v2.GetInt() - 1), index2 = v3.GetInt();
                bool isPg = session.GetMode().Enum == Mode.ModeEnum.PostgreSql;
                if (index1 > index2) return isPg ? ValueArray.Get(slice.GetComponentType(), Value.EmptyValues, session) : ValueNull.Instance;
                if (index1 < 0)
                {
                    if (isPg) index1 = 0;
                    else return ValueNull.Instance;
                }
                if (index2 > slice.GetList().Length)
                {
                    if (isPg) index2 = slice.GetList().Length;
                    else return ValueNull.Instance;
                }
                // Java Arrays.copyOfRange rejects an inverted range and pads the upper end.
                if (index1 > index2) throw new ArgumentException("from > to");
                Value[] values = new Value[index2 - index1];
                Array.Copy(slice.GetList(), index1, values, 0, Math.Min(values.Length, slice.GetList().Length - index1));
                return ValueArray.Get(slice.GetComponentType(), values, session);
            default: throw DbException.GetInternalError("function=" + function);
        }
    }
    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        bool allConst = OptimizeArguments(session, true);
        switch (function)
        {
            case TrimArray:
            case ArraySlice:
                type = args[0].GetTypeInfo();
                if (type.GetValueType() is not Value.Array and not Value.Null)
                    throw MvStore.Db.Store.GetInvalidExpressionTypeException(GetName() + " array argument", args[0]);
                break;
            case ArrayContains: type = TypeInfo.TypeBoolean; break;
            default: throw DbException.GetInternalError("function=" + function);
        }
        return allConst ? TypedValueExpression.GetTypedIfNull(GetValue(session), type) : this;
    }
    /// <inheritdoc/>
    public override string GetName() => Names[function];
}
