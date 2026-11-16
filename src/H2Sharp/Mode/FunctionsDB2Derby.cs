/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.mode.FunctionsDB2Derby.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Modes;

/// <summary>
/// Functions for DB2 and Derby compatibility modes.
/// </summary>
public sealed class FunctionsDB2Derby : ModeFunction
{
    private const int IdentityValLocal = 5001;

    private static readonly Dictionary<string, FunctionInfo> Functions = new(StringComparer.Ordinal);

    private static readonly TypeInfo IdentityValLocalType =
        TypeInfo.GetTypeInfo(Value.Numeric, 31, 0, ExtTypeInfoNumeric.Decimal);

    static FunctionsDB2Derby()
        => Functions["IDENTITY_VAL_LOCAL"] = new FunctionInfo(
            "IDENTITY_VAL_LOCAL", IdentityValLocal, 0, Value.Bigint, true, false);

    /// <summary>Returns the mode-specific function of that name, or null.</summary>
    /// <param name="upperName">the uppercase name</param>
    /// <returns>the function, or null</returns>
    public static FunctionsDB2Derby? GetFunction(string upperName)
        => Functions.TryGetValue(upperName, out FunctionInfo? info)
            ? new FunctionsDB2Derby(info) : null;

    private FunctionsDB2Derby(FunctionInfo info) : base(info)
    {
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
        => Info.Type == IdentityValLocal
            ? session.GetLastIdentity().ConvertToType(type!, session)
            : throw DbException.GetInternalError("type=" + Info.Type);

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        if (Info.Type != IdentityValLocal)
        {
            throw DbException.GetInternalError("type=" + Info.Type);
        }

        type = IdentityValLocalType;
        return this;
    }
}
