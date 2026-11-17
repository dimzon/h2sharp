/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 277: the Prepared base class's own answers — read-only by default, refusals where the
 * port has no plan or execution path, and the object-id guard after a persisted id was consumed.
 */
using System;
using System.Reflection;
using H2Sharp.Command;
using H2Sharp.Engine;
using Xunit;

namespace H2Sharp.Tests;

public sealed class PreparedSurfaceTests
{
    [Fact]
    public void BasePreparedAnswersReadOnlyAndRefusesUnimplementedExecution()
    {
        SessionLocal session = new(new Database(ConnectionInfo.Memory("prepared_surface_own")), "sa");
        try
        {
            Prepared prepared = Assert.IsType<CommandContainer>(
                session.PrepareCommand("SELECT 1")).GetPreparedCommand();

            Assert.True(prepared.IsReadOnly());
            Assert.Throws<TODOException>(() => prepared.Update());

            prepared.SetPersistedObjectId(-3);
            MethodInfo getObjectId = typeof(Prepared).GetMethod(
                "GetObjectId", BindingFlags.Instance | BindingFlags.NonPublic)!;
            TargetInvocationException e = Assert.Throws<TargetInvocationException>(
                () => getObjectId.Invoke(prepared, null));
            Assert.Equal("Prepared.GetObjectId() was called before", e.InnerException!.Message.Split('[')[0].TrimEnd());
        }
        finally
        {
            session.GetDatabase().Close();
        }
    }
}
