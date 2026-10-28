/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.Parser.parseSet and the ParserBase readers it
 * needs (readIfEqualOrTo, readNonNegativeInt, readBooleanSetting,
 * readExpressionOrIdentifier, parseNonKeywords, formatNonKeywords).
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What is left out, and why:
 *
 * * SET PASSWORD and SET SALT ... HASH ... They build an AlterUser over the
 *   session's user; neither the class nor the user is ported, so the branches
 *   refuse by name instead of parsing a statement nothing could run.
 * * readSetCompatibility, the compatibility-mode spellings of SET. They live in
 *   the mode-specific readers (Regclasses, On Duplicate Key) that are not
 *   ported; a REGULAR-mode database never reaches them, and this parser has no
 *   other mode to consult yet.
 * * The quirks-mode BINARY_COLLATION and UUID_COLLATION no-ops. Quirks mode is
 *   a connection flag this port does not read.
 * * The non-keyword bit set consults the token table of this parser, which is
 *   the reference's TOKENS array one-to-one; the reference keeps the helpers in
 *   ParserBase, which this port folds into Parser.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Command;

public sealed partial class Parser
{
    /// <summary>
    /// Reads <c>=</c> or <c>TO</c> when one is written, as the reference's readIfEqualOrTo does.
    /// </summary>
    private void ReadIfEqualOrTo()
    {
        if (!ReadIf(Token.Equal))
        {
            ReadIf(ParserUtil.To);
        }
    }

    /// <summary>
    /// Reads the boolean of a SET statement: ON, TRUE, FALSE, OFF, or a literal.
    /// </summary>
    /// <returns>the boolean</returns>
    private bool ReadBooleanSetting()
    {
        switch (CurrentType)
        {
            case ParserUtil.On:
            case ParserUtil.True:
                Read();
                return true;
            case ParserUtil.False:
                Read();
                return false;
            case Token.Literal:
            {
                bool result = (Current.GetValue(provider) ?? throw DbException.GetSyntaxError(
                    sql, Current.Start, "boolean")).GetBoolean();
                Read();
                return result;
            }
        }

        if (IsWord("OFF"))
        {
            return false;
        }

        throw DbException.GetSyntaxError(sql, Current.Start, "boolean");
    }

    /// <summary>
    /// Reads an expression, or a bare name as a string, as the reference's
    /// readExpressionOrIdentifier does.
    /// </summary>
    /// <returns>the expression</returns>
    private Expressions.Expression ReadExpressionOrIdentifier()
    {
        if (IsIdentifier())
        {
            return ValueExpression.Get(ValueVarchar.Get(ReadIdentifier()));
        }

        return ReadExpression();
    }

    /// <summary>
    /// Whether the current token is a plain identifier, or a keyword the session turned back
    /// into a name, as the reference's isIdentifier does.
    /// </summary>
    private bool IsIdentifier()
    {
        if (CurrentType == ParserUtil.Identifier)
        {
            return true;
        }

        BitArray? nonKeywords = session?.GetNonKeywords();
        return nonKeywords is not null
            && CurrentType >= 0
            && CurrentType < nonKeywords.Length
            && nonKeywords[CurrentType];
    }

    /// <summary>
    /// Builds the command of a SET statement, as the reference's parseSet does.
    /// </summary>
    /// <param name="current">the session</param>
    /// <returns>the prepared statement</returns>
    private Prepared PrepareSet(SessionLocal current)
    {
        SessionLocal session = current;
        if (ReadIf(Token.At))
        {
            Dml.Set command = new(session, Dml.SetTypes.Variable);
            command.SetString(ReadIdentifier());
            ReadIfEqualOrTo();
            command.SetExpression(ReadExpression());
            return command;
        }
        else if (ReadIfName("AUTOCOMMIT"))
        {
            ReadIfEqualOrTo();
            return new Dml.TransactionCommand(
                session, ReadBooleanSetting()
                    ? CommandInterface.SetAutocommitTrue
                    : CommandInterface.SetAutocommitFalse);
        }
        else if (ReadIfName("EXCLUSIVE"))
        {
            ReadIfEqualOrTo();
            Dml.Set command = new(session, Dml.SetTypes.Exclusive);
            command.SetExpression(ReadExpression());
            return command;
        }
        else if (ReadIfName("IGNORECASE"))
        {
            ReadIfEqualOrTo();
            Dml.Set command = new(session, Dml.SetTypes.IgnoreCase);
            command.SetInt(ReadBooleanSetting() ? 1 : 0);
            return command;
        }
        else if (ReadIfName("PASSWORD"))
        {
            throw new TODOException(
                "Parser.parseSet: SET PASSWORD needs AlterUser and users, neither is ported (D-004)");
        }
        else if (ReadIfName("SALT"))
        {
            throw new TODOException(
                "Parser.parseSet: SET SALT ... HASH needs AlterUser and users, neither is ported (D-004)");
        }
        else if (ReadIfName("MODE"))
        {
            ReadIfEqualOrTo();
            Dml.Set command = new(session, Dml.SetTypes.Mode);
            command.SetString(ReadIdentifier());
            return command;
        }
        else if (ReadIfName("DATABASE"))
        {
            ReadIfEqualOrTo();
            ExpectWord("COLLATION");
            return PrepareSetCollation(session);
        }
        else if (ReadIfName("COLLATION"))
        {
            ReadIfEqualOrTo();
            return PrepareSetCollation(session);
        }
        else if (ReadIfName("CLUSTER"))
        {
            ReadIfEqualOrTo();
            Dml.Set command = new(session, Dml.SetTypes.Cluster);
            command.SetString(ReadString());
            return command;
        }
        else if (ReadIfName("DATABASE_EVENT_LISTENER"))
        {
            ReadIfEqualOrTo();
            Dml.Set command = new(session, Dml.SetTypes.DatabaseEventListener);
            command.SetString(ReadString());
            return command;
        }
        else if (ReadIfName("ALLOW_LITERALS"))
        {
            ReadIfEqualOrTo();
            Dml.Set command = new(session, Dml.SetTypes.AllowLiterals);
            int v;
            if (ReadIf(ParserUtil.All))
            {
                v = Constants.AllowLiteralsAll;
            }
            else if (IsWord("NONE"))
            {
                v = Constants.AllowLiteralsNone;
            }
            else if (IsWord("NUMBERS"))
            {
                v = Constants.AllowLiteralsNumbers;
            }
            else
            {
                v = ReadNonNegativeInt();
            }

            command.SetInt(v);
            return command;
        }
        else if (ReadIfName("DEFAULT_TABLE_TYPE"))
        {
            ReadIfEqualOrTo();
            Dml.Set command = new(session, Dml.SetTypes.DefaultTableType);
            int v;
            if (IsWord("MEMORY"))
            {
                v = Tables.Table.TYPE_MEMORY;
            }
            else if (IsWord("CACHED"))
            {
                v = Tables.Table.TYPE_CACHED;
            }
            else
            {
                v = ReadNonNegativeInt();
            }

            command.SetInt(v);
            return command;
        }
        else if (ReadIfName("SCHEMA"))
        {
            ReadIfEqualOrTo();
            Dml.Set command = new(session, Dml.SetTypes.Schema);
            command.SetExpression(ReadExpressionOrIdentifier());
            return command;
        }
        else if (ReadIfName("CATALOG"))
        {
            ReadIfEqualOrTo();
            Dml.Set command = new(session, Dml.SetTypes.Catalog);
            command.SetExpression(ReadExpressionOrIdentifier());
            return command;
        }
        else if (ReadIfName(Dml.SetTypes.GetTypeName(Dml.SetTypes.SchemaSearchPath)))
        {
            ReadIfEqualOrTo();
            Dml.Set command = new(session, Dml.SetTypes.SchemaSearchPath);
            List<string> list = [];
            do
            {
                list.Add(ReadIdentifier());
            }
            while (ReadIf(Token.Comma));

            command.SetStringArray([.. list]);
            return command;
        }
        else if (ReadIfName("JAVA_OBJECT_SERIALIZER"))
        {
            ReadIfEqualOrTo();
            Dml.Set command = new(session, Dml.SetTypes.JavaObjectSerializer);
            command.SetString(ReadString());
            return command;
        }
        else if (ReadIfName("IGNORE_CATALOGS"))
        {
            ReadIfEqualOrTo();
            Dml.Set command = new(session, Dml.SetTypes.IgnoreCatalogs);
            command.SetInt(ReadBooleanSetting() ? 1 : 0);
            return command;
        }
        else if (ReadIfName("SESSION"))
        {
            ExpectWord("CHARACTERISTICS");
            ExpectWord("AS");
            ExpectWord("TRANSACTION");
            return PrepareSetTransactionMode(session);
        }
        else if (ReadIfName("TRANSACTION"))
        {
            // TODO should affect only the current transaction
            return PrepareSetTransactionMode(session);
        }
        else if (ReadIfName("TIME"))
        {
            ExpectWord("ZONE");
            Dml.Set command = new(session, Dml.SetTypes.TimeZone);
            if (!IsWord("LOCAL"))
            {
                command.SetExpression(ReadExpression());
            }

            return command;
        }
        else if (ReadIfName("NON_KEYWORDS"))
        {
            ReadIfEqualOrTo();
            Dml.Set command = new(session, Dml.SetTypes.NonKeywords);
            List<string> list = [];
            if (CurrentType != Token.EndOfInput && CurrentType != Token.Semicolon)
            {
                do
                {
                    list.Add(StringUtils.ToUpperEnglish(ReadIdentifierOrKeyword()));
                }
                while (ReadIf(Token.Comma));
            }

            command.SetStringArray([.. list]);
            return command;
        }
        else if (ReadIfName("DEFAULT_NULL_ORDERING"))
        {
            ReadIfEqualOrTo();
            Dml.Set command = new(session, Dml.SetTypes.DefaultNullOrdering);
            command.SetString(ReadIdentifier());
            return command;
        }
        else if (ReadIfCompat("LOG"))
        {
            throw DbException.GetUnsupportedException("LOG");
        }
        else
        {
            string? upperName = Current.AsIdentifier is null
                ? null
                : StringUtils.ToUpperEnglish(Current.AsIdentifier);
            if (upperName is null)
            {
                throw DbException.GetSyntaxError(sql, Current.Start, "setting name");
            }

            int type = Dml.SetTypes.GetType(upperName);
            if (type < 0 && H2Sharp.Data.H2ConnectionStringBuilder.IsKnownSetting(upperName))
            {
                Read();
                ReadIfEqualOrTo();
                Read();
                return new Dml.NoOperation(session);
            }

            if (type >= 0)
            {
                Read();
                ReadIfEqualOrTo();
                Dml.Set command = new(session, type);
                command.SetExpression(ReadExpression());
                return command;
            }

            // The reference asks the database's compatibility mode for its own spellings of
            // SET here (readSetCompatibility) and then its quirks mode for two no-ops; the
            // mode-specific readers and quirks mode are not ported, so an unknown name is a
            // syntax error for this parser as it is for a REGULAR-mode reference session.
            throw DbException.GetSyntaxError(sql, Current.Start, "setting name");
        }
    }

    /// <summary>
    /// Builds the command of SET SESSION CHARACTERISTICS / SET TRANSACTION, as the reference's
    /// parseSetTransactionMode does.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the prepared statement</returns>
    private Prepared PrepareSetTransactionMode(SessionLocal session)
    {
        IsolationLevel isolationLevel;
        ExpectWord("ISOLATION");
        ExpectWord("LEVEL");
        if (IsWord("READ"))
        {
            if (IsWord("UNCOMMITTED"))
            {
                isolationLevel = IsolationLevel.ReadUncommitted;
            }
            else
            {
                ExpectWord("COMMITTED");
                isolationLevel = IsolationLevel.ReadCommitted;
            }
        }
        else if (IsWord("REPEATABLE"))
        {
            ExpectWord("READ");
            isolationLevel = IsolationLevel.RepeatableRead;
        }
        else if (IsWord("SNAPSHOT"))
        {
            isolationLevel = IsolationLevel.Snapshot;
        }
        else
        {
            ExpectWord("SERIALIZABLE");
            isolationLevel = IsolationLevel.Serializable;
        }

        return new Dml.SetSessionCharacteristics(session, isolationLevel);
    }

    /// <summary>
    /// Builds the command of SET COLLATION, as the reference's parseSetCollation does.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the prepared statement</returns>
    private Dml.Set PrepareSetCollation(SessionLocal session)
    {
        Dml.Set command = new(session, Dml.SetTypes.Collation);
        string name = ReadIdentifier();
        command.SetString(name);
        if (name.Equals(Values.CompareMode.Off, StringComparison.OrdinalIgnoreCase))
        {
            return command;
        }

        // The reference looks the collator up here, to reject an unknown name now and to know
        // its default strength; the port's CompareMode refuses every named collation, because
        // the platform's collation weights are not the reference's, so a named SET COLLATION
        // fails here the way an unknown name fails in the reference.
        _ = Values.CompareMode.GetInstance(name, Values.CompareMode.Tertiary);
        if (IsWord("STRENGTH"))
        {
            // PRIMARY is a keyword in the reference; the other strengths it reads as plain
            // words, and so does this port.
            if (ReadIf(ParserUtil.Primary))
            {
                command.SetInt(Values.CompareMode.Primary);
            }
            else if (ReadIfName("SECONDARY"))
            {
                command.SetInt(Values.CompareMode.Secondary);
            }
            else if (ReadIfName("TERTIARY"))
            {
                command.SetInt(Values.CompareMode.Tertiary);
            }
            else if (ReadIfName("IDENTICAL"))
            {
                command.SetInt(Values.CompareMode.Identical);
            }
        }
        else
        {
            command.SetInt(Values.CompareMode.Tertiary);
        }

        return command;
    }

    /// <summary>
    /// Reads an identifier, or the keyword it is spelled as, as the reference's
    /// readIdentifierOrKeyword does.
    /// </summary>
    /// <returns>the name as written</returns>
    private string ReadIdentifierOrKeyword()
    {
        if (CurrentType >= ParserUtil.FirstKeyword && CurrentType <= ParserUtil.LastKeyword)
        {
            string s = Current.AsIdentifier!;
            Read();
            return s;
        }

        return ReadIdentifier();
    }

    /// <summary>
    /// Reads the word when the token is an unquoted name with that spelling, as the
    /// reference's readIf(String) does.
    /// </summary>
    /// <param name="tokenName">the word</param>
    /// <returns>true when it was read</returns>
    private bool ReadIfName(string tokenName)
    {
        if (!Current.IsQuoted && IsWord(tokenName, consume: false))
        {
            Read();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses the specified collection of non-keywords, as the reference's
    /// ParserBase.parseNonKeywords does.
    /// </summary>
    /// <param name="nonKeywords">array of non-keywords in upper case</param>
    /// <returns>bit set of non-keywords, or null</returns>
    internal static BitArray? ParseNonKeywords(string[] nonKeywords)
    {
        if (nonKeywords.Length == 0)
        {
            return null;
        }

        BitArray set = new(Token.Tokens.Length);
        foreach (string nonKeyword in nonKeywords)
        {
            int index = Array.IndexOf(Token.Tokens, nonKeyword, ParserUtil.FirstKeyword,
                ParserUtil.LastKeyword + 1 - ParserUtil.FirstKeyword);
            if (index >= 0)
            {
                set[index] = true;
            }
        }

        bool any = false;
        for (int i = 0; i < set.Length; i++)
        {
            if (set[i])
            {
                any = true;
                break;
            }
        }

        return any ? set : null;
    }

    /// <summary>
    /// Formats a comma-separated list of keywords, as the reference's
    /// ParserBase.formatNonKeywords does.
    /// </summary>
    /// <param name="nonKeywords">bit set of non-keywords, or null</param>
    /// <returns>comma-separated list of non-keywords</returns>
    internal static string FormatNonKeywords(BitArray? nonKeywords)
    {
        if (nonKeywords is null || nonKeywords.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        for (int i = 0; i < nonKeywords.Length; i++)
        {
            if (nonKeywords[i] && i >= ParserUtil.FirstKeyword && i <= ParserUtil.LastKeyword)
            {
                if (builder.Length > 0)
                {
                    _ = builder.Append(',');
                }

                _ = builder.Append(Token.Tokens[i]);
            }
        }

        return builder.ToString();
    }
}
