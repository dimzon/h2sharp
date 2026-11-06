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
    public int Compress(ReadOnlySpan<byte> input, int inPos, int inLen, Span<byte> output, int outPos)
    {
        input.Slice(inPos, inLen).CopyTo(output.Slice(outPos));
        return outPos + inLen;
    }

    /// <inheritdoc/>
    public void Expand(ReadOnlyMemory<byte> input, Span<byte> output)
        => input.Span[..output.Length].CopyTo(output);
}
