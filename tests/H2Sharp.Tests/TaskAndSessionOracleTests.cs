/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * Task failure capture and Session reentrant ownership, against pinned H2. */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using H2Sharp.Engine;
using H2Sharp.Tests.Oracle;
using H2Task = H2Sharp.Util.H2Task;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class TaskAndSessionOracleTests
{
    [JavaOracleFact]
    public void TaskCapturesFailureAndWrapsItOnGet()
    {
        ArgumentException error = new("probe");
        ProbeTask task = new(_ => throw error);
        task.Execute();
        Assert.Same(error, task.GetException());
        InvalidOperationException wrapped = Assert.Throws<InvalidOperationException>(() => task.Get());
        Assert.Same(error, wrapped.InnerException);
        Assert.True(task.Stop);
        Assert.True(task.IsFinished());
        List<string> java = OracleRunner.Run([], """
            IllegalArgumentException error=new IllegalArgumentException("probe");
            org.h2.util.Task task=new org.h2.util.Task() { public void call() { throw error; } }.execute();
            System.out.println("="+(task.getException()==error));
            try { task.get(); } catch(RuntimeException e) { System.out.println("="+(e.getCause()==error)); }
            System.out.println("="+task.stop+":"+task.isFinished());
            """);
        Assert.Equal(["=true", "=true", "=true:true"], java);
    }

    [JavaOracleFact]
    public void JoinSignalsStopAndWaitsForTheWorker()
    {
        using ManualResetEventSlim started = new();
        ProbeTask task = new(t =>
        {
            started.Set();
            while (!t.Stop) Thread.Sleep(1);
        });
        task.Execute();
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.Null(task.Get());
            Assert.True(task.Stop);
            Assert.True(task.IsFinished());
        }
        finally { task.Join(); }
        List<string> java = OracleRunner.Run([], """
            java.util.concurrent.CountDownLatch started=new java.util.concurrent.CountDownLatch(1);
            org.h2.util.Task task=new org.h2.util.Task() {
              public void call() throws Exception { started.countDown(); while(!stop)Thread.sleep(1); }
            }.execute();
            started.await();
            System.out.println("="+(task.get()==null)+":"+task.stop+":"+task.isFinished());
            """);
        Assert.Equal(["=true:true:true"], java);
    }

    [JavaOracleFact]
    public void SessionLockIsReentrantAndOwnedByTheCallingThread()
    {
        SessionLocal session = new(new Database(new ConnectionInfo("jdbc:h2:mem:session_lock_oracle")), "sa");
        List<string> port = ["=" + session.IsLockedByCurrentThread().ToString().ToLowerInvariant()];
        session.Lock();
        try
        {
            port.Add("=" + session.IsLockedByCurrentThread().ToString().ToLowerInvariant());
            session.Lock();
            session.Unlock();
            port.Add("=" + session.IsLockedByCurrentThread().ToString().ToLowerInvariant());
            bool otherOwnsLock = true;
            H2Task task = new ProbeTask(_ => otherOwnsLock = session.IsLockedByCurrentThread()).Execute();
            Assert.Null(task.GetException());
            port.Add("=" + otherOwnsLock.ToString().ToLowerInvariant());
        }
        finally { session.Unlock(); }
        port.Add("=" + session.IsLockedByCurrentThread().ToString().ToLowerInvariant());
        session.Close();
        List<string> java = OracleRunner.Run([], """
            try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:session_lock_oracle")) {
              org.h2.engine.SessionLocal s=(org.h2.engine.SessionLocal)((org.h2.jdbc.JdbcConnection)c).getSession();
              System.out.println("="+s.isLockedByCurrentThread());
              s.lock();
              try {
                System.out.println("="+s.isLockedByCurrentThread());
                s.lock(); s.unlock(); System.out.println("="+s.isLockedByCurrentThread());
                org.h2.util.Task t=new org.h2.util.Task() { public void call() { System.out.println("="+s.isLockedByCurrentThread()); } }.execute();
                t.get();
              } finally { s.unlock(); }
              System.out.println("="+s.isLockedByCurrentThread());
            }
            """);
        Assert.Equal(java, port);
    }

    [JavaOracleFact]
    public void ScriptErrorNamesMatchTheReferenceReflectionFilter()
    {
        string path = Path.Combine(JavaOracle.ReferenceRoot!, "tests", "H2Sharp.Native.Tests", "Scripts", "ErrorCodeNames.csv");
        List<string> snapshot = File.ReadAllLines(path).Where(line => line.Length > 0 && line[0] != '#')
            .Select(line => "=" + line).ToList();
        List<string> java = OracleRunner.Run([], """
            java.util.Map<Integer,String> map=new java.util.TreeMap<>();
            for(java.lang.reflect.Field f:org.h2.api.ErrorCode.class.getDeclaredFields())
              if(f.getModifiers()==(java.lang.reflect.Modifier.PUBLIC|java.lang.reflect.Modifier.STATIC|java.lang.reflect.Modifier.FINAL)
                  && f.getAnnotation(Deprecated.class)==null) map.put(f.getInt(null),f.getName());
            for(java.util.Map.Entry<Integer,String> e:map.entrySet()) System.out.println("="+e.getKey()+","+e.getValue());
            """);
        Assert.Equal(java, snapshot);
    }

    private sealed class ProbeTask(Action<ProbeTask> action) : H2Task
    {
        public override void Call() => action(this);
    }
}
