/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The seams a test needs, seen through the engine.
 *
 * The reference has no such seams: it reads the time from the machine and draws random
 * numbers from the generator of the runtime. A test that compares the port with the
 * reference cannot hold them still, so the port takes a delegate instead of the machine.
 */
using System;
using H2Sharp.Engine;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class TestSeamsTests
{
    [Fact]
    public void TheClockIsTheOneTheTestHolds()
    {
        DateTimeOffset fixedTime = new(2017, 6, 15, 12, 0, 0, TimeSpan.Zero);
        SessionLocal session = H2Sharp.Engine.Engine.OpenSession(
            new ConnectionInfo("jdbc:h2:mem:seams_clock"), () => fixedTime);

        ValueTimestampTimeZone now = session.CastDataProvider.CurrentTimestamp();

        Assert.Equal("2017-06-15 12:00:00+00", now.GetString());
    }

    [Fact]
    public void TheRandomGeneratorIsTheOneTheTestHolds()
    {
        SessionLocal session = H2Sharp.Engine.Engine.OpenSession(
            new ConnectionInfo("jdbc:h2:mem:seams_random"), null, _ => new FixedRandom());

        Random random = session.GetRandom();

        Assert.Equal(0.5, random.NextDouble());
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
}
