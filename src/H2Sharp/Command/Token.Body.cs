/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.Token.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
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
/// The numbers of the kinds of token and their text are generated, in Token.cs; the kinds
/// themselves are here. A token is one of an identifier, a keyword, a parameter or a literal, and
/// the parser asks a token what it is through <see cref="TokenType"/> rather than by type
/// testing, so the kinds stay sealed and small.
/// </remarks>
public abstract partial class Token
{
    private int start;

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
    internal void SubtractFromStart(int offset)
    {
        start -= offset;
    }

    /// <summary>
    /// The kind of this token, one of the numbers in Token.cs.
    /// </summary>
    internal abstract int TokenType { get; }

    /// <summary>
    /// The token written as an identifier, or null if it is not one: a literal has no name.
    /// </summary>
    internal virtual string? AsIdentifier => null;

    /// <summary>
    /// Whether the identifier was written between double quotes, which makes it a name that is
    /// never a keyword.
    /// </summary>
    internal virtual bool IsQuoted => false;

    /// <summary>
    /// The value of a literal token, or null if this token has none.
    /// </summary>
    /// <remarks>
    /// The value is built once and kept. A statement is parsed more than once, once to find out
    /// what it is and once to plan it, and a literal that was scanned as a string is the same
    /// string both times.
    /// </remarks>
    internal virtual Value? GetValue(CastDataProvider? provider) => null;

    /// <summary>
    /// Whether the token still holds <c>\u</c> escapes, which are converted only where the
    /// statement is parsed.
    /// </summary>
    internal virtual bool NeedsUnicodeConversion => false;

    /// <summary>
    /// Replaces the <c>\u</c> escapes of an identifier or a string with the characters they
    /// stand for.
    /// </summary>
    internal virtual void ConvertUnicode(int uescape)
    {
        throw DbException.GetInternalError();
    }

    /// <summary>
    /// A copy that shares the text and the value of this token, for a parser that reads the same
    /// statement twice.
    /// </summary>
    public Token Clone()
    {
        return (Token)MemberwiseClone();
    }

    /// <summary>
    /// An identifier, either bare or quoted.
    /// </summary>
    internal sealed class IdentifierToken : Token
    {
        private string identifier;

        private readonly bool quoted;

        private bool unicode;

        internal IdentifierToken(int start, string identifier, bool quoted, bool unicode)
            : base(start)
        {
            this.identifier = identifier;
            this.quoted = quoted;
            this.unicode = unicode;
        }

        internal override int TokenType => ParserUtil.Identifier;

        internal override string AsIdentifier => identifier;

        internal override bool IsQuoted => quoted;

        internal override bool NeedsUnicodeConversion => unicode;

        internal override void ConvertUnicode(int uescape)
        {
            if (unicode)
            {
                identifier = StringUtils.DecodeUnicodeStringSql(identifier, uescape);
                unicode = false;
            }
            else
            {
                throw DbException.GetInternalError();
            }
        }

        public override string ToString() => quoted ? StringUtils.QuoteIdentifier(identifier) : identifier;
    }

    /// <summary>
    /// A keyword, which is its own name.
    /// </summary>
    internal sealed class KeywordToken : Token
    {
        private readonly int type;

        internal KeywordToken(int start, int type)
            : base(start)
        {
            this.type = type;
        }

        internal override int TokenType => type;

        internal override string AsIdentifier => Tokens[type]!;

        public override string ToString() => Tokens[type]!;
    }

    /// <summary>
    /// A keyword that is also allowed as a name, which keeps the spelling it was written with,
    /// because the case of a name is a part of the name.
    /// </summary>
    internal sealed class KeywordOrIdentifierToken : Token
    {
        private readonly int type;

        private readonly string identifier;

        internal KeywordOrIdentifierToken(int start, int type, string identifier)
            : base(start)
        {
            this.type = type;
            this.identifier = identifier;
        }

        internal override int TokenType => type;

        internal override string AsIdentifier => identifier;

        public override string ToString() => identifier;
    }

    /// <summary>
    /// The end of the statement.
    /// </summary>
    internal sealed class EndOfInputToken : Token
    {
        internal EndOfInputToken(int start)
            : base(start)
        {
        }

        internal override int TokenType => EndOfInput;
    }

    /// <summary>
    /// A question mark standing for a value that is not written in the statement.
    /// </summary>
    internal sealed class ParameterToken : Token
    {
        internal int index;

        internal ParameterToken(int start, int index)
            : base(start)
        {
            this.index = index;
        }

        internal override int TokenType => Parameter;

        internal override string AsIdentifier => "?";

        /// <summary>
        /// Which parameter this is, counting from one. The number zero is written without a
        /// number, because the first question mark of a statement needs nothing to say.
        /// </summary>
        internal int Index => index;

        public override string ToString() => index == 0 ? "?" : "?" + index;
    }

    /// <summary>
    /// A token that stands for a value.
    /// </summary>
    internal abstract class LiteralToken : Token
    {
        internal Value? value;

        internal LiteralToken(int start)
            : base(start)
        {
        }

        internal sealed override int TokenType => Literal;

        public sealed override string ToString() => GetValue(null)!.GetTraceSql();
    }

    /// <summary>
    /// A string written between single quotes.
    /// </summary>
    internal sealed class CharacterStringToken : LiteralToken
    {
        internal string text;

        private bool unicode;

        internal CharacterStringToken(int start, string @string, bool unicode)
            : base(start)
        {
            this.text = @string;
            this.unicode = unicode;
        }

        internal override Value? GetValue(CastDataProvider? provider)
        {
            value ??= ValueVarchar.Get(text, provider);
            return value;
        }

        internal override bool NeedsUnicodeConversion => unicode;

        internal override void ConvertUnicode(int uescape)
        {
            if (unicode)
            {
                text = StringUtils.DecodeUnicodeStringSql(text, uescape);
                unicode = false;
            }
            else
            {
                throw DbException.GetInternalError();
            }
        }
    }

    /// <summary>
    /// Bytes written between single quotes, which are a binary string rather than a character one.
    /// </summary>
    internal sealed class BinaryStringToken : LiteralToken
    {
        private readonly byte[] bytes;

        internal BinaryStringToken(int start, byte[] bytes)
            : base(start)
        {
            this.bytes = bytes;
        }

        internal override Value? GetValue(CastDataProvider? provider)
        {
            value ??= ValueVarbinary.GetNoCopy(bytes);
            return value;
        }
    }

    /// <summary>
    /// A whole number that fits in an int.
    /// </summary>
    internal sealed class IntegerToken : LiteralToken
    {
        private readonly int number;

        internal IntegerToken(int start, int number)
            : base(start)
        {
            this.number = number;
        }

        internal override Value? GetValue(CastDataProvider? provider)
        {
            value ??= ValueInteger.Get(number);
            return value;
        }
    }

    /// <summary>
    /// A whole number that does not fit in an int.
    /// </summary>
    internal sealed class BigintToken : LiteralToken
    {
        private readonly long number;

        internal BigintToken(int start, long number)
            : base(start)
        {
            this.number = number;
        }

        internal override Value? GetValue(CastDataProvider? provider)
        {
            value ??= ValueBigint.Get(number);
            return value;
        }
    }

    /// <summary>
    /// A value of a type that the tokenizer does not scan itself, such as a date.
    /// </summary>
    internal sealed class ValueToken : LiteralToken
    {
        internal ValueToken(int start, Value value)
            : base(start)
        {
            this.value = value;
        }

        internal override Value? GetValue(CastDataProvider? provider) => value;
    }
}
