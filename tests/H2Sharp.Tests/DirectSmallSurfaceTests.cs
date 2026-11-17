/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 272: direct coverage of small helpers that SQL reaches only indirectly — the
 * two-level row cache, the plan's expression list and the constraint type predicates.
 */
using System;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Command;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class DirectSmallSurfaceTests
{
    [Fact]
    public void SecondLevelCacheDelegatesToBothLevels()
    {
        Cache baseCache = CacheLRU.GetCache(new NoopWriter(), "LRU", 8);
        SoftValuesHashMap<int, CacheObject> map = new();
        CacheSecondLevel cache = new(baseCache, map);
        TestCacheObject record = new(1);

        cache.Put(record);
        Assert.Same(record, cache.Get(1));
        Assert.Same(record, cache.Find(1));

        record.SetChanged(true);
        Assert.Contains(record, cache.GetAllChanged());

        Assert.Equal(baseCache.GetMaxMemory(), cache.GetMaxMemory());
        Assert.Equal(baseCache.GetMemory(), cache.GetMemory());
        cache.SetMaxMemory(1);
        Assert.Equal(1, cache.GetMaxMemory());

        TestCacheObject replacement = new(1);
        Assert.ThrowsAny<Exception>(() => cache.Update(1, replacement));
        Assert.True(cache.Remove(1));

        cache.Clear();
        Assert.Null(cache.Get(1));
    }

    [Fact]
    public void PreparedRendersAnExpressionList()
    {
        string sql = Prepared.GetSimpleSql(
            [ValueExpression.Get(ValueInteger.Get(1)), ValueExpression.Get(ValueVarchar.Get("x"))]);
        Assert.Equal("1, 'x'", sql);
        Assert.Equal(string.Empty, Prepared.GetSimpleSql([]));
        Assert.Throws<ArgumentNullException>(() => Prepared.GetSimpleSql(null!));
    }

    [Fact]
    public void ConstraintPredicatesAndOrderingAnswerDirectly()
    {
        Assert.True(ConstraintTypeRules.IsCheck(Constraint.Type.CHECK));
        Assert.True(ConstraintTypeRules.IsCheck(Constraint.Type.DOMAIN));
        Assert.False(ConstraintTypeRules.IsCheck(Constraint.Type.PRIMARY_KEY));
        Assert.False(ConstraintTypeRules.IsCheck(Constraint.Type.REFERENTIAL));

        Database database = new(ConnectionInfo.Memory("constraint_predicates"));
        SessionLocal session = new(database, "sa");
        try
        {
            Schema schema = database.GetSchema("PUBLIC");
            Domain domain = new(schema, database.AllocateObjectId(), "D");
            domain.SetDataType(TypeInfo.TypeInteger);
            ConstraintDomain constraint = new(schema, database.AllocateObjectId(), "C", domain);
            constraint.SetExpression(session,
                new Command.Parser(session).ParseDomainConstraintExpression("VALUE > 0"));

            Assert.Equal(0, constraint.CompareTo(constraint));
            Assert.True(constraint.CompareTo(null) > 0);
        }
        finally
        {
            database.Close();
        }
    }

    private sealed class NoopWriter : CacheWriter
    {
        public void WriteBack(CacheObject entry)
        {
        }

        public void FlushLog()
        {
        }

        public Trace GetTrace() => null!;
    }

    private sealed class TestCacheObject : CacheObject
    {
        internal TestCacheObject(int pos) => SetPos(pos);

        public override bool CanRemove() => true;

        public override int GetMemory() => 64;
    }
}
