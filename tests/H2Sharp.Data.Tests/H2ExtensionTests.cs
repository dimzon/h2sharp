/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Host-provided scalar functions and the native hash registry, through the ADO.NET provider.
 */
using System;
using System.Data.Common;
using H2Sharp.Crypto;
using H2Sharp.Data;
using H2Sharp.Hashes;
using H2Sharp.JavaLegacy;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2ExtensionTests
{
    [Fact]
    public void ARegisteredUdfIsCallableFromSql()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).RegisterUdf<int, int>("DOUBLE_IT", value => value * 2);
        ((IH2Extensions)connection).RegisterUdf<string?, string>("PREFIX", value => "x:" + value);
        ((IH2Extensions)connection).RegisterUdf<int, int>("NON_NULL_INT", value => value);
        connection.Open();

        Assert.Equal("6", Scalar(connection, "SELECT DOUBLE_IT(3)"));
        Assert.Equal("x:a", Scalar(connection, "SELECT PREFIX('a')"));
        Assert.Equal("x:", Scalar(connection, "SELECT PREFIX(NULL)"));
        Assert.Equal("-", Scalar(connection, "SELECT NON_NULL_INT(NULL)"));
    }

    [Fact]
    public void RegistrationIsOnlyAllowedWhileClosed()
    {
        using H2Connection connection = NewConnection();
        connection.Open();

        Assert.Throws<InvalidOperationException>(
            () => ((IH2Extensions)connection).RegisterUdf<int>("ANY", () => 1));
    }

    [Fact]
    public void RegistrationAfterDisposeIsRefused()
    {
        H2Connection connection = NewConnection();
        connection.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => ((IH2Extensions)connection).RegisterUdf<int>("ANY", () => 1));
    }

    [Fact]
    public void IndependentDatabasesDoNotShareUdfs()
    {
        using H2Connection first = NewConnection();
        ((IH2Extensions)first).RegisterUdf<int>("ONLY_FIRST", () => 1);
        first.Open();

        using H2Connection second = NewConnection();
        second.Open();

        Assert.Equal("1", Scalar(first, "SELECT ONLY_FIRST()"));
        Assert.ThrowsAny<Exception>(() => Scalar(second, "SELECT ONLY_FIRST()"));
    }

    [Fact]
    public void JavaLegacyRestoresTheJdkRandomSequence()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).UseJavaLegacy();
        connection.Open();

        Assert.Equal("0.7308781907032909", Scalar(connection, "SELECT RAND(1)"));
    }

    [Fact]
    public void JavaLegacyProvidesOraHash()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).UseJavaLegacy();
        connection.Open();

        Assert.Equal("3509391659", Scalar(connection, "SELECT ORA_HASH(1)"));
        Assert.Equal("2441322222", Scalar(connection, "SELECT ORA_HASH(1, 4294967295, 10)"));

        using DbCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE ORA_TEST(I BINARY(3), B BLOB, S VARCHAR, C CLOB)";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO ORA_TEST VALUES (X'010203', X'010203', 'abc', 'abc')";
        command.ExecuteNonQuery();
        Assert.Equal("2562861693", Scalar(connection, "SELECT ORA_HASH(I) FROM ORA_TEST"));
        Assert.Equal("2562861693", Scalar(connection, "SELECT ORA_HASH(B) FROM ORA_TEST"));
        Assert.Equal("1191608682", Scalar(connection, "SELECT ORA_HASH(S) FROM ORA_TEST"));
        Assert.Equal("1191608682", Scalar(connection, "SELECT ORA_HASH(C) FROM ORA_TEST"));
    }

    [Fact]
    public void OptionalHashesProvideSha224AndSha3224()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).UseH2Hashes();
        connection.Open();

        Assert.Equal("7bd9bf319961cfdb7fc9351debbcc8a80143d5d0909e8cbccd8b5f0f",
            Scalar(connection, "SELECT RAWTOHEX(HASH('SHA-224', STRINGTOUTF8('****** Message digest test ******'), 1))"));
        Assert.Equal("cb91fec022d97ed63622d382e36e336b65a806888416a549fb4db390",
            Scalar(connection, "SELECT RAWTOHEX(HASH('SHA3-224', STRINGTOUTF8('****** Message digest test ******'), 1))"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalHashesCanKeepOrOverrideBclAlgorithms(bool overrideBcl)
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).UseH2Hashes(overrideBcl);
        connection.Open();

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            Scalar(connection, "SELECT RAWTOHEX(HASH('SHA-256', STRINGTOUTF8('abc'), 1))"));
    }

    [Theory]
    [InlineData("SHA-224", 28)]
    [InlineData("SHA3-224", 28)]
    [InlineData("SHA-512/224", 28)]
    [InlineData("SHA-512/256", 32)]
    [InlineData("SHAKE128", 32)]
    [InlineData("SHAKE256", 64)]
    [InlineData("CSHAKE128", 32)]
    [InlineData("CSHAKE256", 64)]
    [InlineData("TURBOSHAKE128", 32)]
    [InlineData("TURBOSHAKE256", 64)]
    [InlineData("KECCAK-256", 32)]
    [InlineData("KECCAK-384", 48)]
    [InlineData("KECCAK-512", 64)]
    [InlineData("BLAKE2B", 64)]
    [InlineData("BLAKE2S", 32)]
    [InlineData("BLAKE3", 32)]
    [InlineData("KT128", 32)]
    [InlineData("KT256", 64)]
    [InlineData("ASCON-HASH256", 32)]
    [InlineData("ASCON-XOF128", 32)]
    [InlineData("KUPYNA", 64)]
    [InlineData("KUPYNA-256", 32)]
    [InlineData("LSH-256", 32)]
    [InlineData("LSH-512", 64)]
    [InlineData("STREEBOG", 64)]
    [InlineData("STREEBOG-256", 32)]
    [InlineData("RIPEMD-160", 20)]
    [InlineData("SM3", 32)]
    [InlineData("WHIRLPOOL", 64)]
    public void OptionalHashesRegisterEveryPackageAlgorithm(string algorithm, int length)
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).UseH2Hashes();
        connection.Open();

        Assert.Equal(length.ToString(),
            Scalar(connection, $"SELECT OCTET_LENGTH(HASH('{algorithm}', STRINGTOUTF8('abc'), 1))"));
    }

    [Theory]
    [InlineData("AES", "20cff95086c35d686e3f3d9914ac9271")]
    [InlineData("XTEA", "fb760ea9626ba45cce4b0e9a737ba19b")]
    [InlineData("FOG", "7c505a5bbad9a9b134353637b9a1a9b1")]
    public void OptionalCryptoMatchesH2(string algorithm, string expected)
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).UseCrypto();
        connection.Open();

        Assert.Equal(expected,
            Scalar(connection, $"SELECT RAWTOHEX(ENCRYPT('{algorithm}', STRINGTOUTF8('0123456789abcdef'), STRINGTOUTF8('Hello')))"));
        Assert.Equal("Hello",
            Scalar(connection, $"SELECT TRIM(CHAR(0) FROM UTF8TOSTRING(DECRYPT('{algorithm}', STRINGTOUTF8('0123456789abcdef'), ENCRYPT('{algorithm}', STRINGTOUTF8('0123456789abcdef'), STRINGTOUTF8('Hello')))))"));
    }

    [Fact]
    public void NativeHashesAreRegisteredAutomatically()
    {
        using H2Connection connection = NewConnection();
        connection.Open();

        Assert.Equal("32", Scalar(connection,
            "SELECT OCTET_LENGTH(HASH('SHA-256', STRINGTOUTF8('x'), 1))"));
    }

    [Fact]
    public void NativeUuidVersionsHaveTheirVersionNibble()
    {
        using H2Connection connection = NewConnection();
        connection.Open();

        string v4 = Scalar(connection, "SELECT RANDOM_UUID(4)");
        string v7 = Scalar(connection, "SELECT RANDOM_UUID(7)");

        Assert.Equal('4', v4[14]);
        Assert.Equal('7', v7[14]);
    }

    private static H2Connection NewConnection()
        => new($"jdbc:h2:mem:ext_{Guid.NewGuid():N};IGNORE_UNKNOWN_SETTINGS=TRUE");

    private static string Scalar(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? "-"
            : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "-";
    }
}
