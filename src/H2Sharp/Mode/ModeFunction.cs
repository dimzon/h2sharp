/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.mode.ModeFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The namespace is Mode + s: a namespace named Mode would shadow the Mode type for every file
 * under H2Sharp, and a namespace member beats an imported type.
 */
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Functions;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Modes;

/// <summary>
/// Base class for mode-specific functions.
/// </summary>
public abstract class ModeFunction : FunctionN
{
    /// <summary>Constant for a variable number of arguments.</summary>
    public const int VarArgs = -1;

    /// <summary>The information about this function.</summary>
    protected readonly FunctionInfo Info;

    /// <summary>
    /// Creates a mode function.
    /// </summary>
    /// <param name="info">the function information</param>
    protected ModeFunction(FunctionInfo info)
        : base(new Expression[info.ParameterCount != VarArgs ? info.ParameterCount : 4])
    {
        Info = info;
    }

    /// <summary>
    /// Gets the function for the given mode, or null when that mode has no such function.
    /// </summary>
    /// <param name="mode">the mode</param>
    /// <param name="name">the uppercase function name</param>
    /// <returns>the function, or null</returns>
    public static ModeFunction? GetFunction(Mode mode, string name)
        => GetCompatibilityModeFunction(name, mode.Enum);

    private static ModeFunction? GetCompatibilityModeFunction(string name, Mode.ModeEnum modeEnum)
    {
        if (modeEnum == Mode.ModeEnum.Regular)
        {
            return null;
        }

        return modeEnum switch
        {
            Mode.ModeEnum.MsSqlServer => FunctionsMSSQLServer.GetFunction(name),
            Mode.ModeEnum.Legacy => FunctionsLegacy.GetFunction(name),
            Mode.ModeEnum.Db2 or Mode.ModeEnum.Derby => FunctionsDB2Derby.GetFunction(name),
            Mode.ModeEnum.Oracle => FunctionsOracle.GetFunction(name),
            Mode.ModeEnum.MySql or Mode.ModeEnum.MariaDb => FunctionsMySQL.GetFunction(name),
            Mode.ModeEnum.PostgreSql => FunctionsPostgreSQL.GetFunction(name),
            _ => throw new TODOException(
                "ModeFunction.getCompatibilityModeFunction: the " + modeEnum
                + " compatibility mode functions are not ported"),
        };
    }

    /// <inheritdoc/>
    public override string GetName() => Info.Name;

    /// <summary>Whether the function is deterministic.</summary>
    public bool IsDeterministic => Info.Deterministic;

    /// <inheritdoc/>
    public override void DoneWithParameters()
    {
        int count = Info.ParameterCount;
        if (count == VarArgs)
        {
            CheckParameterCount(argsCount);
            base.DoneWithParameters();
        }
        else if (count != argsCount)
        {
            throw DbException.Get(
                Api.ErrorCode.InvalidParameterCount2, Info.Name, argsCount.ToString());
        }
    }

    /// <summary>
    /// Returns the compatibility date-time value function with the given name for this mode,
    /// or null. The reference answers through this dispatch even in Regular mode: TODAY,
    /// SYSDATE and SYSTIMESTAMP are only read in the modes that list them.
    /// </summary>
    /// <param name="mode">the mode</param>
    /// <param name="name">the uppercase function name</param>
    /// <param name="scale">the scale, or -1</param>
    /// <returns>the function, or null</returns>
    public static Expression? GetCompatibilityDateTimeValueFunction(Mode mode, string name, int scale)
    {
        Mode.ModeEnum modeEnum = mode.Enum;
        switch (name)
        {
            case "SYSDATE":
                if (modeEnum is Mode.ModeEnum.Legacy or Mode.ModeEnum.HsqlDb or Mode.ModeEnum.Oracle)
                {
                    return new CompatibilityDateTimeValueFunction(
                        CompatibilityDateTimeValueFunction.Sysdate, -1);
                }

                break;
            case "SYSTIMESTAMP":
                if (modeEnum is Mode.ModeEnum.Legacy or Mode.ModeEnum.Oracle)
                {
                    return new CompatibilityDateTimeValueFunction(
                        CompatibilityDateTimeValueFunction.Systimestamp, scale);
                }

                break;
            case "TODAY":
                if (modeEnum is Mode.ModeEnum.Legacy or Mode.ModeEnum.HsqlDb)
                {
                    return new CurrentDateTimeValueFunction(
                        CurrentDateTimeValueFunction.CurrentDate, scale);
                }

                break;
        }

        return null;
    }

    /// <summary>
    /// Checks a variable argument count. The reference's default refuses every such function.
    /// </summary>
    /// <param name="len">the number of arguments</param>
    protected virtual void CheckParameterCount(int len)
        => throw DbException.GetInternalError("type=" + Info.Type);

    /// <summary>
    /// Reads the values of the arguments, or null when one is NULL and the function requires it.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the values, or null</returns>
    protected Value?[]? GetArgumentsValues(SessionLocal session)
    {
        Value?[] values = new Value?[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            Value v = args[i].GetValue(session);
            if (v == ValueNull.Instance)
            {
                if (Info.NullIfParameterIsNull)
                {
                    return null;
                }

                values[i] = null;
            }
            else
            {
                values[i] = v;
            }
        }

        return values;
    }

    /// <summary>
    /// Gets one argument value, reading it on demand.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="values">the already-read values</param>
    /// <param name="i">the argument index</param>
    /// <returns>the value, or null for an argument that is not there</returns>
    protected Value? GetNullOrValue(SessionLocal session, Value?[] values, int i)
    {
        if (i >= values.Length)
        {
            return null;
        }

        Value? v = values[i];
        if (v is null)
        {
            Expression e = args[i];
            if (e is null)
            {
                return null;
            }

            v = values[i] = e.GetValue(session);
        }

        return v;
    }

    /// <summary>
    /// Optimizes the arguments with this function's determinism.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>whether all arguments are constants</returns>
    protected bool OptimizeArguments(SessionLocal session)
        => OptimizeArguments(session, Info.Deterministic);
}
