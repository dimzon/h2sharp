/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the UUID value.
 *
 * A UUID is the easiest value in the port to get quietly wrong and the hardest to notice, because
 * everything about it is a convention: the text form has dashes the reference does not require and
 * ignores blanks it does not mention, the sixteen bytes are big-endian, and the two halves are
 * compared as unsigned, so a UUID whose high half has its top bit set has to sort after one that
 * does not. A convention that is read a little differently gives a value that is still a UUID and
 * still round trips, so nothing else would notice.
 *
 * The corpus is the texts a person may write: with dashes, without, in either case, with blanks,
 * too short, too long, and with a character that is not a hexadecimal digit at all. The halves go
 * through the oracle as the two numbers they are, so that the high half can have its top bit set.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class ValueUuidOracleTests
{
    /// <summary>
    /// The texts, where each one is either a UUID or a refusal.
    /// </summary>
    private static readonly string[] Texts =
    [
        "cd38d882-7ada-4589-b5fb-7da0ca559d9a",
        "CD38D882-7ADA-4589-B5FB-7DA0CA559D9A",
        "cd38d8827ada4589b5fb7da0ca559d9a",
        "CD38D8827ADA4589B5FB7DA0CA559D9A",
        "cd38d882-7ada-4589-b5fb-7da0ca559d9a ",
        "  cd38d882-7ada-4589-b5fb-7da0ca559d9a",
        "cd38d882 - 7ada - 4589 - b5fb - 7da0ca559d9a",
        "cd38d8827ada-4589b5fb-7da0ca559d9a",
        "00000000-0000-0000-0000-000000000000",
        "ffffffff-ffff-ffff-ffff-ffffffffffff",
        "ffffffffffffffffffffffffffffffff",
        "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF",
        "80000000-0000-0000-8000-000000000000",
        "7fffffff-ffff-ffff-7fff-ffffffffffff",
        "cd38d882-7ada-4589-b5fb-7da0ca559d9",
        "cd38d882-7ada-4589-b5fb-7da0ca559d9aa",
        "cd38d882-7ada-4589-b5fb-7da0ca559d9a-",
        "-cd38d882-7ada-4589-b5fb-7da0ca559d9a",
        "cd38d882-7ada-4589-b5fb-7da0ca559d9g",
        "cd38d882_7ada_4589_b5fb_7da0ca559d9a",
        "cd38d882-7ada-4589-b5fb-7da0ca559d9.5",
        "cd38d882-7ada-4589-b5fb-7da0ca559d9+5",
        "",
        " ",
        "-",
        "0",
        "0123456789abcdef0123456789abcde",
    ];

    /// <summary>
    /// The halves, where the top bit of each is set in turn, because that is where an unsigned
    /// comparison and a signed one disagree.
    /// </summary>
    private static readonly long[] Halves =
    [
        0L,
        1L,
        -1L,
        long.MinValue,
        long.MaxValue,
        unchecked((long)0x8000_0000_0000_0000UL),
        unchecked((long)0xFFFF_FFFF_FFFF_FFFFUL),
        0x0123_4567_89AB_CDEFL,
        unchecked((long)0xFEDC_BA98_7654_3210UL),
        0x0000_0000_0000_0001L,
    ];

    [JavaOracleFact]
    public void TheTextsMatchTheReference()
    {
        List<string> java = OracleRunner.Run(
                Texts,
                """
                for (int i = 0; i < inputs.length; i++) {
                  String s = inputs[i];
                  StringBuilder b = new StringBuilder();
                  try {
                    org.h2.value.ValueUuid v = org.h2.value.ValueUuid.get(s);
                    b.append(v.getString()).append('|');
                    b.append(v.getSQL(new StringBuilder(), 0)).append('|');
                    b.append(v.getValueType()).append(',');
                    b.append(v.getType()).append(',');
                    b.append(v.getMemory()).append(',');
                    b.append(v.charLength()).append(',');
                    b.append(v.octetLength()).append(',');
                    b.append(Long.toHexString(v.getHigh())).append(',');
                    b.append(Long.toHexString(v.getLow())).append(',');
                    b.append(org.h2.util.StringUtils.convertBytesToHex(v.getBytes())).append(',');
                    b.append(v.hashCode()).append(',');
                    b.append(v.getUuid().toString());
                  } catch (RuntimeException e) {
                    b.setLength(0);
                    b.append("REFUSED");
                  }
                  System.out.println(hex(b.toString()));
                }
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp = [.. Texts.Select(Describe)];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"\"{Texts[i]}\":\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void TheHalvesAndTheirOrderMatchTheReference()
    {
        // A half is 64 bits and the oracle's numbers are 32, so the halves travel as decimal text:
        // cut down to an int they would be different numbers on the two sides, which is exactly the
        // kind of quiet disagreement this test exists to catch.
        long[] pairs = [.. Halves.SelectMany(static (h, i) => new[] { h, Halves[(i + 1) % Halves.Length] })];
        List<string> pairInputs =
        [
            .. pairs.Select(static h => h.ToString(CultureInfo.InvariantCulture)),
        ];

        List<string> java = OracleRunner.Run(
                pairInputs,
                """
                for (int i = 0; i + 1 < inputs.length; i += 2) {
                  long high = Long.parseLong(inputs[i]);
                  long low = Long.parseLong(inputs[i + 1]);
                  org.h2.value.CompareMode off = org.h2.value.CompareMode.getInstance(null, 0);
                  org.h2.value.ValueUuid a = org.h2.value.ValueUuid.get(high, low);
                  org.h2.value.ValueUuid b = org.h2.value.ValueUuid.get(low, high);
                  StringBuilder s = new StringBuilder();
                  s.append(a.getString()).append('|');
                  s.append(a.hashCode()).append('|');
                  s.append(org.h2.util.StringUtils.convertBytesToHex(a.getBytes())).append('|');
                  s.append(Long.toHexString(a.getHigh())).append(',').append(Long.toHexString(a.getLow())).append('|');
                  s.append(a.compareTypeSafe(b, off, null)).append(',');
                  s.append(b.compareTypeSafe(a, off, null)).append(',');
                  s.append(a.equals(org.h2.value.ValueUuid.get(high, low))).append(',');
                  s.append(a.equals(b)).append(',');
                  s.append(a.equals(null)).append(',');
                  s.append(org.h2.value.ValueUuid.get(a.getBytes()).equals(a));
                  System.out.println(hex(s.toString()));
                }
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp = [];
        CompareMode off = CompareMode.GetInstance(null, 0);
        for (int i = 0; i + 1 < pairs.Length; i += 2)
        {
            long high = pairs[i];
            long low = pairs[i + 1];
            ValueUuid a = ValueUuid.Get(high, low);
            ValueUuid b = ValueUuid.Get(low, high);
            byte[] bytes = a.GetBytes() ?? [];
            csharp.Add(string.Join(
                '|',
                a.GetString(),
                a.GetHashCode(),
                Hex.BytesToHex(bytes),
                string.Join(',', LongHex(a.GetHigh()), LongHex(a.GetLow())),
                string.Join(
                    ',',
                    a.CompareTypeSafe(b, off, null),
                    b.CompareTypeSafe(a, off, null),
                    J(a.Equals(ValueUuid.Get(high, low))),
                    J(a.Equals(b)),
                    J(a.Equals(null)),
                    J(ValueUuid.Get(bytes).Equals(a)))));
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"pair {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void TheBytesAreRefusedWhenThereAreNotSixteen()
    {
        List<string> java = OracleRunner.Run(
                [],
                """
                for (int i = 0; i < nums.length; i++) {
                  byte[] bytes = new byte[nums[i]];
                  for (int j = 0; j < bytes.length; j++) {
                    bytes[j] = (byte) (j * 7 + 1);
                  }
                  StringBuilder b = new StringBuilder();
                  try {
                    b.append(org.h2.value.ValueUuid.get(bytes).getString());
                  } catch (RuntimeException e) {
                    b.setLength(0);
                    b.append("REFUSED");
                  }
                  System.out.println(hex(b.toString()));
                }
                """,
                numbers: [0, 1, 8, 15, 16, 17, 32],
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp = [];
        foreach (int length in new[] { 0, 1, 8, 15, 16, 17, 32 })
        {
            byte[] bytes = new byte[length];
            for (int j = 0; j < bytes.Length; j++)
            {
                bytes[j] = (byte)((j * 7) + 1);
            }

            csharp.Add(AsText(() => ValueUuid.Get(bytes).GetString()));
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"length {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// A new UUID cannot be compared with the reference, because it depends on the clock and on the
    /// random generator of the runtime. Its shape can be, and is: the version is written into the
    /// top half and the variant into the low half, and both are the point of the two constructors.
    /// </summary>
    [JavaOracleFact]
    public void TheRandomUuidHasTheVersionAndTheVariantOfItsVersion()
    {
        List<string> java = OracleRunner.Run(
                [],
                """
                for (int i = 0; i < nums.length; i++) {
                  try {
                    org.h2.value.ValueUuid v = org.h2.value.ValueUuid.getNewRandom(nums[i]);
                    System.out.println(hex(v.getString().length() + "|"
                        + (v.getHigh() >>> 12 & 0xf) + "|" + nums[i] + "|"
                        + (v.getLow() >>> 62) + "|"
                        + org.h2.util.StringUtils.convertBytesToHex(v.getBytes()).length()));
                  } catch (RuntimeException e) {
                    System.out.println(hex("REFUSED"));
                  }
                }
                """,
                numbers: [4, 7, 1, 5, 0, -1],
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp = [];
        foreach (int version in new[] { 4, 7, 1, 5, 0, -1 })
        {
            csharp.Add(AsText(() =>
            {
                ValueUuid v = ValueUuid.GetNewRandom(version);

                // The version nibble and the two variant bits are what the two constructors write,
                // and the rest of both halves is the clock and the random generator of the runtime,
                // so it is the only part of a new UUID that can be compared at all.
                return string.Join(
                    '|',
                    v.GetString().Length,
                    (v.GetHigh() >> 12) & 0xF,
                    version,
                    (ulong)v.GetLow() >> 62,
                    Hex.BytesToHex(v.GetBytes()).Length);
            }));
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"version {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static string Describe(string text)
    {
        try
        {
            ValueUuid v = ValueUuid.Get(text);
            return string.Join(
                '|',
                v.GetString(),
                v.GetSql(new System.Text.StringBuilder(), 0).ToString(),
                string.Join(
                    ',',
                    v.GetValueType(),
                    v.GetTypeInfo().GetSql(0),
                    v.GetMemory(),
                    v.CharLength(),
                    v.OctetLength(),
                    LongHex(v.GetHigh()),
                    LongHex(v.GetLow()),
                    Hex.BytesToHex(v.GetBytes()),
                    v.GetHashCode(),
                    v.GetUuid().ToString()));
        }
        catch (Exception e) when (e is ArgumentException or FormatException or H2SharpException)
        {
            return "REFUSED";
        }
    }

    /// <remarks>
    /// The halves as <c>Long.toHexString</c> writes them: unsigned, and without padding.
    /// </remarks>
    private static string LongHex(long value)
        => ((ulong)value).ToString("x", CultureInfo.InvariantCulture);

    private static string AsText(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is ArgumentException or FormatException or H2SharpException)
        {
            return "REFUSED";
        }
    }

    private static string J(bool value) => value ? "true" : "false";

    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        if (only.Length == 0 || !only.All(static c => char.IsAsciiHexDigit(c)))
        {
            return hex;
        }

        System.Text.StringBuilder b = new(only.Length / 4);
        for (int i = 0; i + 3 < only.Length; i += 4)
        {
            _ = b.Append((char)Convert.ToInt32(only.Substring(i, 4), 16));
        }

        return b.ToString();
    }
}
