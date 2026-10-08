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
/// Two of the reference's five members are declared here. The other three answer
/// <c>currentTimestamp</c>, <c>currentTimeZone</c> and <c>getJavaObjectSerializer</c>, and each of
/// those returns a type that is not ported yet: a timestamp with a zone, a time zone provider and
/// the Java object serializer. Declaring members that cannot be implemented would make every
/// implementor stub three methods it cannot answer, and a value class should not have to think
/// about a timestamp it will never ask for.
/// <para>
/// <see cref="ZeroBasedEnums"/> is declared because enum conversion asks it and that conversion is
/// ported. <see cref="GetMode"/> is declared because the tokenizer asks it, and the tokenizer reads
/// five flags of the mode; <see cref="Mode"/> carries those five and no others, so what a
/// implementor has to answer is a question it can answer.
/// </para>
/// </remarks>
public interface CastDataProvider
{
    /// <summary>
    /// Returns are ENUM values 0-based.
    /// </summary>
    /// <returns>are ENUM values 0-based</returns>
    bool ZeroBasedEnums();

    /// <summary>
    /// The compatibility mode statements are read in, which is how <c>#</c> becomes part of a name,
    /// how <c>0x1A</c> becomes a string of bytes, how <c>[Test]</c> becomes a quoted name, and how
    /// <c>LIMIT</c> and <c>MINUS</c> become keywords instead of names.
    /// </summary>
    /// <returns>the mode of this provider</returns>
    Mode GetMode();
}
