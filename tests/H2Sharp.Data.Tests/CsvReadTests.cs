/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The optional CSV tools on the port side: CSVREAD re-reads a rewritten file, a missing
 * file is the reference's IO exception, and the CsvHelper-backed behavior is pinned where
 * it deliberately differs from the Java parser (quoted empty fields, whitespace options).
 */
using System;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Text;
using H2Sharp.CsvTools;
using H2Sharp.Data;
using H2Sharp.Data.Extensions;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class CsvReadTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "h2sharp_csvread_" + Guid.NewGuid().ToString("N"));

    public CsvReadTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void ARewrittenFileIsReadAgain()
    {
        string file = Write("rows.csv", "ID,NAME\n1,one\n2,two\n");
        using H2Connection connection = Open("csv_recompile");
        string sql = "SELECT COUNT(*) FROM CSVREAD('" + Sql(file) + "')";
        Assert.Equal(2L, Scalar(connection, sql));
        Write("rows.csv", "ID,NAME\n1,one\n2,two\n3,three\n");
        Assert.Equal(3L, Scalar(connection, sql));
        Assert.Equal("three", Scalar(connection,
            "SELECT NAME FROM CSVREAD('" + Sql(file) + "') WHERE ID = '3'"));
    }

    [Fact]
    public void AMissingFileCarriesTheIoExceptionCode()
    {
        using H2Connection connection = Open("csv_missing_file");
        string missing = Path.Combine(directory, "missing.csv");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM CSVREAD('" + Sql(missing) + "')";
        H2SharpException e = Assert.Throws<H2SharpException>(() => command.ExecuteNonQuery());
        Assert.Equal(90028, e.H2ErrorCode);
    }

    [Fact]
    public void QuotedAndUnquotedEmptyFieldsAreBothNull()
    {
        // CsvHelper does not expose whether a field was quoted, so the port's policy is that
        // the null string (empty by default) is null wherever it appears. The reference keeps
        // a quoted empty string; this is the documented CsvHelper difference.
        string file = Write("empty.csv", "A\n\"\"\nX\n");
        using H2Connection connection = Open("csv_quoted_empty");
        Assert.Equal(1L, Scalar(connection, "SELECT COUNT(A) FROM CSVREAD('" + Sql(file) + "')"));
        Assert.Equal(1L, Scalar(connection,
            "SELECT COUNT(*) FROM CSVREAD('" + Sql(file) + "') WHERE A IS NULL"));
    }

    [Fact]
    public void WhitespaceFollowsThePreserveOption()
    {
        string file = Write("spaces.csv", "A,B\n x , y \n");
        using H2Connection connection = Open("csv_whitespace");
        Assert.Equal("x", Scalar(connection,
            "SELECT A FROM CSVREAD('" + Sql(file) + "')"));
        Assert.Equal(" x ", Scalar(connection,
            "SELECT A FROM CSVREAD('" + Sql(file) + "', NULL, 'preserveWhitespace=true')"));
    }

    [Fact]
    public void AnUnknownOptionIsAnUnsupportedException()
    {
        string file = Write("bogus.csv", "A\n1\n");
        using H2Connection connection = Open("csv_bogus_option");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM CSVREAD('" + Sql(file) + "', NULL, 'bogus=1')";
        H2SharpException e = Assert.Throws<H2SharpException>(() => command.ExecuteNonQuery());
        Assert.Equal(50100, e.H2ErrorCode);
    }

    private H2Connection Open(string name)
    {
        H2Connection connection = new($"Data Source={name};Storage=Memory");
        ((IH2Extensions)connection).UseCsvTools();
        connection.Open();
        return connection;
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static object? Scalar(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value switch
        {
            null => null,
            long l => l,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }

    private static string Sql(string path)
        => path.Replace('\\', '/').Replace("'", "''", StringComparison.Ordinal);
}
