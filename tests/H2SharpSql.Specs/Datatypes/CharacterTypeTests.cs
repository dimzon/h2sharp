// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/datatypes.html — character data types, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Datatypes;

public sealed class CharacterTypeTests
{
    /// <summary>docs/datatypes.html:261 — { CHARACTER | CHAR | NATIONAL { CHARACTER | CHAR } | NCHAR }</summary>
    [Fact]
    public void Character_SupportsAllSpellings() // TYP-0261
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("a  ", connection.QuerySingle<string>("SELECT CAST('a' AS CHARACTER(3))"));
        Assert.Equal("a  ", connection.QuerySingle<string>("SELECT CAST('a' AS CHAR(3))"));
        Assert.Equal("a  ", connection.QuerySingle<string>("SELECT CAST('a' AS NATIONAL CHARACTER(3))"));
        Assert.Equal("a  ", connection.QuerySingle<string>("SELECT CAST('a' AS NCHAR(3))"));
    }

    /// <summary>docs/datatypes.html:262 — [ ( lengthInt [CHARACTERS|OCTETS] ) ]</summary>
    [Fact]
    public void Character_WithLengthCharactersOrOctets() // TYP-0262
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>(
            "SELECT CHAR_LENGTH(CAST('abc' AS CHARACTER(3 CHARACTERS)))"));
        Assert.Equal(3, connection.QuerySingle<int>(
            "SELECT CHAR_LENGTH(CAST('abc' AS CHARACTER(3 OCTETS)))"));
    }

    /// <summary>docs/datatypes.html:281 — { { CHARACTER | CHAR } VARYING</summary>
    [Fact]
    public void CharacterVarying_SupportsBothSpellings() // TYP-0281
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("ab", connection.QuerySingle<string>("SELECT CAST('ab' AS CHARACTER VARYING(5))"));
        Assert.Equal("ab", connection.QuerySingle<string>("SELECT CAST('ab' AS CHAR VARYING(5))"));
    }

    /// <summary>docs/datatypes.html:282 — | VARCHAR</summary>
    [Fact]
    public void CharacterVarying_VarcharSpelling() // TYP-0282
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("ab", connection.QuerySingle<string>("SELECT CAST('ab' AS VARCHAR(5))"));
    }

    /// <summary>docs/datatypes.html:283 — | { NATIONAL { CHARACTER | CHAR } | NCHAR } VARYING</summary>
    [Fact]
    public void CharacterVarying_NationalSpellings() // TYP-0283
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("ab", connection.QuerySingle<string>("SELECT CAST('ab' AS NATIONAL CHARACTER VARYING(5))"));
        Assert.Equal("ab", connection.QuerySingle<string>("SELECT CAST('ab' AS NATIONAL CHAR VARYING(5))"));
        Assert.Equal("ab", connection.QuerySingle<string>("SELECT CAST('ab' AS NCHAR VARYING(5))"));
    }

    /// <summary>docs/datatypes.html:284 — | { VARCHAR_CASESENSITIVE } }</summary>
    [Fact]
    public void CharacterVarying_CaseSensitiveSpelling() // TYP-0284
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("ab", connection.QuerySingle<string>("SELECT CAST('ab' AS VARCHAR_CASESENSITIVE(5))"));
    }

    /// <summary>docs/datatypes.html:285 — [ ( lengthInt [CHARACTERS|OCTETS] ) ]</summary>
    [Fact]
    public void CharacterVarying_WithLengthCharactersOrOctets() // TYP-0285
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>(
            "SELECT CHAR_LENGTH(CAST('abc' AS VARCHAR(3 CHARACTERS)))"));
        Assert.Equal(3, connection.QuerySingle<int>(
            "SELECT CHAR_LENGTH(CAST('abc' AS VARCHAR(3 OCTETS)))"));
    }

    /// <summary>docs/datatypes.html:307 — { { CHARACTER | CHAR } LARGE OBJECT | CLOB</summary>
    [Fact]
    public void CharacterLargeObject_SupportsAllSpellings() // TYP-0307
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("ab", connection.QuerySingle<string>("SELECT CAST('ab' AS CHARACTER LARGE OBJECT)"));
        Assert.Equal("ab", connection.QuerySingle<string>("SELECT CAST('ab' AS CHAR LARGE OBJECT)"));
        Assert.Equal("ab", connection.QuerySingle<string>("SELECT CAST('ab' AS CLOB)"));
    }

    /// <summary>docs/datatypes.html:308 — | { NATIONAL CHARACTER | NCHAR } LARGE OBJECT | NCLOB }</summary>
    [Fact]
    public void CharacterLargeObject_NationalSpellings() // TYP-0308
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("ab", connection.QuerySingle<string>("SELECT CAST('ab' AS NATIONAL CHARACTER LARGE OBJECT)"));
        Assert.Equal("ab", connection.QuerySingle<string>("SELECT CAST('ab' AS NCHAR LARGE OBJECT)"));
        Assert.Equal("ab", connection.QuerySingle<string>("SELECT CAST('ab' AS NCLOB)"));
    }

    /// <summary>docs/datatypes.html:309 — [ ( lengthLong [K|M|G|T|P] [CHARACTERS|OCTETS]) ]</summary>
    [Fact]
    public void CharacterLargeObject_WithScaledLength() // TYP-0309
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("ab", connection.QuerySingle<string>("SELECT CAST('ab' AS CLOB(1 K))"));
        Assert.Equal("ab", connection.QuerySingle<string>("SELECT CAST('ab' AS CLOB(1 K CHARACTERS))"));
        Assert.Equal("ab", connection.QuerySingle<string>("SELECT CAST('ab' AS CLOB(1 K OCTETS))"));
    }

    /// <summary>docs/datatypes.html:329 — VARCHAR_IGNORECASE</summary>
    [Fact]
    public void VarcharIgnoreCase_ComparesIgnoringTheCase() // TYP-0329
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>(
            "SELECT CAST('A' AS VARCHAR_IGNORECASE) = CAST('a' AS VARCHAR_IGNORECASE)"));
    }

    /// <summary>docs/datatypes.html:330 — [ ( lengthInt [CHARACTERS|OCTETS] ) ]</summary>
    [Fact]
    public void VarcharIgnoreCase_WithLength() // TYP-0330
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>(
            "SELECT CAST('A' AS VARCHAR_IGNORECASE(5)) = CAST('a' AS VARCHAR_IGNORECASE(5))"));
    }
}
