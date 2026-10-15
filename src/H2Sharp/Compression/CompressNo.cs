/*
 * Ported from org.h2.compress.CompressNo.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;

namespace H2Sharp.Compression;

/// <summary>
/// The algorithm that does in fact not compress, which is what a file says when the data is
/// already compressed, or encrypted, or random.
/// </summary>
public class CompressNo : Compressor
{
    /// <inheritdoc/>
    public int GetAlgorithm() => Compressors.No;

    /// <inheritdoc/>
    public void SetOptions(string options)
    {
        // Nothing to set.
    }

    /// <inheritdoc/>
    public int Compress(byte[] @in, int inPos, int inLen, byte[] @out, int outPos)
    {
        Array.Copy(@in, inPos, @out, outPos, inLen);
        return outPos + inLen;
    }

    /// <inheritdoc/>
    public void Expand(byte[] @in, int inPos, int inLen, byte[] @out, int outPos, int outLen)
        => Array.Copy(@in, inPos, @out, outPos, outLen);

    /// <inheritdoc/>
    public void Expand(ReadOnlySpan<byte> input, Span<byte> output)
        => input[..output.Length].CopyTo(output);
}
