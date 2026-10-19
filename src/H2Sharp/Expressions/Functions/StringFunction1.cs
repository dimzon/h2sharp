/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.StringFunction1.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Globalization;
using System.Text;

using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A string function with one argument.
/// </summary>
public sealed class StringFunction1 : Function1
{
    // Fold functions

    /// <summary>
    /// UPPER().
    /// </summary>
    public const int Upper = 0;

    /// <summary>
    /// LOWER().
    /// </summary>
    public const int Lower = Upper + 1;

    // Various non-standard functions

    /// <summary>
    /// ASCII() (non-standard).
    /// </summary>
    public const int Ascii = Lower + 1;

    /// <summary>
    /// CHAR() (non-standard).
    /// </summary>
    public const int Char = Ascii + 1;

    /// <summary>
    /// STRINGENCODE() (non-standard).
    /// </summary>
    public const int Stringencode = Char + 1;

    /// <summary>
    /// STRINGDECODE() (non-standard).
    /// </summary>
    public const int Stringdecode = Stringencode + 1;

    /// <summary>
    /// STRINGTOUTF8() (non-standard).
    /// </summary>
    public const int Stringtoutf8 = Stringdecode + 1;

    /// <summary>
    /// UTF8TOSTRING() (non-standard).
    /// </summary>
    public const int Utf8tostring = Stringtoutf8 + 1;

    /// <summary>
    /// HEXTORAW() (non-standard).
    /// </summary>
    public const int Hextoraw = Utf8tostring + 1;

    /// <summary>
    /// RAWTOHEX() (non-standard).
    /// </summary>
    public const int Rawtohex = Hextoraw + 1;

    /// <summary>
    /// SPACE() (non-standard).
    /// </summary>
    public const int Space = Rawtohex + 1;

    /// <summary>
    /// QUOTE_IDENT() (non-standard).
    /// </summary>
    public const int QuoteIdent = Space + 1;

    private static readonly string[] Names =
    [
        "UPPER", "LOWER", "ASCII", "CHAR", "STRINGENCODE", "STRINGDECODE", "STRINGTOUTF8", "UTF8TOSTRING",
        "HEXTORAW", "RAWTOHEX", "SPACE", "QUOTE_IDENT",
    ];

    private readonly int function;

    /// <summary>
    /// Creates a string function with one argument.
    /// </summary>
    /// <param name="arg">the argument</param>
    /// <param name="function">the function code</param>
    public StringFunction1(Expression arg, int function)
        : base(arg)
    {
        this.function = function;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value v = arg.GetValue(session);
        if (v == ValueNull.Instance)
        {
            return ValueNull.Instance;
        }

        switch (function)
        {
            case Upper:
                // This is locale specific, as the reference's toUpperCase() is.
                v = ValueVarchar.Get(v.GetString()!.ToUpper(), session);
                break;
            case Lower:
                // This is locale specific, as the reference's toLowerCase() is.
                v = ValueVarchar.Get(v.GetString()!.ToLower(), session);
                break;
            case Ascii:
                {
                    string s = v.GetString()!;
                    v = s.Length == 0 ? ValueNull.Instance : ValueInteger.Get(s[0]);
                    break;
                }

            case Char:
                v = ValueVarchar.Get(((char)v.GetInt()).ToString(), session);
                break;
            case Stringencode:
                v = ValueVarchar.Get(StringUtils.JavaEncode(v.GetString()!), session);
                break;
            case Stringdecode:
                v = ValueVarchar.Get(StringUtils.JavaDecode(v.GetString()!), session);
                break;
            case Stringtoutf8:
                v = ValueVarbinary.GetNoCopy(JavaUtf8.GetBytes(v.GetString()!));
                break;
            case Utf8tostring:
                v = ValueVarchar.Get(JavaUtf8.GetString(v.GetBytesNoCopy()!), session);
                break;
            case Hextoraw:
                v = HexToRaw(v.GetString()!, session);
                break;
            case Rawtohex:
                v = ValueVarchar.Get(RawToHex(v, session.GetMode()), session);
                break;
            case Space:
                {
                    byte[] chars = new byte[Math.Max(0, v.GetInt())];
                    Array.Fill(chars, (byte)' ');
                    v = ValueVarchar.Get(Encoding.Latin1.GetString(chars), session);
                    break;
                }

            case QuoteIdent:
                v = ValueVarchar.Get(StringUtils.QuoteIdentifier(v.GetString()!), session);
                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        return v;
    }

    private static Value HexToRaw(string s, SessionLocal session)
    {
        if (session.GetMode().Enum == Mode.ModeEnum.Oracle)
        {
            return ValueVarbinary.Get(StringUtils.ConvertHexToBytes(s));
        }

        int len = s.Length;
        if (len % 4 != 0)
        {
            throw DbException.Get(ErrorCode.DataConversionError1, s);
        }

        StringBuilder builder = new(len / 4);
        for (int i = 0; i < len; i += 4)
        {
            try
            {
                _ = builder.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
            }
            catch (FormatException)
            {
                throw DbException.Get(ErrorCode.DataConversionError1, s);
            }
        }

        return ValueVarchar.Get(builder.ToString(), session);
    }

    private static string RawToHex(Value v, Mode mode)
    {
        if (DataType.IsBinaryStringOrSpecialBinaryType(v.GetValueType()))
        {
            return StringUtils.ConvertBytesToHex(v.GetBytesNoCopy()!);
        }

        string s = v.GetString()!;
        if (mode.Enum == Mode.ModeEnum.Oracle)
        {
            return StringUtils.ConvertBytesToHex(JavaUtf8.GetBytes(s));
        }

        int length = s.Length;
        StringBuilder buff = new(4 * length);
        for (int i = 0; i < length; i++)
        {
            string hex = (s[i] & 0xffff).ToString("x", CultureInfo.InvariantCulture);
            for (int j = hex.Length; j < 4; j++)
            {
                _ = buff.Append('0');
            }

            _ = buff.Append(hex);
        }

        return buff.ToString();
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        arg = arg.Optimize(session);
        switch (function)
        {
            // UPPER and LOWER may return string of different length for some characters.
            case Upper:
            case Lower:
            case Stringencode:
            case Space:
            case QuoteIdent:
                type = TypeInfo.TypeVarchar;
                break;
            case Ascii:
                type = TypeInfo.TypeInteger;
                break;
            case Char:
                type = TypeInfo.GetTypeInfo(Value.Varchar, 1L, 0, null);
                break;
            case Stringdecode:
                {
                    TypeInfo t = arg.GetTypeInfo();
                    type = DataType.IsCharacterStringType(t.GetValueType())
                        ? TypeInfo.GetTypeInfo(Value.Varchar, t.GetPrecision(), 0, null)
                        : TypeInfo.TypeVarchar;
                    break;
                }

            case Stringtoutf8:
                type = TypeInfo.TypeVarbinary;
                break;
            case Utf8tostring:
                {
                    TypeInfo t = arg.GetTypeInfo();
                    type = DataType.IsBinaryStringType(t.GetValueType())
                        ? TypeInfo.GetTypeInfo(Value.Varchar, t.GetPrecision(), 0, null)
                        : TypeInfo.TypeVarchar;
                    break;
                }

            case Hextoraw:
                {
                    TypeInfo t = arg.GetTypeInfo();
                    if (session.GetMode().Enum == Mode.ModeEnum.Oracle)
                    {
                        type = DataType.IsCharacterStringType(t.GetValueType())
                            ? TypeInfo.GetTypeInfo(Value.Varbinary, t.GetPrecision() / 2, 0, null)
                            : TypeInfo.TypeVarbinary;
                    }
                    else
                    {
                        type = DataType.IsCharacterStringType(t.GetValueType())
                            ? TypeInfo.GetTypeInfo(Value.Varchar, t.GetPrecision() / 4, 0, null)
                            : TypeInfo.TypeVarchar;
                    }

                    break;
                }

            case Rawtohex:
                {
                    TypeInfo t = arg.GetTypeInfo();
                    long precision = t.GetPrecision();
                    int mul = DataType.IsBinaryStringOrSpecialBinaryType(t.GetValueType()) ? 2
                        : session.GetMode().Enum == Mode.ModeEnum.Oracle ? 6 : 4;
                    type = TypeInfo.GetTypeInfo(
                        Value.Varchar,
                        precision <= long.MaxValue / mul ? precision * mul : long.MaxValue, 0, null);
                    break;
                }

            default:
                throw DbException.GetInternalError("function=" + function);
        }

        if (arg.IsConstant())
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        return this;
    }

    /// <inheritdoc/>
    public override string GetName() => Names[function];
}
