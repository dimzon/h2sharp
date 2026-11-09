/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * The immutable snapshot a connection takes of IH2DataFormat when it opens.
 */
using H2Sharp.Util;

namespace H2Sharp.Data;

/// <summary>
/// The ADO.NET projection a connection uses for one open session.
/// </summary>
internal sealed record H2DataFormatOptions
{
    /// <summary>
    /// The reference-compatible defaults.
    /// </summary>
    internal static readonly H2DataFormatOptions Default = new();

    internal H2BlobFormat BlobFormat { get; init; } = H2BlobFormat.ByteArray;

    internal H2ClobFormat ClobFormat { get; init; } = H2ClobFormat.String;

    internal H2JsonFormat JsonFormat { get; init; } = H2JsonFormat.ByteArray;

    internal H2ArrayFormat ArrayFormat { get; init; } = H2ArrayFormat.Array;

    internal H2TemporalFormat DateFormat { get; init; } = H2TemporalFormat.DateTimeUnspecified;

    internal H2TimeFormat TimeFormat { get; init; } = H2TimeFormat.TimeSpan;

    internal H2TemporalFormat TimeTzFormat { get; init; } = H2TemporalFormat.DateTimeOffset;

    internal H2TemporalFormat TimestampFormat { get; init; } = H2TemporalFormat.DateTimeUnspecified;

    internal H2TemporalFormat TimestampTzFormat { get; init; } = H2TemporalFormat.DateTimeOffset;

    /// <summary>
    /// The session time zone captured when the connection opened. It gives an offset to
    /// zone-less values that the caller asked to read as <see cref="DateTimeOffset"/>.
    /// </summary>
    internal TimeZoneProvider? SessionTimeZone { get; init; }
}
