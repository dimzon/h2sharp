/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.Token.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The kind of a token, its payload, and the factories that make one.
 *
 * Structural deviation from the reference (approved by the user, audit item 1.6): the
 * reference has one class per kind of token, and a tokenizer allocates one per lexeme -
 * forty to eighty small objects per parsed statement. This is one mutable struct per token
 * instead: the payload fields overlay each other because a token is only ever one kind, and
 * the whole statement's token list becomes one array. The member surface is what the
 * reference's classes offered, so the tokenizer and the parser read a token exactly as
 * before; the kind tests that were type tests are the Is... properties below.
 */
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Command;

/// <summary>
/// A token of a parsed statement.
/// </summary>
/// <remarks>
/// Mutable on purpose: the tokenizer numbers parameters in place, converts the <c>\u</c>
/// escapes in place, and the literal value is built once and kept. A token held in a local
/// variable mutates that variable; a token held in a list must be read into one, changed,
/// and written back — which is what the few mutating call sites do.
/// </remarks>
public partial struct Token
{
    private enum Kind : byte
    {
        Keyword,
        Identifier,
        KeywordOrIdentifier,
        EndOfInput,
        Parameter,
        CharacterString,
        BinaryString,
        Integer,
        Bigint,
        Value,
    }

    private Kind kind;

    private int start;

    /// <summary>
    /// The keyword constant of a keyword or a keyword-or-identifier token.
    /// </summary>
    private int type;

    /// <summary>
    /// The text of an identifier, the content of a character string, or the spelling of a
    /// keyword that is also allowed as a name.
    /// </summary>
    private string? text;

    private bool quoted;

    private bool unicode;

    private int index;

    private long number;

    private byte[]? bytes;

    private Value? value;

    private Token(int start)
    {
        this.start = start;
    }

    /// <summary>
    /// Where the token begins in the statement being parsed.
    /// </summary>
    internal int Start
    {
        get => start;
        set => start = value;
    }

    /// <summary>
    /// Moves the token back, for a statement that was read with comments or spaces in front of it
    /// that have been stripped.
    /// </summary>
    internal void SubtractFromStart(int offset) => start -= offset;

    /// <summary>
    /// The kind of this token, one of the numbers in Token.cs.
    /// </summary>
    internal int TokenType => kind switch
    {
        Kind.Keyword or Kind.KeywordOrIdentifier => type,
        Kind.Identifier => ParserUtil.Identifier,
        Kind.EndOfInput => EndOfInput,
        Kind.Parameter => Parameter,
        _ => Literal,
    };

    /// <summary>
    /// The token written as an identifier, or null if it is not one: a literal has no name.
    /// </summary>
    internal string? AsIdentifier => kind switch
    {
        Kind.Identifier or Kind.KeywordOrIdentifier => text,
        Kind.Keyword => Tokens[type],
        Kind.Parameter => "?",
        _ => null,
    };

    /// <summary>
    /// Whether the identifier was written between double quotes, which makes it a name that is
    /// never a keyword.
    /// </summary>
    internal bool IsQuoted => quoted;

    /// <summary>
    /// The content of a character string token, which is the text a <c>\u</c> escape applies to.
    /// </summary>
    internal string? Text => text;

    /// <summary>
    /// Whether the token is a character string, one of the two string literals.
    /// </summary>
    internal bool IsCharacterString => kind == Kind.CharacterString;

    /// <summary>
    /// Whether the token is a string literal, character or binary.
    /// </summary>
    internal bool IsStringLiteral => kind is Kind.CharacterString or Kind.BinaryString;

    /// <summary>
    /// The value of a literal token, or null if this token has none.
    /// </summary>
    /// <remarks>
    /// The value is built once and kept. A statement is parsed more than once, once to find out
    /// what it is and once to plan it, and a literal that was scanned as a string is the same
    /// string both times.
    /// </remarks>
    internal Value? GetValue(CastDataProvider? provider)
    {
        if (kind == Kind.Value)
        {
            return value;
        }

        value ??= kind switch
        {
            Kind.CharacterString => ValueVarchar.Get(text!, provider),
            Kind.BinaryString => ValueVarbinary.GetNoCopy(bytes!),
            Kind.Integer => ValueInteger.Get((int)number),
            Kind.Bigint => ValueBigint.Get(number),
            _ => null,
        };
        return value;
    }

    /// <summary>
    /// Whether the token still holds <c>\u</c> escapes, which are converted only where the
    /// statement is parsed.
    /// </summary>
    internal bool NeedsUnicodeConversion => unicode;

    /// <summary>
    /// Replaces the <c>\u</c> escapes of an identifier or a string with the characters they
    /// stand for.
    /// </summary>
    internal void ConvertUnicode(int uescape)
    {
        if (!unicode)
        {
            throw DbException.GetInternalError();
        }

        text = StringUtils.DecodeUnicodeStringSql(text!, uescape);
        unicode = false;
    }

    /// <summary>
    /// A copy that shares the text and the value of this token, for a parser that reads the same
    /// statement twice.
    /// </summary>
    /// <remarks>
    /// The struct copy is the clone; the method stays so that the call sites read what they
    /// read in the reference, where a token is an object that has to be copied.
    /// </remarks>
    public Token Clone() => this;

    /// <summary>
    /// Which parameter this is, counting from one. The number zero is written without a
    /// number, because the first question mark of a statement needs nothing to say.
    /// </summary>
    internal int Index
    {
        get => index;
        set => index = value;
    }

    /// <inheritdoc/>
    public override string ToString() => kind switch
    {
        Kind.Identifier => quoted ? StringUtils.QuoteIdentifier(text!) : text!,
        Kind.Keyword => Tokens[type]!,
        Kind.KeywordOrIdentifier => text!,
        Kind.Parameter => index == 0 ? "?" : "?" + index,
        Kind.CharacterString or Kind.BinaryString or Kind.Integer or Kind.Bigint or Kind.Value
            => GetValue(null)!.GetTraceSql(),
        _ => string.Empty,
    };

    /// <summary>
    /// An identifier, either bare or quoted.
    /// </summary>
    internal static Token IdentifierToken(int start, string identifier, bool quoted, bool unicode)
        => new(start) { kind = Kind.Identifier, text = identifier, quoted = quoted, unicode = unicode };

    /// <summary>
    /// A keyword, which is its own name.
    /// </summary>
    internal static Token KeywordToken(int start, int type)
        => new(start) { kind = Kind.Keyword, type = type };

    /// <summary>
    /// A keyword that is also allowed as a name, which keeps the spelling it was written with,
    /// because the case of a name is a part of the name.
    /// </summary>
    internal static Token KeywordOrIdentifierToken(int start, int type, string identifier)
        => new(start) { kind = Kind.KeywordOrIdentifier, type = type, text = identifier };

    /// <summary>
    /// The end of the statement.
    /// </summary>
    internal static Token EndOfInputToken(int start) => new(start) { kind = Kind.EndOfInput };

    /// <summary>
    /// A question mark standing for a value that is not written in the statement.
    /// </summary>
    internal static Token ParameterToken(int start, int index)
        => new(start) { kind = Kind.Parameter, index = index };

    /// <summary>
    /// A string written between single quotes.
    /// </summary>
    internal static Token CharacterStringToken(int start, string @string, bool unicode)
        => new(start) { kind = Kind.CharacterString, text = @string, unicode = unicode };

    /// <summary>
    /// Bytes written between single quotes, which are a binary string rather than a character one.
    /// </summary>
    internal static Token BinaryStringToken(int start, byte[] bytes)
        => new(start) { kind = Kind.BinaryString, bytes = bytes };

    /// <summary>
    /// A whole number that fits in an int.
    /// </summary>
    internal static Token IntegerToken(int start, int number)
        => new(start) { kind = Kind.Integer, number = number };

    /// <summary>
    /// A whole number that does not fit in an int.
    /// </summary>
    internal static Token BigintToken(int start, long number)
        => new(start) { kind = Kind.Bigint, number = number };

    /// <summary>
    /// A value of a type that the tokenizer does not scan itself, such as a date.
    /// </summary>
    internal static Token ValueToken(int start, Value value)
        => new(start) { kind = Kind.Value, value = value };
}
