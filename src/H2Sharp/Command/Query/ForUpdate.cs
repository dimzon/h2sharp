/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.query.ForUpdate.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Command.Query;

/// <summary>FOR UPDATE clause.</summary>
public sealed class ForUpdate
{
    /// <summary>Type of FOR UPDATE clause.</summary>
    public enum Type
    {
        /// <summary>Use default lock timeout.</summary>
        Default,

        /// <summary>Use specified lock timeout.</summary>
        Wait,

        /// <summary>Use zero timeout.</summary>
        NoWait,

        /// <summary>Skip locked rows.</summary>
        SkipLocked,
    }

    /// <summary>FOR UPDATE clause without additional parameters.</summary>
    public static readonly ForUpdate Default = new(Type.Default, -1);

    /// <summary>FOR UPDATE NOWAIT clause.</summary>
    public static readonly ForUpdate NoWait = new(Type.NoWait, 0);

    /// <summary>FOR UPDATE SKIP LOCKED clause.</summary>
    public static readonly ForUpdate SkipLocked = new(Type.SkipLocked, -2);

    /// <summary>Returns a FOR UPDATE WAIT clause.</summary>
    /// <param name="timeoutMillis">the timeout in milliseconds</param>
    /// <returns>the clause</returns>
    public static ForUpdate Wait(int timeoutMillis)
    {
        if (timeoutMillis < 0)
        {
            throw DbException.GetInvalidValueException("timeout", timeoutMillis);
        }

        return timeoutMillis == 0 ? NoWait : new ForUpdate(Type.Wait, timeoutMillis);
    }

    private readonly Type type;

    private readonly int timeoutMillis;

    private ForUpdate(Type type, int timeoutMillis)
    {
        this.type = type;
        this.timeoutMillis = timeoutMillis;
    }

    /// <summary>Returns the type of the clause.</summary>
    public Type GetForUpdateType() => type;

    /// <summary>Returns the timeout in milliseconds.</summary>
    public int GetTimeoutMillis() => timeoutMillis;

    /// <inheritdoc/>
    public override string ToString() => GetSql(new StringBuilder()).ToString();

    /// <summary>Appends the SQL of this clause.</summary>
    /// <param name="builder">the builder</param>
    /// <returns>the builder</returns>
    public StringBuilder GetSql(StringBuilder builder)
    {
        _ = builder.Append(" FOR UPDATE");
        switch (type)
        {
            case Type.Wait:
                _ = builder.Append(" WAIT ").Append(timeoutMillis / 1_000);
                int millis = timeoutMillis % 1_000;
                if (millis > 0)
                {
                    _ = StringUtils.AppendZeroPadded(builder.Append('.'), 3, millis);
                }

                break;
            case Type.NoWait:
                _ = builder.Append(" NOWAIT");
                break;
            case Type.SkipLocked:
                _ = builder.Append(" SKIP LOCKED");
                break;
            case Type.Default:
                break;
            default:
                throw DbException.GetInternalError("type=" + type);
        }

        return builder;
    }
}
