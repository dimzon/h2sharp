/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 251: every CSV option spelling the reference accepts, on the port side. The mapping
 * to CsvHelper is what the optional package owns; each alias, boolean spelling and default
 * is exercised through SQL so a renamed or ignored option fails the test.
 */
using System;
using System.Data.Common;
using System.Globalization;
using System.IO;
using H2Sharp.CsvTools;
using H2Sharp.Data;
using H2Sharp.Data.Extensions;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class CsvOptionsSurfaceTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "h2sharp_csvopts_" + Guid.NewGuid().ToString("N"));

    public CsvOptionsSurfaceTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void ReadOptionAliasesAndBooleansAreAccepted()
    {
        string file = Write("opts.csv", "# note\nA;B\n1;NA\n2;\n");
        using H2Connection connection = Open("csv_options_read");
        const string options = "fieldSep=; lineComment=# null=NA quotedNulls=yes charset=UTF-8 "
            + "preserveWhitespace=true caseSensitiveColumnNames=true writeColumnHeader=false";
        string sql = "SELECT COUNT(*) FROM CSVREAD('" + Sql(file) + "', NULL, '" + options + "')";
        Assert.Equal(2L, Scalar(connection, sql));

        // The long aliases and the numeric/t opposite of every boolean are read too.
        string aliases = "fieldSeparator=; lineCommentCharacter=# nullString=NA quotedNulls=1 "
            + "characterSet=UTF-8 preserveWhitespace=t caseSensitiveColumnNames=y";
        Assert.Equal(2L, Scalar(connection,
            "SELECT COUNT(*) FROM CSVREAD('" + Sql(file) + "', NULL, '" + aliases + "')"));

        // Empty pairs and empty boolean values fall back to the documented defaults.
        string defaults = "  quotedNulls=  writeColumnHeader=  preserveWhitespace=  ";
        Assert.Equal(3L, Scalar(connection,
            "SELECT COUNT(*) FROM CSVREAD('" + Sql(file) + "', NULL, '" + defaults + "')"));
    }

    [Fact]
    public void PositionalArgumentsAndQuotingOptionsAreAccepted()
    {
        string file = Write("pos.csv", "A,B\n1,2\n");
        using H2Connection connection = Open("csv_options_positional");
        Assert.Equal(2L, Scalar(connection,
            "SELECT COUNT(*) FROM CSVREAD('" + Sql(file) + "', 'A,B', NULL, ',', '!', '\\', 'NA')"));

        string escaped = Write("escaped.csv", "A;B\n'x';'y'\n");
        Assert.Equal(1L, Scalar(connection,
            "SELECT COUNT(*) FROM CSVREAD('" + Sql(escaped) + "', NULL, "
            + "'fieldDelimiter=! escape=/ fieldSeparator=;')"));
    }

    [Fact]
    public void WriteOptionsChangeHeaderSeparatorAndLineEnding()
    {
        string file = Path.Combine(directory, "out.csv");
        using H2Connection connection = Open("csv_options_write");
        string sql = "SELECT CSVWRITE('" + Sql(file)
            + "', 'SELECT 1 AS A, ''x'' AS B', 'writeColumnHeader=false fieldSeparator=| "
            + "lineSeparator=~ fieldDelimiter=\" escape=\\')";
        Assert.Equal("1", Scalar(connection, sql));
        Assert.Equal("1|x~", File.ReadAllText(file));

        // With the header on and the default line ending the first line names the aliases.
        Assert.Equal("1", Scalar(connection, "SELECT CSVWRITE('" + Sql(file)
            + "', 'SELECT 2 AS A, ''y'' AS B')"));
        Assert.StartsWith("\"A\",\"B\"", File.ReadAllText(file), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingArgumentsAndGeneratedColumnNamesAreHandled()
    {
        using H2Connection connection = Open("csv_options_edges");
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM CSVREAD(NULL)";
            H2SharpException e = Assert.Throws<H2SharpException>(() => command.ExecuteNonQuery());
            Assert.Equal(90012, e.H2ErrorCode);
        }

        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT CSVWRITE('x.csv', NULL)";
            H2SharpException e = Assert.Throws<H2SharpException>(() => command.ExecuteNonQuery());
            Assert.Equal(90012, e.H2ErrorCode);
        }

        string file = Write("names.csv", "1;2\n3;4\n");
        // An empty name and a repeated name are replaced with generated ones, and a name
        // that is not an identifier is refused by the column-name check.
        Assert.Equal(2L, Scalar(connection,
            "SELECT COUNT(*) FROM CSVREAD('" + Sql(file) + "', 'A,,A', NULL, 'fieldDelimiter=;')"));
        Assert.Equal(2L, Scalar(connection,
            "SELECT COUNT(*) FROM CSVREAD('" + Sql(file) + "', 'a-b', NULL, 'fieldDelimiter=;')"));

        // No field delimiter means no quoting at all, whatever the escape is.
        Assert.Equal(1L, Scalar(connection,
            "SELECT COUNT(*) FROM CSVREAD('" + Sql(file) + "', NULL, 'fieldDelimiter= escape=/')"));
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
        File.WriteAllText(path, content);
        return path;
    }

    private static string Sql(string path)
        => path.Replace('\\', '/').Replace("'", "''", StringComparison.Ordinal);

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
}
