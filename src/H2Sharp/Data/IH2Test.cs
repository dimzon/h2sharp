/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Ported from none (new file, no Java counterpart).
 *
 * The seams a test needs: the clock, the generator of random numbers, and the generator of
 * UUIDs.
 *
 * The reference has no such seams. It reads the time from the machine, draws random numbers
 * from the generator of the runtime, and builds UUIDs out of the two. A test that compares
 * the port with the reference cannot hold them still: the two sides answer with different
 * times, and no two runs of the same statement agree with each other. These delegates are
 * the way to hold them still — the port asks the delegate instead of the machine, and the
 * test answers with what it wants to see.
 *
 * The connection implements the interface explicitly, so that the seams do not show up among
 * the members of a connection that nobody is testing.
 */
using System;
using H2Sharp.Util;

namespace H2Sharp.Data;

/// <summary>
/// The seams a test needs: the clock, the generator of random numbers, and the generator of
/// UUIDs.
/// </summary>
/// <remarks>
/// Every delegate is optional, and a delegate that is not set means the port behaves as it
/// does without a test: the machine's clock, the generator of the session, and the UUIDs the
/// port builds itself.
/// </remarks>
public interface IH2Test
{
    /// <summary>
    /// The clock the database reads the current time from.
    /// </summary>
    /// <remarks>
    /// The delegate is asked for the time every time a statement needs "now", so a test that
    /// wants the same time twice has to answer with the same time twice. The time is read
    /// in the local zone of the machine, as the reference reads it.
    /// </remarks>
    Func<DateTimeOffset>? SystemClockProvider { get; set; }

    /// <summary>
    /// The generator of random numbers the database draws from.
    /// </summary>
    /// <remarks>
    /// The delegate is asked once per session, the first time a statement needs a number, and
    /// the generator it answers with is the one the session keeps. A test that wants the same
    /// numbers every run answers with a generator of a fixed seed.
    /// </remarks>
    Func<JavaRandom>? RandomGeneratorProvider { get; set; }

    /// <summary>
    /// The generator of UUIDs the database hands out.
    /// </summary>
    /// <remarks>
    /// The delegate is asked every time a statement needs a UUID. The UUID it answers with is
    /// the one the statement gets, whatever version the statement asked for.
    /// </remarks>
    Func<Guid>? UuidProvider { get; set; }
}
