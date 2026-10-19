/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mode.ModeFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sliced to getFunction, the static lookup the parser asks before it decides whether a name is a
 * function. The class half — ModeFunction extends FunctionN over a FunctionInfo — is the
 * compatibility-mode functions themselves (FunctionsLegacy, FunctionsMySQL, ...), and none of
 * those is ported; the slice answers null for the regular mode, which is the only mode a database
 * can be created in so far, and refuses for every other mode rather than pretending the
 * compatibility functions are absent.
 *
 * The reference walks up from the database to its mode to choose the table; the port's Database
 * does not carry a mode yet, so the mode is passed in by the caller — the same mode
 * SessionLocal.GetMode answers through the cast provider. When Database.getMode arrives, this
 * parameter goes away.
 *
 * The namespace is Mode + s, the same rename the Value package has: a namespace named Mode would
 * shadow the Mode type for every file under H2Sharp, and a namespace member beats an imported
 * type, so a plain Mode would name the namespace instead of the mode.
 */
using H2Sharp.Engine;

namespace H2Sharp.Modes;

/// <summary>
/// Base class for mode-specific functions. Only the static lookup is ported.
/// </summary>
public static class ModeFunction
{
    /// <summary>
    /// Constant for variable number of arguments.
    /// </summary>
    public const int VarArgs = -1;

    /// <summary>
    /// Get an instance of the given function for this mode. If no function with this name is
    /// found, null is returned.
    /// </summary>
    /// <param name="mode">the mode</param>
    /// <param name="name">the upper case function name</param>
    /// <returns>the function object or null</returns>
    public static object? GetFunction(Mode mode, string name)
    {
        Mode.ModeEnum modeEnum = mode.Enum;
        if (modeEnum != Mode.ModeEnum.Regular)
        {
            return GetCompatibilityModeFunction(name, modeEnum);
        }

        return null;
    }

    private static object? GetCompatibilityModeFunction(string name, Mode.ModeEnum modeEnum)
        => throw new TODOException(
            "ModeFunction.getCompatibilityModeFunction: the " + modeEnum
            + " compatibility mode functions are not ported");
}
