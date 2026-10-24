// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestOverflow.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the conversion is ConvertToType over the type info, the reference's main()
// entry is the runner; xUnit runs Test() directly.
using System.Numerics;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Tests numeric overflow on various data types.
/// Other than in Java, overflow is detected and an exception is thrown.
/// </summary>
public sealed class TestOverflow
{
    private readonly List<Value> values = [];

    private int dataType;

    private BigInteger min, max;

    private bool successExpected;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public void Test()
    {
        TestType(Value.Tinyint, sbyte.MinValue, sbyte.MaxValue);
        TestType(Value.Integer, int.MinValue, int.MaxValue);
        TestType(Value.Bigint, long.MinValue, long.MaxValue);
        TestType(Value.Smallint, short.MinValue, short.MaxValue);
    }

    private void TestType(int type, long minValue, long maxValue)
    {
        values.Clear();
        dataType = type;
        min = new BigInteger(minValue);
        max = new BigInteger(maxValue);
        Add(0);
        Add(minValue);
        Add(maxValue);
        Add(maxValue - 1);
        Add(minValue + 1);
        Add(1);
        Add(-1);
        Random random = new(1);
        for (int i = 0; i < 40; i++)
        {
            if (maxValue > int.MaxValue)
            {
                Add(random.NextInt64());
            }
            else
            {
                Add((random.Next(2) == 0 ? 1 : -1) * random.Next((int)maxValue));
            }
        }

        foreach (Value va in values)
        {
            foreach (Value vb in values)
            {
                TestValues(va, vb);
            }
        }
    }

    private void CheckIfExpected(string? a, string b)
    {
        if (successExpected)
        {
            Assert.Equal(b, a);
        }
    }

    private void OnSuccess()
    {
        if (!successExpected)
        {
            throw new Xunit.Sdk.XunitException("Unexpected success");
        }
    }

    private void OnError()
    {
        if (successExpected)
        {
            throw new Xunit.Sdk.XunitException("Unexpected error");
        }
    }

    private void TestValues(Value va, Value vb)
    {
        BigInteger a = BigInteger.Parse(va.GetString()!);
        BigInteger b = BigInteger.Parse(vb.GetString()!);
        successExpected = InRange(-a);
        try
        {
            CheckIfExpected(va.Negate().GetString(), (-a).ToString());
            OnSuccess();
        }
        catch (Exception)
        {
            OnError();
        }

        successExpected = InRange(a + b);
        try
        {
            CheckIfExpected(va.Add(vb).GetString(), (a + b).ToString());
            OnSuccess();
        }
        catch (Exception)
        {
            OnError();
        }

        successExpected = InRange(a - b);
        try
        {
            CheckIfExpected(va.Subtract(vb).GetString(), (a - b).ToString());
            OnSuccess();
        }
        catch (Exception)
        {
            OnError();
        }

        successExpected = InRange(a * b);
        try
        {
            CheckIfExpected(va.Multiply(vb).GetString(), (a * b).ToString());
            OnSuccess();
        }
        catch (Exception)
        {
            OnError();
        }
    }

    private bool InRange(BigInteger v)
    {
        return v.CompareTo(min) >= 0 && v.CompareTo(max) <= 0;
    }

    private void Add(long l)
    {
        values.Add(ValueVarchar.Get("" + l).ConvertToType(
            TypeInfo.GetTypeInfo(dataType)));
    }
}
