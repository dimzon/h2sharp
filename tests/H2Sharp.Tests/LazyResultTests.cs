/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The lazy walk mechanics: rows arrive in order, skipping discards without reading, the
 * limit stops the fetch, and closing ends everything including reset.
 */
using System;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class LazyResultTests
{
    private static SessionLocal NewSession()
    {
        return new SessionLocal(new Database(ConnectionInfo.Memory("lazy")), "sa");
    }

    private sealed class ListLazyResult : LazyResult
    {
        private readonly Value?[][] rows;

        private int position;

        internal ListLazyResult(SessionLocal session, Expression[] expressions, Value?[][] rows)
            : base(session, expressions)
        {
            this.rows = rows;
        }

        public override int GetVisibleColumnCount() => rows.Length == 0 ? 0 : rows[0].Length;

        public override void Dispose() => Close();

        /// <summary>
        /// Starts over, source included: the base forgets the walk, and the walk's source
        /// starts from its beginning, as the query's own reset rewinds its filter.
        /// </summary>
        public override void Reset()
        {
            base.Reset();
            position = 0;
        }

        protected override Value?[]? FetchNextRow() => position < rows.Length ? rows[position++] : null;
    }

    private static Value?[][] Rows() =>
    [
        new Value?[] { ValueInteger.Get(1) },
        new Value?[] { ValueInteger.Get(2) },
        new Value?[] { ValueInteger.Get(3) },
    ];

    private static ListLazyResult NewResult(SessionLocal session)
    {
        return new ListLazyResult(
            session, [ValueExpression.Get(ValueInteger.Get(0))], Rows());
    }

    [Fact]
    public void RowsArriveInOrderWithIds()
    {
        ListLazyResult result = NewResult(NewSession());

        Assert.True(result.Next());
        Assert.Equal(0, result.GetRowId());
        Assert.Equal(1, ((ValueInteger)result.CurrentRow()[0]!).GetLong());
        Assert.True(result.Next());
        Assert.Equal(1, result.GetRowId());
        Assert.True(result.Next());
        Assert.Equal(2, result.GetRowId());
        Assert.False(result.Next());
        Assert.True(result.IsAfterLast());
        Assert.Equal(3, result.GetRowId());
        Assert.False(result.Next());
        Assert.Equal(3, result.GetRowId());
    }

    [Fact]
    public void SkipDiscardsWithoutReading()
    {
        ListLazyResult result = NewResult(NewSession());

        Assert.True(result.Skip());
        Assert.True(result.Next());
        Assert.Equal(2, ((ValueInteger)result.CurrentRow()[0]!).GetLong());
        Assert.True(result.Next());
        Assert.False(result.Next());
    }

    [Fact]
    public void LimitStopsTheFetch()
    {
        ListLazyResult result = NewResult(NewSession());
        result.SetLimit(1);

        Assert.True(result.Next());
        Assert.False(result.HasNext());
        Assert.False(result.Next());
        Assert.True(result.IsAfterLast());
    }

    [Fact]
    public void ResetStartsOverAndCloseEndsEverything()
    {
        ListLazyResult result = NewResult(NewSession());

        Assert.True(result.Next());
        result.Reset();
        Assert.Equal(-1, result.GetRowId());
        Assert.False(result.IsAfterLast());
        Assert.True(result.Next());
        Assert.Equal(1, ((ValueInteger)result.CurrentRow()[0]!).GetLong());

        Assert.False(result.IsClosed());
        result.Close();
        Assert.True(result.IsClosed());
        Assert.False(result.Next());
        Assert.False(result.HasNext());
        Assert.False(result.Skip());
        Assert.ThrowsAny<Exception>(() => result.Reset());
        Assert.True(result.IsLazy());
        Assert.True(result.NeedToClose());
    }

    [Fact]
    public void RowCountIsUnknown()
    {
        ListLazyResult result = NewResult(NewSession());

        Assert.ThrowsAny<Exception>(() => result.GetRowCount());
        Assert.Equal(1, result.GetFetchSize());
    }
}
