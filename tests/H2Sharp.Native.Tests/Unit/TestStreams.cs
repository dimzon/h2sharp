// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestStreams.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): ByteArrayOutputStream/ByteArrayInputStream are MemoryStream, and
// FileUtils.newOutputStream/newInputStream are System.IO.File opens — the test's file is
// a temporary file of its own, and the LZF streams are the ported ones. A .NET stream
// answers 0 at the end where the JDK answers -1, so the loop breaks on 0.
using H2Sharp.Compression;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Tests the LZF stream.
/// </summary>
public sealed class TestStreams
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public void Test()
    {
        TestLZFStreams();
        TestLZFStreamClose();
    }

    private static byte[] GetRandomBytes(Random random)
    {
        int[] sizes = [0, 1, random.Next(1000), random.Next(100000),
            random.Next(1000000)];
        int size = sizes[random.Next(sizes.Length)];
        byte[] buffer = new byte[size];
        if (random.Next(5) == 1)
        {
            random.NextBytes(buffer);
        }
        else if (random.Next(2) == 0)
        {
            int patternLen = random.Next(100) + 1;
            for (int j = 0; j < size; j++)
            {
                buffer[j] = (byte)(j % patternLen);
            }
        }

        return buffer;
    }

    private static void TestLZFStreamClose()
    {
        string fileName = Path.GetTempFileName();
        try
        {
            using (Stream fo = File.OpenWrite(fileName))
            {
                LZFOutputStream outStream = new(fo);
                byte[] hello = "Hello"u8.ToArray();
                outStream.Write(hello, 0, hello.Length);
                outStream.Close();
            }

            using (Stream fi = File.OpenRead(fileName))
            {
                LZFInputStream inStream = new(fi);
                byte[] buff = new byte[100];
                Assert.Equal(5, inStream.Read(buff, 0, buff.Length));
                _ = inStream.ReadByte();
                inStream.Close();
            }
        }
        finally
        {
            File.Delete(fileName);
        }
    }

    private static void TestLZFStreams()
    {
        Random random = new(1);
        int max = 1000;
        for (int i = 0; i < max; i += 3)
        {
            byte[] buffer = GetRandomBytes(random);
            using MemoryStream memory = new();
            LZFOutputStream comp = new(memory);
            if (random.Next(10) == 1)
            {
                comp.Write(buffer, 0, buffer.Length);
            }
            else
            {
                for (int j = 0; j < buffer.Length;)
                {
                    int[] sizes = [0, 1, random.Next(100), random.Next(100000)];
                    int size = sizes[random.Next(sizes.Length)];
                    size = Math.Min(size, buffer.Length - j);
                    if (size == 1)
                    {
                        comp.WriteByte(buffer[j]);
                    }
                    else
                    {
                        comp.Write(buffer, j, size);
                    }

                    j += size;
                }
            }

            comp.Close();
            byte[] compressed = memory.ToArray();
            using MemoryStream input = new(compressed);
            LZFInputStream decompress = new(input);
            byte[] test = new byte[buffer.Length];
            for (int j = 0; j < buffer.Length;)
            {
                int[] sizes = [0, 1, random.Next(100), random.Next(100000)];
                int size = sizes[random.Next(sizes.Length)];
                if (size == 1)
                {
                    int x = decompress.ReadByte();
                    if (x < 0)
                    {
                        break;
                    }

                    test[j++] = (byte)x;
                }
                else
                {
                    size = Math.Min(size, test.Length - j);
                    int l = decompress.Read(test, j, size);
                    if (l <= 0)
                    {
                        break;
                    }

                    j += l;
                }
            }

            decompress.Close();
            Assert.Equal(buffer, test);
        }
    }
}
