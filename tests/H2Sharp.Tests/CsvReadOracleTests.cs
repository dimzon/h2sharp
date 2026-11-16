/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 222: CSVREAD against the reference — the header naming, the quoting and escaping,
 * the null string, the option string, the positional delimiters, the column list argument,
 * and a missing file.
 */
using System;
using System.IO;
using System.Text;
using H2Sharp.CsvTools;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class CsvReadOracleTests : ParserCoverageOracleBase, IDisposable
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "h2sharp_csv_" + Guid.NewGuid().ToString("N"));

    public CsvReadOracleTests() => Directory.CreateDirectory(directory);

    /// <inheritdoc/>
    protected override void ConfigurePort(IH2Extensions extensions) => extensions.UseCsvTools();

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

    [JavaOracleFact]
    public void HeaderQuotingAndNulls()
    {
        string file = Write("f1.csv", "ID,NAME\n1,Hello\n2,\"a,b\"\n3,\"say \"\"hi\"\"\"\n4,\n");
        AssertSameAsReference("csv_header",
            "SELECT * FROM CSVREAD('" + Sql(file) + "')",
            "SELECT ID, NAME FROM CSVREAD('" + Sql(file) + "') ORDER BY ID",
            "SELECT COUNT(*), COUNT(NAME) FROM CSVREAD('" + Sql(file) + "')",
            "SELECT NAME FROM CSVREAD('" + Sql(file) + "') WHERE ID = '3'");
    }

    [JavaOracleFact]
    public void OptionsAndTheColumnList()
    {
        string custom = Write("f2.csv", "ID;NAME\n1;a,b\n2;c\n");
        string noHeader = Write("f3.csv", "1|2\n3|4\n");
        string comment = Write("f4.csv", "#comment\nID,NAME\n1,x\n");
        AssertSameAsReference("csv_options",
            "SELECT * FROM CSVREAD('" + Sql(custom) + "', NULL, 'fieldSeparator=;')",
            "SELECT * FROM CSVREAD('" + Sql(custom) + "', NULL, NULL, ';')",
            "SELECT A, B FROM CSVREAD('" + Sql(noHeader) + "', 'A|B', NULL, '|')",
            "SELECT * FROM CSVREAD('" + Sql(comment) + "', NULL, 'lineComment=#')",
            "SELECT * FROM CSVREAD('" + Sql(noHeader) + "', 'A,B')");
    }

    [JavaOracleFact]
    public void GeneratedAndDeduplicatedHeaderNames()
    {
        string dup = Write("f5.csv", "A,A\n1,2\n");
        string empty = Write("f6.csv", "A,,C\n1,2,3\n");
        AssertSameAsReference("csv_names",
            "SELECT A, A1 FROM CSVREAD('" + Sql(dup) + "')",
            "SELECT A, COLUMN1, C FROM CSVREAD('" + Sql(empty) + "')");
    }

    [JavaOracleFact]
    public void AMissingFileIsAnIoException()
    {
        string missing = Path.Combine(directory, "missing.csv");
        AssertSameAsReference("csv_missing",
            "SELECT * FROM CSVREAD('" + Sql(missing) + "')");
    }

    [JavaOracleFact]
    public void PositionalSeparatorAndNullString()
    {
        string file = Write("f7.csv", "ID|NAME\n1|plain\n2|NULL\n");
        string utf8 = Write("f8.csv", "ID;NAME\n1;ж\n");
        string rows = Write("f9.csv", "ID,NAME\n1,one\n2,two\n");
        AssertSameAsReference("csv_positional",
            "SELECT * FROM CSVREAD('" + Sql(file) + "', NULL, NULL, '|', '\"', '\"', 'NULL')",
            "SELECT * FROM CSVREAD('" + Sql(utf8) + "', NULL, 'fieldSeparator=; charset=UTF-8')",
            "SELECT C.ID, C.NAME FROM CSVREAD('" + Sql(rows) + "') C WHERE C.ID > '1' ORDER BY C.ID",
            "CALL CSVREAD('" + Sql(rows) + "')",
            "CREATE TABLE T(ID INT, NAME VARCHAR(10))",
            "INSERT INTO T SELECT CAST(ID AS INT), NAME FROM CSVREAD('" + Sql(rows) + "')",
            "SELECT ID, NAME FROM T ORDER BY ID",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void WhitespaceAndMalformedRows()
    {
        string spaces = Write("f10.csv", "A,B\n x , y \n");
        string extra = Write("f12.csv", "A,B\n1,2,3\n");
        string shortRow = Write("f13.csv", "A,B\n1\n");
        string bom = Write("f14.csv", "\ufeffA,B\n1,2\n");
        string empty = Write("f15.csv", "");
        AssertSameAsReference("csv_edges",
            "SELECT A, B FROM CSVREAD('" + Sql(spaces) + "')",
            "SELECT A, B FROM CSVREAD('" + Sql(spaces) + "', NULL, 'preserveWhitespace=true')",
            "SELECT A, B FROM CSVREAD('" + Sql(extra) + "')",
            "SELECT A, B IS NULL FROM CSVREAD('" + Sql(shortRow) + "')",
            "SELECT * FROM CSVREAD('" + Sql(bom) + "')",
            "SELECT COUNT(*) FROM CSVREAD('" + Sql(empty) + "')",
            "SELECT * FROM CSVREAD('" + Sql(spaces) + "', NULL, 'bogus=1')");
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, content, Utf8);
        return path;
    }

    private static string Sql(string path) => path.Replace('\\', '/').Replace("'", "''", StringComparison.Ordinal);
}
