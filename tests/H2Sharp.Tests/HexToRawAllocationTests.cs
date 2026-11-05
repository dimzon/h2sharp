/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * HEXTORAW converts four hex digits at a time. The per-chunk scan used to allocate a
 * four-character string for every chunk; this pins the allocation to the result only.
 * Behaviour is covered by BuiltinFunctionCallTests (Java oracle) and the SQL specs; here
 * the function is called directly so the measurement is about the conversion, not the
 * parser or the result set.
 */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Functions;
using H2Sharp.JavaLegacy;
using H2Sharp.MvStore;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;
using Xunit.Abstractions;

namespace H2Sharp.Tests;

[Collection(MeasurementCollection.Name)]
public sealed class HexToRawAllocationTests
{
    private readonly ITestOutputHelper output;

    public HexToRawAllocationTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    private static SessionLocal NewSession()
        => new(new Database(ConnectionInfo.Memory("hex_to_raw")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
            RandomGeneratorProvider = seed => new JavaRandom(seed ?? Environment.TickCount64),
        };

    private static long Measure(Action action)
    {
        for (int i = 0; i < 3; i++)
        {
            action();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static string Hex(int length)
    {
        StringBuilder builder = new(length);
        for (int i = 0; i < length; i++)
        {
            builder.Append(i % 2 == 0 ? '4' : '1');
        }

        return builder.ToString();
    }

    [Fact]
    public void ConversionAllocatesOnlyTheResult()
    {
        const int Length = 4096;
        SessionLocal session = NewSession();
        StringFunction1 function = new(
            ValueExpression.Get(ValueVarchar.Get(Hex(Length))), StringFunction1.Hextoraw);

        long bytes = Measure(() =>
        {
            Value value = function.GetValue(session);
            if (value.GetString()!.Length != Length / 4)
            {
                throw new InvalidOperationException("unexpected result length");
            }
        });

        output.WriteLine(FormattableString.Invariant($"HEXTORAW({Length}) allocated {bytes} bytes"));
        Assert.True(
            bytes < 8 * 1024,
            FormattableString.Invariant($"HEXTORAW allocated {bytes} bytes; the per-chunk strings are back"));
    }

    [Theory]
    [InlineData("00410042", "AB")]
    [InlineData("41424344", "\u4142\u4344")]
    [InlineData("00e9", "\u00e9")]
    public void ConversionKeepsTheReferenceResult(string hex, string expected)
    {
        SessionLocal session = NewSession();
        StringFunction1 function = new(
            ValueExpression.Get(ValueVarchar.Get(hex)), StringFunction1.Hextoraw);

        Assert.Equal(expected, function.GetValue(session).GetString());
    }

    [Theory]
    [InlineData("zzzz")]
    [InlineData("ABC")]
    [InlineData(" 41 ")]
    public void InvalidInputIsADataConversionError(string hex)
    {
        SessionLocal session = NewSession();
        StringFunction1 function = new(
            ValueExpression.Get(ValueVarchar.Get(hex)), StringFunction1.Hextoraw);

        H2SharpException error = Assert.Throws<H2SharpException>(() => function.GetValue(session));
        Assert.Equal(22018, (int)error.H2ErrorCode);
    }
}
