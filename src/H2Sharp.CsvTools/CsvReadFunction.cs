/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * CSVREAD as an optional table-valued .NET function. The SQL shape and the option names are
 * the reference's; the CSV parsing itself is CsvHelper's.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CsvHelper;
using H2Sharp.Data;
using H2Sharp.Data.Extensions;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.CsvTools;

/// <summary>The CSVREAD table function.</summary>
internal static class CsvReadFunction
{
    internal static void Register(IH2Extensions extensions)
    {
        extensions.RegisterTableUdf<string?>("CSVREAD",
            a => Read(a, null, null, null, null, null, null));
        extensions.RegisterTableUdf<string?, string?>("CSVREAD",
            (a, b) => Read(a, b, null, null, null, null, null));
        extensions.RegisterTableUdf<string?, string?, string?>("CSVREAD",
            (a, b, c) => Read(a, b, c, null, null, null, null));
        extensions.RegisterTableUdf<string?, string?, string?, string?>("CSVREAD",
            (a, b, c, d) => Read(a, b, c, d, null, null, null));
        extensions.RegisterTableUdf<string?, string?, string?, string?, string?>("CSVREAD",
            (a, b, c, d, e) => Read(a, b, c, d, e, null, null));
        extensions.RegisterTableUdf<string?, string?, string?, string?, string?, string?>("CSVREAD",
            (a, b, c, d, e, f) => Read(a, b, c, d, e, f, null));
        extensions.RegisterTableUdf<string?, string?, string?, string?, string?, string?, string?>("CSVREAD",
            Read);
    }

    private static H2TableUdfResult Read(
        string? fileName, string? columnList, string? options, string? fieldSeparator,
        string? fieldDelimiter, string? escape, string? nullString)
    {
        if (fileName is null)
        {
            throw DbException.Get(Api.ErrorCode.ParameterNotSet1, "fileName");
        }

        var csv = new CsvOptions();
        string? charset;
        if (options is not null && options.Contains('=', StringComparison.Ordinal))
        {
            charset = csv.ApplyOptions(options);
        }
        else
        {
            charset = options;
            csv.Charset = charset;
            csv.ApplyPositional(fieldSeparator, fieldDelimiter, escape, nullString, null);
        }

        string[]? columns = StringUtils.ArraySplit(columnList, csv.FieldSeparatorRead, true);
        List<string> columnNames = ReadColumnNames(fileName, columns, csv);
        List<H2TableUdfColumn> result = [];
        foreach (string name in columnNames)
        {
            result.Add(new H2TableUdfColumn(name, typeof(string)));
        }

        return new H2TableUdfResult(result, Rows(fileName, columns, columnNames.Count, csv));
    }

    private static List<string> ReadColumnNames(string fileName, string[]? columns, CsvOptions csv)
    {
        List<string> names = [];
        if (columns is null)
        {
            using CsvParser parser = Open(fileName, csv);
            if (ReadRecord(parser, fileName))
            {
                string[] header = parser.Record!;
                for (int i = 0; i < header.Length; i++)
                {
                    string name = header[i];
                    if (name.Length == 0)
                    {
                        name = "COLUMN" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else if (!csv.CaseSensitiveColumnNames && IsSimpleColumnName(name))
                    {
                        name = StringUtils.ToUpperEnglish(name);
                    }

                    names.Add(name);
                }
            }
        }
        else
        {
            names.AddRange(columns);
        }

        return MakeColumnNamesUnique(names);
    }

    private static IEnumerable<object?[]> Rows(
        string fileName, string[]? columns, int columnCount, CsvOptions csv)
    {
        using CsvParser parser = Open(fileName, csv);
        if (columns is null && !ReadRecord(parser, fileName))
        {
            yield break;
        }

        while (true)
        {
            bool next;
            try
            {
                next = parser.Read();
            }
            catch (IOException e)
            {
                throw DbException.Get(
                    Api.ErrorCode.IoException1, e, "IOException reading " + fileName);
            }
            catch (CsvHelperException e)
            {
                throw DbException.Get(Api.ErrorCode.ExceptionInFunction1, e, "CSVREAD");
            }

            if (!next)
            {
                yield break;
            }

            object?[] row = new object?[columnCount];
            for (int i = 0; i < columnCount; i++)
            {
                string? value = i < parser.Count ? parser.Record![i] : null;
                if (value is not null && csv.NullString is not null && value == csv.NullString)
                {
                    value = null;
                }

                row[i] = value;
            }

            yield return row;
        }
    }

    private static bool ReadRecord(CsvParser parser, string fileName)
    {
        try
        {
            return parser.Read();
        }
        catch (IOException e)
        {
            throw DbException.Get(
                Api.ErrorCode.IoException1, e, "IOException reading " + fileName);
        }
        catch (CsvHelperException e)
        {
            throw DbException.Get(Api.ErrorCode.ExceptionInFunction1, e, "CSVREAD");
        }
    }

    private static CsvParser Open(string fileName, CsvOptions csv)
    {
        try
        {
            var reader = new StreamReader(
                File.OpenRead(fileName), csv.GetEncoding(forReading: true),
                detectEncodingFromByteOrderMarks: true);
            return new CsvParser(reader, csv.CreateReadConfiguration());
        }
        catch (IOException e)
        {
            throw DbException.Get(
                Api.ErrorCode.IoException1, e, "IOException reading " + fileName);
        }
    }

    private static List<string> MakeColumnNamesUnique(List<string> names)
    {
        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            if (name.Length == 0)
            {
                name = "C" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            for (int j = 0; j < i; j++)
            {
                if (name == names[j])
                {
                    name += "1";
                    j = -1;
                }
            }

            names[i] = name;
        }

        return names;
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
}
