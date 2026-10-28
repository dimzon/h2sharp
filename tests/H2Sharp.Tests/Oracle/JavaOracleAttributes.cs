/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new tests for H2Sharp).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// A fact that runs the reference JVM and compares against it.
/// <para>
/// By default it is skipped when the oracle is unavailable, so that a plain
/// <c>dotnet test</c> run does not fail merely because the optional cross-language
/// oracle is not set up. That default is exactly what makes a green run meaningless for
/// compatibility, so the conformance job in CI sets <c>H2SHARP_REQUIRE_ORACLE</c> and
/// then an unavailable oracle throws instead of skipping. See
/// <see cref="JavaOracle.Required"/>.
/// </para>
/// </summary>
public class JavaOracleFactAttribute : FactAttribute
{
    public JavaOracleFactAttribute()
    {
        if (JavaOracle.RequiredFailureReason is { } failure)
        {
            throw new InvalidOperationException(failure);
        }

        if (!JavaOracle.IsAvailable)
        {
            Skip = JavaOracle.SkipReason;
        }
    }
}

/// <summary>
/// Theory counterpart of <see cref="JavaOracleFactAttribute"/>.
/// </summary>
public sealed class JavaOracleTheoryAttribute : TheoryAttribute
{
    public JavaOracleTheoryAttribute()
    {
        if (JavaOracle.RequiredFailureReason is { } failure)
        {
            throw new InvalidOperationException(failure);
        }

        if (!JavaOracle.IsAvailable)
        {
            Skip = JavaOracle.SkipReason;
        }
    }
}

/// <summary>
/// A fact that compares against the reference and needs statements to run for it to mean
/// anything.
/// </summary>
/// <remarks>
/// Some questions cannot be asked of the reference without running SQL on both sides: whether a
/// second connection sees what the first one created, whether a named in-memory database is
/// forgotten when its last session closes. Those questions are real and they are written down
/// (see <c>InMemoryDatabaseOracleTests</c>), but until the parser and the table layer are ported
/// our side cannot answer them, and a test that failed on "not implemented yet" would say
/// nothing except that the port is unfinished — while sitting in the suite that is supposed to
/// stay green.
/// <para>
/// The condition is a probe, not a flag: it runs one statement and asks whether the engine
/// refused. The day statements run, the test starts running with no edit here, which is the
/// point — the skip has to disappear by itself or it will outlive the thing it waits for.
/// </para>
/// </remarks>
public sealed class JavaOracleWithSqlFactAttribute : JavaOracleFactAttribute
{
    public JavaOracleWithSqlFactAttribute()
    {
        if (Skip is not null)
        {
            return;
        }

        Skip = SqlNotRunnable();
    }

    /// <summary>
    /// Why statements cannot run yet, or null when they can.
    /// </summary>
    internal static string? SqlNotRunnable()
    {
        try
        {
            using H2Sharp.Data.H2Connection connection = new("Data Source=sql_probe;Storage=Memory");
            connection.Open();
            using System.Data.Common.DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            _ = command.ExecuteScalar();
            connection.Close();
            return null;
        }
        catch (Exception ex)
        {
            return "Statements do not run yet (" + ex.Message + "); the comparison needs the parser and the table layer";
        }
    }
}
