/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 117: the DbObject base contract directly — the trace module id, the modification id
 * with and without a database, the name setter, invalidation and its refusal of a second
 * call, the base children and drop answers, the copy-SQL and dependsOn refusals, and the
 * id-based identity and ToString.
 */
using System;
using H2Sharp.Engine;
using Xunit;

namespace H2Sharp.Tests;

public sealed class DbObjectEdgeTests
{
    /// <summary>A DbObject that answers the protected members for the test.</summary>
    private sealed class TestObject : DbObject
    {
        public TestObject(Database? database, int id, string name, int traceModuleId)
            : base(database, id, name, traceModuleId)
        {
        }

        internal int TraceModuleIdAnswer => TraceModuleId;

        internal void RenameObject(string name) => SetObjectName(name);

        internal void DoInvalidate() => Invalidate();

        public override string? GetCreateSql() => "CREATE NOTHING";

        public override int GetTypeId() => DbObject.Setting;

        public override void RemoveChildrenAndResources(SessionLocal session)
        {
        }
    }

    [Fact]
    public void BaseMembersAnswerTheirShapes()
    {
        Database database = new(ConnectionInfo.Memory("db_object_" + Guid.NewGuid().ToString("N")));
        TestObject obj = new(database, 7, "obj", 42);

        // The trace module id round-trips the constructor argument.
        Assert.Equal(42, obj.TraceModuleIdAnswer);
        Assert.Equal(7, obj.GetId());
        Assert.Equal("obj", obj.GetName());
        Assert.Same(database, obj.GetDatabase());
        Assert.True(obj.IsValid());

        // The modification id starts at the database's current meta id, and each SetModified
        // takes the next one (the reference answers the id before it advances the counter).
        long initial = obj.GetModificationId();
        Assert.Equal(database.GetModificationMetaId(), initial);
        obj.SetModified();
        long first = obj.GetModificationId();
        obj.SetModified();
        Assert.True(obj.GetModificationId() > first);

        // The name setter is what a rename uses under the public Rename.
        obj.RenameObject("renamed");
        Assert.Equal("renamed", obj.GetName());
        obj.Rename("renamed again");
        Assert.Equal("renamed again", obj.GetName());
        Assert.True(obj.GetModificationId() > initial);

        // The base answers: no children, no drop statement, the meta answer from the create SQL.
        Assert.Null(obj.GetChildren());
        Assert.Null(obj.GetDropSql());
        Assert.Equal("CREATE NOTHING", obj.GetCreateSql());
        Assert.Equal("CREATE NOTHING", obj.GetCreateSqlForMeta());

        // The refusals the base keeps for kinds that never ported the copy or the dependency.
        Assert.Throws<TODOException>(() => obj.GetCreateSqlForCopy(null!, "\"T\""));
        Assert.Throws<TODOException>(() => obj.DependsOn(obj));

        // The comment and temporary flags.
        obj.SetComment("a comment");
        Assert.Equal("a comment", obj.GetComment());
        obj.SetComment("");
        Assert.Null(obj.GetComment());
        obj.SetComment(null);
        Assert.Null(obj.GetComment());
        Assert.False(obj.IsTemporary());
        obj.SetTemporary(true);
        Assert.True(obj.IsTemporary());

        // Identity is the id alone.
        TestObject same = new(database, 7, "other", 42);
        TestObject other = new(database, 8, "obj", 42);
        Assert.Equal(obj, same);
        Assert.NotEqual(obj, other);
        Assert.NotEqual<object>(obj, "obj");
        Assert.Equal(obj.GetHashCode(), same.GetHashCode());

        // A detached object has no modification id of its own.
        TestObject detached = new(null, 9, "detached", 42);
        Assert.Equal(-1, detached.GetModificationId());
        Assert.Null(detached.GetDatabase());
        detached.SetModified();
        Assert.Equal(-1, detached.GetModificationId());

        // Invalidation clears the identity, and a second invalidate is refused.
        obj.DoInvalidate();
        Assert.False(obj.IsValid());
        Assert.Equal(-1, obj.GetId());
        Assert.Null(obj.GetDatabase());
        Assert.Null(obj.GetName());
        Assert.StartsWith("?:-1:", obj.ToString(), StringComparison.Ordinal);
        Exception second = Record.Exception(() => obj.DoInvalidate());
        Assert.NotNull(second);
        Assert.Contains("Unexpected code path", second!.Message, StringComparison.Ordinal);

        database.Close();
    }
}
