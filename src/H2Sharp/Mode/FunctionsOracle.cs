/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.mode.FunctionsOracle.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * TO_DATE, TO_TIMESTAMP and TO_TIMESTAMP_TZ are listed with the reference's FunctionInfo, but
 * their bodies need ToDateParser/ToDateTokenizer, which are not ported; they throw a TODO that
 * names the missing parser instead of answering with a format of our own.
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
/// Functions for Oracle compatibility mode.
/// </summary>
public sealed class FunctionsOracle : ModeFunction
{
    private const int AddMonths = 2001;
    private const int SysGuid = AddMonths + 1;
    private const int ToDate = SysGuid + 1;
    private const int ToTimestamp = ToDate + 1;
    private const int ToTimestampTz = ToTimestamp + 1;

    private static readonly Dictionary<string, FunctionInfo> Functions = new(StringComparer.Ordinal);

    static FunctionsOracle()
    {
        Functions["ADD_MONTHS"] = new FunctionInfo(
            "ADD_MONTHS", AddMonths, 2, Value.Timestamp, true, true);
        Functions["SYS_GUID"] = new FunctionInfo("SYS_GUID", SysGuid, 0, Value.Varbinary, false, false);
        Functions["TO_DATE"] = new FunctionInfo("TO_DATE", ToDate, VarArgs, Value.Timestamp, true, true);
        Functions["TO_TIMESTAMP"] = new FunctionInfo(
            "TO_TIMESTAMP", ToTimestamp, VarArgs, Value.Timestamp, true, true);
        Functions["TO_TIMESTAMP_TZ"] = new FunctionInfo(
            "TO_TIMESTAMP_TZ", ToTimestampTz, VarArgs, Value.TimestampTz, true, true);
    }

    /// <summary>Returns the mode-specific function of that name, or null.</summary>
    /// <param name="upperName">the uppercase name</param>
    /// <returns>the function, or null</returns>
    public static FunctionsOracle? GetFunction(string upperName)
        => Functions.TryGetValue(upperName, out FunctionInfo? info) ? new FunctionsOracle(info) : null;

    private FunctionsOracle(FunctionInfo info) : base(info)
    {
    }

    /// <inheritdoc/>
    protected override void CheckParameterCount(int len)
    {
        int min = 0, max = int.MaxValue;
        switch (Info.Type)
        {
            case ToTimestamp:
            case ToTimestampTz:
                min = 1;
                max = 2;
                break;
            case ToDate:
                min = 1;
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
    public override Expression Optimize(SessionLocal session)
    {
        bool allConst = OptimizeArguments(session);
        type = Info.Type == SysGuid
            ? TypeInfo.GetTypeInfo(Value.Varbinary, 16, 0, null)
            : TypeInfo.GetTypeInfo(Info.ReturnDataType);
        if (allConst)
        {
            return ValueExpression.Get(GetValue(session));
        }

        return this;
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
        Value? v1 = GetNullOrValue(session, values, 1);
        return Info.Type switch
        {
            AddMonths => DateTimeFunction.DateaddValue(
                session, DateTimeFunction.Month, v1!.GetInt(), v0!),
            SysGuid => ValueUuid.GetNewRandom(7).ConvertToType(TypeInfo.TypeVarbinary),
            ToDate or ToTimestamp or ToTimestampTz => throw new TODOException(
                "FunctionsOracle." + Info.Name + ": ToDateParser and ToDateTokenizer are not ported"),
            _ => throw DbException.GetInternalError("type=" + Info.Type),
        };
    }
}
