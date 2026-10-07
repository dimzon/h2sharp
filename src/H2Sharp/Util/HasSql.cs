/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.util.HasSQL.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: Java declares this as an interface of int constants. C# has no equivalent
 * of interface constants, so it becomes a static class; the values are a contract because
 * they are combined into the sqlFlags argument threaded through the engine, and they are
 * pinned by HasSqlTests.
 */
namespace H2Sharp.Util;

/// <summary>
/// SQL formatting flags, used to switch trace output between verbose and compact form.
/// </summary>
public static class HasSql
{
    /// <summary>
    /// Quote identifiers only when they are required.
    /// </summary>
    public const int QuoteOnlyWhenRequired = 1;

    /// <summary>
    /// Replace LOB values with their length in trace output.
    /// </summary>
    public const int ReplaceLobsForTrace = 2;

    /// <summary>
    /// Do not include casts in trace output.
    /// </summary>
    public const int NoCasts = 4;

    /// <summary>
    /// Add plan information to trace output.
    /// </summary>
    public const int AddPlanInformation = 8;

    /// <summary>
    /// No flags: quote everything.
    /// </summary>
    public const int DefaultSqlFlags = 0;

    /// <summary>
    /// The flags used for <c>TRACE</c> output.
    /// </summary>
    public const int TraceSqlFlags = QuoteOnlyWhenRequired | ReplaceLobsForTrace;
}
