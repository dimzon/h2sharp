// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestIntArray.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the reference's main() entry is the runner; xUnit runs Test() directly.
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Tests the IntArray class.
/// </summary>
public sealed class TestIntArray
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public void Test()
    {
        TestInit();
        TestRandom();
        TestRemoveRange();
    }

    private void TestRemoveRange()
    {
        IntArray array = new([1, 2, 3, 4, 5]);
        array.RemoveRange(1, 3);
        Assert.Equal(3, array.Size());
        Assert.Equal(1, array.Get(0));
        Assert.Equal(4, array.Get(1));
        Assert.Equal(5, array.Get(2));
    }

    private static void TestInit()
    {
        IntArray array = new([]);
        array.Add(10);
    }

    private void TestRandom()
    {
        IntArray array = new();
        int[] test = [];
        Random random = new(1);
        for (int i = 0; i < 10000; i++)
        {
            int idx = test.Length == 0 ? 0 : random.Next(test.Length);
            int v = random.Next(100);
            int op = random.Next(4);
            switch (op)
            {
                case 0:
                    array.Add(v);
                    test = Add(test, v);
                    break;
                case 1:
                    if (test.Length > idx)
                    {
                        Assert.Equal(Get(test, idx), array.Get(idx));
                    }

                    break;
                case 2:
                    if (test.Length > 0)
                    {
                        array.Remove(idx);
                        test = Remove(test, idx);
                    }

                    break;
                case 3:
                    Assert.Equal(test.Length, array.Size());
                    break;
                default:
                    break;
            }

            Assert.Equal(test.Length, array.Size());
            for (int j = 0; j < test.Length; j++)
            {
                Assert.Equal(test[j], array.Get(j));
            }
        }
    }

    private static int[] Add(int[] array, int i, int value)
    {
        int[] a2 = new int[array.Length + 1];
        Array.Copy(array, 0, a2, 0, array.Length);
        if (i < array.Length)
        {
            Array.Copy(a2, i, a2, i + 1, a2.Length - i - 1);
        }

        array = a2;
        array[i] = value;
        return array;
    }

    private static int[] Add(int[] array, int value)
    {
        return Add(array, array.Length, value);
    }

    private static int Get(int[] array, int i)
    {
        return array[i];
    }

    private static int[] Remove(int[] array, int i)
    {
        int[] a2 = new int[array.Length - 1];
        Array.Copy(array, 0, a2, 0, i);
        if (i < a2.Length)
        {
            Array.Copy(array, i + 1, a2, i, array.Length - i - 1);
        }

        return a2;
    }
}
