/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.mode.FunctionsLegacy.
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
/// Legacy functions, unavailable in Regular mode.
/// </summary>
public class FunctionsLegacy : ModeFunction
{
    private static readonly Dictionary<string, FunctionInfo> Functions = new(StringComparer.Ordinal);

    private const int Identity = 6001;
    private const int ScopeIdentity = Identity + 1;

    static FunctionsLegacy()
    {
        Functions["IDENTITY"] = new FunctionInfo("IDENTITY", Identity, 0, Value.Bigint, true, false);
        Functions["SCOPE_IDENTITY"] = new FunctionInfo(
            "SCOPE_IDENTITY", ScopeIdentity, 0, Value.Bigint, true, false);
    }

    /// <summary>Returns the mode-specific function of that name, or null.</summary>
    /// <param name="upperName">the uppercase name</param>
    /// <returns>the function, or null</returns>
    public static FunctionsLegacy? GetFunction(string upperName)
        => Functions.TryGetValue(upperName, out FunctionInfo? info) ? new FunctionsLegacy(info) : null;

    private FunctionsLegacy(FunctionInfo info) : base(info)
    {
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
        => Info.Type switch
        {
            Identity or ScopeIdentity => session.GetLastIdentity().ConvertToType(type!, session),
            _ => throw DbException.GetInternalError("type=" + Info.Type),
        };

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        type = TypeInfo.GetTypeInfo(Info.ReturnDataType);
        return this;
    }
}
