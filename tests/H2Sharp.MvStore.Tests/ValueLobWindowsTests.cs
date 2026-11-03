/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A LOB whose bytes live in a temporary file, addressed directly: the windows a range read
 * asks for, their refusals, the length-based hash of a big value and the comparison between
 * values held in different places.
 */
using System;
using System.IO;
using System.Text;
using H2Sharp;
using H2Sharp.Values;
using H2Sharp.Values.Lobs;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class ValueLobWindowsTests
{
    private static string WriteTemp(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), "h2sharp-lob-" + Guid.NewGuid().ToString("N") + ".lob");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        return path;
    }

    private static ValueClob ClobOf(string content, long charLength)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        return new ValueClob(new LobDataFile(WriteTemp(content), bytes.Length), bytes.Length, charLength);
    }

    [Fact]
    public void AWindowOfAFileClobAnswersTheTextThere()
    {
        ValueClob value = ClobOf("0123456789", 10);

        Assert.Equal("0123456789", value.GetString());
        Assert.Equal(10, value.OctetLength());
        Assert.Equal("123", value.GetReader(2, 3).ReadToEnd());
        Assert.Equal("0", value.GetReader(1, 1).ReadToEnd());
        Assert.Equal("0123456789", value.GetReader(1, 10).ReadToEnd());
    }

    [Fact]
    public void AWindowOutsideTheValueIsRefused()
    {
        ValueClob value = ClobOf("0123456789", 10);

        Assert.Throws<H2SharpException>(() => value.GetReader(0, 1));
        Assert.Throws<H2SharpException>(() => value.GetReader(5, -1));
        Assert.Throws<H2SharpException>(() => value.GetReader(2, 20));
    }

    [Fact]
    public void AWindowOfUnknownLengthChecksOnlyTheArguments()
    {
        // A CLOB read from a file whose character length was not counted: only the offset and
        // the length themselves are checked.
        ValueClob value = ClobOf("0123456789", -1);

        Assert.Throws<H2SharpException>(() => value.GetReader(0, 1));
        Assert.Throws<H2SharpException>(() => value.GetReader(1, -1));
        Assert.Equal("123", value.GetReader(2, 3).ReadToEnd());
        Assert.Equal("0123456789", value.GetReader(1, 10).ReadToEnd());
    }

    [Fact]
    public void AWindowOfFileBytesAnswersTheBytesThere()
    {
        ValueClob value = ClobOf("0123456789", 10);

        Assert.Equal("123", Encoding.UTF8.GetString(ReadAll(value.GetInputStream(2, 3))));
        Assert.Throws<H2SharpException>(() => value.GetInputStream(0, 1));
        Assert.Throws<H2SharpException>(() => value.GetInputStream(2, -1));
        Assert.Equal(10, value.GetBytes()!.Length);
        Assert.Equal(10, value.GetBytesNoCopy()!.Length);

        // The byte length that was not counted is counted when the bytes are asked for.
        string text = "0123456789";
        byte[] textBytes = Encoding.UTF8.GetBytes(text);
        ValueClob uncounted = new(new LobDataFile(WriteTemp(text), textBytes.Length), -1, -1);
        Assert.Equal(10, uncounted.GetBytes()!.Length);
    }

    [Fact]
    public void ATempFileGoesAwayWithTheValue()
    {
        string path = WriteTemp("temp");
        ValueClob value = new(new LobDataFile(path, 4), 4, 4);
        Assert.True(File.Exists(path));

        value.Remove();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ABigValueHashesByItsLengthAndComparesByItsText()
    {
        string one = new('a', 5000);
        string two = new('b', 5000);
        ValueClob first = ClobOf(one, 5000);
        ValueClob same = ClobOf(one, 5000);
        ValueClob different = ClobOf(two, 5000);

        // Past the hashing limit the hash is the length, so two different big values of one
        // length share it — the comparison settles them.
        Assert.Equal(first.GetHashCode(), different.GetHashCode());
        Assert.Equal(first, same);
        Assert.NotEqual(first, different);
        Assert.NotNull(first);

        // A big value and a small one holding the same text are one value.
        Assert.Equal(ClobOf("small", 5), ValueClob.CreateSmall("small"));

        // Two BLOBs held in different places compare by their bytes.
        byte[] bytes = Encoding.UTF8.GetBytes("bytes");
        ValueBlob fileBlob = new(new LobDataFile(WriteTemp("bytes"), bytes.Length), bytes.Length);
        ValueBlob memoryBlob = ValueBlob.CreateSmall(bytes);
        Assert.Equal(fileBlob, memoryBlob);
        Assert.Equal(0, fileBlob.CompareTypeSafe(memoryBlob, CompareMode.GetInstance(null, 0), null));
    }

    private static byte[] ReadAll(Stream stream)
    {
        using (stream)
        using (MemoryStream copy = new())
        {
            stream.CopyTo(copy);
            return copy.ToArray();
        }
    }
}
