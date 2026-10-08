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
