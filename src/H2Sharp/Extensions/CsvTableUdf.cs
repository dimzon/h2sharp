/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * CSVREAD as the default table-valued .NET function. The SQL shape and the reading rules
 * are the reference's (org.h2.tools.Csv, read path): the same arguments, the same options,
 * the same header and quoting rules. The function is registered in every session, and a
 * registration with the same name and arity replaces it.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using H2Sharp.Data.Extensions;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Extensions;

/// <summary>The default CSVREAD table-valued function.</summary>
internal static class CsvTableUdf
{
    /// <summary>The argument counts the reference's CSVReadFunction reads.</summary>
    private const int MinArguments = 1;

    private const int MaxArguments = 7;

    internal static IEnumerable<TableUdfDefinition> CreateDefinitions()
    {
        for (int arity = MinArguments; arity <= MaxArguments; arity++)
        {
            yield return TableUdfDefinition.CreateRaw("CSVREAD", arity, deterministic: false, Read);
        }
    }

    private static H2TableUdfResult Read(Value[] args)
    {
        if (args[0] is ValueNull)
        {
            throw DbException.Get(Api.ErrorCode.ParameterNotSet1, "fileName");
        }

        string fileName = args[0].GetString()!;
        string? columnList = GetString(args, 1);
        string? options = GetString(args, 2);
        var csv = new CsvRead();
        string? charset;
        if (options is not null && options.Contains('=', StringComparison.Ordinal))
        {
            charset = csv.SetOptions(options);
        }
        else
        {
            charset = options;
            csv.SetDelimiters(GetString(args, 3), GetString(args, 4), GetString(args, 5));
            csv.SetNullString(GetString(args, 6));
        }

        string[]? columns = StringUtils.ArraySplit(columnList, csv.FieldSeparatorRead, true);
        using (CsvRead template = OpenOrThrow(csv, fileName, charset, columns))
        {
            List<H2TableUdfColumn> result = [];
            foreach (string name in template.ColumnNames)
            {
                result.Add(new H2TableUdfColumn(name, typeof(string)));
            }

            return new H2TableUdfResult(result, Rows(csv, fileName, charset, columns));
        }
    }

    private static CsvRead OpenOrThrow(
        CsvRead csv, string fileName, string? charset, string[]? columns)
    {
        try
        {
            return csv.Open(fileName, charset, columns);
        }
        catch (IOException e)
        {
            throw DbException.Get(
                Api.ErrorCode.IoException1, e, "IOException reading " + fileName);
        }
    }

    private static IEnumerable<object?[]> Rows(
        CsvRead csv, string fileName, string? charset, string[]? columns)
    {
        using CsvRead input = OpenOrThrow(csv, fileName, charset, columns);
        while (true)
        {
            string?[]? row;
            try
            {
                row = input.ReadRow();
            }
            catch (IOException e)
            {
                throw DbException.Get(
                    Api.ErrorCode.IoException1, e, "IOException reading " + fileName);
            }

            if (row is null)
            {
                yield break;
            }

            yield return row;
        }
    }

    private static string? GetString(Value[] args, int index)
        => index < args.Length && args[index] is not ValueNull ? args[index].GetString() : null;

    /// <summary>
    /// The read half of the reference's <c>org.h2.tools.Csv</c>: the character-level value
    /// reader, the header naming, and the options string.
    /// </summary>
    private sealed class CsvRead : IDisposable
    {
        private const int EndOfFile = -1;

        private StreamReader? reader;

        private int pushedBack = EndOfFile - 1;

        private bool endOfLine;

        private bool endOfFile;

        private char escapeCharacter = '"';

        private char fieldDelimiter = '"';

        private char fieldSeparatorRead = ',';

        private char lineComment;

        private string? nullString = "";

        private bool caseSensitiveColumnNames;

        private bool preserveWhitespace;

        private bool quotedNulls;

        private string[] columnNames = [];

        internal char FieldSeparatorRead => fieldSeparatorRead;

        internal IReadOnlyList<string> ColumnNames => columnNames;

        internal CsvRead Open(string fileName, string? charset, string[]? columns)
        {
            Encoding encoding;
            if (charset is null)
            {
                encoding = new UTF8Encoding(false);
            }
            else
            {
                encoding = Encoding.GetEncoding(charset);
            }

            var opened = new CsvRead
            {
                escapeCharacter = escapeCharacter,
                fieldDelimiter = fieldDelimiter,
                fieldSeparatorRead = fieldSeparatorRead,
                lineComment = lineComment,
                nullString = nullString,
                caseSensitiveColumnNames = caseSensitiveColumnNames,
                preserveWhitespace = preserveWhitespace,
                quotedNulls = quotedNulls,
            };
            opened.reader = new StreamReader(
                File.OpenRead(fileName), encoding, detectEncodingFromByteOrderMarks: false);
            // The reference ignores a pseudo-BOM in front of the first value.
            int bom = opened.reader.Read();
            if (bom != 0xfeff)
            {
                if (bom >= 0)
                {
                    opened.pushedBack = bom;
                }
                else
                {
                    opened.endOfFile = true;
                }
            }

            if (columns is null)
            {
                opened.ReadHeader();
            }
            else
            {
                opened.columnNames = columns;
            }

            opened.MakeColumnNamesUnique();
            return opened;
        }

        internal string?[]? ReadRow()
        {
            if (reader is null)
            {
                return null;
            }

            string?[] row = new string?[columnNames.Length];
            int i = 0;
            while (true)
            {
                string? v = ReadValue();
                if (v is null)
                {
                    if (endOfLine)
                    {
                        if (i == 0)
                        {
                            if (endOfFile)
                            {
                                return null;
                            }

                            // A line that holds no values at all is skipped.
                            continue;
                        }

                        break;
                    }
                }

                if (i < row.Length)
                {
                    row[i++] = quotedNulls && (v is null || v == nullString) ? null : v;
                }

                if (endOfLine)
                {
                    break;
                }
            }

            return row;
        }

        public void Dispose() => reader?.Dispose();

        private void ReadHeader()
        {
            List<string> list = [];
            while (true)
            {
                string? v = ReadValue();
                if (v is null)
                {
                    if (endOfLine)
                    {
                        if (endOfFile || list.Count > 0)
                        {
                            break;
                        }
                    }
                    else
                    {
                        list.Add("COLUMN" + list.Count.ToString(CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    if (v.Length == 0)
                    {
                        v = "COLUMN" + list.Count.ToString(CultureInfo.InvariantCulture);
                    }
                    else if (!caseSensitiveColumnNames && IsSimpleColumnName(v))
                    {
                        v = StringUtils.ToUpperEnglish(v);
                    }

                    list.Add(v);
                    if (endOfLine)
                    {
                        break;
                    }
                }
            }

            columnNames = [.. list];
        }

        private void MakeColumnNamesUnique()
        {
            for (int i = 0; i < columnNames.Length; i++)
            {
                string n = columnNames[i];
                string name = n.Length == 0
                    ? "C" + (i + 1).ToString(CultureInfo.InvariantCulture)
                    : n;
                for (int j = 0; j < i; j++)
                {
                    if (name == columnNames[j])
                    {
                        name += "1";
                        j = -1;
                    }
                }

                columnNames[i] = name;
            }
        }

        private static bool IsSimpleColumnName(string columnName)
        {
            for (int i = 0; i < columnName.Length; i++)
            {
                char ch = columnName[i];
                if (i == 0)
                {
                    if (ch != '_' && !char.IsLetter(ch))
                    {
                        return false;
                    }
                }
                else if (ch != '_' && !char.IsLetterOrDigit(ch))
                {
                    return false;
                }
            }

            return columnName.Length != 0;
        }

        internal string? SetOptions(string options)
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
                    escapeCharacter = ch;
                }
                else if (IsParam(key, "fieldDelimiter", "fieldDelim"))
                {
                    fieldDelimiter = ch;
                }
                else if (IsParam(key, "fieldSeparator", "fieldSep"))
                {
                    fieldSeparatorRead = ch;
                }
                else if (IsParam(key, "lineComment", "lineCommentCharacter"))
                {
                    lineComment = ch;
                }
                else if (IsParam(key, "lineSeparator", "lineSep"))
                {
                    // Only the writer uses the line separator.
                }
                else if (IsParam(key, "null", "nullString"))
                {
                    nullString = value;
                }
                else if (IsParam(key, "quotedNulls"))
                {
                    quotedNulls = ParseBoolean(value);
                }
                else if (IsParam(key, "charset", "characterSet"))
                {
                    charset = value;
                }
                else if (IsParam(key, "preserveWhitespace"))
                {
                    preserveWhitespace = ParseBoolean(value);
                }
                else if (IsParam(key, "writeColumnHeader"))
                {
                    // Only the writer uses the column header flag.
                }
                else if (IsParam(key, "caseSensitiveColumnNames"))
                {
                    caseSensitiveColumnNames = ParseBoolean(value);
                }
                else
                {
                    throw DbException.GetUnsupportedException(key);
                }
            }

            return charset;
        }

        internal void SetDelimiters(string? fieldSeparator, string? fieldDelimiter, string? escape)
        {
            if (fieldSeparator is not null)
            {
                if (fieldSeparator.Length > 0)
                {
                    fieldSeparatorRead = fieldSeparator[0];
                }
            }

            if (fieldDelimiter is not null)
            {
                this.fieldDelimiter = fieldDelimiter.Length == 0 ? '\0' : fieldDelimiter[0];
            }

            if (escape is not null)
            {
                escapeCharacter = escape.Length == 0 ? '\0' : escape[0];
            }
        }

        internal void SetNullString(string? value) => nullString = value;

        private string? ReadValue()
        {
            endOfLine = false;
            // The reference's value starts where the reader stood before the leading blanks
            // were skipped, so PRESERVE_WHITESPACE keeps them.
            var leading = new StringBuilder();
            while (true)
            {
                int ch = ReadChar();
                if (ch == fieldDelimiter)
                {
                    return ReadDelimitedValue(out ch);
                }

                if (ch == '\n' || ch < 0 || ch == '\r')
                {
                    endOfLine = true;
                    return null;
                }

                if (ch == fieldSeparatorRead)
                {
                    return null;
                }

                if (ch <= ' ')
                {
                    _ = leading.Append((char)ch);
                    continue;
                }

                if (lineComment != 0 && ch == lineComment)
                {
                    do
                    {
                        ch = ReadChar();
                    }
                    while (ch != '\n' && ch >= 0 && ch != '\r');

                    endOfLine = true;
                    return null;
                }

                var value = new StringBuilder();
                _ = value.Append(leading).Append((char)ch);
                while (true)
                {
                    ch = ReadChar();
                    if (ch == fieldSeparatorRead)
                    {
                        break;
                    }

                    if (ch == '\n' || ch < 0 || ch == '\r')
                    {
                        endOfLine = true;
                        break;
                    }

                    value.Append((char)ch);
                }

                string s = value.ToString();
                if (!preserveWhitespace)
                {
                    s = s.Trim();
                }

                return s == nullString ? null : s;
            }
        }

        private string ReadDelimitedValue(out int terminator)
        {
            var raw = new StringBuilder();
            bool containsEscape = false;
            while (true)
            {
                int ch = ReadChar();
                if (ch == fieldDelimiter)
                {
                    ch = ReadChar();
                    if (ch != fieldDelimiter)
                    {
                        terminator = ch;
                        break;
                    }

                    containsEscape = true;
                    _ = raw.Append(fieldDelimiter).Append(fieldDelimiter);
                }
                else if (ch == escapeCharacter && escapeCharacter != fieldDelimiter)
                {
                    int next = ReadChar();
                    if (next < 0)
                    {
                        terminator = ch;
                        break;
                    }

                    containsEscape = true;
                    _ = raw.Append((char)ch).Append((char)next);
                }
                else if (ch < 0)
                {
                    terminator = ch;
                    break;
                }
                else
                {
                    _ = raw.Append((char)ch);
                }
            }

            string value = containsEscape ? UnEscape(raw.ToString()) : raw.ToString();
            while (true)
            {
                int ch = terminator;
                if (ch == fieldSeparatorRead)
                {
                    break;
                }

                if (ch == '\n' || ch < 0 || ch == '\r')
                {
                    endOfLine = true;
                    break;
                }

                if (ch == ' ' || ch == '\t')
                {
                    terminator = ReadChar();
                    continue;
                }

                if (ch >= 0)
                {
                    pushedBack = ch;
                }

                break;
            }

            return value;
        }

        private string UnEscape(string s)
        {
            var buff = new StringBuilder(s.Length);
            int start = 0;
            while (true)
            {
                int idx = s.IndexOf(escapeCharacter, start);
                if (idx < 0)
                {
                    idx = s.IndexOf(fieldDelimiter, start);
                    if (idx < 0)
                    {
                        break;
                    }
                }

                _ = buff.Append(s, start, idx - start);
                if (idx == s.Length - 1)
                {
                    start = s.Length;
                    break;
                }

                _ = buff.Append(s[idx + 1]);
                start = idx + 2;
            }

            _ = buff.Append(s, start, s.Length - start);
            return buff.ToString();
        }

        private int ReadChar()
        {
            if (pushedBack != EndOfFile - 1)
            {
                int ch = pushedBack;
                pushedBack = EndOfFile - 1;
                return ch;
            }

            if (reader is null || endOfFile)
            {
                return EndOfFile;
            }

            int value = reader.Read();
            if (value < 0)
            {
                endOfFile = true;
            }

            return value;
        }

        private static bool ParseBoolean(string value)
            => value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.Ordinal);

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
}
