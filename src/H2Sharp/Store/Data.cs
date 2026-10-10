/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.Data.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The variable size number format code is a port from SQLite,
 * but stored in reverse order (least significant bits in the first byte).
 *
 * This is bytes on disk: the integers are big endian and the strings are the hand-rolled
 * UTF-8 in writeStringWithoutLength, which has no check for malformed input and must not
 * grow one, because a reader that accepted something the writer never wrote would make a
 * file that only it can read.
 *
 * Two transliteration notes. readByte is signed (sbyte), as every Java byte is, and the
 * places that read a byte to decide what it means depend on that: a negative byte is how
 * "there is more" is spelled. And the buffer doubles as Utils.copyBytes does, by
 * (length + plus) * 2 rather than length * 2, so the exact growth is kept — an expansion
 * that lands on a different size is not visible, but one that lands on a different *rule*
 * is a different allocation profile on the page-writing path.
 */
using System;
using System.IO;
using H2Sharp.Engine;
using H2Sharp.Util;

namespace H2Sharp.Store;

/// <summary>
/// This class represents a byte buffer that contains persistent data of a page.
/// </summary>
/// <remarks>
/// Author: Thomas Mueller, Noel Grandin, Nicolas Fortin, Atelier SIG, IRSTV FR CNRS 24888.
/// </remarks>
public class Data
{
    /// <summary>
    /// The data itself.
    /// </summary>
    private byte[] data;

    /// <summary>
    /// The current write or read position.
    /// </summary>
    private int pos;

    private Data(byte[] data)
    {
        this.data = data;
    }

    /// <summary>
    /// Write an integer at the current position.
    /// The current position is incremented.
    /// </summary>
    /// <param name="x">the value</param>
    public void WriteInt(int x)
    {
        Bits.IntToBigEndian(data, pos, x);
        pos += 4;
    }

    /// <summary>
    /// Read an integer at the current position.
    /// The current position is incremented.
    /// </summary>
    /// <returns>the value</returns>
    public int ReadInt()
    {
        int x = Bits.IntFromBigEndian(data, pos);
        pos += 4;
        return x;
    }

    private void WriteStringWithoutLength(char[] chars, int len)
    {
        int p = pos;
        byte[] buff = data;
        for (int i = 0; i < len; i++)
        {
            int c = chars[i];
            if (c < 0x80)
            {
                buff[p++] = (byte)c;
            }
            else if (c >= 0x800)
            {
                buff[p++] = (byte)(0xe0 | (c >> 12));
                buff[p++] = (byte)((c >> 6) & 0x3f);
                buff[p++] = (byte)(c & 0x3f);
            }
            else
            {
                buff[p++] = (byte)(0xc0 | (c >> 6));
                buff[p++] = (byte)(c & 0x3f);
            }
        }

        pos = p;
    }

    /// <summary>
    /// Create a new buffer.
    /// </summary>
    /// <param name="capacity">the initial capacity of the buffer</param>
    /// <returns>the buffer</returns>
    public static Data Create(int capacity)
    {
        return new Data(new byte[capacity]);
    }

    /// <summary>
    /// Get the current write position of this buffer, which is the current
    /// length.
    /// </summary>
    /// <returns>the length</returns>
    public int Length()
    {
        return pos;
    }

    /// <summary>
    /// Get the byte array used for this page.
    /// </summary>
    /// <returns>the byte array</returns>
    public byte[] GetBytes()
    {
        return data;
    }

    /// <summary>
    /// Set the position to 0.
    /// </summary>
    public void Reset()
    {
        pos = 0;
    }

    /// <summary>
    /// Append a number of bytes to this buffer.
    /// </summary>
    /// <param name="buff">the data</param>
    /// <param name="off">the offset in the data</param>
    /// <param name="len">the length in bytes</param>
    public void Write(byte[] buff, int off, int len)
    {
        Array.Copy(buff, off, data, pos, len);
        pos += len;
    }

    /// <summary>
    /// Copy a number of bytes to the given buffer from the current position. The
    /// current position is incremented accordingly.
    /// </summary>
    /// <param name="buff">the output buffer</param>
    /// <param name="off">the offset in the output buffer</param>
    /// <param name="len">the number of bytes to copy</param>
    public void Read(byte[] buff, int off, int len)
    {
        Array.Copy(data, pos, buff, off, len);
        pos += len;
    }

    /// <summary>
    /// Set the current read / write position.
    /// </summary>
    /// <param name="pos">the new position</param>
    public void SetPos(int pos)
    {
        this.pos = pos;
    }

    /// <summary>
    /// Read one single byte.
    /// </summary>
    /// <returns>the value</returns>
    public sbyte ReadByte()
    {
        return (sbyte)data[pos++];
    }

    /// <summary>
    /// Check if there is still enough capacity in the buffer.
    /// This method extends the buffer if required.
    /// </summary>
    /// <param name="plus">the number of additional bytes required</param>
    public void CheckCapacity(int plus)
    {
        if (pos + plus >= data.Length)
        {
            // a separate method to simplify inlining
            Expand(plus);
        }
    }

    private void Expand(int plus)
    {
        // must copy everything, because pos could be 0 and data may be
        // still required
        data = Utils.CopyBytes(data, (data.Length + plus) * 2);
    }

    /// <summary>
    /// Fill up the buffer with empty space and an (initially empty) checksum
    /// until the size is a multiple of Constants.FILE_BLOCK_SIZE.
    /// </summary>
    public void FillAligned()
    {
        // 0..6 > 8, 7..14 > 16, 15..22 > 24, ...
        int len = MathUtils.RoundUpInt(pos + 2, Constants.FileBlockSize);
        pos = len;
        if (data.Length < len)
        {
            CheckCapacity(len - data.Length);
        }
    }

    /// <summary>
    /// Copy a String from a reader to an output stream.
    /// </summary>
    /// <remarks>
    /// The loop stops on a non-positive read, where the reference stops on a negative one. A
    /// Java reader answers -1 at the end of its input and never 0 for a non-empty buffer, so
    /// the reference's test is enough there. .NET readers are not obliged to agree:
    /// StringReader answers 0 at the end of a string, which in the reference's loop writes
    /// nothing, resets, reads nothing, and runs until the process is killed. Stopping on 0 is
    /// the same answer for every reader that ends with -1, and terminates for the ones that
    /// end with 0.
    /// </remarks>
    /// <param name="source">the reader</param>
    /// <param name="target">the output stream</param>
    public static void CopyString(TextReader source, Stream target)
    {
        char[] buff = new char[Constants.IoBufferSize];
        Data d = new(new byte[3 * Constants.IoBufferSize]);
        while (true)
        {
            int l = source.Read(buff, 0, buff.Length);
            if (l <= 0)
            {
                break;
            }

            d.WriteStringWithoutLength(buff, l);
            target.Write(d.data, 0, d.pos);
            d.Reset();
        }
    }
}
