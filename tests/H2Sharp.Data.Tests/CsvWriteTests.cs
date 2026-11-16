/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * CSVWRITE on the port side: the query runs in the calling transaction, headers come from
 * the query labels, options map onto CsvHelper, and the result count is the data-row count.
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

public sealed class CsvWriteTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "h2sharp_csvwrite_" + Guid.NewGuid().ToString("N"));

    public CsvWriteTests() => Directory.CreateDirectory(directory);

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
    public void TheQueryResultIsWrittenWithItsLabels()
    {
        string file = Path.Combine(directory, "out.csv");
        using H2Connection connection = Open("csv_write_rows");
        Execute(connection, "CREATE TABLE T(ID INT, NAME VARCHAR(10))");
        Execute(connection, "INSERT INTO T VALUES (1, 'one'), (2, 'two')");

        Assert.Equal(2, Scalar(connection,
            "SELECT CSVWRITE('" + Sql(file) + "', 'SELECT ID, NAME AS ALIAS FROM T ORDER BY ID')"));
        string[] lines = Lines(file);
        Assert.Equal(["\"ID\",\"ALIAS\"", "\"1\",\"one\"", "\"2\",\"two\""], lines);
    }

    [Fact]
    public void TheQuerySeesTheCallingTransactionAndDoesNotCommitIt()
    {
        string file = Path.Combine(directory, "tx.csv");
        using H2Connection connection = Open("csv_write_tx");
        Execute(connection, "CREATE TABLE T(ID INT)");

        using (DbTransaction transaction = connection.BeginTransaction())
        {
            Execute(connection, "INSERT INTO T VALUES (1)", transaction);
            Assert.Equal(1, Scalar(connection,
                "SELECT CSVWRITE('" + Sql(file) + "', 'SELECT * FROM T')", transaction));
            Assert.Contains("\"1\"", Read(file), StringComparison.Ordinal);
            transaction.Rollback();
        }

        Assert.Equal(0L, ScalarLong(connection, "SELECT COUNT(*) FROM T"));
        Assert.Equal(0, Scalar(connection,
            "SELECT CSVWRITE('" + Sql(file) + "', 'SELECT * FROM T')"));
        Assert.Equal(["\"ID\""], Lines(file));
    }

    [Fact]
    public void OptionsAndTheNullStringMap()
    {
        string file = Path.Combine(directory, "options.csv");
        using H2Connection connection = Open("csv_write_options");
        Execute(connection, "CREATE TABLE T(A INT, B VARCHAR(10))");
        Execute(connection, "INSERT INTO T VALUES (1, NULL)");

        Assert.Equal(1, Scalar(connection,
            "SELECT CSVWRITE('" + Sql(file)
            + "', 'SELECT * FROM T', 'fieldSeparator=; null=NULL')"));
        Assert.Equal(["\"A\";\"B\"", "\"1\";NULL"], Lines(file));

        Assert.Equal(1, Scalar(connection,
            "SELECT CSVWRITE('" + Sql(file)
            + "', 'SELECT * FROM T', 'fieldSeparator=; null=NULL quotedNulls=true')"));
        Assert.Equal(["\"A\";\"B\"", "\"1\";\"NULL\""], Lines(file));

        Assert.Equal(1, Scalar(connection,
            "SELECT CSVWRITE('" + Sql(file)
            + "', 'SELECT * FROM T', NULL, ';', '\"', '\"', 'NULL')"));
        Assert.Equal(["\"A\";\"B\"", "\"1\";NULL"], Lines(file));
    }

    [Fact]
    public void TheWrittenFileReadsBack()
    {
        string file = Path.Combine(directory, "roundtrip.csv");
        using H2Connection connection = Open("csv_write_roundtrip");
        Execute(connection, "CREATE TABLE T(ID INT, NAME VARCHAR(10))");
        Execute(connection, "INSERT INTO T VALUES (1, 'a,b')");
        Assert.Equal(1, Scalar(connection,
            "SELECT CSVWRITE('" + Sql(file) + "', 'SELECT * FROM T')"));
        Assert.Equal("a,b", ScalarString(connection,
            "SELECT NAME FROM CSVREAD('" + Sql(file) + "')"));
    }

    [Fact]
    public void TheWriteErrorsCarryTheirCodes()
    {
        string file = Path.Combine(directory, "missing", "out.csv");
        using H2Connection connection = Open("csv_write_errors");
        Assert.Equal(90028, ErrorCode(connection,
            "SELECT CSVWRITE('" + Sql(file) + "', 'SELECT 1')"));
        Assert.Equal(90012, ErrorCode(connection,
            "SELECT CSVWRITE(NULL, 'SELECT 1')"));
        Assert.Equal(42104, ErrorCode(connection,
            "SELECT CSVWRITE('" + Sql(Path.Combine(directory, "x.csv"))
            + "', 'SELECT * FROM NOPE')"));
    }

    private H2Connection Open(string name)
    {
        H2Connection connection = new($"Data Source={name};Storage=Memory");
        ((IH2Extensions)connection).UseCsvTools();
        connection.Open();
        return connection;
    }

    private static string[] Lines(string file)
        => Read(file).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static string Read(string file) => File.ReadAllText(file, Encoding.UTF8);

    private static int ErrorCode(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Assert.Throws<H2SharpException>(() => command.ExecuteNonQuery()).H2ErrorCode;
    }

    private static void Execute(H2Connection connection, string sql, DbTransaction? transaction = null)
    {
        using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        _ = command.ExecuteNonQuery();
    }

    private static int Scalar(H2Connection connection, string sql, DbTransaction? transaction = null)
        => Convert.ToInt32(ScalarObject(connection, sql, transaction), CultureInfo.InvariantCulture);

    private static string ScalarString(
        H2Connection connection, string sql, DbTransaction? transaction = null)
        => Convert.ToString(ScalarObject(connection, sql, transaction), CultureInfo.InvariantCulture)!;

    private static long ScalarLong(H2Connection connection, string sql, DbTransaction? transaction = null)
        => Convert.ToInt64(ScalarObject(connection, sql, transaction), CultureInfo.InvariantCulture);

    private static object? ScalarObject(
        H2Connection connection, string sql, DbTransaction? transaction = null)
    {
        using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static string Sql(string path)
        => path.Replace('\\', '/').Replace("'", "''", StringComparison.Ordinal);
}
