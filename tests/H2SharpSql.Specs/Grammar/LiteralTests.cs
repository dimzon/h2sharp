// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/grammar.html — literal values, one test per checklist row.
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Grammar;

public sealed class LiteralTests
{
    /// <summary>docs/grammar.html:767 — string | { dollarQuotedString } | numeric | dateAndTime | boolean | bytes</summary>
    [Fact]
    public void Value_CommonLiteralKinds_FormARow() // GRM-0767
    {
        using IDbConnection connection = TestDatabase.Open();

        List<dynamic> rows = connection
            .Query("SELECT 'abc', $$abc$$, 1, DATE '2024-01-02', TRUE, X'41'")
            .AsList();

        Assert.Single(rows);
    }

    /// <summary>docs/grammar.html:768 — | interval | array | { geometry | json | uuid } | null</summary>
    [Fact]
    public void Value_ExoticLiteralKinds_FormARow() // GRM-0768
    {
        using IDbConnection connection = TestDatabase.Open();

        List<dynamic> rows = connection
            .Query("SELECT INTERVAL '1' DAY, ARRAY[1, 2], GEOMETRY 'POINT(1 1)', JSON '{\"a\": 1}', UUID '12345678-1234-1234-1234-1234567890ab', NULL")
            .AsList();

        Assert.Single(rows);
    }

    /// <summary>docs/grammar.html:788 — [ + | - ] { { number [ . [ number ] ] } | { . number } }</summary>
    [Fact]
    public void ApproximateNumeric_WithExponent_IsADouble() // GRM-0788
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1500.0, connection.QuerySingle<double>("SELECT 15E2"));
    }

    /// <summary>docs/grammar.html:789 — E [ + | - ] expNumber</summary>
    [Fact]
    public void ApproximateNumeric_SignedExponent_IsADouble() // GRM-0789
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1500.0, connection.QuerySingle<double>("SELECT 15E+2"));
        Assert.Equal(0.15, connection.QuerySingle<double>("SELECT 15E-2"));
    }

    /// <summary>docs/grammar.html:809 — ARRAY '[' [ expression [,...] ] ']'</summary>
    [Fact]
    public void ArrayLiteral_IndexesFromOne() // GRM-0809
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT (ARRAY[1, 2, 3])[2]"));
    }

    /// <summary>docs/grammar.html:828 — TRUE | FALSE | UNKNOWN</summary>
    [Fact]
    public void BooleanLiterals_UnknownIsNull() // GRM-0828
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT TRUE"));
        Assert.False(connection.QuerySingle<bool>("SELECT FALSE"));
        Assert.True(connection.QuerySingle<bool>("SELECT UNKNOWN IS NULL"));
    }

    /// <summary>docs/grammar.html:847 — X'hex' [ 'hex' [...] ]</summary>
    [Fact]
    public void BytesLiteral_AdjacentPartsAreConcatenated() // GRM-0847
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT OCTET_LENGTH(X'41' '42')"));
    }

    /// <summary>docs/grammar.html:866 — DATE '[-]yyyy-MM-dd'</summary>
    [Fact]
    public void DateLiteral_IsADate() // GRM-0866
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT DAYOFMONTH(DATE '2024-01-02')"));
    }

    /// <summary>docs/grammar.html:904 — $$anything$$</summary>
    [Fact]
    public void DollarQuotedString_KeepsTheContentAsIs() // GRM-0904
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("John's car", connection.QuerySingle<string>("SELECT $$John's car$$"));
    }

    /// <summary>docs/grammar.html:923 — [ + | - ] { { number [ . number ] } | { . number } }</summary>
    [Fact]
    public void ExactNumeric_IsAnExactNumber() // GRM-0923
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1.25m, connection.QuerySingle<decimal>("SELECT 1.25"));
    }

    /// <summary>docs/grammar.html:942 — [+|-] {0x|0X} { [_] { digit | a-f | A-F } [...] } [...]</summary>
    [Fact]
    public void HexNumber_IsRead() // GRM-0942
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(255, connection.QuerySingle<int>("SELECT 0xFF"));
    }

    /// <summary>docs/grammar.html:961 — [+|-] {0o|0O} { [_] { 0-7 } [...] } [...]</summary>
    [Fact]
    public void OctalNumber_IsRead() // GRM-0961
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(15, connection.QuerySingle<int>("SELECT 0o17"));
    }

    /// <summary>docs/grammar.html:980 — [+|-] {0b|0B} { [_] { 0-1 } [...] } [...]</summary>
    [Fact]
    public void BinaryNumber_IsRead() // GRM-0980
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(10, connection.QuerySingle<int>("SELECT 0b1010"));
    }

    /// <summary>docs/grammar.html:999 — [ + | - ] number</summary>
    [Fact]
    public void IntLiteral_IsAnInt() // GRM-0999
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(42, connection.QuerySingle<int>("SELECT 42"));
    }

    /// <summary>docs/grammar.html:1056 — [ + | - ] number</summary>
    [Fact]
    public void LongLiteral_IsALong() // GRM-1056
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3000000000L, connection.QuerySingle<long>("SELECT 3000000000"));
    }

    /// <summary>docs/grammar.html:1075 — NULL</summary>
    [Fact]
    public void NullLiteral_IsNull() // GRM-1075
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT NULL IS NULL"));
    }

    /// <summary>docs/grammar.html:1094 — digit [ [_] digit [...] ] [...]</summary>
    [Fact]
    public void Number_UnderscoresAreIgnored() // GRM-1094
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1000, connection.QuerySingle<int>("SELECT 1_000"));
    }

    /// <summary>docs/grammar.html:1113 — exactNumeric | approximateNumeric | int | long | hexNumber | octalNumber | binaryNumber</summary>
    [Fact]
    public void Numeric_UnderscoresInFractionAreIgnored() // GRM-1113
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(999999999.999999m, connection.QuerySingle<decimal>("SELECT 999_999_999.999_999"));
    }

    /// <summary>docs/grammar.html:1132 — [N]'anything' [...]</summary>
    [Fact]
    public void String_AdjacentPartsAreConcatenated() // GRM-1132
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("ABC", connection.QuerySingle<string>("SELECT 'A' 'B' 'C'"));
        Assert.Equal("abc", connection.QuerySingle<string>("SELECT N'abc'"));
    }

    /// <summary>docs/grammar.html:1133 — | U&{'anything' [...]} [ UESCAPE 'anything' ]</summary>
    [Fact]
    public void String_UnicodeEscape_IsDecoded() // GRM-1133
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("\u00a1", connection.QuerySingle<string>("SELECT U&'|00a1' UESCAPE '|'"));
    }

    /// <summary>docs/grammar.html:1153 — UUID '{ digit | a-f | A-F | - } [...]'</summary>
    [Fact]
    public void UuidLiteral_KeepsItsValue() // GRM-1153
    {
        using IDbConnection connection = TestDatabase.Open();

        string value = connection.QuerySingle<string>(
            "SELECT CAST(UUID '12345678-1234-1234-1234-123456789ABC' AS VARCHAR)");

        Assert.Equal("12345678-1234-1234-1234-123456789abc", value.ToLowerInvariant());
    }
}
