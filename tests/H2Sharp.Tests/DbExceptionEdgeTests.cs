/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 116: the exception factories — the file-version and I/O conversions, the
 * invocation and IO wrappers, the JDBC forms with and without a cause, the
 * next-exception printer with its truncation, the trace, and the fatal exception.
 */
using System;
using System.IO;
using H2Sharp.Api;
using H2Sharp.Message;
using Xunit;

namespace H2Sharp.Tests;

public sealed class DbExceptionEdgeTests
{
    [Fact]
    public void FactoriesAnswerTheirShapes()
    {
        H2SharpException version = DbException.GetFileVersionError("file.mv.db");
        Assert.Equal(ErrorCode.FileVersionError1, version.H2ErrorCode);
        Assert.Contains("file.mv.db", version.Message, StringComparison.Ordinal);

        H2SharpException io = DbException.ConvertIOException(new IOException("disk"), "Compare");
        Assert.Equal(ErrorCode.IoException2, io.H2ErrorCode);
        H2SharpException ioNoMessage = DbException.ConvertIOException(new IOException("disk"), null);
        Assert.Equal(ErrorCode.IoException1, ioNoMessage.H2ErrorCode);

        H2SharpException outOfMemory = DbException.Get(ErrorCode.OutOfMemory);
        Assert.Equal(ErrorCode.OutOfMemory, outOfMemory.H2ErrorCode);
        H2SharpException withCause = DbException.Get(ErrorCode.OutOfMemory, new OutOfMemoryException("oom"), "p");
        Assert.Equal(ErrorCode.OutOfMemory, withCause.H2ErrorCode);
        Assert.IsType<OutOfMemoryException>(withCause.InnerException);

        // An H2 exception inside another answer is the inner one.
        H2SharpException inner = DbException.Get(ErrorCode.GeneralError1, "x");
        H2SharpException wrapped = DbException.ConvertInvocation(inner, "func");
        Assert.Same(inner, wrapped);
        H2SharpException plain = DbException.ConvertInvocation(new InvalidOperationException("boom"), "func");
        Assert.IsType<H2SharpException>(plain);
        Assert.Contains("func", plain.Message, StringComparison.Ordinal);

        // The JDBC forms: without a cause, with one parameter, with parameters.
        Assert.Equal(ErrorCode.FeatureNotSupported1, DbException.GetJdbcSqlException(ErrorCode.FeatureNotSupported1).H2ErrorCode);
        Assert.Equal(
            ErrorCode.ColumnNotFound1,
            DbException.GetJdbcSqlException(ErrorCode.ColumnNotFound1, "ID").H2ErrorCode);
        Assert.Equal(
            ErrorCode.ColumnNotFound1,
            DbException.GetJdbcSqlException(ErrorCode.ColumnNotFound1, new InvalidOperationException(), "ID", "T").H2ErrorCode);

        // The next-exception printer: one level, and more than a hundred levels.
        Assert.Equal(string.Empty, DbException.PrintNextExceptions(DbException.Get(ErrorCode.GeneralError1, "a")));
        Exception deepest = new InvalidOperationException("bottom");
        for (int i = 0; i < 120; i++)
        {
            deepest = new InvalidOperationException("level", deepest);
        }

        string printed = DbException.PrintNextExceptions(deepest);
        Assert.Contains("(truncated)", printed, StringComparison.Ordinal);

        _ = DbException.BuildMessageForException(DbException.Get(ErrorCode.GeneralError1, "m"));
        DbException.TraceThrowable(new IOException("trace"));

        H2SharpFatalException fatal = new("fatal");
        Assert.Equal("fatal", fatal.Message);
        H2SharpFatalException fatalWithCause = new("fatal", new InvalidOperationException("cause"));
        Assert.IsType<InvalidOperationException>(fatalWithCause.InnerException);
    }
}
