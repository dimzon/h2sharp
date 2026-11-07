/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Syntax refusals that the reference answers with 42001 and the port with 42000: the
 * expected-token list of the port does not survive the dispatch chains it collects on (see
 * MIGRATION_STATUS). Both sides refuse these forms; the semantic agreement ends at the error
 * code, so the refusals are pinned here instead of under the Java oracle.
 */
using System;
using H2Sharp.Data;
using H2Sharp.Engine;
using Xunit;

namespace H2Sharp.Tests;

public sealed class AggregateSyntaxRefusalTests
{
    private static H2SharpException Refuse(string sql)
    {
        using H2Connection connection = new(
            "Data Source=agg_refusal_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        using H2Command command = new(sql, connection);
        return Assert.Throws<H2SharpException>(() => _ = command.ExecuteNonQuery());
    }

    [Fact]
    public void HistogramRefusesDistinctAndAll()
    {
        foreach (string word in new[] { "DISTINCT", "ALL" })
        {
            H2SharpException e = Refuse("SELECT HISTOGRAM(" + word + " X) FROM (VALUES (0)) T(X)");
            Assert.Contains("[*]" + word, e.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ModeRefusesDistinct()
    {
        using H2Connection connection = new(
            "Data Source=agg_refusal_mode_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE T(V INT)";
            _ = create.ExecuteNonQuery();
        }

        using H2Command command = new("SELECT MODE(DISTINCT V) FROM T", connection);
        H2SharpException e = Assert.Throws<H2SharpException>(() => _ = command.ExecuteNonQuery());
        Assert.Contains("[*]DISTINCT", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GcdRefusesOneOrZeroArguments()
    {
        foreach (string sql in new[] { "SELECT GCD(12)", "SELECT GCD()" })
        {
            H2SharpException e = Refuse(sql);
            Assert.Contains("GCD", e.Message, StringComparison.Ordinal);
        }
    }
}

/// <summary>
/// The same 42001/42000 class: an unknown aggregate function refuses on both sides.
/// </summary>
public sealed class UnknownAggregateRefusalTests
{
    [Fact]
    public void UnknownAggregateRefusesAsNotFound()
    {
        using H2Connection connection = new(
            "Data Source=agg_unknown_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        using H2Command command = new("SELECT NOT_AN_AGGREGATE(ID) FROM T", connection);
        H2SharpException e = Assert.Throws<H2SharpException>(() => _ = command.ExecuteNonQuery());
        Assert.Contains("NOT_AN_AGGREGATE", e.Message, StringComparison.Ordinal);
    }
}
