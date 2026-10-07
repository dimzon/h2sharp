/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.jdbc.JdbcException and the SQLException subclasses that
 * org.h2.message.DbException returns.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: Java's ten JdbcSQL*Exception subclasses become a single CLR type with an
 * <see cref="SqlExceptionKind"/> discriminator, because System.Data.Common.DbException
 * has no such hierarchy and inventing one would not interoperate with .NET
 * consumers. The mapping in DbException.GetKind preserves exactly which bucket H2
 * put each error code in, so nothing is lost; callers switch on SqlState or
 * <see cref="H2ErrorCode"/> as usual.
 *
 * SqlState follows DbException.SqlState. The H2 error code is exposed as
 * H2ErrorCode rather than ErrorCode, because Exception already has an ErrorCode
 * (HResult) property that means something else.
 */
using System;
using H2Sharp.Engine;

// See docs/PORTING.md section 7: H2Sharp.Message.DbException collides with
// System.Data.Common.DbException, which this type derives from.
using ErrorFactory = H2Sharp.Message.DbException;

namespace H2Sharp;

/// <summary>
/// The bucket an <see cref="H2SharpException"/> falls into. These correspond one to
/// one with the java.sql.SQLException subclasses H2 instantiates.
/// </summary>
public enum SqlExceptionKind
{
    /// <summary>
    /// No specific bucket: org.h2.jdbc.JdbcSQLException.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// JdbcSQLNonTransientException.
    /// </summary>
    NonTransient,

    /// <summary>
    /// JdbcSQLSyntaxErrorException.
    /// </summary>
    SyntaxError,

    /// <summary>
    /// JdbcSQLNonTransientConnectionException.
    /// </summary>
    NonTransientConnection,

    /// <summary>
    /// JdbcSQLDataException.
    /// </summary>
    Data,

    /// <summary>
    /// JdbcSQLIntegrityConstraintViolationException.
    /// </summary>
    IntegrityConstraintViolation,

    /// <summary>
    /// JdbcSQLInvalidAuthorizationSpecException.
    /// </summary>
    InvalidAuthorizationSpec,

    /// <summary>
    /// JdbcSQLTransactionRollbackException.
    /// </summary>
    TransactionRollback,

    /// <summary>
    /// JdbcSQLFeatureNotSupportedException.
    /// </summary>
    FeatureNotSupported,

    /// <summary>
    /// JdbcSQLTimeoutException.
    /// </summary>
    Timeout,

    /// <summary>
    /// JdbcSQLTransientException.
    /// </summary>
    Transient,
}

/// <summary>
/// The exception thrown by H2Sharp. Carries the H2 error code, the SQLSTATE and,
/// when known, the offending SQL statement.
/// </summary>
public sealed class H2SharpException : System.Data.Common.DbException
{
    /// <summary>
    /// Creates an exception.
    /// </summary>
    /// <param name="message">the message, without the H2 error code suffix</param>
    /// <param name="sqlState">the SQLSTATE</param>
    /// <param name="h2ErrorCode">the H2 error code</param>
    /// <param name="kind">the exception bucket</param>
    /// <param name="sql">the offending SQL statement, or null</param>
    /// <param name="cause">the underlying cause, or null</param>
    /// <param name="stackTrace">a recorded stack trace, or null</param>
    internal H2SharpException(
        string message,
        string sqlState,
        int h2ErrorCode,
        SqlExceptionKind kind,
        string? sql,
        Exception? cause,
        string? stackTrace)
        : base(message, cause)
    {
        SqlState = sqlState;
        H2ErrorCode = h2ErrorCode;
        Kind = kind;
        Sql = sql;
        OriginalMessage = message;
        RecordedStackTrace = stackTrace;
    }

    /// <summary>
    /// The SQLSTATE, from ErrorCode.GetState.
    /// </summary>
    public override string SqlState { get; }

    /// <summary>
    /// The H2 error code from H2Sharp.Api.ErrorCode. Named to avoid clashing with
    /// Exception.ErrorCode, which is an HResult.
    /// </summary>
    public int H2ErrorCode { get; }

    /// <summary>
    /// The bucket this error falls into.
    /// </summary>
    public SqlExceptionKind Kind { get; }

    /// <summary>
    /// The offending SQL statement, when known.
    /// </summary>
    public string? Sql { get; internal set; }

    /// <summary>
    /// The message without the " [errorcode-buildid]" suffix that
    /// <c>DbException.buildMessageForException</c> appends.
    /// </summary>
    public string OriginalMessage { get; }

    /// <summary>
    /// A stack trace captured at throw time, when the engine was configured to
    /// record one.
    /// </summary>
    public string? RecordedStackTrace { get; }

    /// <summary>
    /// True if this error code is one of H2's "common" errors.
    /// </summary>
    public bool IsCommon => Api.ErrorCode.IsCommon(H2ErrorCode);

    /// <summary>
    /// Builds the message in H2's documented layout:
    /// <c>{message}; SQL statement:\n{sql} [{errorCode}-{buildId}]</c>.
    /// </summary>
    /// <returns>the formatted message</returns>
    public string BuildMessageForException()
        => ErrorFactory.BuildMessageForException(this);

    /// <inheritdoc/>
    public override string ToString()
        => BuildMessageForException();
}