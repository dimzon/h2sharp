/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.message.DbException.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Modified: the static factory class keeps H2's call sites one-to-one
 * (DbException.get(X) becomes DbException.Get(X)), but there is no wrapper
 * exception type: H2 wrapped a java.sql.SQLException only because JDBC has no
 * mutable "SQL statement" property, whereas H2SharpException has one, so
 * AddSQL mutates in place and no wrapper is required.
 *
 * The SQLException-subclass selection is in Message/ErrorCodeKind.cs, generated
 * by tools/port_dbexception_kind.py.
 *
 * DriverManager.getLogWriter() (used by traceThrowable) has no .NET equivalent;
 * TraceThrowable writes to Debug/Console when H2SHARP_TRACE_EXCEPTIONS is set.
 */
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Util;

namespace H2Sharp.Message;

/// <summary>
/// Factory methods for <see cref="H2SharpException"/>. Mirrors
/// <c>org.h2.message.DbException</c>'s static surface so that call sites translate
/// directly.
/// </summary>
public static class DbException
{
    /// <summary>
    /// If the SQL statement contains this text, then it is never added to the
    /// SQL exception. Hiding the SQL statement may be important if it contains a
    /// password, such as a CREATE LINKED TABLE statement.
    /// </summary>
    public const string HideSql = "--hide--";

    /// <summary>
    /// Create a database exception for a specific error code.
    /// </summary>
    /// <param name="errorCode">the error code</param>
    /// <returns>the exception</returns>
    public static H2SharpException Get(int errorCode)
        => Get(errorCode, (string?)null);

    /// <summary>
    /// Create a database exception for a specific error code.
    /// </summary>
    /// <param name="errorCode">the error code</param>
    /// <param name="p1">the first parameter of the message</param>
    /// <returns>the exception</returns>
    public static H2SharpException Get(int errorCode, string? p1)
        => Get(errorCode, [p1]);

    /// <summary>
    /// Create a database exception for a specific error code.
    /// </summary>
    /// <param name="errorCode">the error code</param>
    /// <param name="cause">the cause of the exception</param>
    /// <param name="parameters">the list of parameters of the message</param>
    /// <returns>the exception</returns>
    public static H2SharpException Get(int errorCode, Exception? cause, params string?[] parameters)
        => Create(errorCode, cause, parameters);

    /// <summary>
    /// Create a database exception for a specific error code.
    /// </summary>
    /// <param name="errorCode">the error code</param>
    /// <param name="parameters">the list of parameters of the message</param>
    /// <returns>the exception</returns>
    public static H2SharpException Get(int errorCode, params string?[] parameters)
        => Create(errorCode, null, parameters);

    /// <summary>
    /// Create a database exception for an arbitrary SQLState.
    /// </summary>
    /// <param name="sqlState">the state to use</param>
    /// <param name="message">the message to use</param>
    /// <returns>the exception</returns>
    public static H2SharpException FromUser(string sqlState, string message)
    {
        // do not translate as sqlstate is arbitrary : avoid "message not found"
        return CreateRaw(message, sql: null, sqlState, 0, cause: null, stackTrace: null);
    }

    /// <summary>
    /// Create a syntax error exception.
    /// </summary>
    /// <param name="sql">the SQL statement</param>
    /// <param name="index">the position of the error in the SQL statement</param>
    /// <returns>the exception</returns>
    public static H2SharpException GetSyntaxError(string? sql, int index)
    {
        sql = StringUtils.AddAsterisk(sql, index);
        return Get(ErrorCode.SyntaxError1, sql);
    }

    /// <summary>
    /// Create a syntax error exception.
    /// </summary>
    /// <param name="sql">the SQL statement</param>
    /// <param name="index">the position of the error in the SQL statement</param>
    /// <param name="message">the message</param>
    /// <returns>the exception</returns>
    public static H2SharpException GetSyntaxError(string? sql, int index, string message)
    {
        // Java calls getJdbcSQLException(SYNTAX_ERROR_2, null, sql, message), i.e.
        // cause=null and params={sql, message}. The 42001 template is
        // "Syntax error in SQL statement {0}; expected {1}", so the substituted
        // parameters carry the order: statement first, expectation second.
        sql = StringUtils.AddAsterisk(sql, index);
        return Get(ErrorCode.SyntaxError2, sql, message);
    }

    /// <summary>
    /// Create a syntax error exception for a specific error code.
    /// </summary>
    /// <param name="errorCode">the error code</param>
    /// <param name="sql">the SQL statement</param>
    /// <param name="index">the position of the error in the SQL statement</param>
    /// <param name="parameters">the list of parameters of the message</param>
    /// <returns>the exception</returns>
    public static H2SharpException GetSyntaxError(
        int errorCode, string? sql, int index, params string?[] parameters)
    {
        sql = StringUtils.AddAsterisk(sql, index);
        string sqlState = ErrorCode.GetState(errorCode);
        string message = ErrorMessages.Translate(sqlState, parameters);
        return CreateRaw(message, sql, sqlState, errorCode, cause: null, stackTrace: null);
    }

    /// <summary>
    /// Gets an exception meaning this feature is not supported.
    /// </summary>
    /// <param name="message">what exactly is not supported</param>
    /// <returns>the exception</returns>
    public static H2SharpException GetUnsupportedException(string message)
        => Get(ErrorCode.FeatureNotSupported1, message);

    /// <summary>
    /// Gets an exception meaning this value is invalid.
    /// </summary>
    /// <param name="param">the name of the parameter</param>
    /// <param name="value">the value passed</param>
    /// <returns>the exception</returns>
    public static H2SharpException GetInvalidValueException(string param, object? value)
        => Get(ErrorCode.InvalidValue2, value?.ToString() ?? "null", param);

    /// <summary>
    /// Gets an exception meaning this value is invalid.
    /// </summary>
    /// <param name="cause">the cause of the exception</param>
    /// <param name="param">the name of the parameter</param>
    /// <param name="value">the value passed</param>
    /// <returns>the exception</returns>
    public static H2SharpException GetInvalidValueException(
        Exception cause, string param, object? value)
        => Get(ErrorCode.InvalidValue2, cause, value?.ToString() ?? "null", param);

    /// <summary>
    /// Gets an exception meaning this value is too long.
    /// </summary>
    /// <param name="columnOrType">column with data type or data type name</param>
    /// <param name="value">string representation of value, will be truncated to 80
    /// characters</param>
    /// <param name="valueLength">the actual length of value, <c>-1</c> if unknown</param>
    /// <returns>the exception</returns>
    public static H2SharpException GetValueTooLongException(
        string columnOrType, string value, long valueLength)
    {
        int length = value.Length;
        int m = valueLength >= 0 ? 22 : 0;
        StringBuilder builder = length > 80
            ? new StringBuilder(83 + m).Append(value, 0, 80).Append("...")
            : new StringBuilder(length + m).Append(value);
        if (valueLength >= 0)
        {
            builder.Append(" (").Append(valueLength.ToString(CultureInfo.InvariantCulture)).Append(')');
        }

        return Get(ErrorCode.ValueTooLong2, columnOrType, builder.ToString());
    }

    /// <summary>
    /// Gets a file version exception.
    /// </summary>
    /// <param name="dataFileName">the name of the database</param>
    /// <returns>the exception</returns>
    public static H2SharpException GetFileVersionError(string dataFileName)
        => Get(ErrorCode.FileVersionError1,
            "Old database: " + dataFileName
            + " - please convert the database to a SQL script and re-create it.");

    /// <summary>
    /// Gets an internal error.
    /// </summary>
    /// <param name="s">the message</param>
    /// <returns>the exception</returns>
    public static Exception GetInternalError(string s)
    {
        Exception e = new(s);
        TraceThrowable(e);
        return e;
    }

    /// <summary>
    /// Gets an internal error.
    /// </summary>
    /// <returns>the exception</returns>
    public static Exception GetInternalError() => GetInternalError("Unexpected code path");

    /// <summary>
    /// Convert a throwable to an <see cref="H2SharpException"/> using the default
    /// mapping.
    /// </summary>
    /// <param name="e">the root cause</param>
    /// <returns>the exception object</returns>
    public static H2SharpException Convert(Exception e)
    {
        if (e is H2SharpException alreadyH2)
        {
            return alreadyH2;
        }

        if (e is OutOfMemoryException oom)
        {
            return Get(ErrorCode.OutOfMemory, oom);
        }

        if (e is IOException)
        {
            return Get(ErrorCode.IoException1, e, e.ToString());
        }

        if (e is H2SharpFatalException)
        {
            // Java re-throws java.lang.Error as-is; H2Sharp uses
            // H2SharpFatalException for the cases that must not be swallowed.
            throw e;
        }

        return Get(ErrorCode.GeneralError1, e, e.ToString());
    }

    /// <summary>
    /// Convert an exception raised inside a user-defined function or alias.
    /// </summary>
    /// <param name="t">the target exception</param>
    /// <param name="message">the added message or null</param>
    /// <returns>the exception object</returns>
    public static H2SharpException ConvertInvocation(Exception t, string? message)
    {
        if (t is H2SharpException)
        {
            return Convert(t);
        }

        string m = message is null ? t.Message : message + ": " + t.Message;
        return Get(ErrorCode.ExceptionInFunction1, t, m);
    }

    /// <summary>
    /// Convert an IO exception to a database exception.
    /// </summary>
    /// <param name="e">the root cause</param>
    /// <param name="message">the message or null</param>
    /// <returns>the exception object</returns>
    public static H2SharpException ConvertIOException(IOException e, string? message)
    {
        if (message is null)
        {
            if (e.InnerException is H2SharpException inner)
            {
                return inner;
            }

            return Get(ErrorCode.IoException1, e, e.ToString());
        }

        return Get(ErrorCode.IoException2, e, e.ToString(), message);
    }

    /// <summary>
    /// Gets the exception object for a specific error code.
    /// </summary>
    /// <param name="errorCode">the error code</param>
    /// <returns>the exception object</returns>
    public static H2SharpException GetJdbcSqlException(int errorCode)
        => GetJdbcSqlException(errorCode, (Exception?)null);

    /// <summary>
    /// Gets the exception object for a specific error code.
    /// </summary>
    /// <param name="errorCode">the error code</param>
    /// <param name="p1">the first parameter of the message</param>
    /// <returns>the exception object</returns>
    public static H2SharpException GetJdbcSqlException(int errorCode, string? p1)
        => GetJdbcSqlException(errorCode, null, p1);

    /// <summary>
    /// Gets the exception object for a specific error code.
    /// </summary>
    /// <param name="errorCode">the error code</param>
    /// <param name="cause">the cause of the exception</param>
    /// <param name="parameters">the list of parameters of the message</param>
    /// <returns>the exception object</returns>
    public static H2SharpException GetJdbcSqlException(
        int errorCode, Exception? cause, params string?[] parameters)
        => Create(errorCode, cause, parameters);

    /// <summary>
    /// Creates an exception with all fields supplied.
    /// </summary>
    /// <param name="message">the reason</param>
    /// <param name="sql">the SQL statement</param>
    /// <param name="state">the SQL state</param>
    /// <param name="errorCode">the error code</param>
    /// <param name="cause">the exception that was the reason for this exception</param>
    /// <param name="stackTrace">the recorded stack trace</param>
    /// <returns>the exception object</returns>
    public static H2SharpException GetJdbcSqlException(
        string message, string? sql, string state, int errorCode,
        Exception? cause, string? stackTrace)
        => CreateRaw(message, sql, state, errorCode, cause, stackTrace);

    /// <summary>
    /// Attaches the SQL statement to an exception, if it does not have one yet.
    /// </summary>
    /// <param name="e">the exception</param>
    /// <param name="sql">the SQL statement</param>
    /// <returns>the same exception instance</returns>
    public static H2SharpException AddSql(H2SharpException e, string? sql)
    {
        if (e.Sql is null)
        {
            e.Sql = FilterSql(sql);
        }

        return e;
    }

    /// <summary>
    /// Builds the message for an exception in H2's documented layout.
    /// </summary>
    /// <param name="e">the exception</param>
    /// <returns>the message</returns>
    public static string BuildMessageForException(H2SharpException e)
    {
        string? s = e.OriginalMessage;
        StringBuilder buff = new(s is not null ? s : "- ");
        s = e.Sql;
        if (s is not null)
        {
            buff.Append("; SQL statement:\n").Append(s);
        }

        buff.Append(" [")
            .Append(e.H2ErrorCode.ToString(CultureInfo.InvariantCulture))
            .Append('-')
            .Append(Constants.BuildId.ToString(CultureInfo.InvariantCulture))
            .Append(']');
        return buff.ToString();
    }

    /// <summary>
    /// Returns the inner exceptions, up to 100 of them, as text. .NET has no
    /// SQLException.getNextException chain, so H2SharpException aggregates
    /// exceptions in <see cref="Exception.InnerException"/> instead; this walks
    /// that chain.
    /// </summary>
    /// <param name="e">the exception</param>
    /// <returns>one line per nested exception</returns>
    public static string PrintNextExceptions(Exception e)
    {
        StringBuilder buff = new();
        int i = 0;
        Exception? current = e.InnerException;
        while (current is not null)
        {
            if (i++ == 100)
            {
                buff.AppendLine("(truncated)");
                return buff.ToString();
            }

            buff.AppendLine(current.ToString());
            current = current.InnerException;
        }

        return buff.ToString();
    }

    /// <summary>
    /// Writes the exception to the diagnostic output when exception tracing is
    /// enabled via the <c>h2sharp.traceExceptions</c> setting.
    /// </summary>
    /// <param name="e">the exception</param>
    public static void TraceThrowable(Exception e)
    {
        if (!Utils.GetProperty("h2sharp.traceExceptions", false))
        {
            return;
        }

        Debug.WriteLine(e.ToString());
        Console.Error.WriteLine(e);
    }

    private static H2SharpException Create(int errorCode, Exception? cause, string?[] parameters)
    {
        string sqlState = ErrorCode.GetState(errorCode);
        string message = ErrorMessages.Translate(sqlState, parameters);
        return CreateRaw(message, null, sqlState, errorCode, cause, stackTrace: null);
    }

    private static H2SharpException CreateRaw(
        string message, string? sql, string state, int errorCode,
        Exception? cause, string? stackTrace)
        => new(
            message,
            FilterSqlState(state),
            errorCode,
            ErrorCodeKind.GetKind(errorCode),
            FilterSql(sql),
            cause,
            stackTrace);

    /// <summary>
    /// A null SQLSTATE is not allowed by ADO.NET; fall back to the generic one.
    /// </summary>
    private static string FilterSqlState(string state)
        => string.IsNullOrEmpty(state) ? "HY000" : state;

    private static string? FilterSql(string? sql)
        => sql is null || !sql.Contains(HideSql, StringComparison.Ordinal) ? sql : "-";
}

/// <summary>
/// Thrown for conditions that must propagate instead of being converted into an
/// <see cref="H2SharpException"/>. This is the .NET stand-in for the
/// <c>java.lang.Error</c> cases that <c>DbException.convert</c> re-throws.
/// </summary>
public sealed class H2SharpFatalException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="message">the message</param>
    public H2SharpFatalException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="message">the message</param>
    /// <param name="cause">the cause</param>
    public H2SharpFatalException(string message, Exception? cause) : base(message, cause)
    {
    }
}
