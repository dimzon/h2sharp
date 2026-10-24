// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestStringCache.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the reference's main() entry is the runner; xUnit runs Test() directly. The
// benchmark (runBenchmark/testToUpperCache) is not part of the assertions and is not
// ported; the intern path of the reference uses String.intern, and the port's cache is
// StringUtils.Cache.
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Tests the string cache facility.
/// </summary>
public sealed class TestStringCache
{
    /// <summary>
    /// Flag to indicate the test should stop.
    /// </summary>
    private volatile bool stop;

    private readonly Random random = new(1);

    private readonly string?[] some = [null, "", "ABC",
        "this is a medium sized string", "1", "2"];

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public void Test()
    {
        TestToUpperToLower();
        StringUtils.ClearCache();
        TestSingleThread(20000);
        TestMultiThreads();
    }

    private static void TestToUpperToLower()
    {
        Random r = new();
        for (int i = 0; i < 1000; i++)
        {
            System.Text.StringBuilder buff = new();
            for (int a = 0; a < 100; a++)
            {
                _ = buff.Append((char)r.Next());
            }

            string text = buff.ToString();
            string b = StringUtils.ToUpperEnglish(text);
            string c = text.ToUpperInvariant();
            Assert.Equal(c, b);
            string d = StringUtils.ToLowerEnglish(text);
            string e = text.ToLowerInvariant();
            Assert.Equal(e, d);
        }
    }

    private string RandomString()
    {
        if (random.Next(2) == 0)
        {
            string? s = some[random.Next(some.Length)];
            if (s is not null && random.Next(2) == 0)
            {
                s = new string(s);
            }

            return s!;
        }

        int len = random.Next(2) == 0 ? random.Next(1000) : random.Next(10);
        System.Text.StringBuilder buff = new(len);
        for (int i = 0; i < len; i++)
        {
            _ = buff.Append(random.Next(0xfff));
        }

        return buff.ToString();
    }

    /// <summary>
    /// Test one string operation using the string cache.
    /// </summary>
    private void TestString()
    {
        string a = RandomString();
        string b = StringUtils.Cache(a);
        Assert.Equal(a, b);
    }

    private void TestSingleThread(int len)
    {
        for (int i = 0; i < len; i++)
        {
            TestString();
        }
    }

    private void TestMultiThreads()
    {
        int threadCount = 100;
        Thread[] threads = new Thread[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            threads[i] = new Thread(() =>
            {
                while (!stop)
                {
                    TestString();
                }
            });
            threads[i].Start();
        }

        Thread.Sleep(500);
        stop = true;
        foreach (Thread thread in threads)
        {
            thread.Join();
        }
    }
}
