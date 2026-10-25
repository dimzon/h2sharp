/*
 * Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.util.Task, H2 2.5.
 * Approved D-024: H2Task avoids collision with System.Threading.Tasks.Task.
 * Copyright 2026 H2Sharp contributors.
 */
using System;
using System.Threading;

namespace H2Sharp.Util;

/// <summary>A method executed by a background thread, retaining its result or exception.</summary>
public abstract class H2Task
{
    private static int counter;
    public volatile bool Stop;
    private volatile object? result = null;
    private volatile bool finished;
    private Thread? thread;
    private volatile Exception? ex;

    public abstract void Call();

    public void Run()
    {
        try { Call(); }
        catch (Exception e) { ex = e; }
        finished = true;
    }

    public H2Task Execute() => Execute(GetType().FullName + ":" + (Interlocked.Increment(ref counter) - 1));

    public H2Task Execute(string threadName)
    {
        thread = new Thread(Run) { Name = threadName, IsBackground = true };
        thread.Start();
        return this;
    }

    public object? Get()
    {
        Exception? e = GetException();
        // Java RuntimeException(cause) has no direct .NET type; preserve the cause.
        if (e is not null) throw new InvalidOperationException("Background task failed", e);
        return result;
    }

    public bool IsFinished() => finished;
    public Exception? GetException()
    {
        Join();
        return ex;
    }

    public void Join()
    {
        Stop = true;
        if (thread is null) throw new InvalidOperationException("Thread not started");
        try { thread.Join(); }
        catch (ThreadInterruptedException) { /* InterruptedException is ignored in H2 Task.join. */ }
    }
}
