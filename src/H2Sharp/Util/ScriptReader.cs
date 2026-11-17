/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.util.ScriptReader.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.IO;
using H2Sharp.Engine;
using H2Sharp.Message;

namespace H2Sharp.Util;

/// <summary>
/// Splits an SQL script into single statements, ignoring semicolons in comments and quotes.
/// </summary>
public sealed class ScriptReader : IDisposable
{
    private readonly TextReader reader;
    private char[] buffer;
    private int bufferPos;
    private int bufferStart = -1;
    private int bufferEnd;
    private bool endOfFile;
    private bool insideRemark;
    private bool blockRemark;
    private bool skipRemarks;
    private int remarkStart;

    /// <summary>
    /// Creates a script reader over the given text reader.
    /// </summary>
    /// <param name="reader">the reader</param>
    public ScriptReader(TextReader reader)
    {
        this.reader = reader;
        buffer = new char[Constants.IoBufferSize * 2];
    }

    /// <inheritdoc/>
    public void Dispose() => reader.Dispose();

    /// <summary>
    /// Reads one statement, or null at the end of the script.
    /// </summary>
    /// <returns>the SQL statement, or null</returns>
    public string? ReadStatement()
    {
        if (endOfFile)
        {
            return null;
        }

        return ReadStatementLoop();
    }

    private string? ReadStatementLoop()
    {
        bufferStart = bufferPos;
        int c = Read();
        while (true)
        {
            if (c < 0)
            {
                endOfFile = true;
                if (bufferPos - 1 == bufferStart)
                {
                    return null;
                }

                break;
            }

            if (c == ';')
            {
                break;
            }

            switch (c)
            {
                case '$':
                    {
                        c = Read();
                        if (c == '$' && (bufferPos - bufferStart < 3 || buffer[bufferPos - 3] <= ' '))
                        {
                            // dollar-quoted string
                            while (true)
                            {
                                c = Read();
                                if (c < 0)
                                {
                                    break;
                                }

                                if (c == '$')
                                {
                                    c = Read();
                                    if (c < 0)
                                    {
                                        break;
                                    }

                                    if (c == '$')
                                    {
                                        break;
                                    }
                                }
                            }

                            c = Read();
                        }

                        break;
                    }
                case '\'':
                    while (true)
                    {
                        c = Read();
                        if (c < 0)
                        {
                            break;
                        }

                        if (c == '\'')
                        {
                            break;
                        }
                    }

                    c = Read();
                    break;
                case '"':
                    while (true)
                    {
                        c = Read();
                        if (c < 0)
                        {
                            break;
                        }

                        if (c == '"')
                        {
                            break;
                        }
                    }

                    c = Read();
                    break;
                case '/':
                    {
                        c = Read();
                        if (c == '*')
                        {
                            // block comment, possibly nested
                            StartRemark(block: true);
                            int level = 1;
                            while (true)
                            {
                                c = Read();
                                if (c < 0)
                                {
                                    break;
                                }

                                if (c == '*')
                                {
                                    c = Read();
                                    if (c < 0)
                                    {
                                        ClearRemark();
                                        break;
                                    }

                                    if (c == '/' && --level == 0)
                                    {
                                        EndRemark();
                                        break;
                                    }
                                }
                                else if (c == '/')
                                {
                                    c = Read();
                                    if (c < 0)
                                    {
                                        ClearRemark();
                                        break;
                                    }

                                    if (c == '*')
                                    {
                                        level++;
                                    }
                                }
                            }

                            c = Read();
                        }
                        else if (c == '/')
                        {
                            // single line comment
                            ReadLineRemark();
                            c = Read();
                        }

                        break;
                    }
                case '-':
                    {
                        c = Read();
                        if (c == '-')
                        {
                            ReadLineRemark();
                            c = Read();
                        }

                        break;
                    }
                default:
                    c = Read();
                    break;
            }
        }

        return new string(buffer, bufferStart, bufferPos - 1 - bufferStart);
    }

    private void ReadLineRemark()
    {
        StartRemark(block: false);
        while (true)
        {
            int c = Read();
            if (c < 0)
            {
                ClearRemark();
                return;
            }

            if (c is '\r' or '\n')
            {
                EndRemark();
                return;
            }
        }
    }

    private void StartRemark(bool block)
    {
        blockRemark = block;
        remarkStart = bufferPos - 2;
        insideRemark = true;
    }

    private void EndRemark()
    {
        ClearRemark();
        insideRemark = false;
    }

    private void ClearRemark()
    {
        if (skipRemarks && remarkStart >= 0)
        {
            Array.Fill(buffer, ' ', remarkStart, Math.Min(bufferPos, buffer.Length) - remarkStart);
        }
    }

    private int Read()
    {
        if (bufferPos >= bufferEnd)
        {
            return ReadBuffer();
        }

        return buffer[bufferPos++];
    }

    private int ReadBuffer()
    {
        if (endOfFile)
        {
            return -1;
        }

        int keep = bufferPos - bufferStart;
        if (keep > 0)
        {
            char[] src = buffer;
            if (keep + Constants.IoBufferSize > src.Length)
            {
                if (src.Length >= int.MaxValue / 2)
                {
                    throw DbException.GetInternalError(
                        "Error in parsing script, statement size exceeds 1G");
                }

                buffer = new char[src.Length * 2];
            }

            Array.Copy(src, bufferStart, buffer, 0, keep);
        }

        remarkStart -= bufferStart;
        bufferStart = 0;
        bufferPos = keep;
        int len = reader.Read(buffer, keep, Constants.IoBufferSize);
        if (len == 0)
        {
            // Ensure bufferPos > bufferEnd so another read returns -1.
            bufferEnd = -1024;
            endOfFile = true;
            bufferPos++;
            return -1;
        }

        bufferEnd = keep + len;
        return buffer[bufferPos++];
    }

    /// <summary>Whether the last statement ended inside a comment.</summary>
    public bool IsInsideRemark() => insideRemark;

    /// <summary>Whether the open comment is a block comment.</summary>
    public bool IsBlockRemark() => blockRemark;

    /// <summary>Whether comments are replaced with spaces as they are read.</summary>
    public void SetSkipRemarks(bool skip) => skipRemarks = skip;
}
