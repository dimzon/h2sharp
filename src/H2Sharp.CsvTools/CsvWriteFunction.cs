/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * CSVWRITE as an optional .NET function. It runs its query on the calling statement's own
 * session through H2Database.CurrentSessionConnection, so the query sees exactly the
 * transaction the caller is in, and writes the rows with CsvHelper.
 */
using System;
using System.Data.Common;
using System.Globalization;
using System.IO;
using CsvHelper;
using H2Sharp.Data;
using H2DbException = H2Sharp.Message.DbException;

namespace H2Sharp.CsvTools;

/// <summary>The CSVWRITE scalar function.</summary>
internal static class CsvWriteFunction
{
    internal static void Register(IH2Extensions extensions)
    {
        extensions.RegisterUdf<string?, string?, int>("CSVWRITE",
            (a, b) => Write(a, b, null, null, null, null, null, null));
        extensions.RegisterUdf<string?, string?, string?, int>("CSVWRITE",
            (a, b, c) => Write(a, b, c, null, null, null, null, null));
        extensions.RegisterUdf<string?, string?, string?, string?, int>("CSVWRITE",
            (a, b, c, d) => Write(a, b, c, d, null, null, null, null));
        extensions.RegisterUdf<string?, string?, string?, string?, string?, int>("CSVWRITE",
            (a, b, c, d, e) => Write(a, b, c, d, e, null, null, null));
        extensions.RegisterUdf<string?, string?, string?, string?, string?, string?, int>("CSVWRITE",
            (a, b, c, d, e, f) => Write(a, b, c, d, e, f, null, null));
        extensions.RegisterUdf<string?, string?, string?, string?, string?, string?, string?, int>(
            "CSVWRITE", (a, b, c, d, e, f, g) => Write(a, b, c, d, e, f, g, null));
        extensions.RegisterUdf<string?, string?, string?, string?, string?, string?, string?, string?, int>(
            "CSVWRITE", Write);
    }

    private static int Write(
        string? fileName, string? query, string? options, string? fieldSeparator,
        string? fieldDelimiter, string? escape, string? nullString, string? lineSeparator)
    {
        if (fileName is null)
        {
            throw H2DbException.Get(Api.ErrorCode.ParameterNotSet1, "fileName");
        }

        if (query is null)
        {
            throw H2DbException.Get(Api.ErrorCode.ParameterNotSet1, "query");
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
            csv.ApplyPositional(fieldSeparator, fieldDelimiter, escape, nullString, lineSeparator);
        }

        H2Connection connection = H2Database.CurrentSessionConnection
            ?? throw H2DbException.Get(
                Api.ErrorCode.GeneralError1, "CSVWRITE requires a session context");

        using DbCommand command = connection.CreateCommand();
        command.CommandText = query;
        using DbDataReader reader = command.ExecuteReader();
        int columnCount = reader.FieldCount;
        try
        {
            using var stream = new StreamWriter(
                File.Create(fileName), csv.GetEncoding(forReading: false));
            using var writer = new CsvWriter(stream, csv.CreateWriteConfiguration());
            if (csv.WriteColumnHeader)
            {
                for (int i = 0; i < columnCount; i++)
                {
                    writer.WriteField(reader.GetName(i), csv.QuoteNonNull);
                }

                writer.NextRecord();
            }

            int rows = 0;
            while (reader.Read())
            {
                for (int i = 0; i < columnCount; i++)
                {
                    if (reader.IsDBNull(i))
                    {
                        if (csv.NullString is not null)
                        {
                            writer.WriteField(
                                csv.NullString,
                                csv.QuotedNulls && csv.FieldDelimiter != '\0');
                        }
                        else
                        {
                            writer.WriteField(string.Empty, false);
                        }
                    }
                    else
                    {
                        writer.WriteField(reader.GetString(i), csv.QuoteNonNull);
                    }
                }

                writer.NextRecord();
                rows++;
            }

            writer.Flush();
            return rows;
        }
        catch (IOException e)
        {
            throw H2DbException.Get(
                Api.ErrorCode.IoException1, e, "IOException writing " + fileName);
        }
    }
}
