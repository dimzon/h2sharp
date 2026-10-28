/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The seams a test needs, seen through ADO.NET.
 *
 * The connection implements IH2Test explicitly, so the seams are reached by casting the
 * connection to the interface. The clock, the generator of random numbers and the generator
 * of UUIDs are then the ones the test answers with, and the statements see them.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2TestSeamsTests
{
    [Fact]
    public void TheRandomNumbersAreTheOnesTheTestHolds()
    {
        using H2Connection connection = Open("seams_random");
        ((IH2Test)connection).RandomGeneratorProvider = _ => new FixedRandom();

        connection.Open();

        Assert.Equal("0.5", Scalar(connection, "SELECT RAND()"));
    }

    [Fact]
    public void TheSeedIsPassedToTheGeneratorProvider()
    {
        using H2Connection connection = Open("seams_seed");
        List<long?> seeds = [];
        ((IH2Test)connection).RandomGeneratorProvider = seed =>
        {
            seeds.Add(seed);
            return new Random((int)(seed ?? 0));
        };

        connection.Open();
        _ = Scalar(connection, "SELECT RAND(7)");

        Assert.Equal(new long?[] { null, 7 }, seeds);
    }

    [Fact]
    public void TheUuidsAreTheOnesTheTestHolds()
    {
        using H2Connection connection = Open("seams_uuid");
        ((IH2Test)connection).UuidProvider = () => new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");

        connection.Open();

        Assert.Equal("0f8fad5b-d9cb-469f-a165-70867728950e", Scalar(connection, "SELECT RANDOM_UUID()"));
    }

    /// <summary>
    /// A generator that always answers with the same number.
    /// </summary>
    private sealed class FixedRandom : Random
    {
        /// <summary>
        /// Creates the generator.
        /// </summary>
        internal FixedRandom()
            : base(0)
        {
        }

        /// <inheritdoc/>
        public override double NextDouble() => 0.5;
    }

    /// <summary>
    /// Creates a connection to a database of its own, not yet open.
    /// </summary>
    /// <param name="name">the name of the database</param>
    /// <returns>the connection</returns>
    private static H2Connection Open(string name)
    {
        return new H2Connection($"Data Source=ado_{name};Storage=Memory");
    }

    /// <summary>
    /// Runs a query that answers with one value and returns it as text.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the query</param>
    /// <returns>the value</returns>
    private static string Scalar(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null || value is DBNull ? "-" : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "-";
    }
}
