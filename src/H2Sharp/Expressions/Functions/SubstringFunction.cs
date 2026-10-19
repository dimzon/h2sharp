/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.SubstringFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;

using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A SUBSTRING function.
/// </summary>
public sealed class SubstringFunction : FunctionN
{
    /// <summary>
    /// Creates a substring function.
    /// </summary>
    public SubstringFunction()
        : base(new Expression[3])
    {
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value? v1, Value? v2, Value? v3)
    {
        if (type!.GetValueType() == Value.Varbinary)
        {
            byte[] s = v1!.GetBytesNoCopy()!;
            int sl = s.Length;
            int start = v2!.GetInt();
            // These compatibility conditions violate the Standard
            if (start == 0)
            {
                start = 1;
            }
            else if (start < 0)
            {
                start = sl + start + 1;
            }

            int end = v3 == null ? Math.Max(sl + 1, start) : start + v3.GetInt();
            // SQL Standard requires "data exception - substring error" when
            // end < start but H2 does not throw it for compatibility
            start = Math.Max(start, 1);
            end = Math.Min(end, sl + 1);
            if (start > sl || end <= start)
            {
                return ValueVarbinary.Empty;
            }

            start--;
            end--;
            if (start == 0 && end == s.Length)
            {
                return v1.ConvertToType(TypeInfo.TypeVarbinary);
            }

            return ValueVarbinary.GetNoCopy(s[start..end]);
        }
        else
        {
            string s = v1!.GetString()!;
            int sl = s.Length;
            int start = v2!.GetInt();
            // These compatibility conditions violate the Standard
            if (start == 0)
            {
                start = 1;
            }
            else if (start < 0)
            {
                start = sl + start + 1;
            }

            int end = v3 == null ? Math.Max(sl + 1, start) : start + v3.GetInt();
            // SQL Standard requires "data exception - substring error" when
            // end < start but H2 does not throw it for compatibility
            start = Math.Max(start, 1);
            end = Math.Min(end, sl + 1);
            if (start > sl || end <= start)
            {
                return session.GetMode().TreatEmptyStringsAsNull ? ValueNull.Instance : ValueVarchar.Empty;
            }

            return ValueVarchar.Get(s[(start - 1)..(end - 1)], null);
        }
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        bool allConst = OptimizeArguments(session, true);
        int len = args.Length;
        if (len < 2 || len > 3)
        {
            throw DbException.Get(ErrorCode.InvalidParameterCount2, GetName(), "2..3");
        }

        TypeInfo argType = args[0].GetTypeInfo();
        long p = argType.GetPrecision();
        Expression arg = args[1];
        Value? v;
        if (arg.IsConstant() && (v = arg.GetValue(session)) != ValueNull.Instance)
        {
            // if only two arguments are used,
            // subtract offset from first argument length
            p -= v.GetLong() - 1;
        }

        if (args.Length == 3)
        {
            arg = args[2];
            if (arg.IsConstant() && (v = arg.GetValue(session)) != ValueNull.Instance)
            {
                // if the third argument is constant it is at most this value
                p = Math.Min(p, v.GetLong());
            }
        }

        p = Math.Max(0, p);
        type = TypeInfo.GetTypeInfo(
            DataType.IsBinaryStringType(argType.GetValueType()) ? Value.Varbinary : Value.Varchar, p, 0, null);
        if (allConst)
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        return this;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        _ = args[0].GetUnenclosedSql(builder.Append(GetName()).Append('('), sqlFlags);
        _ = args[1].GetUnenclosedSql(builder.Append(" FROM "), sqlFlags);
        if (args.Length > 2)
        {
            _ = args[2].GetUnenclosedSql(builder.Append(" FOR "), sqlFlags);
        }

        return builder.Append(')');
    }

    /// <inheritdoc/>
    public override string GetName() => "SUBSTRING";
}
