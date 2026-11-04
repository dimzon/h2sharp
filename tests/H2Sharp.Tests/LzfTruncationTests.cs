/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What a truncated LZF stream answers, pinned.
 *
 * The header of a block is read byte by byte in the reference, and a stream that ends
 * mid-header hands -1 to the shifts for every byte it could not produce; the resulting value
 * then fails further down the read. The port reads the header in one call and reconstructs
 * exactly that value, missing slots included, and this table is what pins the equivalence:
 * every prefix length of a real compressed stream, and the outcome each one produced before
 * the read was batched. A change to the header read that answers a different int for any
 * prefix length turns the table red here rather than surfacing as a shifted failure in
 * production.
 */
using System;
using System.IO;
using System.Text;
using H2Sharp.Compression;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Pins the outcome of every truncation prefix of one compressed stream.
/// </summary>
public sealed class LzfTruncationTests
{
    private const string PayloadText = "hello lzf world hello lzf";

    /// <summary>
    /// The outcomes, one per prefix length from 0 to the full stream, in the form the probe
    /// recorded them: "Not an LZFInputStream" while the magic is incomplete, the empty read
    /// the end-of-stream quirk answers at the exact boundary, the "Unexpected end" the damaged
    /// headers die with, and the payload itself.
    /// </summary>
    private static readonly string[] Outcomes =
    [
        "IOException:Not an LZFInputStream",   // 0
        "IOException:Not an LZFInputStream",   // 1
        "IOException:Not an LZFInputStream",   // 2
        "IOException:Not an LZFInputStream",   // 3
        "OK:0:",                               // 4
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",
        "IOException:Unexpected end of LZF stream",   // 34
        "OK:25:68656C6C6F206C7A6620776F726C642068656C6C6F206C7A66",   // 35
    ];

    [Fact]
    public void TruncatedStreamsKeepTheirOutcome()
    {
        byte[] payload = Encoding.UTF8.GetBytes(PayloadText);
        MemoryStream compressed = new();
        using (LZFOutputStream @out = new(compressed))
        {
            @out.Write(payload, 0, payload.Length);
        }

        byte[] full = compressed.ToArray();
        Assert.Equal(Outcomes.Length, full.Length + 1);

        for (int len = 0; len <= full.Length; len++)
        {
            Assert.Equal(Outcomes[len], OutcomeOf(full, len));
        }
    }

    /// <summary>
    /// The end-of-stream quirk is the one place the truncation table is not an exception: a
    /// stream that ends exactly where a block header would begin reads as a clean end, which
    /// is how a reader learns that there is nothing more.
    /// </summary>
    private static string OutcomeOf(byte[] full, int len)
    {
        try
        {
            using LZFInputStream input = new(new MemoryStream(full, 0, len, writable: false));
            using MemoryStream copy = new();
            byte[] buffer = new byte[256];
            while (true)
            {
                int n = input.Read(buffer, 0, buffer.Length);
                if (n <= 0)
                {
                    break;
                }

                copy.Write(buffer, 0, n);
            }

            return "OK:" + copy.Length + ":" + Convert.ToHexString(copy.ToArray());
        }
        catch (Exception e)
        {
            return e.GetType().Name + ":" + e.Message;
        }
    }
}
