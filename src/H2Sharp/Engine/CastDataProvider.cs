/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.engine.CastDataProvider.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
namespace H2Sharp.Engine;

/// <summary>
/// Provides information for type casts and comparison operations.
/// </summary>
/// <remarks>
/// Three of the reference's five members are declared here: <c>currentTimestamp</c>,
/// <c>currentTimeZone</c> and <c>getMode</c>. The one that is still not is
/// <c>getJavaObjectSerializer</c>, which returns a type that is not ported yet.
/// <para>
/// <c>currentTimestamp</c> and <c>currentTimeZone</c> arrived with the conversions into the
/// date and time types, which are the only things that ask them; before that they were left
/// out because a value class should not have to implement a timestamp it will never ask for.
/// A provider now has to answer for "now" and for the zone, and the contract is the
/// reference's: the timestamp must be the same within a transaction or within one command,
/// which is why it is a method rather than a field on the caller.
/// </para>
/// <para>
/// <see cref="ZeroBasedEnums"/> is declared because enum conversion asks it and that conversion
/// is ported. <see cref="GetMode"/> is declared because the tokenizer asks it, and the
/// tokenizer reads five flags of the mode; <see cref="Mode"/> carries those five and no others,
/// so what an implementor has to answer is a question it can answer.
/// </para>
/// </remarks>
public interface CastDataProvider
{
    /// <summary>
    /// Returns the current timestamp with maximum resolution. The value must be the same within
    /// a transaction or within execution of a command.
    /// </summary>
    /// <returns>the current timestamp for CURRENT_TIMESTAMP(9)</returns>
    Values.ValueTimestampTimeZone CurrentTimestamp();

    /// <summary>
    /// Returns the current time zone.
    /// </summary>
    /// <returns>the current time zone</returns>
    Util.TimeZoneProvider CurrentTimeZone();

    /// <summary>
    /// Returns are ENUM values 0-based.
    /// </summary>
    /// <returns>are ENUM values 0-based</returns>
    bool ZeroBasedEnums();

    /// <summary>
    /// Returns the database mode.
    /// </summary>
    /// <returns>the database mode</returns>
    Mode GetMode();
}
