/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.Tokenizer.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Command;

/// <summary>
/// Reads a statement into the tokens the parser asks questions of.
/// </summary>
/// <remarks>
/// The class is one big switch over the next character, and the switch is the whole design: what a
/// character means depends on the characters after it, so <c>&lt;</c> may be a smaller-than, a
/// not-equal or the start of a bracket, and a letter may begin a keyword, a name, a string
/// literal or a number with an exponent. Everything else in the class is one of the readers the
/// switch calls.
/// <para>
/// Two things are worth saying about the transliteration itself. The reference counts positions in
/// UTF-16 code units and so does this, so a position in an error message is the same position on
/// both sides. And the reference reads a name by looking at the first letter and then comparing
/// against the keywords that begin with it, rather than asking a table; the tables of keywords that
/// begin with each letter are kept here in the same order, because the order is the order of the
/// comparisons and the comparisons are what make a name a keyword.
/// </para>
/// </remarks>
public sealed class Tokenizer
{
    private readonly CastDataProvider provider;

    private readonly bool identifiersToUpper;

    private readonly bool identifiersToLower;

    /// <summary>
    /// The keywords that are also allowed as names, which is how <c>ORDER</c> can be a column name
    /// without being the keyword. Null when none are.
    /// </summary>
    private readonly BitArray? nonKeywords;

    /// <summary>
    /// Creates a tokenizer.
    /// </summary>
    /// <param name="provider">where the mode comes from</param>
    /// <param name="identifiersToUpper">whether a name is folded to upper case</param>
    /// <param name="identifiersToLower">whether a name is folded to lower case</param>
    /// <param name="nonKeywords">the kinds of keyword that may also be a name, or null</param>
    internal Tokenizer(
        CastDataProvider provider, bool identifiersToUpper, bool identifiersToLower, BitArray? nonKeywords)
    {
        this.provider = provider;
        this.identifiersToUpper = identifiersToUpper;
        this.identifiersToLower = identifiersToLower;
        this.nonKeywords = nonKeywords;
    }

    /// <summary>
    /// Splits a statement into tokens. The entry point the tests use, with the fold and the
    /// non-keyword set of the default mode, so that a test does not have to know them.
    /// </summary>
    /// <param name="sql">the statement</param>
    /// <param name="stopOnCloseParen">whether to stop at the first closing parenthesis</param>
    /// <param name="parameters">the parameters the statement names, and the count is written back</param>
    /// <param name="provider">where the mode comes from</param>
    /// <returns>the tokens, the last of which is the end of input</returns>
    internal static List<Token> TokenizeForTest(
        string sql,
        bool stopOnCloseParen,
        BitArray parameters,
        CastDataProvider provider)
    {
        Tokenizer tokenizer = new(provider, identifiersToUpper: true, identifiersToLower: false, nonKeywords: null);
        return tokenizer.Tokenize(sql, stopOnCloseParen, parameters);
    }

    /// <summary>
    /// Reads a statement into tokens.
    /// </summary>
    /// <param name="sql">the statement</param>
    /// <param name="stopOnCloseParen">whether to stop at the first closing parenthesis, which is
    /// how an expression inside a larger statement is read</param>
    /// <param name="parameters">the parameters the statement names, and the count is written back</param>
    /// <returns>the tokens, the last of which is the end of input</returns>
    internal List<Token> Tokenize(string sql, bool stopOnCloseParen, BitArray parameters)
    {
        List<Token> tokens = [];
        int end = sql.Length - 1;
        bool foundUnicode = false;
        int lastParameter = 0;

        for (int i = 0; i <= end;)
        {
            char c = sql[i];
            Token token;

            switch (c)
            {
                case '!':
                    if (i < end)
                    {
                        char c2 = sql[i + 1];
                        if (c2 == '=')
                        {
                            token = new Token.KeywordToken(i++, Token.NotEqual);
                            break;
                        }

                        if (c2 == '~')
                        {
                            token = new Token.KeywordToken(i++, Token.NotTilde);
                            break;
                        }
                    }

                    throw DbException.GetSyntaxError(sql, i);

                case '"':
                case '`':
                    i = ReadQuotedIdentifier(sql, end, i, i, c, false, tokens);
                    continue;

                case '#':
                    if (provider.GetMode().SupportPoundSymbolForColumnNames)
                    {
                        i = ReadIdentifier(sql, end, i, i, tokens);
                        continue;
                    }

                    throw DbException.GetSyntaxError(sql, i);

                case '$':
                    if (i < end)
                    {
                        char c2 = sql[i + 1];
                        if (c2 == '$')
                        {
                            int stringStart = i + 2;
                            int stringEnd = sql.IndexOf("$$", stringStart, StringComparison.Ordinal);
                            if (stringEnd < 0)
                            {
                                throw DbException.GetSyntaxError(sql, i);
                            }

                            token = new Token.CharacterStringToken(i, sql[stringStart..stringEnd], false);
                            i = stringEnd + 1;
                        }
                        else
                        {
                            i = ParseParameterIndex(sql, end, i, tokens);
                            lastParameter = AssignParameterIndex(tokens, lastParameter, parameters);
                            continue;
                        }
                    }
                    else
                    {
                        // A bare question mark: numbered in the order it is read. A parameter
                        // written by name comes through as an At token followed by the name as an
                        // identifier — that is how this tokenizer spells "@Age", and the parser
                        // binds the two.
                        token = new Token.ParameterToken(i, 0);
                    }

                    break;

                case '%':
                    token = new Token.KeywordToken(i, Token.Percent);
                    break;

                case '&':
                    if (i < end && sql[i + 1] == '&')
                    {
                        token = new Token.KeywordToken(i++, Token.SpatialIntersects);
                        break;
                    }

                    throw DbException.GetSyntaxError(sql, i);

                case '\'':
                    i = ReadCharacterString(sql, i, end, i, false, tokens);
                    continue;

                case '(':
                    token = new Token.KeywordToken(i, Token.OpenParen);
                    break;

                case ')':
                    token = new Token.KeywordToken(i, Token.CloseParen);
                    if (stopOnCloseParen)
                    {
                        tokens.Add(token);
                        end = SkipWhitespace(sql, end, i + 1) - 1;
                        break;
                    }

                    break;

                case '*':
                    token = new Token.KeywordToken(i, Token.Asterisk);
                    break;

                case '+':
                    token = new Token.KeywordToken(i, Token.PlusSign);
                    break;

                case ',':
                    token = new Token.KeywordToken(i, Token.Comma);
                    break;

                case '-':
                    if (i < end && sql[i + 1] == '-')
                    {
                        i = SkipSimpleComment(sql, end, i);
                        continue;
                    }

                    token = new Token.KeywordToken(i, Token.MinusSign);
                    break;

                case '.':
                    if (i < end)
                    {
                        char c2 = sql[i + 1];
                        if (c2 is >= '0' and <= '9')
                        {
                            i = ReadFloat(sql, i, end, i + 1, false, tokens);
                            continue;
                        }
                    }

                    token = new Token.KeywordToken(i, Token.Dot);
                    break;

                case '/':
                    if (i < end)
                    {
                        char c2 = sql[i + 1];
                        if (c2 == '*')
                        {
                            i = SkipBracketedComment(sql, end, i);
                            continue;
                        }

                        if (c2 == '/')
                        {
                            i = SkipSimpleComment(sql, end, i);
                            continue;
                        }
                    }

                    token = new Token.KeywordToken(i, Token.Slash);
                    break;

                case '0':
                    if (i < end)
                    {
                        switch (sql[i + 1] & 0xFFDF)
                        {
                            case 'B':
                                i = ReadIntegerNumber(sql, i, end, i + 2, tokens, "Binary number", 2);
                                continue;

                            case 'O':
                                i = ReadIntegerNumber(sql, i, end, i + 2, tokens, "Octal number", 8);
                                continue;

                            case 'X':
                                if (provider.GetMode().ZeroExLiteralsAreBinaryStrings)
                                {
                                    i = Read0xBinaryString(sql, end, i + 2, tokens);
                                }
                                else
                                {
                                    i = ReadIntegerNumber(sql, i, end, i + 2, tokens, "Hex number", 16);
                                }

                                continue;
                        }
                    }

                    goto case '1';

                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    i = ReadNumeric(sql, i, end, i + 1, c, tokens);
                    continue;

                case ':':
                    if (i < end)
                    {
                        char c2 = sql[i + 1];
                        if (c2 == ':')
                        {
                            token = new Token.KeywordToken(i++, Token.ColonColon);
                            break;
                        }

                        if (c2 == '=')
                        {
                            token = new Token.KeywordToken(i++, Token.ColonEq);
                            break;
                        }
                    }

                    token = new Token.KeywordToken(i, Token.Colon);
                    break;

                case ';':
                    token = new Token.KeywordToken(i, Token.Semicolon);
                    break;

                case '<':
                    if (i < end)
                    {
                        char c2 = sql[i + 1];
                        if (c2 == '=')
                        {
                            token = new Token.KeywordToken(i++, Token.SmallerEqual);
                            break;
                        }

                        if (c2 == '>')
                        {
                            token = new Token.KeywordToken(i++, Token.NotEqual);
                            break;
                        }
                    }

                    token = new Token.KeywordToken(i, Token.Smaller);
                    break;

                case '=':
                    token = new Token.KeywordToken(i, Token.Equal);
                    break;

                case '>':
                    if (i < end && sql[i + 1] == '=')
                    {
                        token = new Token.KeywordToken(i++, Token.BiggerEqual);
                        break;
                    }

                    token = new Token.KeywordToken(i, Token.Bigger);
                    break;

                case '?':
                    if (i + 1 < end && sql[i + 1] == '?')
                    {
                        char c3 = sql[i + 2];
                        if (c3 == '(')
                        {
                            token = new Token.KeywordToken(i, Token.OpenBracket);
                            i += 2;
                            break;
                        }

                        if (c3 == ')')
                        {
                            token = new Token.KeywordToken(i, Token.CloseBracket);
                            i += 2;
                            break;
                        }
                    }

                    i = ParseParameterIndex(sql, end, i, tokens);
                    lastParameter = AssignParameterIndex(tokens, lastParameter, parameters);
                    continue;

                case '@':
                    token = new Token.KeywordToken(i, Token.At);
                    break;

                case 'A':
                case 'a':
                    i = ReadA(sql, end, i, tokens);
                    continue;

                case 'B':
                case 'b':
                    i = ReadB(sql, end, i, tokens);
                    continue;

                case 'C':
                case 'c':
                    i = ReadC(sql, end, i, tokens);
                    continue;

                case 'D':
                case 'd':
                    i = ReadD(sql, end, i, tokens);
                    continue;

                case 'E':
                case 'e':
                    i = ReadE(sql, end, i, tokens);
                    continue;

                case 'F':
                case 'f':
                    i = ReadF(sql, end, i, tokens);
                    continue;

                case 'G':
                case 'g':
                    i = ReadG(sql, end, i, tokens);
                    continue;

                case 'H':
                case 'h':
                    i = ReadH(sql, end, i, tokens);
                    continue;

                case 'I':
                case 'i':
                    i = ReadI(sql, end, i, tokens);
                    continue;

                case 'J':
                case 'j':
                    i = ReadJ(sql, end, i, tokens);
                    continue;

                case 'K':
                case 'k':
                    i = ReadK(sql, end, i, tokens);
                    continue;

                case 'L':
                case 'l':
                    i = ReadL(sql, end, i, tokens);
                    continue;

                case 'M':
                case 'm':
                    i = ReadM(sql, end, i, tokens);
                    continue;

                case 'N':
                case 'n':
                    if (i < end && sql[i + 1] == '\'')
                    {
                        i = ReadCharacterString(sql, i, end, i + 1, false, tokens);
                    }
                    else
                    {
                        i = ReadN(sql, end, i, tokens);
                    }

                    continue;

                case 'O':
                case 'o':
                    i = ReadO(sql, end, i, tokens);
                    continue;

                case 'P':
                case 'p':
                    i = ReadP(sql, end, i, tokens);
                    continue;

                case 'Q':
                case 'q':
                    i = ReadQ(sql, end, i, tokens);
                    continue;

                case 'R':
                case 'r':
                    i = ReadR(sql, end, i, tokens);
                    continue;

                case 'S':
                case 's':
                    i = ReadS(sql, end, i, tokens);
                    continue;

                case 'T':
                case 't':
                    i = ReadT(sql, end, i, tokens);
                    continue;

                case 'U':
                case 'u':
                    if (i + 1 < end && sql[i + 1] == '&')
                    {
                        char c3 = sql[i + 2];
                        if (c3 == '"')
                        {
                            i = ReadQuotedIdentifier(sql, end, i, i + 2, '"', true, tokens);
                            foundUnicode = true;
                            continue;
                        }

                        if (c3 == '\'')
                        {
                            i = ReadCharacterString(sql, i, end, i + 2, true, tokens);
                            foundUnicode = true;
                            continue;
                        }
                    }

                    i = ReadU(sql, end, i, tokens);
                    continue;

                case 'V':
                case 'v':
                    i = ReadV(sql, end, i, tokens);
                    continue;

                case 'W':
                case 'w':
                    i = ReadW(sql, end, i, tokens);
                    continue;

                case 'X':
                case 'x':
                    if (i < end && sql[i + 1] == '\'')
                    {
                        i = ReadBinaryString(sql, i, end, i + 1, tokens);
                    }
                    else
                    {
                        i = ReadIdentifier(sql, end, i, i, tokens);
                    }

                    continue;

                case 'Y':
                case 'y':
                    i = ReadY(sql, end, i, tokens);
                    continue;

                case 'Z':
                case 'z':
                    i = ReadIdentifier(sql, end, i, i, tokens);
                    continue;

                case '[':
                    if (provider.GetMode().SquareBracketQuotedNames)
                    {
                        int identifierStart = i + 1;
                        int identifierEnd = sql.IndexOf(']', identifierStart);
                        if (identifierEnd < 0)
                        {
                            throw DbException.GetSyntaxError(sql, i);
                        }

                        token = new Token.IdentifierToken(
                            i, sql[identifierStart..identifierEnd], true, false);
                        i = identifierEnd;
                    }
                    else
                    {
                        token = new Token.KeywordToken(i, Token.OpenBracket);
                    }

                    break;

                case ']':
                    token = new Token.KeywordToken(i, Token.CloseBracket);
                    break;

                case '_':
                    i = Read_(sql, end, i, tokens);
                    continue;

                case '{':
                    token = new Token.KeywordToken(i, Token.OpenBrace);
                    break;

                case '|':
                    if (i < end && sql[i + 1] == '|')
                    {
                        token = new Token.KeywordToken(i++, Token.Concatenation);
                        break;
                    }

                    throw DbException.GetSyntaxError(sql, i);

                case '}':
                    token = new Token.KeywordToken(i, Token.CloseBrace);
                    break;

                case '~':
                    token = new Token.KeywordToken(i, Token.Tilde);
                    break;

                default:
                    if (c <= ' ')
                    {
                        i++;
                        continue;
                    }

                    int tokenStart = i;
                    int cp = JavaCharacter.IsHighSurrogate(c) ? JavaCharacter.CodePointAt(sql, i++) : c;
                    if (JavaCharacter.IsSpaceChar(cp))
                    {
                        i++;
                        continue;
                    }

                    if (JavaCharacter.IsJavaIdentifierStart(cp))
                    {
                        i = ReadIdentifier(sql, end, tokenStart, i, tokens);
                        continue;
                    }

                    throw DbException.GetSyntaxError(sql, tokenStart);
            }

            tokens.Add(token);
            i++;
        }

        if (foundUnicode)
        {
            ProcessUescape(sql, tokens);
        }

        tokens.Add(new Token.EndOfInputToken(end + 1));
        return tokens;
    }

    private int ReadIdentifier(string sql, int end, int tokenStart, int i, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, i);
        tokens.Add(new Token.IdentifierToken(tokenStart, ExtractIdentifier(sql, tokenStart, endIndex), false, false));
        return endIndex;
    }

    private int ReadA(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type;
        if (length == 2)
        {
            type = (sql[tokenStart + 1] & 0xFFDF) == 'S' ? ParserUtil.As : ParserUtil.Identifier;
        }
        else if (Eq("ALL", sql, tokenStart, length))
        {
            type = ParserUtil.All;
        }
        else if (Eq("AND", sql, tokenStart, length))
        {
            type = ParserUtil.And;
        }
        else if (Eq("ANY", sql, tokenStart, length))
        {
            type = ParserUtil.Any;
        }
        else if (Eq("ARRAY", sql, tokenStart, length))
        {
            type = ParserUtil.Array;
        }
        else if (Eq("ASYMMETRIC", sql, tokenStart, length))
        {
            type = ParserUtil.Asymmetric;
        }
        else if (Eq("AUTHORIZATION", sql, tokenStart, length))
        {
            type = ParserUtil.Authorization;
        }
        else
        {
            type = ParserUtil.Identifier;
        }

        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadB(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type = Eq("BETWEEN", sql, tokenStart, length) ? ParserUtil.Between : ParserUtil.Identifier;
        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadC(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type;
        if (Eq("CASE", sql, tokenStart, length))
        {
            type = ParserUtil.Case;
        }
        else if (Eq("CAST", sql, tokenStart, length))
        {
            type = ParserUtil.Cast;
        }
        else if (Eq("CHECK", sql, tokenStart, length))
        {
            type = ParserUtil.Check;
        }
        else if (Eq("CONSTRAINT", sql, tokenStart, length))
        {
            type = ParserUtil.Constraint;
        }
        else if (Eq("CROSS", sql, tokenStart, length))
        {
            type = ParserUtil.Cross;
        }
        else if (length >= 12 && Eq("CURRENT_", sql, tokenStart, 8))
        {
            type = GetTokenTypeCurrent(sql, tokenStart, length);
        }
        else
        {
            type = ParserUtil.Identifier;
        }

        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    /// <remarks>
    /// The five words that begin with <c>CURRENT</c> are told apart by their length first, because
    /// there are only four of them at any one length and a comparison of a character that is not a
    /// character costs more than a comparison of a number. The reference does the same, and the
    /// order of the comparisons is the order here.
    /// </remarks>
    private static int GetTokenTypeCurrent(string s, int tokenStart, int length)
    {
        tokenStart += 8;
        switch (length)
        {
            case 12:
                if (EqCurrent("CURRENT_DATE", s, tokenStart, length))
                {
                    return ParserUtil.CurrentDate;
                }

                if (EqCurrent("CURRENT_PATH", s, tokenStart, length))
                {
                    return ParserUtil.CurrentPath;
                }

                if (EqCurrent("CURRENT_ROLE", s, tokenStart, length))
                {
                    return ParserUtil.CurrentRole;
                }

                if (EqCurrent("CURRENT_TIME", s, tokenStart, length))
                {
                    return ParserUtil.CurrentTime;
                }

                if (EqCurrent("CURRENT_USER", s, tokenStart, length))
                {
                    return ParserUtil.CurrentUser;
                }

                break;

            case 14:
                if (EqCurrent("CURRENT_SCHEMA", s, tokenStart, length))
                {
                    return ParserUtil.CurrentSchema;
                }

                break;

            case 15:
                if (EqCurrent("CURRENT_CATALOG", s, tokenStart, length))
                {
                    return ParserUtil.CurrentCatalog;
                }

                break;

            case 17:
                if (EqCurrent("CURRENT_TIMESTAMP", s, tokenStart, length))
                {
                    return ParserUtil.CurrentTimestamp;
                }

                break;
        }

        return ParserUtil.Identifier;
    }

    private static bool EqCurrent(string expected, string s, int start, int length)
    {
        for (int i = 8; i < length; i++)
        {
            if (expected[i] != (s[start++] & 0xFFDF))
            {
                return false;
            }
        }

        return true;
    }

    private int ReadD(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type;
        if (Eq("DAY", sql, tokenStart, length))
        {
            type = ParserUtil.Day;
        }
        else if (Eq("DEFAULT", sql, tokenStart, length))
        {
            type = ParserUtil.Default;
        }
        else if (Eq("DISTINCT", sql, tokenStart, length))
        {
            type = ParserUtil.Distinct;
        }
        else
        {
            type = ParserUtil.Identifier;
        }

        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadE(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type;
        if (Eq("ELSE", sql, tokenStart, length))
        {
            type = ParserUtil.Else;
        }
        else if (Eq("END", sql, tokenStart, length))
        {
            type = ParserUtil.End;
        }
        else if (Eq("EXCEPT", sql, tokenStart, length))
        {
            type = ParserUtil.Except;
        }
        else if (Eq("EXISTS", sql, tokenStart, length))
        {
            type = ParserUtil.Exists;
        }
        else
        {
            type = ParserUtil.Identifier;
        }

        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadF(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type;
        if (Eq("FETCH", sql, tokenStart, length))
        {
            type = ParserUtil.Fetch;
        }
        else if (Eq("FROM", sql, tokenStart, length))
        {
            type = ParserUtil.From;
        }
        else if (Eq("FOR", sql, tokenStart, length))
        {
            type = ParserUtil.For;
        }
        else if (Eq("FOREIGN", sql, tokenStart, length))
        {
            type = ParserUtil.Foreign;
        }
        else if (Eq("FULL", sql, tokenStart, length))
        {
            type = ParserUtil.Full;
        }
        else if (Eq("FALSE", sql, tokenStart, length))
        {
            type = ParserUtil.False;
        }
        else
        {
            type = ParserUtil.Identifier;
        }

        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadG(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type = Eq("GROUP", sql, tokenStart, length) ? ParserUtil.Group : ParserUtil.Identifier;
        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadH(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type;
        if (Eq("HAVING", sql, tokenStart, length))
        {
            type = ParserUtil.Having;
        }
        else if (Eq("HOUR", sql, tokenStart, length))
        {
            type = ParserUtil.Hour;
        }
        else
        {
            type = ParserUtil.Identifier;
        }

        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadI(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type;
        if (length == 2)
        {
            switch (sql[tokenStart + 1] & 0xFFDF)
            {
                case 'F':
                    type = ParserUtil.If;
                    break;

                case 'N':
                    type = ParserUtil.In;
                    break;

                case 'S':
                    type = ParserUtil.Is;
                    break;

                default:
                    type = ParserUtil.Identifier;
                    break;
            }
        }
        else if (Eq("INNER", sql, tokenStart, length))
        {
            type = ParserUtil.Inner;
        }
        else if (Eq("INTERSECT", sql, tokenStart, length))
        {
            type = ParserUtil.Intersect;
        }
        else if (Eq("INTERVAL", sql, tokenStart, length))
        {
            type = ParserUtil.Interval;
        }
        else
        {
            type = ParserUtil.Identifier;
        }

        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadJ(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type = Eq("JOIN", sql, tokenStart, length) ? ParserUtil.Join : ParserUtil.Identifier;
        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadK(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type = Eq("KEY", sql, tokenStart, length) ? ParserUtil.Key : ParserUtil.Identifier;
        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadL(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type;
        if (Eq("LEFT", sql, tokenStart, length))
        {
            type = ParserUtil.Left;
        }
        else if (Eq("LIMIT", sql, tokenStart, length))
        {
            // LIMIT is a keyword only where it is a keyword: in the modes that allow the clause it
            // is read as one, and in the modes that do not it is a name like any other.
            type = provider.GetMode().Limit ? ParserUtil.Limit : ParserUtil.Identifier;
        }
        else if (Eq("LIKE", sql, tokenStart, length))
        {
            type = ParserUtil.Like;
        }
        else if (Eq("LOCALTIME", sql, tokenStart, length))
        {
            type = ParserUtil.Localtime;
        }
        else if (Eq("LOCALTIMESTAMP", sql, tokenStart, length))
        {
            type = ParserUtil.Localtimestamp;
        }
        else
        {
            type = ParserUtil.Identifier;
        }

        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadM(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type;
        if (Eq("MINUS", sql, tokenStart, length))
        {
            type = provider.GetMode().MinusIsExcept ? ParserUtil.Minus : ParserUtil.Identifier;
        }
        else if (Eq("MINUTE", sql, tokenStart, length))
        {
            type = ParserUtil.Minute;
        }
        else if (Eq("MONTH", sql, tokenStart, length))
        {
            type = ParserUtil.Month;
        }
        else
        {
            type = ParserUtil.Identifier;
        }

        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadN(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type;
        if (Eq("NOT", sql, tokenStart, length))
        {
            type = ParserUtil.Not;
        }
        else if (Eq("NATURAL", sql, tokenStart, length))
        {
            type = ParserUtil.Natural;
        }
        else if (Eq("NULL", sql, tokenStart, length))
        {
            type = ParserUtil.Null;
        }
        else
        {
            type = ParserUtil.Identifier;
        }

        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadO(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type;
        if (length == 2)
        {
            switch (sql[tokenStart + 1] & 0xFFDF)
            {
                case 'N':
                    type = ParserUtil.On;
                    break;

                case 'R':
                    type = ParserUtil.Or;
                    break;

                default:
                    type = ParserUtil.Identifier;
                    break;
            }
        }
        else if (Eq("OFFSET", sql, tokenStart, length))
        {
            type = ParserUtil.Offset;
        }
        else if (Eq("ORDER", sql, tokenStart, length))
        {
            type = ParserUtil.Order;
        }
        else
        {
            type = ParserUtil.Identifier;
        }

        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadP(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type = Eq("PRIMARY", sql, tokenStart, length) ? ParserUtil.Primary : ParserUtil.Identifier;
        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadQ(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type = Eq("QUALIFY", sql, tokenStart, length) ? ParserUtil.Qualify : ParserUtil.Identifier;
        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadR(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type;
        if (Eq("RIGHT", sql, tokenStart, length))
        {
            type = ParserUtil.Right;
        }
        else if (Eq("ROW", sql, tokenStart, length))
        {
            type = ParserUtil.Row;
        }
        else if (Eq("ROWNUM", sql, tokenStart, length))
        {
            type = ParserUtil.Rownum;
        }
        else
        {
            type = ParserUtil.Identifier;
        }

        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadS(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type;
        if (Eq("SECOND", sql, tokenStart, length))
        {
            type = ParserUtil.Second;
        }
        else if (Eq("SELECT", sql, tokenStart, length))
        {
            type = ParserUtil.Select;
        }
        else if (Eq("SESSION_USER", sql, tokenStart, length))
        {
            type = ParserUtil.SessionUser;
        }
        else if (Eq("SET", sql, tokenStart, length))
        {
            type = ParserUtil.Set;
        }
        else if (Eq("SOME", sql, tokenStart, length))
        {
            type = ParserUtil.Some;
        }
        else if (Eq("SYMMETRIC", sql, tokenStart, length))
        {
            type = ParserUtil.Symmetric;
        }
        else if (Eq("SYSTEM_USER", sql, tokenStart, length))
        {
            type = ParserUtil.SystemUser;
        }
        else
        {
            type = ParserUtil.Identifier;
        }

        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadT(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type;
        if (length == 2)
        {
            type = (sql[tokenStart + 1] & 0xFFDF) == 'O' ? ParserUtil.To : ParserUtil.Identifier;
        }
        else if (Eq("TABLE", sql, tokenStart, length))
        {
            type = ParserUtil.Table;
        }
        else if (Eq("TRUE", sql, tokenStart, length))
        {
            type = ParserUtil.True;
        }
        else
        {
            type = ParserUtil.Identifier;
        }

        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadU(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type;
        if (Eq("UESCAPE", sql, tokenStart, length))
        {
            type = ParserUtil.Uescape;
        }
        else if (Eq("UNION", sql, tokenStart, length))
        {
            type = ParserUtil.Union;
        }
        else if (Eq("UNIQUE", sql, tokenStart, length))
        {
            type = ParserUtil.Unique;
        }
        else if (Eq("UNKNOWN", sql, tokenStart, length))
        {
            type = ParserUtil.Unknown;
        }
        else if (Eq("USER", sql, tokenStart, length))
        {
            type = ParserUtil.User;
        }
        else if (Eq("USING", sql, tokenStart, length))
        {
            type = ParserUtil.Using;
        }
        else
        {
            type = ParserUtil.Identifier;
        }

        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadV(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type;
        if (Eq("VALUE", sql, tokenStart, length))
        {
            type = ParserUtil.Value;
        }
        else if (Eq("VALUES", sql, tokenStart, length))
        {
            type = ParserUtil.Values;
        }
        else
        {
            type = ParserUtil.Identifier;
        }

        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadW(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type;
        if (Eq("WHEN", sql, tokenStart, length))
        {
            type = ParserUtil.When;
        }
        else if (Eq("WHERE", sql, tokenStart, length))
        {
            type = ParserUtil.Where;
        }
        else if (Eq("WINDOW", sql, tokenStart, length))
        {
            type = ParserUtil.Window;
        }
        else if (Eq("WITH", sql, tokenStart, length))
        {
            type = ParserUtil.With;
        }
        else
        {
            type = ParserUtil.Identifier;
        }

        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadY(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int length = endIndex - tokenStart;
        int type = Eq("YEAR", sql, tokenStart, length) ? ParserUtil.Year : ParserUtil.Identifier;
        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    /// <remarks>
    /// A name may begin with an underscore, which is how <c>_ROWID_</c> is a keyword at all. The
    /// reference compares from the second character, having already established the first one by
    /// being here, so this does the same.
    /// </remarks>
    private int Read_(string sql, int end, int tokenStart, List<Token> tokens)
    {
        int endIndex = FindIdentifierEnd(sql, end, tokenStart);
        int type = endIndex - tokenStart == 7
            && RegionMatchesIgnoringCase("_ROWID_", 1, sql, tokenStart + 1, 6)
                ? ParserUtil._ROWID_
                : ParserUtil.Identifier;
        return ReadIdentifierOrKeyword(sql, tokenStart, tokens, endIndex, type);
    }

    private int ReadIdentifierOrKeyword(
        string sql, int tokenStart, List<Token> tokens, int endIndex, int type)
    {
        Token token;
        if (type == ParserUtil.Identifier)
        {
            token = new Token.IdentifierToken(
                tokenStart, ExtractIdentifier(sql, tokenStart, endIndex), false, false);
        }
        else if (nonKeywords is not null && type < nonKeywords.Length && nonKeywords.Get(type))
        {
            token = new Token.KeywordOrIdentifierToken(
                tokenStart, type, ExtractIdentifier(sql, tokenStart, endIndex));
        }
        else
        {
            token = new Token.KeywordToken(tokenStart, type);
        }

        tokens.Add(token);
        return endIndex;
    }

    /// <remarks>
    /// The first character is known to be the one that led here, so the comparison starts at the
    /// second: a keyword is compared with its first character already matched, and the masking of
    /// the character of the statement is what makes the comparison ignore case.
    /// </remarks>
    private static bool Eq(string expected, string s, int start, int length)
    {
        if (length != expected.Length)
        {
            return false;
        }

        for (int i = 1; i < length; i++)
        {
            if (expected[i] != (s[++start] & 0xFFDF))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RegionMatchesIgnoringCase(
        string expected, int expectedStart, string s, int start, int length)
    {
        for (int i = 0; i < length; i++)
        {
            if ((expected[expectedStart + i] & 0xFFDF) != (s[start + i] & 0xFFDF))
            {
                return false;
            }
        }

        return true;
    }

    private int FindIdentifierEnd(string sql, int end, int i)
    {
        i++;
        for (; ; )
        {
            if (i > end)
            {
                break;
            }

            // The fast path reads the character as is: CategoryOf answers BMP values from the
            // same BCL call either way, and a lone surrogate reads as its own value on both
            // paths, so only a high surrogate needs the pair read.
            char c = sql[i];
            int cp;
            bool isPart;
            if (JavaCharacter.IsHighSurrogate(c))
            {
                cp = JavaCharacter.CodePointAt(sql, i);
                isPart = JavaCharacter.IsJavaIdentifierPart(cp);
            }
            else
            {
                cp = c;
                isPart = JavaCharacter.IsJavaIdentifierPart(c);
            }

            if (!isPart
                && (cp != '#' || !provider.GetMode().SupportPoundSymbolForColumnNames))
            {
                break;
            }

            i += JavaCharacter.CharCount(cp);
        }

        return i;
    }

    private string ExtractIdentifier(string sql, int beginIndex, int endIndex)
        => ConvertCase(sql[beginIndex..endIndex]);

    private int ReadQuotedIdentifier(
        string sql, int end, int tokenStart, int i, char c, bool unicode, List<Token> tokens)
    {
        int identifierEnd = sql.IndexOf(c, ++i);
        if (identifierEnd < 0)
        {
            throw DbException.GetSyntaxError(sql, tokenStart);
        }

        string s = sql[i..identifierEnd];
        i = identifierEnd + 1;

        // Two quotes in a row are one of them, which is the only way to write the quote inside a
        // quoted name.
        if (i <= end && sql[i] == c)
        {
            StringBuilder builder = new(s);
            do
            {
                identifierEnd = sql.IndexOf(c, i + 1);
                if (identifierEnd < 0)
                {
                    throw DbException.GetSyntaxError(sql, tokenStart);
                }

                _ = builder.Append(sql, i, identifierEnd - i);
                i = identifierEnd + 1;
            }
            while (i <= end && sql[i] == c);

            s = builder.ToString();
        }

        if (c == '`')
        {
            s = ConvertCase(s);
        }

        tokens.Add(new Token.IdentifierToken(tokenStart, s, true, unicode));
        return i;
    }

    private string ConvertCase(string s)
    {
        if (identifiersToUpper)
        {
            return StringUtils.ToUpperEnglish(s);
        }

        return identifiersToLower ? StringUtils.ToLowerEnglish(s) : s;
    }

    private static int ReadBinaryString(string sql, int tokenStart, int end, int i, List<Token> tokens)
    {
        MemoryStream result = new();
        int stringEnd;
        do
        {
            stringEnd = sql.IndexOf('\'', ++i);
            if (stringEnd < 0 || (stringEnd < end && sql[stringEnd + 1] == '\''))
            {
                throw DbException.GetSyntaxError(sql, tokenStart);
            }

            _ = StringUtils.ConvertHexWithSpacesToBytes(result, sql, i, stringEnd);
            i = SkipWhitespace(sql, end, stringEnd + 1);
        }
        while (i <= end && sql[i] == '\'');

        tokens.Add(new Token.BinaryStringToken(tokenStart, result.ToArray()));
        return i;
    }

    private static int ReadCharacterString(
        string sql, int tokenStart, int end, int i, bool unicode, List<Token> tokens)
    {
        string? s = null;
        StringBuilder? builder = null;
        int stringEnd;
        do
        {
            stringEnd = sql.IndexOf('\'', ++i);
            if (stringEnd < 0)
            {
                throw DbException.GetSyntaxError(sql, tokenStart);
            }

            if (s is null)
            {
                s = sql[i..stringEnd];
            }
            else
            {
                builder ??= new StringBuilder(s);
                _ = builder.Append(sql, i, stringEnd - i);
            }

            i = stringEnd + 1;

            // A second quote is either the end of the string or the start of one more piece of the
            // same string: a quote inside a literal is written twice, and a literal may be written
            // in pieces with whitespace and comments between them.
            if (i <= end && sql[i] == '\'')
            {
                builder ??= new StringBuilder(s);
                do
                {
                    stringEnd = sql.IndexOf('\'', i + 1);
                    if (stringEnd < 0)
                    {
                        throw DbException.GetSyntaxError(sql, tokenStart);
                    }

                    _ = builder.Append(sql, i, stringEnd - i);
                    i = stringEnd + 1;
                }
                while (i <= end && sql[i] == '\'');
            }

            i = SkipWhitespace(sql, end, i);
        }
        while (i <= end && sql[i] == '\'');

        if (builder is not null)
        {
            s = builder.ToString();
        }

        tokens.Add(new Token.CharacterStringToken(tokenStart, s!, unicode));
        return i;
    }

    /// <remarks>
    /// Whitespace and comments are the same thing to the reader: both of them are nothing, and a
    /// comment is found where a space would have been. That is why this is called from inside a
    /// string literal as well as from the main loop, where it skips the gap between the pieces of a
    /// literal written in several.
    /// </remarks>
    private static int SkipWhitespace(string sql, int end, int i)
    {
        while (i <= end)
        {
            // Same split as the identifier scan: a high surrogate is the only character whose
            // answer needs the pair, everything else reads as is with the same result.
            char c = sql[i];
            int cp;
            bool isWs;
            if (JavaCharacter.IsHighSurrogate(c))
            {
                cp = JavaCharacter.CodePointAt(sql, i);
                isWs = JavaCharacter.IsWhitespace(cp);
            }
            else
            {
                cp = c;
                isWs = JavaCharacter.IsWhitespace(c);
            }

            if (!isWs)
            {
                if (cp == '/' && i < end)
                {
                    char c2 = sql[i + 1];
                    if (c2 == '*')
                    {
                        i = SkipBracketedComment(sql, end, i);
                        continue;
                    }

                    if (c2 == '/')
                    {
                        i = SkipSimpleComment(sql, end, i);
                        continue;
                    }
                }

                break;
            }

            i += JavaCharacter.CharCount(cp);
        }

        return i;
    }

    private static int Read0xBinaryString(string sql, int end, int i, List<Token> tokens)
    {
        int start = i;
        for (char c; i <= end && (((c = sql[i]) is >= '0' and <= '9') || ((c = (char)(c & 0xFFDF)) is >= 'A' and <= 'F'));)
        {
            i++;
        }

        if (i <= end && JavaCharacter.IsJavaIdentifierPart(JavaCharacter.CodePointAt(sql, i)))
        {
            throw DbException.Get(ErrorCode.HexStringWrong1, sql[start..(i + 1)]);
        }

        tokens.Add(new Token.BinaryStringToken(start, StringUtils.ConvertHexToBytes(sql[start..i])));
        return i;
    }

    private static int ReadIntegerNumber(
        string sql, int tokenStart, int end, int i, List<Token> tokens, string name, int radix)
    {
        if (i > end)
        {
            throw DbException.GetSyntaxError(sql, tokenStart, name);
        }

        int maxDigit, maxLetter;
        if (radix > 10)
        {
            maxDigit = '9';
            maxLetter = ('A' - 11) + radix;
        }
        else
        {
            maxDigit = ('0' - 1) + radix;
            maxLetter = -1;
        }

        int start = i;
        long number = 0;
        char c;
        int lastUnderscore = int.MinValue;

        do
        {
            c = sql[i];
            if (c >= '0' && c <= maxDigit)
            {
                number = (number * radix) + c - '0';
            }
            else if (c == '_')
            {
                if (lastUnderscore == i - 1)
                {
                    throw DbException.GetSyntaxError(sql, tokenStart, name);
                }

                lastUnderscore = i;
                continue;
            }
            else if (maxLetter >= 0 && (c = (char)(c & 0xFFDF)) >= 'A' && c <= maxLetter)
            {
                number = (number * radix) + c - ('A' - 10);
            }
            else if (i == start)
            {
                throw DbException.GetSyntaxError(sql, tokenStart, name);
            }
            else
            {
                break;
            }

            // Past what an int can hold the digits are still counted, because whether the number is
            // a BIGINT or a NUMERIC depends on how many of them there are.
            if (number > int.MaxValue)
            {
                while (++i <= end)
                {
                    c = sql[i];
                    if (c >= '0' && c <= maxDigit)
                    {
                        //
                    }
                    else if (c == '_')
                    {
                        if (lastUnderscore == i - 1)
                        {
                            throw DbException.GetSyntaxError(sql, tokenStart, name);
                        }

                        lastUnderscore = i;
                        continue;
                    }
                    else if (maxLetter >= 0 && (c = (char)(c & 0xFFDF)) >= 'A' && c <= 'F')
                    {
                        //
                    }
                    else
                    {
                        break;
                    }
                }

                if (lastUnderscore == i - 1)
                {
                    throw DbException.GetSyntaxError(sql, tokenStart, name);
                }

                return FinishBigInteger(
                    sql, tokenStart, end, i, start, i <= end && c == 'L', lastUnderscore >= 0, radix, tokens);
            }
        }
        while (++i <= end);

        if (lastUnderscore == i - 1)
        {
            throw DbException.GetSyntaxError(sql, tokenStart, name);
        }

        bool bigint = i <= end && c == 'L';
        if (bigint)
        {
            i++;
        }

        if (i <= end && JavaCharacter.IsJavaIdentifierPart(JavaCharacter.CodePointAt(sql, i)))
        {
            throw DbException.GetSyntaxError(sql, tokenStart, name);
        }

        tokens.Add(bigint
            ? new Token.BigintToken(start, number)
            : new Token.IntegerToken(start, (int)number));
        return i;
    }

    private static int ReadNumeric(
        string sql, int tokenStart, int end, int i, char c, List<Token> tokens)
    {
        long number = c - '0';
        int lastUnderscore = int.MinValue;
        for (; i <= end; i++)
        {
            c = sql[i];
            if (c is < '0' or > '9')
            {
                if (lastUnderscore == i - 1)
                {
                    throw DbException.GetSyntaxError(sql, tokenStart, "Numeric");
                }

                switch (c)
                {
                    case '.':
                        return ReadFloat(sql, tokenStart, end, i, lastUnderscore >= 0, tokens);

                    case 'E':
                    case 'e':
                        return ReadApproximateNumeric(sql, tokenStart, end, i, lastUnderscore >= 0, tokens);

                    case 'L':
                    case 'l':
                        return FinishBigInteger(
                            sql, tokenStart, end, i, tokenStart, true, lastUnderscore >= 0, 10, tokens);

                    case '_':
                        lastUnderscore = i;
                        continue;
                }

                break;
            }

            number = (number * 10) + (c - '0');
            if (number > int.MaxValue)
            {
                while (++i <= end)
                {
                    c = sql[i];
                    if (c is < '0' or > '9')
                    {
                        if (lastUnderscore == i - 1)
                        {
                            throw DbException.GetSyntaxError(sql, tokenStart, "Numeric");
                        }

                        switch (c)
                        {
                            case '.':
                                return ReadFloat(sql, tokenStart, end, i, lastUnderscore >= 0, tokens);

                            case 'E':
                            case 'e':
                                return ReadApproximateNumeric(
                                    sql, tokenStart, end, i, lastUnderscore >= 0, tokens);

                            case '_':
                                lastUnderscore = i;
                                continue;
                        }

                        break;
                    }
                }

                if (lastUnderscore == i - 1)
                {
                    throw DbException.GetSyntaxError(sql, tokenStart, "Numeric");
                }

                return FinishBigInteger(
                    sql, tokenStart, end, i, tokenStart, c == 'L' || c == 'l', lastUnderscore >= 0, 10, tokens);
            }
        }

        if (lastUnderscore == i - 1)
        {
            throw DbException.GetSyntaxError(sql, tokenStart, "Numeric");
        }

        tokens.Add(new Token.IntegerToken(tokenStart, (int)number));
        return i;
    }

    private static int ReadFloat(
        string sql, int tokenStart, int end, int i, bool withUnderscore, List<Token> tokens)
    {
        int start = i + 1;
        int lastUnderscore = int.MinValue;
        while (++i <= end)
        {
            char c = sql[i];
            if (c is < '0' or > '9')
            {
                if (lastUnderscore == i - 1)
                {
                    throw DbException.GetSyntaxError(sql, tokenStart, "Numeric");
                }

                switch (c)
                {
                    case 'E':
                    case 'e':
                        return ReadApproximateNumeric(sql, tokenStart, end, i, withUnderscore, tokens);

                    case '_':
                        if (i == start)
                        {
                            throw DbException.GetSyntaxError(sql, tokenStart, "Numeric");
                        }

                        lastUnderscore = i;
                        withUnderscore = true;
                        continue;
                }

                break;
            }
        }

        if (lastUnderscore == i - 1)
        {
            throw DbException.GetSyntaxError(sql, tokenStart, "Numeric");
        }

        tokens.Add(new Token.ValueToken(
            tokenStart, ValueNumeric.Get(ReadBigDecimal(sql, tokenStart, i, withUnderscore))));
        return i;
    }

    /// <remarks>
    /// A number with an exponent is a DECFLOAT and not a NUMERIC, which is the only place the
    /// tokenizer builds a decimal floating point value. The exponent may be written with a sign and
    /// may not be empty, because <c>1E</c> is a name that happens to start with a digit.
    /// </remarks>
    private static int ReadApproximateNumeric(
        string sql, int tokenStart, int end, int i, bool withUnderscore, List<Token> tokens)
    {
        if (i == end)
        {
            throw DbException.GetSyntaxError(sql, tokenStart, "Approximate numeric");
        }

        char c = sql[++i];
        if (c == '+' || c == '-')
        {
            if (i == end)
            {
                throw DbException.GetSyntaxError(sql, tokenStart, "Approximate numeric");
            }

            c = sql[++i];
        }

        if (c is < '0' or > '9')
        {
            throw DbException.GetSyntaxError(sql, tokenStart, "Approximate numeric");
        }

        int lastUnderscore = int.MinValue;
        while (++i <= end)
        {
            c = sql[i];
            if (c is < '0' or > '9')
            {
                if (lastUnderscore == i - 1)
                {
                    throw DbException.GetSyntaxError(sql, tokenStart, "Approximate numeric");
                }

                if (c == '_')
                {
                    lastUnderscore = i;
                    withUnderscore = true;
                    continue;
                }

                break;
            }
        }

        if (lastUnderscore == i - 1)
        {
            throw DbException.GetSyntaxError(sql, tokenStart, "Approximate numeric");
        }

        tokens.Add(new Token.ValueToken(
            tokenStart, ValueDecfloat.Get(ReadBigDecimal(sql, tokenStart, i, withUnderscore))));
        return i;
    }

    private static BigDecimal ReadBigDecimal(string sql, int tokenStart, int i, bool withUnderscore)
    {
        string text = ReadAndRemoveUnderscores(sql, tokenStart, i, withUnderscore);
        try
        {
            return BigDecimal.Parse(text);
        }
        catch (FormatException)
        {
            throw DbException.GetSyntaxError(sql, tokenStart, "Numeric");
        }
    }

    private static int FinishBigInteger(
        string sql, int tokenStart, int end, int i, int start, bool asBigint,
        bool withUnderscore, int radix, List<Token> tokens)
    {
        int endIndex = i;
        if (asBigint)
        {
            i++;
        }

        if (radix == 16 && i <= end && JavaCharacter.IsJavaIdentifierPart(JavaCharacter.CodePointAt(sql, i)))
        {
            throw DbException.GetSyntaxError(sql, tokenStart, "Hex number");
        }

        BigInteger bigInteger = ParseInteger(
            ReadAndRemoveUnderscores(sql, start, endIndex, withUnderscore), radix);
        Token token;
        if (bigInteger.CompareTo(ValueBigint.MaxBi) > 0)
        {
            if (asBigint)
            {
                throw DbException.GetSyntaxError(sql, tokenStart, "BIGINT");
            }

            token = new Token.ValueToken(tokenStart, ValueNumeric.Get(bigInteger));
        }
        else
        {
            token = new Token.BigintToken(tokenStart, (long)bigInteger);
        }

        tokens.Add(token);
        return i;
    }

    /// <remarks>
    /// The reference builds a <c>java.math.BigInteger</c> from the digits and a radix. .NET parses
    /// the same text with the same radixes and the same rules about a sign, so this is a parse with
    /// nothing to say about it beyond which styles are allowed.
    /// </remarks>
    private static BigInteger ParseInteger(string text, int radix)
        => BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private static string ReadAndRemoveUnderscores(
        string sql, int start, int endIndex, bool withUnderscore)
    {
        if (!withUnderscore)
        {
            return sql[start..endIndex];
        }

        StringBuilder builder = new(endIndex - start - 1);
        for (; start < endIndex; start++)
        {
            char c = sql[start];
            if (c != '_')
            {
                _ = builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static int SkipBracketedComment(string sql, int end, int i)
    {
        int tokenStart = i;
        i += 2;
        for (int level = 1; level > 0;)
        {
            for (; ; )
            {
                if (i >= end)
                {
                    throw DbException.GetSyntaxError(sql, tokenStart);
                }

                char c = sql[i++];
                if (c == '*')
                {
                    if (sql[i] == '/')
                    {
                        level--;
                        i++;
                        break;
                    }
                }
                else if (c == '/' && sql[i] == '*')
                {
                    level++;
                    i++;
                }
            }
        }

        return i;
    }

    private static int SkipSimpleComment(string sql, int end, int i)
    {
        i += 2;
        for (char c; i <= end && (c = sql[i]) != '\n' && c != '\r'; i++)
        {
            //
        }

        return i;
    }

    private static int ParseParameterIndex(string sql, int end, int i, List<Token> tokens)
    {
        int tokenStart = i;
        long number = 0;
        for (char c; ++i <= end && (c = sql[i]) is >= '0' and <= '9';)
        {
            number = (number * 10) + (c - '0');
            if (number > int.MaxValue)
            {
                throw DbException.GetInvalidValueException("parameter index", number);
            }
        }

        // ?0 is a parameter written as a name and not the first one, which would mean nothing: the
        // first parameter is written without a number at all.
        if (i > tokenStart + 1 && number == 0)
        {
            throw DbException.GetInvalidValueException("parameter index", number);
        }

        tokens.Add(new Token.ParameterToken(tokenStart, (int)number));
        return i;
    }

    /// <remarks>
    /// A statement may not mix the two ways of writing a parameter: either all of them are written
    /// without a number, in which case they are numbered in the order they are read, or all of
    /// them are written with a number, in which case the numbers are the positions. The reference
    /// remembers which one it has seen so far in <c>lastParameter</c>, where a positive value is
    /// the last number it assigned and a negative one means the parameters were written by hand.
    /// </remarks>
    private static int AssignParameterIndex(List<Token> tokens, int lastParameter, BitArray parameters)
    {
        Token.ParameterToken parameter = (Token.ParameterToken)tokens[^1];
        int index = parameter.Index;
        if (index == 0)
        {
            if (lastParameter < 0)
            {
                throw DbException.Get(ErrorCode.CannotMixIndexedAndUnindexedParams);
            }

            parameter.index = index = ++lastParameter;
        }
        else if (lastParameter > 0)
        {
            throw DbException.Get(ErrorCode.CannotMixIndexedAndUnindexedParams);
        }
        else
        {
            lastParameter = -1;
        }

        parameters.Set(index - 1, true);
        return lastParameter;
    }

    /// <remarks>
    /// The escapes of a statement are converted at the end rather than where they are read,
    /// because the escape character is not known until the <c>UESCAPE</c> is read, and the
    /// <c>UESCAPE</c> comes after the names it applies to. So this walks the tokens once, and when
    /// it finds a token that needs it, it looks ahead for a <c>UESCAPE</c> and takes the escape
    /// character from the string after it; a <c>UESCAPE</c> with nothing usable after it is an
    /// error, and it is reported at the place the word begins, seven characters along.
    /// </remarks>
    private static void ProcessUescape(string sql, List<Token> tokens)
    {
        for (int index = 0; index < tokens.Count; index++)
        {
            Token token = tokens[index];
            if (!token.NeedsUnicodeConversion)
            {
                continue;
            }

            int uescape = '\\';
            if (index + 1 < tokens.Count)
            {
                Token t2 = tokens[index + 1];
                if (t2.TokenType == ParserUtil.Uescape)
                {
                    tokens.RemoveAt(index + 1);
                    if (index + 1 < tokens.Count)
                    {
                        Token t3 = tokens[index + 1];
                        tokens.RemoveAt(index + 1);
                        if (t3 is Token.CharacterStringToken characterStringToken)
                        {
                            string s = characterStringToken.text;
                            if (JavaCharacter.CharCount(s.Length) == 1 && s.Length == 1)
                            {
                                int escape = s[0];
                                if (!JavaCharacter.IsWhitespace(escape)
                                    && (escape < '0' || escape > '9')
                                    && (escape < 'A' || escape > 'F')
                                    && (escape < 'a' || escape > 'f')
                                    && escape is not ('"' or '\'' or '+'))
                                {
                                    uescape = escape;
                                    token.ConvertUnicode(uescape);
                                    continue;
                                }
                            }
                        }
                    }

                    throw DbException.GetSyntaxError(sql, t2.Start + 7, "'<Unicode escape character>'");
                }
            }

            token.ConvertUnicode(uescape);
        }
    }
}
