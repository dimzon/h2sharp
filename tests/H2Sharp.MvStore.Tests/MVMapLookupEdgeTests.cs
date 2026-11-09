/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of MVMap's lookup edges: GetKey past the end of a multi-level
 * tree, the non-generic key-list enumerator, and the decision-maker singletons.
 */
using System;
using System.Collections;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class MVMapLookupEdgeTests : StoreTestBase
{
    [Fact]
    public void GetKeyPastTheEndOfADeepTreeAnswersDefault()
    {
        string file = TempFile("mvmap-lookup.mv");
        DeleteFile(file);
        using MVStore s = new MVStore.Builder()
            .FileName(file)
            .PageSplitSize(64)
            .Open();
        MVMap<int, string> m = s.OpenMap<int, string>("deep");
        for (int i = 0; i < 600; i++)
        {
            m.Put(i, "v" + i);
        }

        s.Commit();
        Assert.Equal(0, m.GetKey(0));
        Assert.Equal(default, m.GetKey(600));
        Assert.Equal(default, m.GetKey(100000));
        Assert.Equal(default, m.GetKey(-1));
    }

    [Fact]
    public void KeyListNonGenericEnumeratorAnswers()
    {
        using MVStore s = new MVStore.Builder().Open();
        MVMap<int, string> m = s.OpenMap<int, string>("m");
        m.Put(1, "a");
        m.Put(2, "b");
        int count = 0;
        IEnumerator e = ((IEnumerable)m.KeyList()).GetEnumerator();
        while (e.MoveNext())
        {
            Assert.IsType<int>(e.Current);
            count++;
        }

        Assert.Equal(2, count);
    }

    [Fact]
    public void DecisionMakerSingletonsDescribeThemselves()
    {
        Assert.False(string.IsNullOrEmpty(DecisionMaker<int>.DefaultDecision().ToString()));
        Assert.False(string.IsNullOrEmpty(DecisionMaker<int>.PutDecision().ToString()));
        Assert.False(string.IsNullOrEmpty(DecisionMaker<int>.RemoveDecision().ToString()));
        Assert.False(string.IsNullOrEmpty(DecisionMaker<int>.IfAbsentDecision().ToString()));
        Assert.False(string.IsNullOrEmpty(DecisionMaker<int>.IfPresentDecision().ToString()));
        DecisionMaker<int>.PutDecision().Reset();
    }
}