/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.ConcatenationOperation. */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions.Functions;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>Character, binary, or array concatenation.</summary>
public sealed class ConcatenationOperation : OperationN
{
    /// <summary>Creates an operation with variable parameters.</summary>
    public ConcatenationOperation() : base(new Expression[4]) { }
    /// <summary>Creates a binary concatenation.</summary>
    public ConcatenationOperation(Expression op1, Expression op2) : base([op1, op2]) => argsCount = 2;
    /// <inheritdoc/>
    public override bool NeedParentheses() => true;
    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0) builder.Append(" || ");
            args[i].GetSql(builder, sqlFlags, AutoParentheses);
        }
        return builder;
    }
    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        int l = args.Length;
        if (l == 2)
        {
            Value v1 = args[0].GetValue(session).ConvertToType(type!, session);
            if (v1 is ValueNull) return v1;
            Value v2 = args[1].GetValue(session).ConvertToType(type!, session);
            if (v2 is ValueNull) return v2;
            return GetValue(session, v1, v2);
        }
        return GetValue(session, l);
    }
    private Value GetValue(SessionLocal session, Value l, Value r)
    {
        if (type!.GetValueType() == Value.Varchar)
            return ValueVarchar.Get(l.GetString() + r.GetString());
        if (type.GetValueType() == Value.Varbinary)
        {
            byte[] a = l.GetBytesNoCopy()!, b = r.GetBytesNoCopy()!;
            byte[] bytes = new byte[a.Length + b.Length];
            Array.Copy(a, bytes, a.Length);
            Array.Copy(b, 0, bytes, a.Length, b.Length);
            return ValueVarbinary.GetNoCopy(bytes);
        }
        Value[] leftValues = ((ValueArray)l).GetList(), rightValues = ((ValueArray)r).GetList();
        Value[] values = new Value[leftValues.Length + rightValues.Length];
        Array.Copy(leftValues, values, leftValues.Length);
        Array.Copy(rightValues, 0, values, leftValues.Length, rightValues.Length);
        return ValueArray.Get((TypeInfo)type.GetExtTypeInfo()!, values, session);
    }
    private Value GetValue(SessionLocal session, int l)
    {
        Value[] values = new Value[l];
        for (int i = 0; i < l; i++)
        {
            Value v = args[i].GetValue(session).ConvertToType(type!, session);
            if (v is ValueNull) return v;
            values[i] = v;
        }
        if (type!.GetValueType() == Value.Varchar)
        {
            StringBuilder builder = new();
            foreach (Value v in values) builder.Append(v.GetString());
            return ValueVarchar.Get(builder.ToString(), session);
        }
        if (type.GetValueType() == Value.Varbinary)
        {
            int total = 0;
            foreach (Value v in values) total += v.GetBytesNoCopy()!.Length;
            byte[] bytes = new byte[total];
            int offset = 0;
            foreach (Value v in values)
            {
                byte[] b = v.GetBytesNoCopy()!;
                Array.Copy(b, 0, bytes, offset, b.Length);
                offset += b.Length;
            }
            return ValueVarbinary.GetNoCopy(bytes);
        }
        int length = 0;
        foreach (Value v in values) length += ((ValueArray)v).GetList().Length;
        Value[] result = new Value[length];
        int position = 0;
        foreach (Value v in values)
        {
            Value[] elements = ((ValueArray)v).GetList();
            Array.Copy(elements, 0, result, position, elements.Length);
            position += elements.Length;
        }
        return ValueArray.Get((TypeInfo)type.GetExtTypeInfo()!, result, session);
    }
    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        DetermineType(session);
        InlineSubexpressions(e => e is ConcatenationOperation && e.GetTypeInfo().GetValueType() == type!.GetValueType());
        if (type!.GetValueType() == Value.Varchar && session.GetMode().TreatEmptyStringsAsNull)
            return new ConcatFunction(ConcatFunction.Concat, args).Optimize(session);
        bool allConst = true, anyConst = false;
        foreach (Expression arg in args)
        {
            if (arg.IsConstant()) anyConst = true;
            else allConst = false;
        }
        if (allConst) return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        if (anyConst)
        {
            int offset = 0;
            for (int i = 0; i < args.Length; i++)
            {
                Expression arg1 = args[i];
                if (arg1.IsConstant())
                {
                    Value v1 = arg1.GetValue(session).ConvertToType(type, session);
                    if (v1 is ValueNull) return TypedValueExpression.Get(ValueNull.Instance, type);
                    if (IsEmpty(v1)) continue;
                    while (i + 1 < args.Length && args[i + 1].IsConstant())
                    {
                        Value v2 = args[++i].GetValue(session).ConvertToType(type, session);
                        if (v2 is ValueNull) return TypedValueExpression.Get(ValueNull.Instance, type);
                        if (!IsEmpty(v2)) v1 = GetValue(session, v1, v2);
                    }
                    arg1 = ValueExpression.Get(v1);
                }
                args[offset++] = arg1;
            }
            if (offset == 1)
                return TypeInfo.AreSameTypes(type, args[0].GetTypeInfo()) ? args[0] : new CastSpecification(args[0], type);
            argsCount = offset;
            DoneWithParameters();
        }
        return this;
    }
    private void DetermineType(SessionLocal session)
    {
        bool anyArray = false, allBinary = true, allCharacter = true;
        TypeInfo[] types = new TypeInfo[args.Length]; // existing port overload accepts types, not Typed[].
        for (int i = 0; i < args.Length; i++)
        {
            args[i] = args[i].Optimize(session);
            types[i] = args[i].GetTypeInfo();
            int t = types[i].GetValueType();
            if (t == Value.Array) { anyArray = true; allBinary = allCharacter = false; }
            else if (t == Value.Null) { }
            else if (DataType.IsBinaryStringType(t)) allCharacter = false;
            else if (DataType.IsCharacterStringType(t)) allBinary = false;
            else allBinary = allCharacter = false;
        }
        if (anyArray) type = TypeInfo.GetTypeInfo(Value.Array, -1, 0, TypeInfo.GetHigherType(types).GetExtTypeInfo());
        else if (allBinary || allCharacter)
        {
            long precision = GetPrecision(0);
            for (int i = 1; i < args.Length; i++) precision = DataType.AddPrecision(precision, GetPrecision(i));
            type = TypeInfo.GetTypeInfo(allBinary ? Value.Varbinary : Value.Varchar, precision, 0, null);
        }
        else type = TypeInfo.TypeVarchar;
    }
    private long GetPrecision(int i) => args[i].GetTypeInfo().GetValueType() != Value.Null ? args[i].GetTypeInfo().GetPrecision() : 0;
    private static bool IsEmpty(Value v) => v.GetValueType() switch
    {
        Value.Varchar => v.GetString()!.Length == 0,
        Value.Varbinary => v.GetBytesNoCopy()!.Length == 0,
        _ => ((ValueArray)v).GetList().Length == 0,
    };
}
