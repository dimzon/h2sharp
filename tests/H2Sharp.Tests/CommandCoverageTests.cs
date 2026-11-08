/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of Command: the trace-gated start, the cancellation check,
 * the slow-query log and the non-transactional commit path.
 */
using System;
using System.Reflection;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class CommandCoverageTests
{
    [Fact]
    public void CommandSurfaceAnswersThePort()
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("cmd_cov_" + Guid.NewGuid().ToString("N"))), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };

        // Trace-gated start: with a listener and info level on, Start records the timestamp.
        session.GetDatabase().SetTraceListener(new NoOpTraceListener());
        session.GetDatabase().SetTraceLevelFile(2);
        CommandContainer container = Assert.IsType<CommandContainer>(
            session.PrepareCommand("SELECT 1"));
        H2Sharp.Command.Command command = container.GetPreparedCommand().GetCommand()!;

        // Start() is protected; invoke it to set the timestamp.
        MethodInfo? start = typeof(H2Sharp.Command.Command).GetMethod(
            "Start", BindingFlags.NonPublic | BindingFlags.Instance);
        start?.Invoke(command, null);
        Assert.NotEqual(0L, GetStartTime(command));

        // Slow-query log: backdate the start so the elapsed time exceeds the limit,
        // then Stop runs the trace branch.
        SetStartTime(command, System.Diagnostics.Stopwatch.GetTimestamp() - (200L * System.Diagnostics.Stopwatch.Frequency / 1000));
        command.Stop(true);

        // Cancellation check: cancel the command, then CheckCanceled throws.
        command.Cancel();
        H2SharpException canceled = Assert.Throws<H2SharpException>(
            () => command.CheckCanceled());
        Assert.Equal(Api.ErrorCode.StatementWasCanceled, canceled.H2ErrorCode);

        // Non-transactional commit path: a CREATE TABLE with auto-commit off.
        session.SetAutoCommit(false);
        CommandContainer create = Assert.IsType<CommandContainer>(
            session.PrepareCommand("CREATE TABLE COV(A INT)"));
        create.ExecuteUpdate(false);
        create.Stop(true);

        session.Close();
    }

    private static long GetStartTime(H2Sharp.Command.Command command)
    {
        FieldInfo field = typeof(H2Sharp.Command.Command).GetField(
            "StartTimeNanos",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (long)field.GetValue(command)!;
    }

    private static void SetStartTime(H2Sharp.Command.Command command, long value)
    {
        FieldInfo field = typeof(H2Sharp.Command.Command).GetField(
            "StartTimeNanos",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(command, value);
    }

    private sealed class NoOpTraceListener : IH2DatabaseTraceListener
    {
        public void Write(H2TraceLevel level, string module, string message, Exception? t)
        {
        }

        public bool IsEnabled(H2TraceLevel level) => true;
    }
}
