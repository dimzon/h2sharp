/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 222: CSVREAD's own behavior on the port side — the statement is recompiled so a
 * rewritten file is seen, and a missing file is the reference's IO exception.
 */
using System;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Text;
using H2Sharp.Data;
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
        string file = Path.Combine(directory, "rows.csv");
        File.WriteAllText(file, "ID,NAME\n1,one\n2,two\n", new UTF8Encoding(false));
        using H2Connection connection = new("Data Source=csv_recompile;Storage=Memory");
        connection.Open();

        string sql = "SELECT COUNT(*) FROM CSVREAD('" + Sql(file) + "')";
        Assert.Equal(2L, Scalar(connection, sql));
        File.WriteAllText(file, "ID,NAME\n1,one\n2,two\n3,three\n", new UTF8Encoding(false));
        Assert.Equal(3L, Scalar(connection, sql));
        Assert.Equal("three", Scalar(connection,
            "SELECT NAME FROM CSVREAD('" + Sql(file) + "') WHERE ID = '3'"));
    }

    [Fact]
    public void AMissingFileCarriesTheIoExceptionCode()
    {
        using H2Connection connection = new("Data Source=csv_missing_file;Storage=Memory");
        connection.Open();
        string missing = Path.Combine(directory, "missing.csv");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM CSVREAD('" + Sql(missing) + "')";
        H2SharpException e = Assert.Throws<H2SharpException>(() => command.ExecuteNonQuery());
        Assert.Equal(90028, e.H2ErrorCode);
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
