using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.MvStore;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The write buffer and the free space map against the reference.
/// </summary>
/// <remarks>
/// Both are small and both decide where bytes land, so both are compared at the byte level and after
/// every operation that can move a position around.
/// <para>
/// The free space map is worth a second look because it has no natural answer. The tail past the last
/// used block is free but is not stored anywhere, so it has to be found by asking for the bit after
/// the last set one — and a bit set that treats "past the end" as occupied would answer a perfectly
/// good question with the end of the file. The corpus therefore interleaves allocate, mark and free
/// and prints the map after each step.
/// </para>
/// <para>
/// The bit set is <c>java.util.BitSet</c>, which .NET does not have. Its
/// <c>nextClearBit</c> is defined over the whole number line with everything past the end counting as
/// clear, and that definition is what the allocation walk relies on.
/// </para>
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class WriteBufferOracleTests
{
    [Fact]
    public void WriteBufferAgreesWithTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                WriteBufferProgram(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en"],
                packageName: "org.h2.mvstore")
            .ToList();

        List<string> csharp = [.. WriteBufferLines()];

        Compare(java, csharp);
    }

    [Fact]
    public void FreeSpaceBitSetAgreesWithTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                FreeSpaceProgram(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en"],
                packageName: "org.h2.mvstore")
            .ToList();

        List<string> csharp = [.. FreeSpaceLines()];

        Compare(java, csharp);
    }

    /// <summary>The reference program for the write buffer, in its own package.</summary>
    private static string WriteBufferProgram() =>
        """
        {
        class R {
        static String hex(org.h2.mvstore.WriteBuffer b, int from, int to) {
          byte[] all = new byte[to - from];
          b.position(from);
          b.get(all);
          b.position(to);
          return org.h2.util.StringUtils.convertBytesToHex(all);
        }
        }

        org.h2.mvstore.WriteBuffer b = new org.h2.mvstore.WriteBuffer(8);
        System.out.println("cap|" + b.capacity() + "|" + b.position() + "|" + b.limit());
        b.put((byte) 0xab);
        System.out.println("byte|" + b.position() + "|" + R.hex(b, 0, b.position()));
        b.putChar('é');
        System.out.println("char|" + b.position() + "|" + R.hex(b, 0, b.position()));
        b.putShort((short) -2);
        System.out.println("short|" + b.position() + "|" + R.hex(b, 0, b.position()));
        b.putInt(-123456);
        System.out.println("int|" + b.position() + "|" + R.hex(b, 0, b.position()));
        b.putLong(-1234567890123L);
        System.out.println("long|" + b.position() + "|" + R.hex(b, 0, b.position()));
        b.putFloat(1.5f);
        System.out.println("float|" + b.position() + "|" + R.hex(b, 0, b.position()));
        b.putDouble(-2.25d);
        System.out.println("double|" + b.position() + "|" + R.hex(b, 0, b.position()));
        b.putVarInt(300);
        System.out.println("varint|" + b.position() + "|" + R.hex(b, 0, b.position()));
        b.putVarLong(1099511627776L);
        System.out.println("varlong|" + b.position() + "|" + R.hex(b, 0, b.position()));
        b.putStringData("héllo", 5);
        System.out.println("string|" + b.position() + "|" + R.hex(b, 0, b.position()));
        byte[] more = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        b.put(more, 2, 5);
        System.out.println("slice|" + b.position() + "|" + R.hex(b, 0, b.position()));
        b.putInt(0, 0x11223344);
        b.putShort(4, (short) 0x5566);
        System.out.println("patched|" + R.hex(b, 0, b.position()));

        // Grow well past the initial size, which is what the buffer exists for.
        b.put(new byte[100], 0, 100);
        System.out.println("grown|" + b.position() + "|" + (b.capacity() >= 108));

        byte[] dst = new byte[10];
        b.position(b.position() - 10);
        b.get(dst);
        System.out.println("get|" + org.h2.util.StringUtils.convertBytesToHex(dst) + "|" + b.position());

        b.limit(200);
        System.out.println("limit|" + b.limit() + "|" + (b.capacity() >= 200));
        b.clear();
        System.out.println("cleared|" + b.position() + "|" + b.limit() + "|" + (b.capacity() >= 200));

        org.h2.mvstore.WriteBuffer small = new org.h2.mvstore.WriteBuffer();
        System.out.println("default|" + small.capacity());
        small.putInt(7);
        System.out.println("defint|" + small.position() + "|" + R.hex(small, 0, small.position()));
        }
        """;

    /// <summary>The reference program for the free space map, in its own package.</summary>
    private static string FreeSpaceProgram() =>
        """
        {
        org.h2.mvstore.FreeSpaceBitSet s = new org.h2.mvstore.FreeSpaceBitSet(4, 16);
        System.out.println("new|" + s + "|" + s.getFirstFree() + "|" + s.getLastFree()
            + "|" + s.getAfterLastBlock() + "|" + s.getFillRate());

        long p = s.allocate(48);
        System.out.println("a48|" + p + "|" + s + "|" + s.getAfterLastBlock() + "|" + s.getFillRate());
        System.out.println("used|" + s.isUsed(64, 32) + "|" + s.isUsed(0, 16) + "|" + s.isFree(64, 32));

        s.markUsed(256, 32);
        System.out.println("mark|" + s + "|" + s.getAfterLastBlock() + "|" + s.getFillRate());
        s.free(256, 32);
        System.out.println("free|" + s + "|" + s.getAfterLastBlock() + "|" + s.getFillRate());

        // Blocks 4 to 6 are taken by the allocation above, so the fragmentation marks
        // go past them: marking an allocated block is a double mark, refused on both sides.
        s.markUsed(256, 16);
        s.markUsed(288, 16);
        s.markUsed(320, 16);
        System.out.println("frag|" + s + "|" + s.getAfterLastBlock());
        System.out.println("predict|" + s.predictAllocation(2, 0, -1) + "|" + s.isFragmented());
        System.out.println("predict2|" + s.predictAllocation(8, 0, -1) + "|" + s.isFragmented());
        System.out.println("alloc2|" + s.allocate(32) + "|" + s);
        System.out.println("allocbig|" + s.allocate(256) + "|" + s);
        System.out.println("reserved|" + s.allocate(32, 100, 120) + "|" + s);
        System.out.println("moveprio|" + s.getMovePriority(70) + "|" + s.getMovePriority(10) + "|"
            + s.getMovePriority(1000));
        System.out.println("beforeclear|" + s.getFillRate());
        s.clear();
        System.out.println("afterclear|" + s + "|" + s.getFirstFree() + "|" + s.getLastFree());

        // A double mark is refused, and that refusal is part of the contract.
        s.markUsed(64, 16);
        try {
          s.markUsed(64, 16);
          System.out.println("doublemark|no");
        } catch (RuntimeException e) {
          System.out.println("doublemark|THROWS");
        }

        org.h2.mvstore.FreeSpaceBitSet t = new org.h2.mvstore.FreeSpaceBitSet(0, 8);
        System.out.println("empty|" + t + "|" + t.getFirstFree() + "|" + t.getLastFree() + "|" + t.getFillRate());
        System.out.println("alloc1|" + t.allocate(8) + "|" + t);
        System.out.println("alloc2|" + t.allocate(8) + "|" + t);
        System.out.println("alloc3|" + t.allocate(8) + "|" + t);
        }
        """;

    private static IEnumerable<string> WriteBufferLines()
    {
        WriteBuffer b = new(8);
        yield return "cap|" + b.Capacity() + "|" + b.Position() + "|" + b.Limit();

        b.Put(0xab);
        yield return "byte|" + b.Position() + "|" + HexOf(b, 0, b.Position());

        b.PutChar('é');
        yield return "char|" + b.Position() + "|" + HexOf(b, 0, b.Position());

        b.PutShort(-2);
        yield return "short|" + b.Position() + "|" + HexOf(b, 0, b.Position());

        b.PutInt(-123456);
        yield return "int|" + b.Position() + "|" + HexOf(b, 0, b.Position());

        b.PutLong(-1234567890123L);
        yield return "long|" + b.Position() + "|" + HexOf(b, 0, b.Position());

        b.PutFloat(1.5f);
        yield return "float|" + b.Position() + "|" + HexOf(b, 0, b.Position());

        b.PutDouble(-2.25d);
        yield return "double|" + b.Position() + "|" + HexOf(b, 0, b.Position());

        b.PutVarInt(300);
        yield return "varint|" + b.Position() + "|" + HexOf(b, 0, b.Position());

        b.PutVarLong(1099511627776L);
        yield return "varlong|" + b.Position() + "|" + HexOf(b, 0, b.Position());

        b.PutStringData("héllo", 5);
        yield return "string|" + b.Position() + "|" + HexOf(b, 0, b.Position());

        byte[] more = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        b.Put(more, 2, 5);
        int atSlice = b.Position();
        yield return "slice|" + atSlice.ToString(CultureInfo.InvariantCulture) + "|" + HexOf(b, 0, atSlice);

        b.PutInt(0, 0x11223344);
        b.PutShort(4, (short)0x5566);
        yield return "patched|" + HexOf(b, 0, b.Position());

        b.Put(new byte[100], 0, 100);
        yield return "grown|" + b.Position() + "|" + J(b.Capacity() >= 108);

        byte[] dst = new byte[10];
        b.Position(b.Position() - 10);
        b.Get(dst);
        yield return "get|" + Hex.BytesToHex(dst) + "|" + b.Position();

        b.Limit(200);
        yield return "limit|" + b.Limit() + "|" + J(b.Capacity() >= 200);
        b.Clear();
        yield return "cleared|" + b.Position() + "|" + b.Limit() + "|" + J(b.Capacity() >= 200);

        WriteBuffer small = new();
        yield return "default|" + small.Capacity();
        small.PutInt(7);
        yield return "defint|" + small.Position() + "|" + HexOf(small, 0, small.Position());
    }

    private static IEnumerable<string> FreeSpaceLines()
    {
        FreeSpaceBitSet s = new(4, 16);
        yield return string.Join(
            "|", "new", s, L(s.GetFirstFree()), L(s.GetLastFree()),
            s.GetAfterLastBlock(), s.GetFillRate());

        long p = s.Allocate(48);
        yield return string.Join("|", "a48", L(p), s, s.GetAfterLastBlock(), s.GetFillRate());
        yield return string.Join("|", "used", J(s.IsUsed(64, 32)), J(s.IsUsed(0, 16)), J(s.IsFree(64, 32)));

        s.MarkUsed(256, 32);
        yield return string.Join("|", "mark", s, s.GetAfterLastBlock(), s.GetFillRate());
        s.Free(256, 32);
        yield return string.Join("|", "free", s, s.GetAfterLastBlock(), s.GetFillRate());

        s.MarkUsed(256, 16);
        s.MarkUsed(288, 16);
        s.MarkUsed(320, 16);
        yield return string.Join("|", "frag", s, s.GetAfterLastBlock());
        yield return string.Join("|", "predict", L(s.PredictAllocation(2, 0, -1)), J(s.IsFragmented()));
        yield return string.Join("|", "predict2", L(s.PredictAllocation(8, 0, -1)), J(s.IsFragmented()));
        yield return string.Join("|", "alloc2", L(s.Allocate(32)), s);
        yield return string.Join("|", "allocbig", L(s.Allocate(256)), s);
        yield return string.Join("|", "reserved", L(s.Allocate(32, 100, 120)), s);
        yield return string.Join(
            "|", "moveprio", s.GetMovePriority(70), s.GetMovePriority(10), s.GetMovePriority(1000));
        yield return "beforeclear|" + s.GetFillRate();
        s.Clear();
        yield return string.Join("|", "afterclear", s, L(s.GetFirstFree()), L(s.GetLastFree()));

        s.MarkUsed(64, 16);
        yield return "doublemark|" + Try(() => { s.MarkUsed(64, 16); return "no"; }, "THROWS");

        FreeSpaceBitSet t = new(0, 8);
        yield return string.Join("|", "empty", t, L(t.GetFirstFree()), L(t.GetLastFree()), t.GetFillRate());
        yield return string.Join("|", "alloc1", L(t.Allocate(8)), t);
        yield return string.Join("|", "alloc2", L(t.Allocate(8)), t);
        yield return string.Join("|", "alloc3", L(t.Allocate(8)), t);
    }

    private static string HexOf(WriteBuffer b, int from, int to)
    {
        byte[] all = new byte[to - from];
        b.Position(from);
        b.Get(all);
        b.Position(to);
        return Hex.BytesToHex(all);
    }

    private static string Try(Func<string> body, string onThrow)
    {
        try
        {
            return body();
        }
        catch (Exception)
        {
            return onThrow;
        }
    }

    private static string J(bool value) => value ? "true" : "false";

    private static string L(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Compare(List<string> java, List<string> csharp)
    {
        if (java.Count != csharp.Count)
        {
            int common = Math.Min(java.Count, csharp.Count);
            int first = -1;
            for (int i = 0; i < common; i++)
            {
                if (java[i] != csharp[i])
                {
                    first = i;
                    break;
                }
            }

            Assert.Fail(
                "строк: java=" + java.Count.ToString(CultureInfo.InvariantCulture)
                + " csharp=" + csharp.Count.ToString(CultureInfo.InvariantCulture)
                + "\n  первое различие: " + first.ToString(CultureInfo.InvariantCulture)
                + "\n    java:   " + (first >= 0 ? java[first] : "-")
                + "\n    csharp: " + (first >= 0 ? csharp[first] : "-")
                + "\n  последняя java:   " + (java.Count > 0 ? java[^1] : "-")
                + "\n  последняя csharp: " + (csharp.Count > 0 ? csharp[^1] : "-"));
        }

        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }
}
