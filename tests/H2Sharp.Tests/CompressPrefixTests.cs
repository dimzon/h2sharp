/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A tight output buffer yields a prefix of the full stream, as the reference's bounded
 * deflate does — never a truncated body with a trailer that claims the whole input. And the
 * full stream inflates on the JVM, which is what "a file written on one side opens on the
 * other" means.
 */
using System.Collections.Generic;
using H2Sharp.Compression;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class CompressPrefixTests
{
    private static byte[] PatternInput()
    {
        byte[] input = new byte[200];
        for (int i = 0; i < input.Length; i++)
        {
            input[i] = (byte)((i * 37 + (i / 50)) % 251);
        }

        return input;
    }

    [Fact]
    public void ATightBufferYieldsAPrefixOfTheFullStream()
    {
        byte[] input = PatternInput();

        CompressDeflate compressor = new();
        byte[] big = new byte[1000];
        int endFull = compressor.Compress(input, 0, input.Length, big, 0);
        Assert.True(endFull > 10);

        byte[] small = new byte[10];
        int endTight = compressor.Compress(input, 0, input.Length, small, 0);
        Assert.Equal(small.Length, endTight);
        Assert.Equal(big[..endTight], small);
    }

    private const string JavaInflate = """
        String h = inputs[0];
        byte[] data = new byte[h.length() / 2];
        for (int i = 0; i < data.length; i++) {
            data[i] = (byte) Integer.parseInt(h.substring(i * 2, i * 2 + 2), 16);
        }
        java.util.zip.Inflater inf = new java.util.zip.Inflater();
        inf.setInput(data);
        byte[] out = new byte[200];
        int n = inf.inflate(out);
        inf.end();
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < n; i++) {
            b.append(String.format("%02x", out[i]));
        }
        System.out.println("=" + b.toString());
        """;

    [JavaOracleFact]
    public void TheFullStreamInflatesOnTheJvm()
    {
        byte[] input = PatternInput();

        CompressDeflate compressor = new();
        byte[] output = new byte[1000];
        int end = compressor.Compress(input, 0, input.Length, output, 0);

        List<string> java = OracleRunner.Run([Hex.BytesToHex(output[..end])], JavaInflate, null, null, null);
        Assert.Equal(["=" + Hex.BytesToHex(input)], java);
    }
}
