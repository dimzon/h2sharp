/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.util.json.JSONTextSource.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: the reference returns an int code point from nextCharAfterWhitespace and uses -1 for
 * the end. A char cannot hold that, so the end is int.MinValue here, which no character can be.
 */
using System;
using System.Text;

namespace H2Sharp.Util.Json;

/// <summary>
/// JSON text source: the parsing loop, with the reading of individual characters left to a
/// subclass.
/// </summary>
/// <remarks>
/// The loop is where JSON's grammar actually lives. A subclass only has to say how to read a
/// character, a keyword and a number, and what happens between two of them follows from here —
/// which is why the source and the target are separate: the same grammar serves a document being
/// read from text, from bytes, or written out.
/// </remarks>
public abstract class JsonTextSource
{
    /// <summary>
    /// The value that stands for the end of the input. It is not a character code, so it cannot
    /// collide with one.
    /// </summary>
    protected const int EndOfInput = int.MinValue;

    /// <summary>
    /// The output.
    /// </summary>
    protected readonly JsonTarget Target;

    private readonly StringBuilder builder;

    /// <summary>
    /// Creates a source.
    /// </summary>
    /// <param name="target">where the parsed value goes</param>
    protected JsonTextSource(JsonTarget target)
    {
        Target = target;
        builder = new StringBuilder();
    }

    /// <summary>
    /// Parse the text and write it to the output.
    /// </summary>
    protected void Parse()
    {
        bool comma = false;
        for (int ch; (ch = NextCharAfterWhitespace()) != EndOfInput;)
        {
            if (ch == '}' || ch == ']')
            {
                // A separator followed by the end of the container is a trailing comma.
                if (comma)
                {
                    throw new ArgumentException();
                }

                if (ch == '}')
                {
                    Target.EndObject();
                }
                else
                {
                    Target.EndArray();
                }

                continue;
            }

            if (ch == ',')
            {
                // Two separators in a row, or a separator where no value came before it.
                if (comma || !Target.IsValueSeparatorExpected())
                {
                    throw new ArgumentException();
                }

                comma = true;
                continue;
            }

            // A value must follow a separator and must not appear without one.
            if (comma != Target.IsValueSeparatorExpected())
            {
                throw new ArgumentException();
            }

            comma = false;
            switch (ch)
            {
                case 'f':
                    ReadKeyword1("false");
                    Target.ValueFalse();
                    break;
                case 'n':
                    ReadKeyword1("null");
                    Target.ValueNull();
                    break;
                case 't':
                    ReadKeyword1("true");
                    Target.ValueTrue();
                    break;
                case '{':
                    Target.StartObject();
                    break;
                case '[':
                    Target.StartArray();
                    break;
                case '"':
                    {
                        string s = ReadString();

                        // Whether this was a name or a value is a question for the target: only it
                        // knows whether it is inside an object and waiting for a name.
                        if (Target.IsPropertyExpected())
                        {
                            if (NextCharAfterWhitespace() != ':')
                            {
                                throw new ArgumentException();
                            }

                            Target.Member(s);
                        }
                        else
                        {
                            Target.ValueString(s);
                        }

                        break;
                    }

                case '-':
                    ParseNumber(false);
                    break;
                case '0':
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    ParseNumber(true);
                    break;
                default:
                    throw new ArgumentException();
            }
        }
    }

    /// <summary>
    /// Skip all whitespace characters, and get the next character.
    /// </summary>
    /// <returns>the character code, or <see cref="EndOfInput"/></returns>
    protected abstract int NextCharAfterWhitespace();

    /// <summary>
    /// Read the specified keyword, or (if there is no match), throw.
    /// </summary>
    /// <param name="keyword">the expected keyword, with its first character already read</param>
    protected abstract void ReadKeyword1(string keyword);

    /// <summary>
    /// Parse a number, the first character of which has been read.
    /// </summary>
    /// <param name="positive">whether it needs to be positive</param>
    protected abstract void ParseNumber(bool positive);

    /// <summary>
    /// Read the next character, which may be any of them and is not allowed to run out.
    /// </summary>
    /// <returns>the character code</returns>
    protected abstract int NextChar();

    /// <summary>
    /// Read 4 hex characters (0-9, a-f, A-F), and return the Unicode character.
    /// </summary>
    /// <returns>the character</returns>
    protected abstract char ReadHex();

    private string ReadString()
    {
        builder.Clear();
        bool inSurrogate = false;
        for (; ; )
        {
            int ch = NextChar();
            switch (ch)
            {
                case '"':
                    // A pair that never got its second half.
                    if (inSurrogate)
                    {
                        throw new ArgumentException();
                    }

                    return builder.ToString();
                case '\\':
                    ch = NextChar();
                    switch (ch)
                    {
                        case '"':
                        case '/':
                        case '\\':
                            AppendNonSurrogate((char)ch, inSurrogate);
                            break;
                        case 'b':
                            AppendNonSurrogate('\b', inSurrogate);
                            break;
                        case 'f':
                            AppendNonSurrogate('\f', inSurrogate);
                            break;
                        case 'n':
                            AppendNonSurrogate('\n', inSurrogate);
                            break;
                        case 'r':
                            AppendNonSurrogate('\r', inSurrogate);
                            break;
                        case 't':
                            AppendNonSurrogate('\t', inSurrogate);
                            break;
                        case 'u':
                            inSurrogate = AppendChar(ReadHex(), inSurrogate);
                            break;
                        default:
                            throw new ArgumentException();
                    }

                    break;
                default:
                    if (ch <= 0xffff)
                    {
                        inSurrogate = AppendChar((char)ch, inSurrogate);
                    }
                    else
                    {
                        // A code point outside the basic plane arrived as one value, and the only
                        // way to have one is a matching pair, so nothing can be pending.
                        if (inSurrogate)
                        {
                            throw new ArgumentException();
                        }

                        _ = builder.Append(char.ConvertFromUtf32(ch));
                        inSurrogate = false;
                    }

                    break;
            }
        }
    }

    private void AppendNonSurrogate(char ch, bool inSurrogate)
    {
        if (inSurrogate)
        {
            throw new ArgumentException();
        }

        _ = builder.Append(ch);
    }

    /// <remarks>
    /// A pair has to be matched in the right order and without anything in between: a high
    /// surrogate must be followed by a low one, and a low one must not appear on its own. That
    /// covers both the <c>\u</c> escapes and the characters that were already in the input.
    /// </remarks>
    private bool AppendChar(char ch, bool inSurrogate)
    {
        if (inSurrogate != char.IsLowSurrogate(ch))
        {
            throw new ArgumentException();
        }

        if (inSurrogate)
        {
            inSurrogate = false;
        }
        else if (char.IsHighSurrogate(ch))
        {
            inSurrogate = true;
        }

        _ = builder.Append(ch);
        return inSurrogate;
    }
}
