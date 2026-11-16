/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.mode.FunctionInfo.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
namespace H2Sharp.Modes;

/// <summary>
/// Information about a built-in function of a compatibility mode.
/// </summary>
public sealed class FunctionInfo
{
    /// <summary>The name of the function.</summary>
    public readonly string Name;

    /// <summary>The function type.</summary>
    public readonly int Type;

    /// <summary>The number of parameters, or -1 for a variable number.</summary>
    public readonly int ParameterCount;

    /// <summary>The value type of the result.</summary>
    public readonly int ReturnDataType;

    /// <summary>Whether a NULL argument makes the result NULL.</summary>
    public readonly bool NullIfParameterIsNull;

    /// <summary>Whether the function is deterministic.</summary>
    public readonly bool Deterministic;

    /// <summary>Creates the information of a function.</summary>
    public FunctionInfo(
        string name, int type, int parameterCount, int returnDataType,
        bool nullIfParameterIsNull, bool deterministic)
    {
        Name = name;
        Type = type;
        ParameterCount = parameterCount;
        ReturnDataType = returnDataType;
        NullIfParameterIsNull = nullIfParameterIsNull;
        Deterministic = deterministic;
    }

    /// <summary>Copies the information under a different name.</summary>
    public FunctionInfo(FunctionInfo source, string name)
    {
        Name = name;
        Type = source.Type;
        ParameterCount = source.ParameterCount;
        ReturnDataType = source.ReturnDataType;
        NullIfParameterIsNull = source.NullIfParameterIsNull;
        Deterministic = source.Deterministic;
    }
}
