/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 284: the Value base class's generic helpers — byte-window streams, short conversion,
 * signum and SQL rendering — which typed values normally override.
 */
using System;
using System.IO;
using System.Text;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ValueCoreSurfaceTests
{
    [Fact]
    public void AValueCanHandOutAWindowOfItsBytes()
    {
        Value value = ValueVarchar.Get("abc");
        using Stream stream = value.GetInputStream(2, 2);
        using StreamReader reader = new(stream);
        Assert.Equal("bc", reader.ReadToEnd());
    }

    [Fact]
    public void GenericConversionsAndRenderingHaveTheirBaseAnswers()
    {
        Value value = ValueVarchar.Get("abc");
        Assert.ThrowsAny<Exception>(() => value.GetShort());
        Assert.ThrowsAny<Exception>(() => value.GetSignum());

        StringBuilder builder = new();
        Assert.Same(builder, ValueNull.Instance.GetSql(builder, 0));
    }
}
