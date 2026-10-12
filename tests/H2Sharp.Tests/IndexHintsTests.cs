/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * An index hint answers by name, and a name written in another case is the same name.
 *
 * H2 folds every name before it compares it, and the fold is Java's English one rather than
 * .NET's: the two runtimes fold different letters, so a .NET comparison would answer "same"
 * where the JVM answers "different" and the other way round, exactly on the names where the
 * two disagree. This is asserted rather than assumed because the disagreement is silent — both
 * sides answer confidently, and only one of them is right.
 */
using System.Collections.Generic;
using H2Sharp.Tables;
using Xunit;

namespace H2Sharp.Tests;

public sealed class IndexHintsTests
{
    [Fact]
    public void ANameInAnotherCaseIsTheSameName()
    {
        IndexHints hints = IndexHints.CreateUseIndexHints(["MyIndex"]);

        Assert.True(hints.AllowIndex("MYINDEX"));
        Assert.True(hints.AllowIndex("myindex"));
        Assert.True(hints.AllowIndex("MyIndex"));
        Assert.False(hints.AllowIndex("OtherIndex"));
        Assert.False(hints.AllowIndex(null));
    }
}
