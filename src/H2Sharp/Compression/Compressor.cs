/*
 * Ported from org.h2.compress.Compressor.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;

namespace H2Sharp.Compression;

/// <summary>
/// The numbers the store writes into a file to say which algorithm compressed it, and which
/// <see cref="Compressor"/> answers to which.
/// </summary>
/// <remarks>
/// These are the constants of the reference's <c>Compressor</c> interface, which in Java are fields
/// of the interface and in this port cannot be. The numbers are the file format and are not
/// negotiable: a file written by one side has to mean the same thing to the other.
/// </remarks>
public static class Compressors
{
    /// <summary>
    /// Nothing is compressed.
    /// </summary>
    public const int No = 0;

    /// <summary>
    /// LZF is used.
    /// </summary>
    public const int LZF = 1;

    /// <summary>
    /// DEFLATE is used.
    /// </summary>
    public const int Deflate = 2;
}

/// <summary>
/// What every compression algorithm of the store has to be able to do.
/// </summary>
/// <remarks>
/// The interface is the reference's, down to the numbers it writes into a file, because those
/// numbers are what a file written by one side has to mean to the other. What it is *not* given is
/// a name of its own: the algorithm tells which of the three it is, and the store reads that number
/// out of the file it opens.
/// </remarks>
public interface Compressor
{
    /// <summary>
    /// Which of the three algorithms this is.
    /// </summary>
    /// <returns>the algorithm</returns>
    int GetAlgorithm();

    /// <summary>
    /// Compresses a number of bytes.
    /// </summary>
    /// <param name="in">the input</param>
    /// <param name="inPos">where to start reading</param>
    /// <param name="inLen">how many bytes to read</param>
    /// <param name="out">where to write</param>
    /// <param name="outPos">where to start writing</param>
    /// <returns>where the writing ended</returns>
    int Compress(byte[] @in, int inPos, int inLen, byte[] @out, int outPos);

    /// <summary>
    /// Expands a number of compressed bytes.
    /// </summary>
    /// <param name="in">the compressed data</param>
    /// <param name="inPos">where to start reading</param>
    /// <param name="inLen">how many bytes to read</param>
    /// <param name="out">where to write</param>
    /// <param name="outPos">where to start writing</param>
    /// <param name="outLen">how many bytes the answer has</param>
    void Expand(byte[] @in, int inPos, int inLen, byte[] @out, int outPos, int outLen);

    /// <summary>
    /// Expands compressed bytes into a span.
    /// </summary>
    /// <remarks>
    /// The input is a view, not a copy: the caller hands the slice it already holds, and a
    /// deflate stream that cannot read a span reads it through a stream over this memory
    /// instead. The output is the answer at full length.
    /// </remarks>
    /// <param name="input">the compressed data</param>
    /// <param name="output">where the answer goes</param>
    void Expand(ReadOnlyMemory<byte> input, Span<byte> output);

    /// <summary>
    /// Sets the options of the algorithm, which may trade compression for speed.
    /// </summary>
    /// <param name="options">the options</param>
    void SetOptions(string options);
}
