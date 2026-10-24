// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestUtils.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the reference's main() entry is the runner; xUnit runs Test() directly.
//
// Deferred (see MIGRATION_STATUS.md): testGetNonPrimitiveClass and testReflectionUtils
// exercise the reflective Java interop (java.lang.System.nanoTime, newInstance of Java
// classes, primitive-to-wrapper class mapping); the port explicitly has no equivalent
// entry points (see Utils' header), so those two halves are not ported here.
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Tests reflection utilities.
/// </summary>
public sealed class TestUtils
{
    /// <summary>
    /// Dummy field.
    /// </summary>
    public readonly string TestField = "abc";

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public void Test()
    {
        TestIOUtils();
        TestSortTopN();
        TestSortTopNRandom();
        // testGetNonPrimitiveClass, testReflectionUtils: deferred, see the header.
        TestParseBoolean();
    }

    private static void TestIOUtils()
    {
        for (int i = 0; i < 20; i++)
        {
            byte[] data = new byte[i];
            using MemoryStream input = new(data);
            byte[] buffer = new byte[i];
            Assert.Equal(0, IOUtils.ReadFully(input, buffer, -2));
            Assert.Equal(0, IOUtils.ReadFully(input, buffer, -1));
            Assert.Equal(0, IOUtils.ReadFully(input, buffer, 0));
            for (int j = 1, off = 0; ; j += 1)
            {
                int read = Math.Max(0, Math.Min(i - off, j));
                int l = IOUtils.ReadFully(input, buffer, j);
                Assert.Equal(read, l);
                off += l;
                if (l == 0)
                {
                    break;
                }
            }

            Assert.Equal(0, IOUtils.ReadFully(input, buffer, 1));
        }

        for (int i = 0; i < 10; i++)
        {
            char[] data = new char[i];
            using StringReader input = new(new string(data));
            char[] buffer = new char[i];
            Assert.Equal(0, IOUtils.ReadFully(input, buffer, -2));
            Assert.Equal(0, IOUtils.ReadFully(input, buffer, -1));
            Assert.Equal(0, IOUtils.ReadFully(input, buffer, 0));
            for (int j = 1, off = 0; ; j += 1)
            {
                int read = Math.Max(0, Math.Min(i - off, j));
                int l = IOUtils.ReadFully(input, buffer, j);
                Assert.Equal(read, l);
                off += l;
                if (l == 0)
                {
                    break;
                }
            }

            Assert.Equal(0, IOUtils.ReadFully(input, buffer, 1));
        }
    }

    private static void TestSortTopN()
    {
        Comparison<int> comp = (a, b) => a.CompareTo(b);
        int[] arr = [];
        Utils.SortTopN(arr, 0, 0, comp);

        arr = [1];
        Utils.SortTopN(arr, 0, 1, comp);

        arr = [3, 5, 1, 4, 2];
        Utils.SortTopN(arr, 0, 2, comp);
        Assert.Equal(1, arr[0]);
        Assert.Equal(2, arr[1]);
    }

    private static void TestSortTopNRandom()
    {
        Random rnd = new();
        Comparison<int> comp = (a, b) => a.CompareTo(b);
        for (int z = 0; z < 10000; z++)
        {
            int length = 1 + rnd.Next(500);
            int[] arr = new int[length];
            for (int i = 0; i < length; i++)
            {
                arr[i] = rnd.Next(50);
            }

            int[] arr2 = (int[])arr.Clone();
            int offset = rnd.Next(length);
            int limit = rnd.Next(length - offset + 1);
            Utils.SortTopN(arr, offset, offset + limit, comp);
            Array.Sort(arr2, comp);
            for (int i = offset, end = offset + limit; i < end; i++)
            {
                if (arr[i] != arr2[i])
                {
                    throw new Xunit.Sdk.XunitException(offset + " " + end + "\n"
                        + string.Join(",", arr) + "\n" + string.Join(",", arr2));
                }
            }
        }
    }

    private static void TestParseBooleanCheckFalse(string value)
    {
        Assert.False(Utils.ParseBoolean(value, false, false));
        Assert.False(Utils.ParseBoolean(value, false, true));
        Assert.False(Utils.ParseBoolean(value, true, false));
        Assert.False(Utils.ParseBoolean(value, true, true));
    }

    private static void TestParseBooleanCheckTrue(string value)
    {
        Assert.True(Utils.ParseBoolean(value, false, false));
        Assert.True(Utils.ParseBoolean(value, false, true));
        Assert.True(Utils.ParseBoolean(value, true, false));
        Assert.True(Utils.ParseBoolean(value, true, true));
    }

    private static void TestParseBoolean()
    {
        // Test for default value in case of null
        Assert.False(Utils.ParseBoolean(null, false, false));
        Assert.False(Utils.ParseBoolean(null, false, true));
        Assert.True(Utils.ParseBoolean(null, true, false));
        Assert.True(Utils.ParseBoolean(null, true, true));
        // Test assorted valid strings
        TestParseBooleanCheckFalse("0");
        TestParseBooleanCheckFalse("f");
        TestParseBooleanCheckFalse("F");
        TestParseBooleanCheckFalse("n");
        TestParseBooleanCheckFalse("N");
        TestParseBooleanCheckFalse("no");
        TestParseBooleanCheckFalse("No");
        TestParseBooleanCheckFalse("NO");
        TestParseBooleanCheckFalse("false");
        TestParseBooleanCheckFalse("False");
        TestParseBooleanCheckFalse("FALSE");
        TestParseBooleanCheckTrue("1");
        TestParseBooleanCheckTrue("t");
        TestParseBooleanCheckTrue("T");
        TestParseBooleanCheckTrue("y");
        TestParseBooleanCheckTrue("Y");
        TestParseBooleanCheckTrue("yes");
        TestParseBooleanCheckTrue("Yes");
        TestParseBooleanCheckTrue("YES");
        TestParseBooleanCheckTrue("true");
        TestParseBooleanCheckTrue("True");
        TestParseBooleanCheckTrue("TRUE");
        // Test other values
        Assert.False(Utils.ParseBoolean("BAD", false, false));
        Assert.True(Utils.ParseBoolean("BAD", true, false));
        Assert.Throws<ArgumentException>(() => Utils.ParseBoolean("BAD", false, true));
        Assert.Throws<ArgumentException>(() => Utils.ParseBoolean("BAD", true, true));
    }
}
