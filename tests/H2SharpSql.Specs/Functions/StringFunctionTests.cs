// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/functions.html — string functions, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class StringFunctionTests
{
    /// <summary>docs/functions.html:1927 — ASCII(string)</summary>
    [Fact]
    public void Ascii_ReturnsTheCharacterCode() // FUN-1927
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(65, connection.QuerySingle<int>("SELECT ASCII('A')"));
    }

    /// <summary>docs/functions.html:1945 — BIT_LENGTH(bytes)</summary>
    [Fact]
    public void BitLength_CountsTheBits() // FUN-1945
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(16, connection.QuerySingle<int>("SELECT BIT_LENGTH(X'4142')"));
    }

    /// <summary>docs/functions.html:1963 — { CHAR_LENGTH | CHARACTER_LENGTH | { LENGTH } } ( string )</summary>
    [Fact]
    public void CharLength_CountsTheCharacters() // FUN-1963
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>("SELECT CHAR_LENGTH('abc')"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT CHARACTER_LENGTH('abc')"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT LENGTH('abc')"));
    }

    /// <summary>docs/functions.html:1981 — OCTET_LENGTH(bytes)</summary>
    [Fact]
    public void OctetLength_CountsTheBytes() // FUN-1981
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>("SELECT OCTET_LENGTH(STRINGTOUTF8('abc'))"));
    }

    /// <summary>docs/functions.html:1999 — { CHAR | CHR } ( int )</summary>
    [Fact]
    public void Char_ReturnsTheCharacter() // FUN-1999
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("A", connection.QuerySingle<string>("SELECT CHAR(65)"));
        Assert.Equal("A", connection.QuerySingle<string>("SELECT CHR(65)"));
    }

    /// <summary>docs/functions.html:2017 — CONCAT(string, string [,...])</summary>
    [Fact]
    public void Concat_JoinsTheStrings() // FUN-2017
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("abc", connection.QuerySingle<string>("SELECT CONCAT('a', 'b', 'c')"));
    }

    /// <summary>docs/functions.html:2035 — CONCAT_WS(separatorString, string, string [,...])</summary>
    [Fact]
    public void ConcatWs_PutsTheSeparatorBetween() // FUN-2035
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("a,b", connection.QuerySingle<string>("SELECT CONCAT_WS(',', 'a', 'b')"));
    }

    /// <summary>docs/functions.html:2053 — DIFFERENCE(string, string)</summary>
    [Fact]
    public void Difference_ComparesTheSoundexValues() // FUN-2053
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT DIFFERENCE('Anne', 'Ann') BETWEEN 0 AND 4"));
    }

    /// <summary>docs/functions.html:2071 — HEXTORAW(string)</summary>
    [Fact]
    public void HexToRaw_ConvertsTheDigitsToBytes() // FUN-2071
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT HEXTORAW('41') = X'41'"));
    }

    /// <summary>docs/functions.html:2089 — RAWTOHEX({string|bytes})</summary>
    [Fact]
    public void RawToHex_ConvertsToHexDigits() // FUN-2089
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("4142", connection.QuerySingle<string>("SELECT RAWTOHEX(X'4142')"));
        // H2 converts character input by UTF-16 code points.
        Assert.Equal("00410042", connection.QuerySingle<string>("SELECT RAWTOHEX('AB')"));
    }

    /// <summary>docs/functions.html:2107 — INSERT(originalString, startInt, lengthInt, addString)</summary>
    [Fact]
    public void Insert_ReplacesAPartOfTheString() // FUN-2107
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("HXXo", connection.QuerySingle<string>("SELECT INSERT('Hello', 2, 3, 'XX')"));
    }

    /// <summary>docs/functions.html:2125 — { LOWER | { LCASE } } ( string )</summary>
    [Fact]
    public void Lower_LowersTheCase() // FUN-2125
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("abc", connection.QuerySingle<string>("SELECT LOWER('AbC')"));
        Assert.Equal("abc", connection.QuerySingle<string>("SELECT LCASE('AbC')"));
    }

    /// <summary>docs/functions.html:2143 — { UPPER | { UCASE } } ( string )</summary>
    [Fact]
    public void Upper_UppersTheCase() // FUN-2143
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("ABC", connection.QuerySingle<string>("SELECT UPPER('AbC')"));
        Assert.Equal("ABC", connection.QuerySingle<string>("SELECT UCASE('AbC')"));
    }

    /// <summary>docs/functions.html:2161 — LEFT(string, int)</summary>
    [Fact]
    public void Left_TakesTheFirstCharacters() // FUN-2161
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("He", connection.QuerySingle<string>("SELECT LEFT('Hello', 2)"));
    }

    /// <summary>docs/functions.html:2179 — RIGHT(string, int)</summary>
    [Fact]
    public void Right_TakesTheLastCharacters() // FUN-2179
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("lo", connection.QuerySingle<string>("SELECT RIGHT('Hello', 2)"));
    }

    /// <summary>docs/functions.html:2197 — { LOCATE(searchString, string [, startInt]) }</summary>
    [Fact]
    public void Locate_FindsTheSubstring() // FUN-2197
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>("SELECT LOCATE('l', 'Hello')"));
        Assert.Equal(4, connection.QuerySingle<int>("SELECT LOCATE('l', 'Hello', 4)"));
    }

    /// <summary>docs/functions.html:2198 — | { INSTR(string, searchString, [, startInt]) }</summary>
    [Fact]
    public void Instr_FindsTheSubstring() // FUN-2198
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>("SELECT INSTR('Hello', 'l')"));
        Assert.Equal(4, connection.QuerySingle<int>("SELECT INSTR('Hello', 'l', 4)"));
    }

    /// <summary>docs/functions.html:2199 — | { POSITION(searchString, string) }</summary>
    [Fact]
    public void Position_FindsTheSubstring() // FUN-2199
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>("SELECT POSITION('l', 'Hello')"));
    }

    /// <summary>docs/functions.html:2219 — LPAD(string, int[, paddingString])</summary>
    [Fact]
    public void Lpad_PadsOnTheLeft() // FUN-2219
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("...Hi", connection.QuerySingle<string>("SELECT LPAD('Hi', 5, '.')"));
    }

    /// <summary>docs/functions.html:2237 — RPAD(string, int[, paddingString])</summary>
    [Fact]
    public void Rpad_PadsOnTheRight() // FUN-2237
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("Hi...", connection.QuerySingle<string>("SELECT RPAD('Hi', 5, '.')"));
    }

    /// <summary>docs/functions.html:2255 — LTRIM(string [, charactersToTrimString])</summary>
    [Fact]
    public void Ltrim_TrimsOnTheLeft() // FUN-2255
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("x", connection.QuerySingle<string>("SELECT LTRIM('  x')"));
        Assert.Equal("abc", connection.QuerySingle<string>("SELECT LTRIM('xxabc', 'x')"));
    }

    /// <summary>docs/functions.html:2273 — RTRIM(string [, charactersToTrimString])</summary>
    [Fact]
    public void Rtrim_TrimsOnTheRight() // FUN-2273
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("x", connection.QuerySingle<string>("SELECT RTRIM('x  ')"));
        Assert.Equal("abc", connection.QuerySingle<string>("SELECT RTRIM('abcxx', 'x')"));
    }

    /// <summary>docs/functions.html:2291 — BTRIM(string [, charactersToTrimString])</summary>
    [Fact]
    public void Btrim_TrimsOnBothSides() // FUN-2291
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("x", connection.QuerySingle<string>("SELECT BTRIM('  x  ')"));
        Assert.Equal("abc", connection.QuerySingle<string>("SELECT BTRIM('xxabcxx', 'x')"));
    }

    /// <summary>docs/functions.html:2309 — TRIM ( [ [ LEADING | TRAILING | BOTH ] [ characterToTrimString ] FROM ] string )</summary>
    [Fact]
    public void Trim_SupportsTheStandardForms() // FUN-2309
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("x", connection.QuerySingle<string>("SELECT TRIM('  x  ')"));
        Assert.Equal("abcxx", connection.QuerySingle<string>("SELECT TRIM(LEADING 'x' FROM 'xxabcxx')"));
        Assert.Equal("abc", connection.QuerySingle<string>("SELECT TRIM(BOTH 'x' FROM 'xxabcxx')"));
    }

    /// <summary>docs/functions.html:2327 — REGEXP_REPLACE(inputString, regexString, replacementString [, flagsString])</summary>
    [Fact]
    public void RegexpReplace_ReplacesWithRegex() // FUN-2327
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("HeLLo", connection.QuerySingle<string>("SELECT REGEXP_REPLACE('Hello', 'l', 'L')"));
        Assert.Equal("X", connection.QuerySingle<string>("SELECT REGEXP_REPLACE('a', 'A', 'X', 'i')"));
    }

    /// <summary>docs/functions.html:2345 — REGEXP_LIKE(inputString, regexString [, flagsString])</summary>
    [Fact]
    public void RegexpLike_MatchesWithRegex() // FUN-2345
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT REGEXP_LIKE('Hello', '.*ell.*')"));
        Assert.True(connection.QuerySingle<bool>("SELECT REGEXP_LIKE('abc', 'ABC', 'i')"));
    }

    /// <summary>docs/functions.html:2363 — REGEXP_SUBSTR(inputString, regexString [, positionInt, occurrenceInt, flagsString, groupInt])</summary>
    [Fact]
    public void RegexpSubstr_ExtractsWithRegex() // FUN-2363
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("ll", connection.QuerySingle<string>("SELECT REGEXP_SUBSTR('Hello', 'l+')"));
        Assert.Equal("l", connection.QuerySingle<string>("SELECT REGEXP_SUBSTR('Hello', 'l', 4)"));
    }

    /// <summary>docs/functions.html:2381 — REPEAT(string, int)</summary>
    [Fact]
    public void Repeat_RepeatsTheString() // FUN-2381
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("ababab", connection.QuerySingle<string>("SELECT REPEAT('ab', 3)"));
    }

    /// <summary>docs/functions.html:2399 — REPLACE(string, searchString [, replacementString])</summary>
    [Fact]
    public void Replace_ReplacesOrRemovesTheSubstring() // FUN-2399
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("HeLLo", connection.QuerySingle<string>("SELECT REPLACE('Hello', 'l', 'L')"));
        Assert.Equal("Heo", connection.QuerySingle<string>("SELECT REPLACE('Hello', 'l')"));
    }

    /// <summary>docs/functions.html:2417 — SOUNDEX(string)</summary>
    [Fact]
    public void Soundex_ReturnsFourCharacters() // FUN-2417
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(4, connection.QuerySingle<int>("SELECT LENGTH(SOUNDEX('Anne'))"));
        Assert.True(connection.QuerySingle<bool>("SELECT SOUNDEX('Anne') = SOUNDEX('Ann')"));
    }

    /// <summary>docs/functions.html:2435 — SPACE(int)</summary>
    [Fact]
    public void Space_ReturnsTheSpaces() // FUN-2435
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>("SELECT LENGTH(SPACE(3))"));
    }

    /// <summary>docs/functions.html:2453 — STRINGDECODE(string)</summary>
    [Fact]
    public void StringDecode_UndoesStringEncode() // FUN-2453
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("A", connection.QuerySingle<string>("SELECT STRINGDECODE('\\u0041')"));
        Assert.Equal("a", connection.QuerySingle<string>("SELECT STRINGDECODE(STRINGENCODE('a'))"));
    }

    /// <summary>docs/functions.html:2471 — STRINGENCODE(string)</summary>
    [Fact]
    public void StringEncode_KeepsPlainText() // FUN-2471
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("abc", connection.QuerySingle<string>("SELECT STRINGENCODE('abc')"));
    }

    /// <summary>docs/functions.html:2489 — STRINGTOUTF8(string)</summary>
    [Fact]
    public void StringToUtf8_ReturnsTheBytes() // FUN-2489
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>("SELECT OCTET_LENGTH(STRINGTOUTF8('abc'))"));
    }

    /// <summary>docs/functions.html:2507 — SUBSTRING ( {string|bytes} FROM startInt [ FOR lengthInt ] )</summary>
    [Fact]
    public void Substring_FromForForm() // FUN-2507
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("ell", connection.QuerySingle<string>("SELECT SUBSTRING('Hello' FROM 2 FOR 3)"));
    }

    /// <summary>docs/functions.html:2508 — | { { SUBSTRING | SUBSTR } ( {string|bytes}, startInt [, lengthInt ] ) }</summary>
    [Fact]
    public void Substring_CommaFormAndSubstrAlias() // FUN-2508
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("ell", connection.QuerySingle<string>("SELECT SUBSTRING('Hello', 2, 3)"));
        Assert.Equal("ello", connection.QuerySingle<string>("SELECT SUBSTR('Hello', 2)"));
    }

    /// <summary>docs/functions.html:2527 — UTF8TOSTRING(bytes)</summary>
    [Fact]
    public void Utf8ToString_UndoesStringToUtf8() // FUN-2527
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("abc", connection.QuerySingle<string>("SELECT UTF8TOSTRING(STRINGTOUTF8('abc'))"));
    }

    /// <summary>docs/functions.html:2545 — QUOTE_IDENT(string)</summary>
    [Fact]
    public void QuoteIdent_QuotesTheIdentifier() // FUN-2545
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("\"test\"", connection.QuerySingle<string>("SELECT QUOTE_IDENT('test')"));
    }
}
