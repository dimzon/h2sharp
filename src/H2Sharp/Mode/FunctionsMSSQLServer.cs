/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.mode.FunctionsMSSQLServer.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Functions;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Modes;

/// <summary>
/// Functions for MSSQLServer compatibility mode.
/// </summary>
public sealed class FunctionsMSSQLServer : ModeFunction
{
    private static readonly Dictionary<string, FunctionInfo> Functions = new(StringComparer.Ordinal);

    private const int Charindex = 4001;
    private const int Getdate = Charindex + 1;
    private const int Isnull = Getdate + 1;
    private const int Len = Isnull + 1;
    private const int Newid = Len + 1;
    private const int Newsequentialid = Newid + 1;
    private const int ScopeIdentity = Newsequentialid + 1;

    private static readonly TypeInfo ScopeIdentityType =
        TypeInfo.GetTypeInfo(Value.Numeric, 38, 0, null);

    static FunctionsMSSQLServer()
    {
        Functions["CHARINDEX"] = new FunctionInfo("CHARINDEX", Charindex, VarArgs, Value.Integer, true, true);
        Functions["GETDATE"] = new FunctionInfo("GETDATE", Getdate, 0, Value.Timestamp, false, true);
        Functions["LEN"] = new FunctionInfo("LEN", Len, 1, Value.Integer, true, true);
        Functions["NEWID"] = new FunctionInfo("NEWID", Newid, 0, Value.Uuid, true, false);
        Functions["NEWSEQUENTIALID"] = new FunctionInfo(
            "NEWSEQUENTIALID", Newsequentialid, 0, Value.Uuid, true, false);
        Functions["ISNULL"] = new FunctionInfo("ISNULL", Isnull, 2, Value.Null, false, true);
        Functions["SCOPE_IDENTITY"] = new FunctionInfo(
            "SCOPE_IDENTITY", ScopeIdentity, 0, Value.Numeric, true, false);
    }

    /// <summary>
    /// Returns the mode-specific function with the given name, or null.
    /// </summary>
    /// <param name="upperName">the uppercase function name</param>
    /// <returns>the function, or null</returns>
    public static FunctionsMSSQLServer? GetFunction(string upperName)
    {
        if (Functions.TryGetValue(upperName, out FunctionInfo? info))
        {
            return new FunctionsMSSQLServer(info);
        }

        return null;
    }

    private FunctionsMSSQLServer(FunctionInfo info) : base(info)
    {
    }

    /// <inheritdoc/>
    protected override void CheckParameterCount(int len)
    {
        int min, max;
        switch (Info.Type)
        {
            case Charindex:
                min = 2;
                max = 3;
                break;
            default:
                throw DbException.GetInternalError("type=" + Info.Type);
        }

        if (len < min || len > max)
        {
            throw DbException.Get(
                Api.ErrorCode.InvalidParameterCount2, Info.Name, min + ".." + max);
        }
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value?[]? values = GetArgumentsValues(session);
        if (values is null)
        {
            return ValueNull.Instance;
        }

        Value? v0 = GetNullOrValue(session, values, 0);
        switch (Info.Type)
        {
            case Len:
                {
                    long len;
                    if (v0!.GetValueType() == Value.Char)
                    {
                        string s = v0.GetString()!;
                        int l = s.Length;
                        while (l > 0 && s[l - 1] == ' ')
                        {
                            l--;
                        }

                        len = l;
                    }
                    else
                    {
                        len = v0.CharLength();
                    }

                    return ValueBigint.Get(len);
                }
            case ScopeIdentity:
                return session.GetLastIdentity().ConvertToType(type!, session);
            default:
                throw DbException.GetInternalError("type=" + Info.Type);
        }
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        switch (Info.Type)
        {
            case Charindex:
                return new StringFunction(args, StringFunction.Locate).Optimize(session);
            case Getdate:
                return new CurrentDateTimeValueFunction(
                    CurrentDateTimeValueFunction.Localtimestamp, 3).Optimize(session);
            case Isnull:
                return new CoalesceFunction(CoalesceFunction.Coalesce, args).Optimize(session);
            case Newid:
                // MS SQL Server uses version 4.
                return new RandFunction(
                    ValueExpression.Get(ValueInteger.Get(4)), RandFunction.RandomUuid).Optimize(session);
            case Newsequentialid:
                // MS SQL Server uses something non-standard, use version 7 instead.
                return new RandFunction(
                    ValueExpression.Get(ValueInteger.Get(7)), RandFunction.RandomUuid).Optimize(session);
            case ScopeIdentity:
                type = ScopeIdentityType;
                break;
            default:
                type = TypeInfo.GetTypeInfo(Info.ReturnDataType);
                if (OptimizeArguments(session))
                {
                    return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
                }

                break;
        }

        return this;
    }
}
