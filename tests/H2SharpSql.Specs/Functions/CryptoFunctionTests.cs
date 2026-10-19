// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/functions.html — cryptografic functions, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class CryptoFunctionTests
{
    /// <summary>docs/functions.html:1761 — ENCRYPT(algorithmString, keyBytes, dataBytes)</summary>
    [Fact]
    public void Encrypt_HidesTheData() // FUN-1761
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>(
            "SELECT ENCRYPT('AES', STRINGTOUTF8('0123456789abcdef'), STRINGTOUTF8('Hello')) " +
            "<> STRINGTOUTF8('Hello')"));
    }

    /// <summary>docs/functions.html:1779 — DECRYPT(algorithmString, keyBytes, dataBytes)</summary>
    [Fact]
    public void Decrypt_UndoesEncrypt() // FUN-1779
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("Hello", connection.QuerySingle<string>(
            "SELECT UTF8TOSTRING(DECRYPT('AES', STRINGTOUTF8('0123456789abcdef'), " +
            "ENCRYPT('AES', STRINGTOUTF8('0123456789abcdef'), STRINGTOUTF8('Hello'))))"));
    }

    /// <summary>docs/functions.html:1797 — HASH(algorithmString, expression [, iterationInt])</summary>
    [Fact]
    public void Hash_ReturnsTheDigest() // FUN-1797
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(32, connection.QuerySingle<int>(
            "SELECT OCTET_LENGTH(HASH('SHA-256', STRINGTOUTF8('x'), 1))"));
    }

    /// <summary>docs/functions.html:1835 — COMPRESS(dataBytes [, algorithmString])</summary>
    [Fact]
    public void Compress_ProducesBytes() // FUN-1835
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT COMPRESS(STRINGTOUTF8('Hello')) IS NOT NULL"));
    }

    /// <summary>docs/functions.html:1853 — EXPAND(bytes)</summary>
    [Fact]
    public void Expand_UndoesCompress() // FUN-1853
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("Hello", connection.QuerySingle<string>(
            "SELECT UTF8TOSTRING(EXPAND(COMPRESS(STRINGTOUTF8('Hello'))))"));
    }
}
