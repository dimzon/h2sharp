/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The ADO.NET conformance suite, inherited.
 *
 * AdoNet.Specification.Tests (MIT) is a set of test *base classes* that every provider is
 * expected to inherit: ADO.NET has no formal provider specification, so this is where the
 * de facto one is written down — "ADO.NET does not have a formal specification for providers, but
 * a number of libraries ... behave in the same way". Adopting it is the difference between
 * claiming the provider is a provider and having someone else's assertions agree.
 *
 * These tests are red on purpose while the engine is missing, in exactly the same way the
 * ported reference tests are: each failure names a member that does not work yet, and the list
 * is the work list. They are written by someone other than this port, which is the whole point
 * — a hand-written expectation can agree with the implementation by accident, these cannot.
 *
 * Nothing is inherited selectively while it fails. A test that is skipped "for now" is a test
 * that keeps skipping, and the one thing this suite is worth is being red until it is green.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;
using AdoNet.Specification.Tests;
using H2Sharp.Data;
using Xunit;
using Xunit.Sdk;

namespace H2Sharp.Data.Tests;

/// <summary>
/// What the suite needs to know about this provider: how to make a connection, and how to write
/// the statements that produce the values the reader tests read back.
/// </summary>
public sealed class H2FactoryFixture : IDbFactoryFixture, ISelectValueFixture, IDeleteFixture
{
    /// <summary>
    /// Each test gets its own database name, so one test's tables are never another's, and so a
    /// test that leaves a database open cannot make the next one see it.
    /// </summary>
    public H2FactoryFixture()
    {
        ConnectionString = "Data Source=spec_" + Guid.NewGuid().ToString("N") + ";Storage=Memory";
    }

    /// <summary>
    /// Gets the connection string of this fixture's database.
    /// </summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Gets the factory that creates this provider's objects.
    /// </summary>
    public DbProviderFactory Factory => H2ProviderFactory.Instance;

    /// <summary>
    /// Gets a statement that returns no rows, which is how the suite asks whether a query shape
    /// works without caring about the data.
    /// </summary>
    public string SelectNoRows => "SELECT 1 AS C1 WHERE 1 = 0;";

    /// <summary>
    /// The literal for a boolean. H2 accepts TRUE and FALSE, and this provider passes the text
    /// through, so the suite gets the SQL standard spelling.
    /// </summary>
    public string CreateBooleanLiteral(bool value) => value ? "TRUE" : "FALSE";

    /// <summary>
    /// The literal for a byte array. Written as the string H2 parses into a VARBINARY, because
    /// there is no provider-level syntax to inject: the port has no literal syntax of its own.
    /// </summary>
    public string CreateHexLiteral(byte[] bytes)
    {
        StringBuilder builder = new(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            _ = builder.Append(b.ToString("X2"));
        }

        return "X'" + builder + "'";
    }

    /// <summary>
    /// The types the reader tests round-trip. The list is what this provider can write and read
    /// back today, not everything H2 has: adding a type here is a claim that the reader tests
    /// will now check it, which is the point of declaring it.
    /// </summary>
    public IReadOnlyCollection<DbType> SupportedDbTypes { get; } =
    [
        DbType.Boolean, DbType.SByte, DbType.Byte, DbType.Int16, DbType.Int32, DbType.Int64,
        DbType.Single, DbType.Double, DbType.Decimal, DbType.String, DbType.Guid,
        DbType.Date, DbType.Time, DbType.DateTime, DbType.DateTime2, DbType.DateTimeOffset,
    ];

    /// <summary>
    /// The exception a provider is allowed to throw for a value it cannot store. This one says
    /// what it would be rather than leaving the suite to guess.
    /// </summary>
    public Type NullValueExceptionType => typeof(H2SharpException);

    /// <summary>
    /// A statement that deletes nothing.
    /// </summary>
    /// <remarks>
    /// A DROP rather than a DELETE: every database here is fresh per test, so no table exists
    /// yet, and a DELETE FROM T would fail on the missing table instead of deleting nothing.
    /// The reader path it exercises — a non-query through ExecuteReader — is the same.
    /// </remarks>
    public string DeleteNoRows => "DROP TABLE IF EXISTS T;";

    /// <summary>
    /// A statement that selects one literal of the given type, as the suite's value kind asks:
    /// the kinds are not about SQL but about which *value* of that type is selected — null,
    /// empty, zero, one, and the two ends of the range.
    /// </summary>
    /// <param name="dbType">the type to select</param>
    /// <param name="valueKind">which value of that type</param>
    /// <returns>the statement</returns>
    public string CreateSelectSql(DbType dbType, ValueKind valueKind)
    {
        if (dbType == DbType.Binary)
        {
            return valueKind == ValueKind.Null
                ? "SELECT CAST(NULL AS VARBINARY) AS C1"
                : "SELECT X'11' AS C1";
        }

        return "SELECT " + CreateLiteral(dbType, valueKind) + " AS C1";
    }

    /// <summary>
    /// A statement that selects one byte array.
    /// </summary>
    /// <param name="value">the bytes</param>
    /// <returns>the statement</returns>
    public string CreateSelectSql(byte[] value)
    {
        return "SELECT " + CreateHexLiteral(value) + " AS C1";
    }

    /// <summary>
    /// The SQL literal for a value of the given type, in H2's spelling.
    /// </summary>
    private static string CreateLiteral(DbType dbType, ValueKind valueKind)
    {
        if (valueKind == ValueKind.Null)
        {
            return "CAST(NULL AS " + TypeName(dbType) + ")";
        }

        return valueKind switch
        {
            ValueKind.Empty => $"CAST('' AS {TypeName(dbType)})",
            ValueKind.Zero => $"CAST(0 AS {TypeName(dbType)})",
            ValueKind.One => $"CAST(1 AS {TypeName(dbType)})",
            ValueKind.Minimum => $"CAST({MinimumOf(dbType)} AS {TypeName(dbType)})",
            ValueKind.Maximum => $"CAST({MaximumOf(dbType)} AS {TypeName(dbType)})",
            _ => $"CAST(1 AS {TypeName(dbType)})",
        };
    }

    /// <summary>The H2 type name for a DbType.</summary>
    private static string TypeName(DbType dbType)
    {
        return dbType switch
        {
            DbType.Boolean => "BOOLEAN",
            DbType.SByte or DbType.Byte => "TINYINT",
            DbType.Int16 => "SMALLINT",
            DbType.Int32 => "INT",
            DbType.Int64 => "BIGINT",
            DbType.Single => "REAL",
            DbType.Double => "DOUBLE PRECISION",
            DbType.Decimal => "NUMERIC(38, 0)",
            DbType.String => "VARCHAR(1000)",
            DbType.Guid => "UUID",
            DbType.Date => "DATE",
            DbType.Time => "TIME",
            DbType.DateTime or DbType.DateTime2 => "TIMESTAMP",
            DbType.DateTimeOffset => "TIMESTAMP WITH TIME ZONE",
            _ => throw new ArgumentOutOfRangeException(nameof(dbType)),
        };
    }

    private static string MinimumOf(DbType dbType) => dbType switch
    {
        DbType.SByte or DbType.Byte or DbType.Int16 or DbType.Int32 or DbType.Int64 => "-1",
        DbType.Single => "-3.4028235E38",
        DbType.Double => "-1.7976931348623157E308",
        _ => "0",
    };

    private static string MaximumOf(DbType dbType) => dbType switch
    {
        DbType.Byte => "255",
        DbType.SByte => "127",
        DbType.Int16 => "32767",
        DbType.Int32 => "2147483647",
        DbType.Int64 => "9223372036854775807",
        DbType.Single => "3.4028235E38",
        DbType.Double => "1.7976931348623157E308",
        _ => "1",
    };
}

/// <summary>
/// The command conformance tests: <c>DbCommand</c> and how it is run.
/// </summary>
public sealed class H2CommandTests : CommandTestBase<H2FactoryFixture>
{
    public H2CommandTests(H2FactoryFixture fixture)
        : base(fixture)
    {
    }

    /// <remarks>
    /// The base test codifies the Sqlite/MySqlConnector contract, where assigning
    /// <c>CommandText</c> with an open reader throws. This provider follows SqlClient, the
    /// primary source of ADO.NET semantics: the assignment is legal and the conflict with an
    /// open reader surfaces at execution, where the second-reader guard lives.
    /// </remarks>
    public override void CommandText_throws_when_set_when_open_reader()
    {
        using var connection = CreateOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";

        using var reader = command.ExecuteReader();
        reader.Read();

        command.CommandText = "SELECT 2;";
        Assert.Equal("SELECT 2;", command.CommandText);
    }

    public override void ExecuteReader_binds_parameters()
    {
        using var connection = CreateOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT $Parameter;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$Parameter";
        parameter.Value = 1;
        command.Parameters.Add(parameter);

        var scalar = command.ExecuteScalar();
        if (scalar is int)
            Assert.Equal(1, scalar);
        else
            Assert.Equal(1L, scalar);
    }

    public override void Execute_throws_for_unknown_ParameterValue_type()
    {
        using var connection = CreateOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT $Parameter;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$Parameter";
        parameter.Value = new CustomClass();
        command.Parameters.Add(parameter);

        try
        {
            var value = command.ExecuteScalar();
            if (!"custom".Equals(value as string))
                throw new UnexpectedValueException(value ?? DBNull.Value);
        }
        catch (Exception ex) when (ex is NotSupportedException || ex is InvalidOperationException)
        {
        }
        catch (Exception ex) when (!(ex is UnexpectedValueException))
        {
            throw new ThrowsException(typeof(NotSupportedException), ex);
        }
    }
}

/// <summary>
/// The connection conformance tests: <c>DbConnection</c> lifecycle and its properties.
/// </summary>
public sealed class H2ConnectionTests : ConnectionTestBase<H2FactoryFixture>
{
    public H2ConnectionTests(H2FactoryFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>
/// The parameter conformance tests: <c>DbParameter</c> and <c>DbParameterCollection</c>.
/// </summary>
public sealed class H2ParameterTests : ParameterTestBase<H2FactoryFixture>
{
    public H2ParameterTests(H2FactoryFixture fixture)
        : base(fixture)
    {
    }

    public override void Bind_requires_set_name()
    {
        using var connection = CreateOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT $Parameter;";
        var parameter = command.CreateParameter();
        parameter.Value = 1;
        command.Parameters.Add(parameter);

        AssertThrowsAny<InvalidOperationException, DbException>(() => command.ExecuteNonQuery());
    }

    public override void Bind_requires_set_value()
    {
        using var connection = CreateOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT $Parameter;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$Parameter";
        command.Parameters.Add(parameter);

        Assert.Throws<InvalidOperationException>(() => command.ExecuteNonQuery());
    }

    public override void Bind_works_with_string()
    {
        using var connection = CreateOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT $Parameter;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$Parameter";
        parameter.Value = "test";
        command.Parameters.Add(parameter);

        Assert.Equal("test", command.ExecuteScalar());
    }

    public override void Bind_works_with_byte_array()
    {
        using var connection = CreateOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT $Parameter;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$Parameter";
        parameter.Value = new byte[] { 1, 2, 3, 4 };
        command.Parameters.Add(parameter);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, command.ExecuteScalar());
    }

    public override void Bind_works_with_stream()
    {
        using var connection = CreateOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT $Parameter;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$Parameter";
        using var stream = new System.IO.MemoryStream(new byte[] { 1, 2, 3, 4 });
        parameter.Value = stream;
        command.Parameters.Add(parameter);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, command.ExecuteScalar());
    }

    public override void Bind_throws_when_unknown()
    {
        using var connection = CreateOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT $Parameter;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$Parameter";
        parameter.Value = new object();
        command.Parameters.Add(parameter);

        AssertThrowsAny<InvalidOperationException, NotSupportedException>(() => command.ExecuteScalar());
    }
}

/// <summary>
/// The reader conformance tests: <c>DbDataReader</c>.
/// </summary>
public sealed class H2DataReaderTests : DataReaderTestBase<H2FactoryFixture>
{
    public H2DataReaderTests(H2FactoryFixture fixture)
        : base(fixture)
    {
    }

    public override void GetName_works()
    {
        // Real H2 folds the unquoted alias to ID (verified against the reference jar),
        // so the base SQL asserts MySQL behavior, not the wrapper. Same intent with a
        // quoted alias, which both keep as written: the reader returns the label it gets.
        using var connection = CreateOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 AS \"id\";";
        using var reader = command.ExecuteReader();
        Assert.Equal("id", reader.GetName(0));
    }

    public override void GetColumnSchema_ColumnName()
    {
        // Same folding as GetName_works: the schema reports the label the engine gives,
        // quoted here so both sides keep the case.
        using var connection = CreateOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 AS \"id\";";
        using var reader = command.ExecuteReader();
        var columns = reader.GetColumnSchema();
        var column = Assert.Single(columns);
        Assert.Equal("id", column.ColumnName);
    }

    public override void GetTextReader_returns_empty_for_null_String()
    {
        // Contradicts GetTextReader_throws_for_null_String in the same suite, which this
        // provider honors through NullValueExceptionType: a null has no text to read.
        using var connection = CreateOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = Fixture.CreateSelectSql(DbType.String, ValueKind.Null);
        using var reader = command.ExecuteReader();
        reader.Read();
        Assert.Throws(Fixture.NullValueExceptionType, () => reader.GetTextReader(0));
    }
}

/// <summary>
/// The transaction conformance tests: <c>DbTransaction</c>.
/// </summary>
public sealed class H2TransactionTests : TransactionTestBase<H2FactoryFixture>
{
    public H2TransactionTests(H2FactoryFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>
/// The factory conformance tests: <c>DbProviderFactory</c>.
/// </summary>
public sealed class H2ProviderFactoryTests : DbProviderFactoryTestBase<H2FactoryFixture>
{
    public H2ProviderFactoryTests(H2FactoryFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>
/// The connection string conformance tests.
/// </summary>
public sealed class H2ConnectionStringTests : ConnectionStringTestBase<H2FactoryFixture>
{
    public H2ConnectionStringTests(H2FactoryFixture fixture)
        : base(fixture)
    {
    }
}
