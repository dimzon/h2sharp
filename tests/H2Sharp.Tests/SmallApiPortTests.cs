/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Small classes with no SQL surface of their own: the trace listener writer that presents the
 * embedder's listener to the engine, and the read-only memory stream the compression code reads
 * through.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using H2Sharp.Compression;
using H2Sharp.Data;
using H2Sharp.Message;
using Xunit;

namespace H2Sharp.Tests;

public sealed class SmallApiPortTests
{
    /// <summary>A listener recording what the engine's writer forwards.</summary>
    private sealed class RecordingListener : IH2DatabaseTraceListener
    {
        public List<string> Lines { get; } = [];

        public List<bool> EnabledAnswers { get; set; } = [true];

        private int enabledAsk;

        public void Write(H2TraceLevel level, string module, string message, Exception? t)
            => Lines.Add(level + "|" + module + "|" + message + "|" + (t is null ? "-" : t.GetType().Name));

        public bool IsEnabled(H2TraceLevel level)
        {
            bool answer = EnabledAnswers[Math.Min(enabledAsk++, EnabledAnswers.Count - 1)];
            return answer;
        }
    }

    [Fact]
    public void TraceListenerWriterForwardsToTheListener()
    {
        RecordingListener listener = new();
        TraceListenerWriter writer = new(listener);

        // The name is the engine's own business; the writer has none.
        writer.SetName("db");

        writer.Write((int)H2TraceLevel.Error, "database", "boom", null);
        writer.Write((int)H2TraceLevel.Info, 3, "note", new InvalidOperationException("why"));

        Assert.Equal(
        [
            "Error|database|boom|-",
            "Info|" + Trace.ModuleNames[3] + "|note|InvalidOperationException",
        ], listener.Lines);
    }

    [Fact]
    public void TraceListenerWriterAsksTheListenerAboutLevels()
    {
        RecordingListener listener = new() { EnabledAnswers = [true, false] };
        TraceListenerWriter writer = new(listener);

        Assert.True(writer.IsEnabled((int)H2TraceLevel.Info));
        Assert.False(writer.IsEnabled((int)H2TraceLevel.Debug));
    }

    [Fact]
    public void ReadOnlyMemoryStreamReadsForward()
    {
        ReadOnlyMemoryStream stream = new(new byte[] { 1, 2, 3, 4, 5 });

        Assert.True(stream.CanRead);
        Assert.False(stream.CanSeek);
        Assert.False(stream.CanWrite);
        Assert.Equal(5, stream.Length);

        byte[] buffer = new byte[3];
        Assert.Equal(3, stream.Read(buffer, 0, 3));
        Assert.Equal([1, 2, 3], buffer[..3]);
        Assert.Equal(2, stream.Read(buffer.AsSpan()));
        Assert.Equal([4, 5, 3], buffer);
        // The stream is exhausted and answers zero reads.
        Assert.Equal(0, stream.Read(buffer, 0, 3));

        // Seeking and writing refuse: the stream stands for a fixed buffer.
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.Position = 0);
        Assert.Throws<NotSupportedException>(() => stream.Write(buffer, 0, 1));
    }

    [Fact]
    public void CommandBuilderRefusesCommandGeneration()
    {
        // The schema machinery is not there yet: every command-generation question refuses
        // instead of answering something plausible.
        H2CommandBuilder builder = new();
        H2DataAdapter adapter = new();
        builder.DataAdapter = adapter;
        Assert.Same(adapter, builder.DataAdapter);

        System.Reflection.BindingFlags protectedInstance =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        foreach (string name in new[] { "GetParameterName", "GetParameterPlaceholder" })
        {
            System.Reflection.MethodInfo byOrdinal = typeof(H2CommandBuilder).GetMethod(
                name, protectedInstance, [typeof(int)])!;
            Assert.Throws<System.Reflection.TargetInvocationException>(
                () => byOrdinal.Invoke(builder, [0]));
        }

        System.Reflection.MethodInfo apply = typeof(H2CommandBuilder).GetMethod(
            "ApplyParameterInfo", protectedInstance)!;
        Assert.Throws<System.Reflection.TargetInvocationException>(
            () => apply.Invoke(builder, [null!, null!, StatementType.Insert, false]));
    }
}
