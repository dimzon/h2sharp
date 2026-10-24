// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestCompress.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): the reference's main() entry is the runner; xUnit runs Test() directly;
// Task/stop mechanics are .NET tasks with a cancellation source.
//
// Deferred (see MIGRATION_STATUS.md): the database performance pass (testPerformance,
// testDatabase) and the stream-wrapping + ByteBuffer halves of testByteArray/testByteBuffer
// rely on CompressTool.wrapOutputStream/wrapInputStream and the ByteBuffer surface, which
// the port does not have. The array-based compress/expand and variable-int halves port.
using H2Sharp.Tools;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Data compression tests.
/// </summary>
public sealed class TestCompress : TestDb
{
    private readonly byte[] buff = new byte[10];

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        // The performance database pass is out of scope, see the header.
        TestVariableSizeInt();
        TestMultiThreaded();
        Test_(0);
        Test_(1);
        Test_(7);
        Test_(50);
        Test_(200);
        Test_(4000000);
        TestVariableEnd();
    }

    private void TestVariableSizeInt()
    {
        Assert.Equal(1, CompressTool.GetVariableIntLength(0));
        Assert.Equal(2, CompressTool.GetVariableIntLength(0x80));
        Assert.Equal(3, CompressTool.GetVariableIntLength(0x4000));
        Assert.Equal(4, CompressTool.GetVariableIntLength(0x200000));
        Assert.Equal(5, CompressTool.GetVariableIntLength(0x10000000));
        Assert.Equal(5, CompressTool.GetVariableIntLength(-1));
        for (int x = 0; x < 0x20000; x++)
        {
            TestVar(x);
            TestVar(int.MinValue + x);
            TestVar(int.MaxValue - x);
            TestVar(0x200000 + x - 100);
            TestVar(0x10000000 + x - 100);
        }
    }

    private void TestVar(int x)
    {
        int len = CompressTool.GetVariableIntLength(x);
        int l2 = CompressTool.WriteVariableInt(buff, 0, x);
        Assert.Equal(len, l2);
        int x2 = CompressTool.ReadVariableInt(buff, 0);
        Assert.Equal(x2, x);
    }

    private void TestMultiThreaded()
    {
        using CancellationTokenSource stopped = new();
        Task[] tasks = new Task[3];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                CompressTool tool = CompressTool.GetInstance();
                byte[] b = new byte[1024];
                Random r = new();
                while (!stopped.IsCancellationRequested)
                {
                    r.NextBytes(b);
                    byte[] test = tool.Expand(tool.Compress(b, "LZF"));
                    Assert.Equal(b, test);
                }
            }, stopped.Token);
        }

        Thread.Sleep(1000);
        stopped.Cancel();
        Task.WaitAll(tasks);
    }

    private void TestVariableEnd()
    {
        CompressTool utils = CompressTool.GetInstance();
        System.Text.StringBuilder b = new();
        for (int i = 0; i < 90; i++)
        {
            _ = b.Append('0');
        }

        string prefix = b.ToString();
        for (int i = 0; i < 100; i++)
        {
            b = new System.Text.StringBuilder(prefix);
            for (int j = 0; j < i; j++)
            {
                _ = b.Append((char)('1' + j));
            }

            string test = b.ToString();
            byte[] input = System.Text.Encoding.UTF8.GetBytes(test);
            Assert.Equal(input, utils.Expand(utils.Compress(input, "LZF")));
        }
    }

    private void Test_(int len)
    {
        TestByteArray(len);
        // testByteBuffer: deferred, see the header.
    }

    private void TestByteArray(int len)
    {
        Random r = new(len);
        for (int pattern = 0; pattern < 4; pattern++)
        {
            byte[] b = new byte[len];
            switch (pattern)
            {
                case 0:
                    // leave empty
                    break;
                case 1:
                    r.NextBytes(b);
                    break;
                case 2:
                    for (int x = 0; x < len; x++)
                    {
                        b[x] = (byte)(x & 10);
                    }

                    break;
                case 3:
                    for (int x = 0; x < len; x++)
                    {
                        b[x] = (byte)(x / 10);
                    }

                    break;
            }

            if (r.Next(2) < 1)
            {
                for (int x = 0; x < len; x++)
                {
                    if (r.Next(20) < 1)
                    {
                        b[x] = (byte)r.Next(255);
                    }
                }
            }

            CompressTool utils = CompressTool.GetInstance();
            // level 9 is highest, strategy 2 is huffman only
            foreach (string a in new[] { "LZF", "No",
                "Deflate", "Deflate level 9 strategy 2" })
            {
                byte[] output = utils.Compress(b, a);
                byte[] test = utils.Expand(output);
                Assert.Equal(b.Length, test.Length);
                Assert.Equal(b, test);
                Array.Fill(test, (byte)0);
                CompressTool.Expand(output, test, 0);
                Assert.Equal(b, test);
            }

            // The stream-wrapping loop of the reference relies on
            // CompressTool.wrapOutputStream/wrapInputStream, which the port does not have;
            // see the header.
        }
    }
}
