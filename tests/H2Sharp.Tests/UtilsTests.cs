/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new tests for H2Sharp).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class UtilsTests
{
    [Fact]
    public void IndexOf_FindsPattern()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        Assert.Equal(0, Utils.IndexOf(bytes, [1], 0));
        Assert.Equal(2, Utils.IndexOf(bytes, [3], 0));
        Assert.Equal(-1, Utils.IndexOf(bytes, [9], 0));
    }

    [Fact]
    public void IndexOf_EmptyPatternReturnsStart()
    {
        byte[] bytes = [1, 2, 3];
        Assert.Equal(0, Utils.IndexOf(bytes, [], 0));
        Assert.Equal(2, Utils.IndexOf(bytes, [], 2));
    }

    [Fact]
    public void IndexOf_HonoursStart()
    {
        byte[] bytes = [7, 7, 7];
        Assert.Equal(2, Utils.IndexOf(bytes, [7], 2));
        Assert.Equal(-1, Utils.IndexOf(bytes, [7], 3));
    }

    [Fact]
    public void CompareSecure_IsOrderIndependentAndConstantShaped()
    {
        Assert.True(Utils.CompareSecure([1, 2, 3], [1, 2, 3]));
        Assert.False(Utils.CompareSecure([1, 2, 3], [3, 2, 1]));
        Assert.False(Utils.CompareSecure([1, 2], [1, 2, 3]));
        Assert.True(Utils.CompareSecure([], []));
        Assert.True(Utils.CompareSecure(null, null));
        Assert.False(Utils.CompareSecure([1], null));
        Assert.False(Utils.CompareSecure(null, [1]));
    }

    [Fact]
    public void Copy_GrowsTargetWhenTooSmall()
    {
        byte[] target = new byte[2];
        byte[] result = Utils.Copy([1, 2, 3], target);
        Assert.Equal([1, 2, 3], result);
        Assert.NotSame(target, result);

        byte[] bigTarget = new byte[5];
        byte[] reused = Utils.Copy([1, 2, 3], bigTarget);
        Assert.Same(bigTarget, reused);
    }

    [Fact]
    public void NewBytes_ReturnsSharedEmptyForZeroLength()
        => Assert.Same(Utils.EmptyBytes, Utils.NewBytes(0));

    [Fact]
    public void CloneByteArray_CopiesAndHandlesEmpty()
    {
        byte[] source = [1, 2, 3];
        byte[] clone = Utils.CloneByteArray(source);
        Assert.Equal(source, clone);
        Assert.NotSame(source, clone);

        Assert.Same(Utils.EmptyBytes, Utils.CloneByteArray([]));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("t", true)]
    [InlineData("y", true)]
    [InlineData("T", true)]
    [InlineData("Y", true)]
    [InlineData("0", false)]
    [InlineData("f", false)]
    [InlineData("n", false)]
    [InlineData("no", false)]
    [InlineData("NO", false)]
    [InlineData("yes", true)]
    [InlineData("YES", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    public void ParseBoolean_AcceptsH2Spellings(string value, bool expected)
        => Assert.Equal(expected, Utils.ParseBoolean(value, false, false));

    [Fact]
    public void ParseBoolean_ReturnsDefaultForUnknown()
        => Assert.True(Utils.ParseBoolean("maybe", true, false));

    [Fact]
    public void ParseBoolean_ThrowsWhenRequested()
    {
        Assert.Throws<ArgumentException>(() => Utils.ParseBoolean("maybe", true, true));
        Assert.Throws<ArgumentException>(() => Utils.ParseBoolean("maybe", false, true));
    }

    [Fact]
    public void ParseBoolean_ReturnsDefaultForNull()
        => Assert.True(Utils.ParseBoolean(null, true, true));

    [Fact]
    public void GetProperty_ParsesJavaIntegerDecodeForms()
    {
        const string Key = "H2SHARP_TEST_PROPERTY";
        try
        {
            Environment.SetEnvironmentVariable(Key, "0x1F");
            Assert.Equal(31, Utils.GetProperty(Key, 0));

            Environment.SetEnvironmentVariable(Key, "#20");
            Assert.Equal(0x20, Utils.GetProperty(Key, 0));

            Environment.SetEnvironmentVariable(Key, "010");
            Assert.Equal(8, Utils.GetProperty(Key, 0));

            Environment.SetEnvironmentVariable(Key, "123");
            Assert.Equal(123, Utils.GetProperty(Key, 0));

            Environment.SetEnvironmentVariable(Key, "not-a-number");
            Assert.Equal(-7, Utils.GetProperty(Key, -7));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Key, null);
        }
    }

    [Fact]
    public void GetProperty_ReturnsDefaultWhenUnset()
    {
        const string Key = "H2SHARP_UNSET_PROPERTY";
        Assert.Equal("fallback", Utils.GetProperty(Key, "fallback"));
        Assert.Equal(42, Utils.GetProperty(Key, 42));
        Assert.True(Utils.GetProperty(Key, true));
    }

    [Fact]
    public void GetByteArrayHash_IsStableAndLengthSensitive()
    {
        byte[] a = [1, 2, 3];
        int h1 = Utils.GetByteArrayHash(a);
        Assert.Equal(h1, Utils.GetByteArrayHash(a));
        Assert.NotEqual(h1, Utils.GetByteArrayHash([1, 2, 4]));
        Assert.NotEqual(h1, Utils.GetByteArrayHash([1, 2]));
    }

    [Fact]
    public void GetByteArrayHash_HandlesLongArrays()
    {
        // >= 50 elements takes the sampled branch; it must still be deterministic
        // and must not throw.
        byte[] bytes = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();
        Assert.Equal(Utils.GetByteArrayHash(bytes), Utils.GetByteArrayHash(bytes));
    }

    [Fact]
    public void StableSort_KeepsTiesInArrivalOrder()
    {
        // Tied keys keep the order they arrived in: that is the whole contract of a stable
        // sort, and what a limit trims after it. An unstable sort answers the same keys in
        // some other order, which this fails.
        (int key, int seq)[] rows =
        [
            (2, 0), (1, 0), (2, 1), (0, 0), (1, 1), (2, 2), (0, 1), (1, 2),
        ];
        Utils.StableSort(rows, static (a, b) => a.key.CompareTo(b.key));

        Assert.Equal(
            [(0, 0), (0, 1), (1, 0), (1, 1), (1, 2), (2, 0), (2, 1), (2, 2)],
            rows);
    }

    [Fact]
    public void StableSort_HandlesEdges()
    {
        int[] empty = [];
        Utils.StableSort(empty, Comparer<int>.Default.Compare);
        Assert.Empty(empty);

        int[] single = [42];
        Utils.StableSort(single, Comparer<int>.Default.Compare);
        Assert.Equal([42], single);

        // Two hundred tied rows, past every small-array path of the merge.
        (int key, int seq)[] tied = new (int, int)[200];
        for (int i = 0; i < tied.Length; i++)
        {
            tied[i] = (i % 5, i);
        }

        Utils.StableSort(tied, static (a, b) => a.key.CompareTo(b.key));
        for (int i = 1; i < tied.Length; i++)
        {
            Assert.True(tied[i - 1].key < tied[i].key
                || (tied[i - 1].key == tied[i].key && tied[i - 1].seq < tied[i].seq));
        }
    }

    [Fact]
    public void StableSort_SortsOnlyTheWindow()
    {
        // The window sorts stably by key with ties in arrival order, and everything outside
        // it stays exactly where it was: the LIMIT path depends on both.
        (int key, int seq)[] rows =
        [
            (9, 0), (2, 0), (1, 0), (2, 1), (0, 0), (1, 1), (2, 2), (8, 0),
        ];
        Utils.StableSort(rows, 1, 7, static (a, b) => a.key.CompareTo(b.key));

        Assert.Equal(
            [(9, 0), (0, 0), (1, 0), (1, 1), (2, 0), (2, 1), (2, 2), (8, 0)],
            rows);
    }

    [Fact]
    public void StableSort_HandlesWindowEdges()
    {
        // An empty window and a single-element window change nothing, at either end.
        int[] values = [3, 1, 2];
        Utils.StableSort(values, 1, 1, Comparer<int>.Default.Compare);
        Utils.StableSort(values, 0, 0, Comparer<int>.Default.Compare);
        Utils.StableSort(values, 3, 3, Comparer<int>.Default.Compare);
        Assert.Equal([3, 1, 2], values);

        Utils.StableSort(values, 2, 3, Comparer<int>.Default.Compare);
        Assert.Equal([3, 1, 2], values);

        // A window past the small-merge widths still sorts: eleven elements take three
        // passes, an odd number, which is the path that copies the window back.
        (int key, int seq)[] wide = new (int, int)[11];
        for (int i = 0; i < wide.Length; i++)
        {
            wide[i] = (10 - i, i);
        }

        Utils.StableSort(wide, 0, wide.Length, static (a, b) => a.key.CompareTo(b.key));
        for (int i = 0; i < wide.Length; i++)
        {
            Assert.Equal((i, 10 - i), wide[i]);
        }
    }

    [Fact]
    public void SortTopN_PlacesTopValuesInRequestedRange()
    {
        // H2's javadoc says "descending order", but the implementation is a partial
        // quicksort that moves the smallest-per-comparer values towards low and the
        // largest towards high, then sorts the requested range. With the natural
        // ascending comparer, position 0..n ends up holding the n *smallest* values,
        // ascending. Elements outside the range are unspecified.
        int[] values = [5, 1, 9, 3, 7, 2, 8];
        Utils.SortTopN(values, 0, 3, Comparer<int>.Default.Compare);
        Assert.Equal([1, 2, 3], values.Take(3).ToArray());
        Assert.Equal(7, values.Length);
    }

    [JavaOracleTheory]
    [InlineData(0, 3)]
    [InlineData(4, 6)]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    [InlineData(0, 10)]
    public void SortTopN_MatchesJava(int from, int to)
    {
        int[] inputs = [5, 1, 9, 3, 7, 2, 8, 0, 6, 4];

        string javaOutput = RunSortTopNOnReferenceJvm(inputs, from, to);

        int[] actual = [.. inputs];
        Utils.SortTopN(actual, from, to, Comparer<int>.Default.Compare);

        // Only the requested range is defined by H2: partialQuickSort picks a random
        // pivot, so elements outside [from, to) are explicitly unspecified and differ
        // run to run in both implementations. Compare the requested range only.
        // Java prints Integer[] via Arrays.toString, i.e. "[1, 2, 3]".
        string actualRange = "[" + string.Join(", ", actual[from..to]) + "]";
        string javaRange = ExtractRange(javaOutput, from, to);
        Assert.True(
            javaRange == actualRange,
            $"from={from} to={to} n={inputs.Length}\n"
            + $"java raw   = {javaOutput}\n"
            + $"java range = {javaRange}\n"
            + $"cs   raw   = [{string.Join(", ", actual)}]\n"
            + $"cs   range = {actualRange}");
    }

    private static string ExtractRange(string javaArraysToString, int from, int to)
    {
        string inner = javaArraysToString.Trim();
        inner = inner[1..^1];
        string[] parts = inner.Split(", ");
        return "[" + string.Join(", ", parts[from..to]) + "]";
    }

    private static string RunSortTopNOnReferenceJvm(int[] inputs, int from, int to)
    {
        if (JavaOracle.H2Jar is null)
        {
            throw new InvalidOperationException(
                "H2 jar not found. Build it with: "
                + "(cd reference/h2/h2 && mvn -B -q -DskipTests package)");
        }

        // Java has no primitive array sort with a Comparator, so the oracle snippet
        // must box: Utils.sortTopN is generic over X[].
        string source = "import java.util.Arrays;\n"
            + "import java.util.Comparator;\n"
            + "public class Oracle {\n"
            + "  public static void main(String[] a) {\n"
            + "    Integer[] xs = { " + string.Join(",", inputs) + " };\n"
            + "    org.h2.util.Utils.sortTopN(xs, " + from + ", " + to
            + ", Comparator.naturalOrder());\n"
            + "    System.out.println(java.util.Arrays.toString(xs));\n"
            + "  }\n"
            + "}\n";

        return JavaOracle.Run(source, "Oracle", JavaOracle.H2Jar).Trim();
    }

    [Fact]
    public void SortTopN_DoesNothingForDegenerateRanges()
    {
        int[] values = [3, 1, 2];
        Utils.SortTopN(values, 2, 2, Comparer<int>.Default.Compare);
        Assert.Equal([3, 1, 2], values);

        Utils.SortTopN(values, 0, 3, Comparer<int>.Default.Compare);
        Assert.Equal([1, 2, 3], values);
    }

    [Fact]
    public void CurrentNanoTime_IsNeverZero()
    {
        Assert.NotEqual(0L, Utils.CurrentNanoTime());
        Assert.NotEqual(0L, Utils.CurrentNanoTimePlusMillis(0));
        // -1_000_000 ns + 1 ms == 0, which Java maps to 1.
        Assert.Equal(1L, Utils.NanoTimePlusMillis(-1_000_000L, 1));
    }

    [Fact]
    public void NanoTimePlusMillis_AddsOffset()
    {
        // 5 ms = 5_000_000 ns
        Assert.Equal(1_000_000L + 5_000_000L, Utils.NanoTimePlusMillis(1_000_000L, 5));
    }

    [Fact]
    public void BackgroundExecutor_RunsWorkAndShutsDown()
    {
        Utils.BackgroundExecutor executor = Utils.CreateSingleThreadExecutor("h2sharp-test");
        Assert.Equal("h2sharp-test", executor.Name);

        StringBuilder observed = new();
        ManualResetEventSlim done = new(false);
        executor.Execute(() =>
        {
            observed.Append("a");
            done.Set();
        });

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Utils.FlushExecutor(executor);
        Assert.Equal("a", observed.ToString());

        Utils.ShutdownExecutor(executor);
        Assert.Throws<InvalidOperationException>(() => executor.Execute(() => { }));
    }

    [Fact]
    public void MemoryHelpers_ReturnPositiveValues()
    {
        // GetMemoryUsed reads the live heap in KB after a collection, so it is non-negative and
        // is zero on a heap that happens to be empty. Asserting "> 0" was asking for a value the
        // runtime does not promise, and it flaked once under a parallel run: the assertion was
        // about the test host rather than about the code.
        Assert.True(Utils.GetMemoryUsed() >= 0);
        Assert.True(Utils.GetMemoryMax() >= 0);
        Assert.True(Utils.GetGarbageCollectionTime() >= 0);
        Assert.True(Utils.ScaleForAvailableMemory(64) > 0);
    }
}
