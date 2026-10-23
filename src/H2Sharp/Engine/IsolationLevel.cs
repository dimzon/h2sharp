// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.engine.IsolationLevel.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: a Java enum with per-constant fields becomes a C# enum for the constants
// (the ordinal order is the reference's declaration order) with the field-backed methods
// on a static class beside it. The store layer's stand-in for this type
// (MvStore.Tx.IsolationLevel) is gone: the reference's mvstore.tx imports this type, and
// so does the port now.
using H2Sharp.Message;

namespace H2Sharp.Engine;

/// <summary>
/// The transaction isolation level.
/// </summary>
public enum IsolationLevel
{
    /// <summary>
    /// Reads are not committed.
    /// </summary>
    ReadUncommitted,

    /// <summary>
    /// Reads are committed.
    /// </summary>
    ReadCommitted,

    /// <summary>
    /// Reads are repeatable.
    /// </summary>
    RepeatableRead,

    /// <summary>
    /// Snapshot isolation.
    /// </summary>
    Snapshot,

    /// <summary>
    /// Serializable isolation.
    /// </summary>
    Serializable,
}

/// <summary>
/// The field-backed members of the reference's isolation level enum.
/// </summary>
public static class IsolationLevels
{
    /// <summary>
    /// Returns the SQL representation of this isolation level.
    /// </summary>
    /// <param name="level">the isolation level</param>
    /// <returns>SQL representation of this isolation level</returns>
    public static string GetSql(this IsolationLevel level)
    {
        return level switch
        {
            IsolationLevel.ReadUncommitted => "READ UNCOMMITTED",
            IsolationLevel.ReadCommitted => "READ COMMITTED",
            IsolationLevel.RepeatableRead => "REPEATABLE READ",
            IsolationLevel.Snapshot => "SNAPSHOT",
            _ => "SERIALIZABLE",
        };
    }

    /// <summary>
    /// Returns the JDBC constant for this isolation level.
    /// </summary>
    /// <param name="level">the isolation level</param>
    /// <returns>the JDBC constant for this isolation level</returns>
    /// <remarks>
    /// The reference reads the java.sql.Connection constants (1, 2, 4, 8) and
    /// Constants.TRANSACTION_SNAPSHOT (6); the JDK constants have no ported home, so they
    /// stay literals with their names here.
    /// </remarks>
    public static int GetJdbc(this IsolationLevel level)
    {
        return level switch
        {
            // Connection.TRANSACTION_READ_UNCOMMITTED
            IsolationLevel.ReadUncommitted => 1,
            // Connection.TRANSACTION_READ_COMMITTED
            IsolationLevel.ReadCommitted => 2,
            // Connection.TRANSACTION_REPEATABLE_READ
            IsolationLevel.RepeatableRead => 4,
            IsolationLevel.Snapshot => Constants.TransactionSnapshot,
            // Connection.TRANSACTION_SERIALIZABLE
            _ => 8,
        };
    }

    /// <summary>
    /// Returns the LOCK_MODE equivalent for PageStore and old versions of H2.
    /// </summary>
    /// <param name="level">the isolation level</param>
    /// <returns>the LOCK_MODE equivalent</returns>
    public static int GetLockMode(this IsolationLevel level)
    {
        return level switch
        {
            IsolationLevel.ReadUncommitted => Constants.LockModeOff,
            IsolationLevel.ReadCommitted => Constants.LockModeReadCommitted,
            _ => Constants.LockModeTable,
        };
    }

    /// <summary>
    /// Returns whether a non-repeatable read phenomena is allowed.
    /// </summary>
    /// <param name="level">the isolation level</param>
    /// <returns>whether a non-repeatable read phenomena is allowed</returns>
    public static bool AllowNonRepeatableRead(this IsolationLevel level)
    {
        return (int)level < (int)IsolationLevel.RepeatableRead;
    }

    /// <summary>
    /// Returns the isolation level for the given JDBC constant.
    /// </summary>
    /// <param name="level">the JDBC constant</param>
    /// <returns>the isolation level</returns>
    /// <remarks>
    /// See <see cref="GetJdbc"/> for the java.sql.Connection constants.
    /// </remarks>
    public static IsolationLevel FromJdbc(int level)
    {
        return level switch
        {
            // Connection.TRANSACTION_READ_UNCOMMITTED
            1 => IsolationLevel.ReadUncommitted,
            // Connection.TRANSACTION_READ_COMMITTED
            2 => IsolationLevel.ReadCommitted,
            // Connection.TRANSACTION_REPEATABLE_READ
            4 => IsolationLevel.RepeatableRead,
            Constants.TransactionSnapshot => IsolationLevel.Snapshot,
            // Connection.TRANSACTION_SERIALIZABLE
            8 => IsolationLevel.Serializable,
            _ => throw DbException.GetInvalidValueException("isolation level", level),
        };
    }

    /// <summary>
    /// Returns the isolation level for the given LOCK_MODE.
    /// </summary>
    /// <param name="lockMode">the LOCK_MODE</param>
    /// <returns>the isolation level</returns>
    public static IsolationLevel FromLockMode(int lockMode)
    {
        return lockMode switch
        {
            Constants.LockModeOff => IsolationLevel.ReadUncommitted,
            Constants.LockModeTable => IsolationLevel.Serializable,
            Constants.LockModeTableGc => IsolationLevel.Serializable,
            _ => IsolationLevel.ReadCommitted,
        };
    }

    /// <summary>
    /// Returns the isolation level for the given SQL representation.
    /// </summary>
    /// <param name="sql">the SQL representation</param>
    /// <returns>the isolation level</returns>
    public static IsolationLevel FromSql(string sql)
    {
        return sql switch
        {
            "READ UNCOMMITTED" => IsolationLevel.ReadUncommitted,
            "READ COMMITTED" => IsolationLevel.ReadCommitted,
            "REPEATABLE READ" => IsolationLevel.RepeatableRead,
            "SNAPSHOT" => IsolationLevel.Snapshot,
            "SERIALIZABLE" => IsolationLevel.Serializable,
            _ => throw DbException.GetInvalidValueException("isolation level", sql),
        };
    }
}
