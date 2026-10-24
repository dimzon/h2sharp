/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.SysInfoFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What is left out, and why:
 *
 * * The admin check on MEMORY_FREE and MEMORY_USED. The port has no users, so there is
 *   nobody to ask; whoever can run a statement may read the counters.
 */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// Database or session information function.
/// </summary>
public sealed class SysInfoFunction : Expression
{
    /// <summary>AUTOCOMMIT().</summary>
    public const int Autocommit = 0;

    /// <summary>DATABASE_PATH().</summary>
    public const int DatabasePath = Autocommit + 1;

    /// <summary>H2VERSION().</summary>
    public const int H2version = DatabasePath + 1;

    /// <summary>LOCK_MODE().</summary>
    public const int LockMode = H2version + 1;

    /// <summary>LOCK_TIMEOUT().</summary>
    public const int LockTimeout = LockMode + 1;

    /// <summary>MEMORY_FREE().</summary>
    public const int MemoryFree = LockTimeout + 1;

    /// <summary>MEMORY_USED().</summary>
    public const int MemoryUsed = MemoryFree + 1;

    /// <summary>READONLY().</summary>
    public const int Readonly = MemoryUsed + 1;

    /// <summary>SESSION_ID().</summary>
    public const int SessionId = Readonly + 1;

    /// <summary>TRANSACTION_ID().</summary>
    public const int TransactionId = SessionId + 1;

    private static readonly int[] Types =
    [
        Value.Boolean, Value.Varchar, Value.Varchar, Value.Integer, Value.Integer,
        Value.Bigint, Value.Bigint, Value.Boolean, Value.Integer, Value.Varchar,
    ];

    private static readonly string[] Names =
    [
        "AUTOCOMMIT", "DATABASE_PATH", "H2VERSION", "LOCK_MODE", "LOCK_TIMEOUT",
        "MEMORY_FREE", "MEMORY_USED", "READONLY", "SESSION_ID", "TRANSACTION_ID",
    ];

    /// <summary>
    /// Gets the name for a function id.
    /// </summary>
    /// <param name="function">the function id</param>
    /// <returns>the name</returns>
    public static string GetName(int function) => Names[function];

    private readonly int function;

    private readonly TypeInfo type;

    /// <summary>
    /// Creates an information function.
    /// </summary>
    /// <param name="function">one of the constants above</param>
    public SysInfoFunction(int function)
    {
        this.function = function;
        type = TypeInfo.GetTypeInfo(Types[function]);
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value result;
        switch (function)
        {
            case Autocommit:
                result = ValueBoolean.Get(session.GetAutoCommit());
                break;
            case DatabasePath:
                // The port opens in-memory databases only, and those have no path.
                result = ValueNull.Instance;
                break;
            case H2version:
                result = ValueVarchar.Get(H2Sharp.Engine.Constants.Version);
                break;
            case LockMode:
                result = ValueInteger.Get(session.GetDatabase().GetLockMode());
                break;
            case LockTimeout:
                result = ValueInteger.Get(session.GetLockTimeout());
                break;
            case MemoryFree:
            {
                GCMemoryInfo info = GC.GetGCMemoryInfo();
                result = ValueBigint.Get(info.TotalAvailableMemoryBytes - info.HeapSizeBytes);
                break;
            }

            case MemoryUsed:
                result = ValueBigint.Get(GC.GetGCMemoryInfo().HeapSizeBytes);
                break;
            case Readonly:
                result = ValueBoolean.Get(session.GetDatabase().IsReadOnly());
                break;
            case SessionId:
                result = ValueInteger.Get(session.GetId());
                break;
            case TransactionId:
                // The port tracks no transactions, so there is never an uncommitted one.
                result = ValueNull.Instance;
                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        return result;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
        => builder.Append(GetName()).Append("()");

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        if (visitor.GetVisitorType() == ExpressionVisitor.Deterministic)
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => type;

    /// <inheritdoc/>
    public override int GetCost() => 1;

    /// <summary>
    /// Gets the name of this function.
    /// </summary>
    /// <returns>the name</returns>
    public string GetName() => Names[function];
}
