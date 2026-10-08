/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Allocation guards for the value layer.
 *
 * These pin behaviour that a refactor would break quietly and a functional test would not
 * notice. Two things are guarded here and both were measured against the reference's own
 * shape:
 *
 * Small integers and small bigints are preallocated, so asking for one allocates nothing at
 * all. A query with a literal in it builds one of these per row parsed, and a megabyte of
 * garbage per million rows is the difference between gen0 pressure and a collection.
 *
 * A bigint outside the preallocated range costs one object, even when the shared cache is
 * about to answer with an equal value that is already there. That is what the reference does,
 * because it builds the candidate and then asks the cache, and the number is recorded rather
 * than improved: a deviation that saves the allocation is a deviation from the reference, and
 * that is a decision to make on purpose, not in passing.
 *
 * Rendering is free when the caller reuses its builder, which is how the engine calls it, so
 * that is guarded too.
 */
using System;
using System.Globalization;
using System.Text;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Pins the allocation behaviour of the value layer.
/// </summary>
public sealed class ValueAllocationTests
{
    private static long Measure(Action action)
    {
        for (int i = 0; i < 3; i++)
        {
            action();
        }

        long smallest = long.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocated < smallest)
            {
                smallest = allocated;
            }
        }

        return smallest;
    }

    /// <summary>
    /// What one call costs, measured over <see cref="Count"/> calls.
    /// </summary>
    /// <param name="action">the call to measure</param>
    /// <returns>the number of bytes per call</returns>
    private static long MeasurePerCall(Action action) => Measure(() =>
    {
        for (int i = 0; i < Count; i++)
        {
            action();
        }
    }) / Count;

    private const int Count = 100_000;

    [Fact]
    public void PreallocatedSmallValuesAllocateNothing()
    {
        long integers = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                _ = ValueInteger.Get(i % 128);
            }
        });

        long bigints = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                _ = ValueBigint.Get(i % 90);
            }
        });

        Assert.Equal(0, integers);
        Assert.Equal(0, bigints);
    }

    [Fact]
    public void NullAllocatesNothing()
    {
        long allocated = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                _ = ValueNull.Instance;
            }
        });

        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// One object for a value outside the preallocated range, which is what the reference
    /// costs as well.
    /// </summary>
    [Fact]
    public void BigintsOutsideTheRangeCostOneObject()
    {
        long allocated = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                _ = ValueBigint.Get(1_000_000L + i);
            }
        });

        // Recorded rather than improved: the reference builds the candidate and then asks the
        // cache, so it pays the same. If this is ever changed, the change is on purpose and
        // this bound is what says so.
        Assert.InRange(allocated, Count, Count * 64L);
    }

    [Fact]
    public void RenderingIntoAReusedBuilderAllocatesNothing()
    {
        StringBuilder reused = new();
        Value v = ValueInteger.Get(42);

        long allocated = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                reused.Clear();
                _ = v.GetSql(reused, 0);
                _ = v.GetString();
            }
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ArithmeticOnCachedValuesAllocatesNothing()
    {
        // The interesting case: a loop over literals, which is what a query does, must not
        // allocate once the values are inside the preallocated range.
        Value a = ValueInteger.Get(100);
        Value b = ValueInteger.Get(7);

        long allocated = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                _ = a.Add(b);
                _ = a.Subtract(b);
                _ = a.Multiply(b);
                _ = a.Divide(b, TypeInfo.TypeBigint);
                _ = a.Modulus(b);
            }
        });

        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// Rendering a double allocates the string the formatter returns, and nothing else.
    /// </summary>
    /// <remarks>
    /// One string per call, not one per digit count. The search that finds the shortest
    /// representation used to format a candidate into a fresh string on every step, which came
    /// to about a kilobyte per number; the guards in JavaNumberFormatAllocationTests hold that
    /// line, and this one says the whole call still costs one string.
    /// </remarks>
    [Fact]
    public void DoubleRenderingCostsOneString()
    {
        StringBuilder reused = new();
        ValueDouble v = ValueDouble.Get(1.5);

        long allocated = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                reused.Clear();
                _ = v.GetSql(reused, 0);
            }
        });

        Assert.InRange(allocated, Count * 32L, Count * 128L);
    }

    [Fact]
    public void DoubleArithmeticCostsOneValuePerOperation()
    {
        // 1.5 + 1.5 is 3.0, which is not one of the preallocated values, so each operation
        // builds one. That is the shape of the reference too: arithmetic creates values and the
        // cache only saves memory when the result repeats.
        ValueDouble a = ValueDouble.Get(1.5);

        long allocated = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                _ = a.Add(a);
                _ = a.Multiply(a);
            }
        });

        Assert.InRange(allocated, Count * 24L, Count * 128L);
    }

    [Fact]
    public void StringParsingAllocatesNothing()
    {
        // The string is already built by the time anything asks to convert it, and converting it
        // must not allocate: this is the path a WHERE clause on a numeric column takes for every
        // row that reaches it.
        Value v = ValueVarchar.Get("42");

        long allocated = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                _ = v.GetInt();
                _ = v.GetLong();
                _ = v.GetDouble();
            }
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void StringValuesCostOneObjectBeforeTheCache()
    {
        // The same shape as the bigint case: a candidate is built and only then offered to the
        // cache, so a repeated literal still costs one object. Recorded rather than improved.
        long allocated = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                _ = ValueVarchar.Get("42");
            }
        });

        Assert.InRange(allocated, Count * 16L, Count * 64L);
    }

    [Fact]
    public void StringRenderingAllocatesNothing()
    {
        StringBuilder reused = new();
        Value v = ValueVarchar.Get("a short literal");

        long allocated = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                reused.Clear();
                _ = v.GetSql(reused, 0);
            }
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void BinaryValuesRenderAndDescribeThemselvesForFree()
    {
        // The same two properties the string values rely on: rendering into a caller's builder
        // costs nothing, and the type is built once and kept, so asking for the precision again
        // costs nothing either. Without the second one every print of a BINARY would rebuild its
        // own TypeInfo.
        StringBuilder reused = new();
        Value v = ValueBinary.Get([0x41, 0x42, 0x43]);

        long allocated = Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                reused.Clear();
                _ = v.GetSql(reused, 0);
                _ = v.GetSql(reused, HasSql.NoCasts);
                _ = v.GetTypeInfo().GetPrecision();
            }
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void BinaryValuesCostACopyAndAnObject()
    {
        // Get clones the array it is handed and then builds the value, so a three byte array
        // costs one copy and one object. The getter copies again by contract, and the one that
        // promises not to copies nothing.
        byte[] source = [0x41, 0x42, 0x43];
        Value v = ValueVarbinary.Get(source);

        Assert.InRange(MeasurePerCall(() => ValueVarbinary.Get(source)), 56, 96);
        Assert.InRange(MeasurePerCall(() => v.GetBytes()), 24, 48);
        Assert.Equal(0, MeasurePerCall(() => v.GetBytesNoCopy()));
    }

    [Fact]
    public void CharComparisonsOnlyPayForThePaddingTheyDrop()
    {
        // A CHAR without padding costs nothing to compare: the trimming finds nothing to cut and
        // hands back the same string. A CHAR with padding costs one substring per side, which is
        // what the reference does too, because its conversion to CHAR does the same substring
        // before the two values are put side by side. Comparing a CHAR against a VARCHAR pays for
        // both sides, since both have to become CHAR first.
        CompareMode off = CompareMode.GetInstance(null, 0);
        Value plain = ValueChar.Get("abc");
        Value padded = ValueChar.Get("abc   ");

        Assert.Equal(0, MeasurePerCall(() => plain.CompareTo(plain, null, off)));
        Assert.InRange(MeasurePerCall(() => plain.CompareTo(padded, null, off)), 24, 48);
        Assert.InRange(
            MeasurePerCall(() => padded.CompareTo(ValueVarchar.Get("abc"), null, off)), 48, 96);

        // Rendering and describing stay free, like every other type.
        StringBuilder reused = new();
        Assert.Equal(0, Measure(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                reused.Clear();
                _ = plain.GetSql(reused, 0);
                _ = plain.GetSql(reused, HasSql.NoCasts);
                _ = plain.GetTypeInfo().GetPrecision();
            }
        }));
    }

    [Fact]
    public void Utf8CostsTheResultAndNothingElse()
    {
        // Decoding rents its buffer, so what is left is the string the caller asked for: 152 bytes
        // for 64 characters, whatever the bytes held. Before the buffer was rented this was 416 for
        // anything that was not pure ASCII, because the StringBuilder allocated alongside the
        // result.
        byte[] ascii = new byte[64];
        byte[] broken = new byte[64];
        for (int i = 0; i < 64; i++)
        {
            ascii[i] = (byte)('a' + i % 26);
            broken[i] = ascii[i];
        }

        broken[20] = 0xED;
        broken[21] = 0xA0;
        broken[22] = 0x80;

        int resultBytes = 24 + 64 * 2;
        Assert.Equal(resultBytes, MeasurePerCall(() => JavaUtf8.GetString(ascii)));
        Assert.Equal(resultBytes, MeasurePerCall(() => JavaUtf8.GetString(broken)));

        // Encoding counts first and allocates once, which is why an ASCII string costs its own
        // array and not a rounded up one.
        string text = new('a', 43);
        Assert.InRange(MeasurePerCall(() => JavaUtf8.GetBytes(text)), 64, 96);
    }

    [Fact]
    public void TheCacheAnswersWithTheSameInstance()
    {
        // Not an allocation test, but the property the cache exists for: equal values are the
        // same object once they are in the shared cache, which is what makes a repeated literal
        // cheap.
        Value first = ValueBigint.Get(1_000_000L);
        Value second = ValueBigint.Get(1_000_000L);
        Assert.True(ReferenceEquals(first, second));

        Value.ClearCache();
        Value third = ValueBigint.Get(1_000_000L);
        Assert.Equal(first, third);
        Assert.True(
            first.GetHashCode().ToString(CultureInfo.InvariantCulture)
                == third.GetHashCode().ToString(CultureInfo.InvariantCulture));
    }
}
