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
/// Only one of the reference's five members is declared here. The other four answer
/// <c>currentTimestamp</c>, <c>currentTimeZone</c>, <c>getMode</c> and
/// <c>getJavaObjectSerializer</c>, and each of those returns a type that is not ported yet:
/// a timestamp with a zone, a time zone provider, the database mode and the Java object
/// serializer. Declaring members that cannot be implemented would make every implementor
/// stub four methods it cannot answer, and a value class should not have to think about a
/// timestamp it will never ask for.
/// <para>
/// The one that is declared is <see cref="ZeroBasedEnums"/>, because enum conversion asks it
/// and that conversion is ported. The rest arrive with the conversions that need them.
/// </para>
/// </remarks>
public interface CastDataProvider
{
    /// <summary>
    /// Returns are ENUM values 0-based.
    /// </summary>
    /// <returns>are ENUM values 0-based</returns>
    bool ZeroBasedEnums();
}
