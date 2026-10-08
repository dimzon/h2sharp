/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the JAVA_OBJECT type and the conversion into it.
 *
 * A JAVA_OBJECT is a byte string that refuses to be anything else, and the refusal is the
 * interesting part: there is no text rendering of one, so every way of asking it for its text
 * has to fail rather than invent a representation. The corpus therefore spends most of its rows
 * on refusals and on the one conversion that does work, which is from the other byte-string
 * types.
 *
 * The empty value is compared too, because it is the one case where the type has a singleton and
 * the other byte-string types do not.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class ValueJavaObjectOracleTests
{
    private static readonly byte[][] Payloads =
    [
        [],
        [0x00],
        [0x01, 0x02],
        [0x41, 0x42, 0x43],
        [0x7F, 0x80, 0xFF],
    ];

    [JavaOracleFact]
    public void JavaObjectValuesMatchTheReference()
    {
        string body = """
                        for (int i = 0; i < payloads.length; i++) {
                          byte[] bytes = payloads[i];
                          org.h2.value.ValueJavaObject v =
                              org.h2.value.ValueJavaObject.getNoCopy(bytes.clone());
                          StringBuilder b = new StringBuilder();
                          b.append(v.getValueType()).append('|');
                          b.append(v.getType().getSQL(0)).append('|');
                          b.append(v.getBytesNoCopy().length).append('|');
                          b.append(hex.apply(v.getBytesNoCopy())).append('|');
                          b.append(v.getSQL(0)).append('|');
                          b.append(v.getSQL(org.h2.util.HasSQL.NO_CASTS)).append('|');
                          b.append(v.equals(v)).append('|');
                          b.append(v.equals(org.h2.value.ValueJavaObject.getNoCopy(
                              bytes.clone()))).append('|');
                          b.append(v.compareTo(
                              org.h2.value.ValueJavaObject.getNoCopy(bytes.clone()), null, off));
                          try {
                            b.append('|').append(v.getString());
                          } catch (RuntimeException e) {
                            b.append('|').append("THROWS").append(
                                (e instanceof org.h2.message.DbException
                                    ? ((org.h2.message.DbException)e).getErrorCode() : 0));
                          }
                          System.out.println(b.toString());
                        }
              """;

        List<string> java = OracleRunner.Run(
                [],
                Declare + body,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .ToList();

        List<string> csharp = [.. Payloads.Select(Describe)];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [Fact]
    public void AJavaObjectIsBytesAndRefusesToBeText()
    {
        ValueJavaObject value = ValueJavaObject.Get([0x41, 0x42, 0x43]);

        Assert.Equal(Value.JavaObject, value.GetValueType());
        Assert.Equal(new byte[] { 0x41, 0x42, 0x43 }, value.GetBytes());
        Assert.Equal("X'414243'", value.GetSql(new StringBuilder(), HasSql.NoCasts).ToString());

        // There is no text of an object, and inventing one would produce a string nothing can
        // read back, so the answer is the conversion error the reference raises.
        H2SharpException error = Assert.Throws<H2SharpException>(() => value.GetString());
        Assert.Equal(ErrorCode.DataConversionError1, error.H2ErrorCode);

        // The empty object is a value like any other, and two empty ones are the same value.
        Assert.Equal(ValueJavaObject.GetNoCopy([]), ValueJavaObject.GetNoCopy([]));
    }

    private static string Describe(byte[] payload)
    {
        ValueJavaObject v = ValueJavaObject.GetNoCopy((byte[])payload.Clone());
        ValueJavaObject same = ValueJavaObject.GetNoCopy((byte[])payload.Clone());
        StringBuilder line = new();
        _ = line.Append(v.GetValueType()).Append('|')
            .Append(v.GetTypeInfo().GetSql(new StringBuilder(), 0)).Append('|')
            .Append(v.GetBytesNoCopy()!.Length).Append('|')
            .Append(Hex(v.GetBytesNoCopy()!)).Append('|')
            .Append(v.GetSql(new StringBuilder(), 0)).Append('|')
            .Append(v.GetSql(new StringBuilder(), HasSql.NoCasts)).Append('|')
            .Append(Bool(v.Equals(v))).Append('|')
            .Append(Bool(v.Equals(same))).Append('|')
            .Append(v.CompareTypeSafe(same, CompareMode.GetInstance(null, 0), null)).Append('|');
        try
        {
            _ = line.Append('|').Append(v.GetString());
        }
        catch (H2SharpException e)
        {
            // No leading separator here: the one the try wrote is already in the line, because
            // the append of the text is what threw.
            _ = line.Append("THROWS").Append(e.H2ErrorCode);
        }

        return line.ToString();
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Hex(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "-";
        }

        StringBuilder line = new(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            _ = line.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return line.ToString();
    }

    private const string Declare = """
                        org.h2.value.CompareMode off = org.h2.value.CompareMode.getInstance(null, 0);
                        java.util.function.Function<byte[], String> hex = bytes -> {
                          StringBuilder b = new StringBuilder();
                          for (int i = 0; i < bytes.length; i++) {
                            b.append(String.format("%02x", bytes[i] & 0xff));
                          }
                          return b.length() == 0 ? "-" : b.toString();
                        };
                        byte[][] payloads = new byte[][] {
                            {},
                            {0x00},
                            {0x01, 0x02},
                            {0x41, 0x42, 0x43},
                            {0x7F, (byte)0x80, (byte)0xFF}
                        };
              """;
}
