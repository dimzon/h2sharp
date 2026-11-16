/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The H2 CSV arguments and option string, mapped onto CsvHelper's configuration. This is
 * deliberately only a mapping: parsing is CsvHelper's job, so the names, the defaults and
 * the boolean spellings are the reference's, while the character handling is not ours.
 */
using System;
using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.CsvTools;

/// <summary>H2's CSV options, translated to CsvHelper configuration.</summary>
internal sealed class CsvOptions
{
    internal char FieldSeparatorRead { get; private set; } = ',';

    internal string FieldSeparatorWrite { get; private set; } = ",";

    internal char FieldDelimiter { get; private set; } = '"';

    internal char Escape { get; private set; } = '"';

    internal char LineComment { get; private set; }

    internal string? NullString { get; private set; } = "";

    internal bool QuotedNulls { get; private set; }

    internal bool PreserveWhitespace { get; private set; }

    internal bool CaseSensitiveColumnNames { get; private set; }

    internal bool WriteColumnHeader { get; private set; } = true;

    internal string? LineSeparator { get; private set; }

    internal string? Charset { get; set; }

    /// <summary>
    /// Applies the option string the reference's <c>Csv.setOptions</c> reads. Answers the
    /// charset named there, if any.
    /// </summary>
    /// <param name="options">the option string</param>
    /// <returns>the charset, or null</returns>
    internal string? ApplyOptions(string options)
    {
        string? charset = null;
        foreach (string pair in StringUtils.ArraySplit(options, ' ', false)!)
        {
            if (pair.Length == 0)
            {
                continue;
            }

            int index = pair.IndexOf('=', StringComparison.Ordinal);
            string key = index < 0 ? pair : pair[..index].Trim();
            string value = index < 0 ? string.Empty : pair[(index + 1)..];
            char ch = value.Length == 0 ? '\0' : value[0];
            if (IsParam(key, "escape", "esc", "escapeCharacter"))
            {
                Escape = ch;
            }
            else if (IsParam(key, "fieldDelimiter", "fieldDelim"))
            {
                FieldDelimiter = ch;
            }
            else if (IsParam(key, "fieldSeparator", "fieldSep"))
            {
                FieldSeparatorRead = ch;
                FieldSeparatorWrite = value;
            }
            else if (IsParam(key, "lineComment", "lineCommentCharacter"))
            {
                LineComment = ch;
            }
            else if (IsParam(key, "lineSeparator", "lineSep"))
            {
                LineSeparator = value;
            }
            else if (IsParam(key, "null", "nullString"))
            {
                NullString = value;
            }
            else if (IsParam(key, "quotedNulls"))
            {
                QuotedNulls = ParseBoolean(value);
            }
            else if (IsParam(key, "charset", "characterSet"))
            {
                charset = value;
            }
            else if (IsParam(key, "preserveWhitespace"))
            {
                PreserveWhitespace = ParseBoolean(value);
            }
            else if (IsParam(key, "writeColumnHeader"))
            {
                WriteColumnHeader = ParseBoolean(value, defaultValue: true);
            }
            else if (IsParam(key, "caseSensitiveColumnNames"))
            {
                CaseSensitiveColumnNames = ParseBoolean(value);
            }
            else
            {
                throw DbException.GetUnsupportedException(key);
            }
        }

        Charset = charset;
        return charset;
    }

    /// <summary>
    /// Applies the positional arguments the reference reads when the option string is not
    /// written.
    /// </summary>
    internal void ApplyPositional(
        string? fieldSeparator, string? fieldDelimiter, string? escape, string? nullString,
        string? lineSeparator)
    {
        if (fieldSeparator is not null)
        {
            if (fieldSeparator.Length > 0)
            {
                FieldSeparatorRead = fieldSeparator[0];
            }

            FieldSeparatorWrite = fieldSeparator;
        }

        if (fieldDelimiter is not null)
        {
            FieldDelimiter = fieldDelimiter.Length == 0 ? '\0' : fieldDelimiter[0];
        }

        if (escape is not null)
        {
            Escape = escape.Length == 0 ? '\0' : escape[0];
        }

        if (nullString is not null)
        {
            NullString = nullString;
        }

        if (lineSeparator is not null)
        {
            LineSeparator = lineSeparator;
        }
    }

    /// <summary>The encoding a function reads or writes with.</summary>
    /// <param name="forReading">whether the file is read</param>
    internal Encoding GetEncoding(bool forReading)
    {
        if (Charset is not null)
        {
            return Encoding.GetEncoding(Charset);
        }

        // The reference reads UTF-8 by default and writes with the platform default, which
        // is UTF-8 on .NET.
        _ = forReading;
        return new UTF8Encoding(false);
    }

    /// <summary>The configuration a read uses.</summary>
    internal CsvConfiguration CreateReadConfiguration()
    {
        CsvConfiguration configuration = new(CultureInfo.InvariantCulture)
        {
            Delimiter = FieldSeparatorRead == '\0' ? "," : FieldSeparatorRead.ToString(),
            HasHeaderRecord = false,
            IgnoreBlankLines = true,
            DetectColumnCountChanges = false,
            BadDataFound = null,
            MissingFieldFound = null,
            AllowComments = LineComment != '\0',
            Comment = LineComment,
            TrimOptions = PreserveWhitespace ? TrimOptions.None : TrimOptions.Trim,
            WhiteSpaceChars = WhitespaceChars(),
            Encoding = GetEncoding(forReading: true),
        };
        ApplyQuoting(configuration);
        return configuration;
    }

    /// <summary>The configuration a write uses.</summary>
    internal CsvConfiguration CreateWriteConfiguration()
    {
        CsvConfiguration configuration = new(CultureInfo.InvariantCulture)
        {
            Delimiter = FieldSeparatorWrite.Length == 0 ? "," : FieldSeparatorWrite,
            HasHeaderRecord = false,
            NewLine = LineSeparator ?? Environment.NewLine,
            Encoding = GetEncoding(forReading: false),
        };
        ApplyQuoting(configuration);
        return configuration;
    }

    /// <summary>Whether a non-null field is quoted, as the reference's writer does.</summary>
    internal bool QuoteNonNull => Escape != '\0' && FieldDelimiter != '\0';

    private void ApplyQuoting(CsvConfiguration configuration)
    {
        if (FieldDelimiter == '\0')
        {
            // No field delimiter: no quoting at all. CsvHelper still needs a character for
            // its configuration, so the default one is kept but never applied.
            configuration.Mode = CsvMode.NoEscape;
            configuration.Quote = '"';
            configuration.Escape = Escape == '\0' ? '"' : Escape;
            return;
        }

        configuration.Quote = FieldDelimiter;
        configuration.Escape = Escape == '\0' ? FieldDelimiter : Escape;
        configuration.Mode = configuration.Escape == FieldDelimiter
            ? CsvMode.RFC4180
            : CsvMode.Escape;
    }

    private static char[] WhitespaceChars()
    {
        // The reference trims Java's char <= ' ' after an unquoted value.
        char[] chars = new char[' ' + 1];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)i;
        }

        return chars;
    }

    private static bool ParseBoolean(string value, bool defaultValue = false)
    {
        if (value.Length == 0)
        {
            return defaultValue;
        }

        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.Ordinal)
            || value.Equals("t", StringComparison.OrdinalIgnoreCase)
            || value.Equals("y", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsParam(string key, params string[] values)
    {
        foreach (string value in values)
        {
            if (key.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
