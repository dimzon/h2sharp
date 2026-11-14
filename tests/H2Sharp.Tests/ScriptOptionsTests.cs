/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * SCRIPT options the reference has and the port now reads: the CHARSET of the TO file and
 * the SCHEMA/TABLE filters, plus the constants section the plain dump was missing.
 */
using System;
using System.IO;
using System.Text;
using H2Sharp.Command;
using H2Sharp.Engine;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ScriptOptionsTests : IDisposable
{
    private readonly SessionLocal session;
    private readonly string directory;

    public ScriptOptionsTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "script_options_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        session = new SessionLocal(
            new Database(ConnectionInfo.Memory("script_options_" + Guid.NewGuid().ToString("N"))), "sa");
    }

    public void Dispose()
    {
        try { Directory.Delete(directory, true); } catch { }
    }

    private void Execute(string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        if (container.IsQuery())
        {
            using Result.ResultInterface result = container.ExecuteQuery(10, 0, false);
            while (result.Next())
            {
            }
        }
        else
        {
            _ = container.ExecuteUpdate(false);
        }
    }

    private string FilePath(string name) => Path.Combine(directory, name).Replace("\\", "/");

    private string ReadText(string name) => File.ReadAllText(Path.Combine(directory, name));

    [Fact]
    public void TableFilterWritesOnlyTheChosenTable()
    {
        Execute("CREATE TABLE A(ID INT)");
        Execute("CREATE TABLE B(ID INT)");
        Execute("INSERT INTO A VALUES (1)");
        Execute("INSERT INTO B VALUES (1)");
        string file = FilePath("tables.sql");

        Execute("SCRIPT NODATA TO '" + file + "' TABLE A");

        string text = ReadText("tables.sql");
        Assert.Contains("\"A\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"B\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaFilterWritesOnlyTheChosenSchema()
    {
        Execute("CREATE SCHEMA S1");
        Execute("CREATE SCHEMA S2");
        Execute("CREATE TABLE S1.T1(ID INT)");
        Execute("CREATE TABLE S2.T2(ID INT)");
        Execute("CREATE TABLE P(ID INT)");
        string file = FilePath("schemas.sql");

        Execute("SCRIPT NODATA TO '" + file + "' SCHEMA S1");

        string text = ReadText("schemas.sql");
        Assert.Contains("\"S1\"", text, StringComparison.Ordinal);
        Assert.Contains("\"T1\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"T2\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TABLE \"P\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownSchemaIsRefused()
    {
        H2SharpException e = Assert.Throws<H2SharpException>(
            () => Execute("SCRIPT NODATA SCHEMA NOPE"));
        Assert.Equal(Api.ErrorCode.SchemaNotFound1, e.H2ErrorCode);
    }

    [Fact]
    public void TheDefaultCharsetIsUtf8WithoutBom()
    {
        Execute("CREATE TABLE T(ID INT)");
        string file = FilePath("utf8.sql");

        Execute("SCRIPT NODATA TO '" + file + "'");

        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, "utf8.sql"));
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal((byte)'-', bytes[0]);
        Assert.Contains("CREATE MEMORY TABLE", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void Utf16LeIsWrittenWithoutBom()
    {
        Execute("CREATE TABLE T(ID INT)");
        string file = FilePath("utf16.sql");

        Execute("SCRIPT NODATA TO '" + file + "' CHARSET 'UTF-16LE'");

        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, "utf16.sql"));
        Assert.Equal((byte)'-', bytes[0]);
        Assert.Equal(0x00, bytes[1]);
        string text = new UnicodeEncoding(bigEndian: false, byteOrderMark: false)
            .GetString(bytes);
        Assert.Contains("CREATE MEMORY TABLE", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UTF-8")]
    [InlineData("UTF-16")]
    [InlineData("UTF-16BE")]
    [InlineData("UTF-16LE")]
    [InlineData("UTF-32")]
    [InlineData("UTF-32BE")]
    [InlineData("UTF-32LE")]
    [InlineData("US-ASCII")]
    [InlineData("ISO-8859-1")]
    public void EverySupportedCharsetRoundTrips(string charset)
    {
        Execute("CREATE TABLE T(ID INT)");
        string name = charset.Replace("-", "_") + ".sql";
        string file = FilePath(name);

        Execute("SCRIPT NODATA TO '" + file + "' CHARSET '" + charset + "'");

        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, name));
        Encoding encoding = charset.ToUpperInvariant() switch
        {
            "UTF-8" => new UTF8Encoding(false, true),
            "UTF-16" => new UnicodeEncoding(true, true, true),
            "UTF-16BE" => new UnicodeEncoding(true, false, true),
            "UTF-16LE" => new UnicodeEncoding(false, false, true),
            "UTF-32" => new UTF32Encoding(true, true, true),
            "UTF-32BE" => new UTF32Encoding(true, false, true),
            "UTF-32LE" => new UTF32Encoding(false, false, true),
            _ => Encoding.GetEncoding(charset),
        };
        Assert.Contains("CREATE MEMORY TABLE", encoding.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownCharsetIsRefused()
    {
        string file = FilePath("koi8.sql");
        H2SharpException e = Assert.Throws<H2SharpException>(
            () => Execute("SCRIPT NODATA TO '" + file + "' CHARSET 'KOI8-R'"));
        Assert.Contains("charset", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstantsAreScripted()
    {
        Execute("CREATE CONSTANT C VALUE 1");
        string file = FilePath("constants.sql");

        Execute("SCRIPT NODATA TO '" + file + "'");

        Assert.Contains("CREATE CONSTANT", ReadText("constants.sql"), StringComparison.Ordinal);
        Assert.Contains("\"C\"", ReadText("constants.sql"), StringComparison.Ordinal);
    }
}
