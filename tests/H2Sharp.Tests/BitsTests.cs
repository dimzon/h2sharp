/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new tests for H2Sharp).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential test: org.h2.util.Bits.uuidToBytes must produce the same 16 bytes
 * as System.Guid's canonical text order. The expected values are computed by the
 * reference JVM at test time; the hard-coded cases are the ones recorded in
 * docs/notes/uuid-byte-order.md.
 */
using System;
using System.Text;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class BitsTests
{
    // Expected byte layouts verified against the reference JVM (JDK 25); see
    // docs/notes/uuid-byte-order.md.
    public static TheoryData<string, string> KnownUuids => new()
    {
        { "00000000-0000-0000-0000-000000000001", "00000000000000000000000000000001" },
        { "ffffffff-ffff-ffff-ffff-ffffffffffff", "ffffffffffffffffffffffffffffffff" },
        { "123e4567-e89b-12d3-a456-426614174000", "123e4567e89b12d3a456426614174000" },
        { "80000000-0000-0000-0000-000000000000", "80000000000000000000000000000000" },
        { "00000000-0001-0000-8000-000000000000", "00000000000100008000000000000000" },
    };

    [Theory]
    [MemberData(nameof(KnownUuids))]
    public void UuidToBytes_MatchesJavaBigEndianLayout(string uuidText, string expectedHex)
    {
        Guid uuid = Guid.Parse(uuidText);
        Assert.Equal(expectedHex, ToHex(Bits.UuidToBytes(uuid)));
    }

    [Theory]
    [MemberData(nameof(KnownUuids))]
    public void BytesToUuid_RoundTrips(string uuidText, string expectedHex)
    {
        Guid uuid = Guid.Parse(uuidText);
        byte[] bytes = Bits.UuidToBytes(uuid);

        Assert.Equal(uuid, Bits.BytesToUuid(bytes));
        Assert.Equal(expectedHex, ToHex(bytes));
    }

    [Fact]
    public void UuidToBytes_FromMsbLsb_MatchesUuidOverload()
    {
        foreach (string uuidText in new[]
        {
            "00000000-0000-0000-0000-000000000001",
            "ffffffff-ffff-ffff-ffff-ffffffffffff",
            "123e4567-e89b-12d3-a456-426614174000",
        })
        {
            Guid uuid = Guid.Parse(uuidText);
            Assert.Equal(
                Bits.UuidToBytes(uuid),
                Bits.UuidToBytes(Bits.Msb(uuid), Bits.Lsb(uuid)));
        }
    }

    [Fact]
    public void MsbAndLsb_ReturnSignedValuesLikeJava()
    {
        // Java: getMostSignificantBits() is signed; 0x8000... has the high bit set.
        Assert.Equal(long.MinValue, Bits.Msb(Guid.Parse("80000000-0000-0000-0000-000000000000")));
        Assert.Equal(-1L, Bits.Msb(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")));
        Assert.Equal(-1L, Bits.Lsb(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")));

        Assert.Equal(0L, Bits.Msb(Guid.Parse("00000000-0000-0000-0000-000000000001")));
        Assert.Equal(1L, Bits.Lsb(Guid.Parse("00000000-0000-0000-0000-000000000001")));
    }

    [Fact]
    public void BytesToUuid_RejectsWrongLength()
        => Assert.Throws<ArgumentException>(
            () => Bits.BytesToUuid(new byte[15]));

    // Integer.reverse / Long.reverse: the values the JDK answers, plus the self-inverse
    // property both references answer, over values that walk every mask-ladder stride.
    [Theory]
    [InlineData(1, int.MinValue)]
    [InlineData(2, 0x40000000)]
    [InlineData(0x0000F000, 0x000F0000)]
    [InlineData(0, 0)]
    [InlineData(-1, -1)]
    public void Reverse_Int_MatchesJavaIntegerReverse(int value, int expected)
        => Assert.Equal(expected, Bits.Reverse(value));

    [Theory]
    [InlineData(1L, long.MinValue)]
    [InlineData(-1L, -1L)]
    [InlineData(0L, 0L)]
    public void Reverse_Long_MatchesJavaLongReverse(long value, long expected)
        => Assert.Equal(expected, Bits.Reverse(value));

    [Theory]
    [InlineData(0x01234567)]
    [InlineData(unchecked((int)0x89ABCDEF))]
    [InlineData(0x7FFFFFFF)]
    [InlineData(0x55555555)]
    [InlineData(-1863261734)] // 0x9180010A, one value per stride
    public void Reverse_Int_IsSelfInverse(int value)
        => Assert.Equal(value, Bits.Reverse(Bits.Reverse(value)));

    [Theory]
    [InlineData(0x0123456789ABCDEFL)]
    [InlineData(0x5555555555555555L)]
    public void Reverse_Long_IsSelfInverse(long value)
        => Assert.Equal(value, Bits.Reverse(Bits.Reverse(value)));

    private static string ToHex(byte[] bytes)
    {
        StringBuilder builder = new(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            builder.Append(b.ToString("x2"));
        }

        return builder.ToString();
    }
}
