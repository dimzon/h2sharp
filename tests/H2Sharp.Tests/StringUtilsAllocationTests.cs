/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Allocation guards for the string helpers. The differential tests in
 * StringUtilsOracleTests prove the behaviour matches Java; these prove the behaviour
 * stays cheap, which is just as easy to lose in a refactor and much harder to notice.
 * A regression here shows up as gen0 pressure in a database workload long before it
 * shows up in a functional test.
 */
using System;
using System.Linq;
using System.Text;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Pins the allocation behaviour of the hot string paths.
/// </summary>
public sealed class StringUtilsAllocationTests
{
    /// <summary>
    /// Runs the action once to force any lazy statics, then measures the allocations of
    /// the second run on the current thread.
    /// </summary>
    private static long Measure(Action action)
    {
        action();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>
    /// A long input, so that any per-character allocation dominates the fixed overhead
    /// and the assertion is about the algorithm rather than about the call itself.
    /// </summary>
    private const int Length = 4096;

    private static string LongAscii()
    {
        StringBuilder builder = new(Length);
        for (int i = 0; i < Length; i++)
        {
            builder.Append((char)('a' + (i % 26)));
        }

        return builder.ToString();
    }

    private static string LongWithEscapes()
    {
        StringBuilder builder = new(Length);
        for (int i = 0; i < Length; i++)
        {
            builder.Append(i % 7 == 0 ? '\t' : (char)('a' + (i % 26)));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Slack allowed on top of the raw char count for a string object header, length
    /// field and alignment. Kept as a small range rather than an exact number so the
    /// test states "this allocation and no other" without pinning the runtime layout.
    /// </summary>
    private const int StringOverhead = 64;

    private static long StringSize(int chars) => (chars * sizeof(char)) + StringOverhead;

    [Fact]
    public void BytesToHex_AllocatesOnlyTheResult()
    {
        byte[] bytes = new byte[Length];
        for (int i = 0; i < Length; i++)
        {
            bytes[i] = (byte)i;
        }

        // Two chars per byte, and nothing else: no StringBuilder, no char[] chunk.
        long allocated = Measure(() => Hex.BytesToHex(bytes));

        Assert.InRange(allocated, (Length * 2 * sizeof(char)), StringSize(Length * 2));
    }

    [Fact]
    public void BytesToHex_FromSpanAllocatesResultPlusScratchBuffer()
    {
        byte[] bytes = new byte[Length];

        // The span overload cannot pass the span to string.Create, so above the stack
        // buffer size it also needs a scratch array. Documented, and still far below the
        // StringBuilder based original.
        long allocated = Measure(() => Hex.BytesToHex(bytes.AsSpan()));

        Assert.InRange(allocated, (Length * 2 * sizeof(char)), StringSize(Length * 2) * 2);
    }

    [Fact]
    public void BytesToHex_EmptyAllocatesNothing()
    {
        Assert.Equal(0, Measure(() => Hex.BytesToHex([])));
    }

    [Fact]
    public void AppendBytesHex_DoesNotAllocatePerCharacter()
    {
        byte[] bytes = new byte[Length];
        StringBuilder builder = new(bytes.Length * 2);
        builder.Length = 0;

        long allocated = Measure(() =>
        {
            builder.Length = 0;
            Hex.AppendBytesHex(builder, bytes);
        });

        // Reusing a builder that already has the capacity: the only allocation left may
        // be the builder's own chunk growth, and for this size it is already grown.
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ToUpperEnglish_AlreadyUppercaseAllocatesNothing()
    {
        // This is the identifier normalisation path: H2 uppercases names on every
        // lookup, and most of them are already uppercase. Returning the original
        // instance keeps that path allocation free.
        const string AlreadyUpper = "SOME_IDENTIFIER_NAME";

        Assert.Equal(0, Measure(() => StringUtils.ToUpperEnglish(AlreadyUpper)));
    }

    [Fact]
    public void ToLowerEnglish_AlreadyLowercaseAllocatesNothing()
    {
        const string AlreadyLower = "some_identifier_name";

        Assert.Equal(0, Measure(() => StringUtils.ToLowerEnglish(AlreadyLower)));
    }

    [Fact]
    public void ToUpperEnglish_ShortStringIsCachedAndAllocatesNothing()
    {
        const string Name = "table_name";

        Assert.Equal(0, Measure(() => StringUtils.ToUpperEnglish(Name)));
    }

    [Fact]
    public void JavaEncode_AllocatesOnlyTheResult()
    {
        string input = LongWithEscapes();
        long allocated = Measure(() => StringUtils.JavaEncode(input));

        // Escapes only expand the output, so the result is never shorter than the input
        // and the whole cost is that one string.
        Assert.InRange(allocated, input.Length * sizeof(char), input.Length * 3 * sizeof(char));
    }

    [Fact]
    public void JavaDecode_WithoutEscapesIsAllocationFree()
    {
        string input = LongAscii();

        Assert.Same(input, StringUtils.JavaDecode(input));
        Assert.Equal(0, Measure(() => StringUtils.JavaDecode(input)));
    }

    [Fact]
    public void JavaDecode_AllocatesOnlyTheResult()
    {
        string input = StringUtils.JavaEncode(LongWithEscapes());
        long allocated = Measure(() => StringUtils.JavaDecode(input));

        Assert.InRange(allocated, (Length * sizeof(char)), StringSize(Length) * 2);
    }

    [Fact]
    public void Pad_AllocatesOnlyTheResult()
    {
        // A width that fits the stack buffer, which is the normal case for a column
        // width or a formatted field.
        const int Width = 64;
        long allocated = Measure(() => StringUtils.Pad("value", Width, " ", right: true));

        Assert.InRange(allocated, (Width * sizeof(char)), StringSize(Width));
    }

    [Fact]
    public void Pad_WidePaddingAllocatesResultPlusScratchBuffer()
    {
        const int Width = 512;
        long allocated = Measure(() => StringUtils.Pad("value", Width, " ", right: true));

        // Above the stack buffer size a scratch array is needed, then the result string.
        Assert.InRange(allocated, (Width * sizeof(char)), StringSize(Width) * 2);
    }

    [Fact]
    public void Pad_SurrogatePaddingAllocatesOnlyTheResult()
    {
        const int Width = 64;
        long allocated = Measure(() =>
            StringUtils.Pad("v", Width, "\U0001F600", right: false));

        // 31 surrogate pairs, each two chars, plus the one payload char.
        Assert.InRange(allocated, (63 * sizeof(char)), StringSize(63));
    }

    [Fact]
    public void TrimAndTrimSubstring_ReturnTheOriginalWhenNothingIsTrimmed()
    {
        const string Value = "already_trimmed";

        Assert.Same(Value, StringUtils.Trim(Value, leading: true, trailing: true, ' '));
        Assert.Same(Value, StringUtils.TrimSubstring(Value, 0));

        // And they really do trim when there is something to trim.
        Assert.Equal("lready_trimmed", StringUtils.TrimSubstring(Value, 1));
        Assert.Equal("value", StringUtils.Trim("  value  ", leading: true, trailing: true, ' '));
    }

    [Fact]
    public void StartsWithIgnoringCase_DoesNotAllocatePerCall()
    {
        // The PRIMARY comparer resolves a cached CompareInfo, so a repeated comparison
        // must not allocate; only the very first call after a culture change may.
        StringUtils.StartsWithIgnoringCase("Café", "caf");

        Assert.Equal(
            0,
            Measure(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    StringUtils.StartsWithIgnoringCase("Café", "caf");
                }
            }));
    }

    [Fact]
    public void ArrayCombine_AllocatesOnlyTheResult()
    {
        string[] list = new string[64];
        for (int i = 0; i < list.Length; i++)
        {
            list[i] = "element," + i;
        }

        string expected = StringUtils.ArrayCombine(list, ',');
        long allocated = Measure(() => StringUtils.ArrayCombine(list, ','));

        // One allocation sized to the result. The StringBuilder version this replaced
        // measured 4464 bytes here because the builder reallocated and copied its chunk;
        // the bound below leaves room for the runtime's object header only.
        Assert.InRange(
            allocated,
            expected.Length * sizeof(char),
            (expected.Length * sizeof(char)) + StringOverhead);
    }

    [Fact]
    public void ArraySplit_AllocatesTheResultArrayOnly()
    {
        const string Text = "alpha,beta,gamma,delta,epsilon,zeta,eta,theta";

        // Two calls to warm up, then measure: the result array and its element strings.
        string[] expected = StringUtils.ArraySplit(Text, ',', trim: false)!;
        long allocated = Measure(() => StringUtils.ArraySplit(Text, ',', trim: false));

        // The element strings, the array itself and one scratch buffer, and nothing that
        // scales with a List plus a ToArray copy on top.
        long minimum = expected.Sum(static s => s.Length * (long)sizeof(char))
            + (expected.Length * IntPtr.Size);
        Assert.InRange(allocated, minimum, minimum + (512 * sizeof(char)));
    }

    [Fact]
    public void UrlEncode_AllocatesTheResultStringAndTheUtf8Bytes()
    {
        const string Url = "https://host/path?a=1&b=2";

        string expected = StringUtils.UrlEncode(Url);
        StringUtils.UrlEncode(Url);
        long allocated = Measure(() => StringUtils.UrlEncode(Url));

        // The UTF-8 byte array is unavoidable without a custom encoder; the rest is the
        // single exactly sized result.
        Assert.InRange(
            allocated,
            (expected.Length * sizeof(char)),
            (expected.Length * sizeof(char)) + (Url.Length * sizeof(char)) + 128);
    }

    /// <summary>
    /// An escaped separator must not be counted as one. The result array is sized from a
    /// counting pass, so a pass that ignored the escape produced an extra empty trailing
    /// element. Kept as a plain unit test so it fails without a JVM.
    /// </summary>
    [Theory]
    [InlineData("a\\,b,c", ',', new[] { "a,b", "c" })]
    [InlineData("a\\;b;c", ';', new[] { "a;b", "c" })]
    [InlineData("\\,", ',', new[] { "," })]
    [InlineData("a,,b", ',', new[] { "a", "", "b" })]
    [InlineData(",", ',', new[] { "", "" })]
    [InlineData("x\\", ',', new[] { "x\\" })]
    public void ArraySplit_CountsOnlyUnescapedSeparators(
        string input, char separator, string[] expected)
    {
        Assert.Equal(expected, StringUtils.ArraySplit(input, separator, trim: false));
    }

    /// <summary>
    /// When the separator is itself a backslash, the separator test wins and the escape
    /// handling must not swallow the next character.
    /// </summary>
    [Theory]
    [InlineData("a\\b", new[] { "a", "b" })]
    [InlineData("\\", new[] { "", "" })]
    [InlineData("a\\", new[] { "a", "" })]
    public void ArraySplit_HandlesBackslashSeparator(string input, string[] expected)
    {
        Assert.Equal(expected, StringUtils.ArraySplit(input, '\\', trim: false));
    }
}
