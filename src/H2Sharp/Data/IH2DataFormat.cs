/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * How a connection presents engine values to ADO.NET.
 *
 * JDBC has Blob and Clob handles; ADO.NET has no such types, so the provider has to pick
 * a CLR shape for BLOB, CLOB, JSON and ARRAY. The reference-compatible shapes are the
 * defaults; a caller that integrates with an ORM can ask for streams, readers, documents
 * or lists instead. The choice is cosmetic: it belongs to one connection instance and
 * never reaches the database, the SQL parser, the stored values or the parameters.
 */
namespace H2Sharp.Data;

/// <summary>
/// Chooses the CLR shape in which a connection presents BLOB, CLOB, JSON and ARRAY values.
/// </summary>
/// <remarks>
/// <para>
/// The interface is implemented explicitly by <see cref="H2Connection"/>, the way
/// <see cref="IH2Test"/> and <see cref="IH2Extensions"/> are, so ordinary connection
/// IntelliSense is not cluttered with options nobody is configuring. The properties may be
/// changed only while the connection is closed and not disposed; <see cref="H2Connection.Open"/>
/// takes a snapshot that stays fixed until the connection is closed.
/// </para>
/// <para>
/// The choice affects only the ADO.NET projection: <see cref="System.Data.Common.DbDataReader.GetValue"/>,
/// <c>GetValues</c>, <c>ExecuteScalar</c>, and the matching column metadata
/// (<c>GetFieldType</c>, <c>GetColumnSchema</c>, <c>GetSchemaTable</c>). Explicit getters keep
/// their reference behavior regardless of the option: <c>GetString</c>, <c>GetBytes</c>,
/// <c>GetStream</c>, <c>GetTextReader</c>, <c>GetFieldValue&lt;T&gt;</c> continue to work as
/// before. The engine, the database identity, SQL semantics, stored values, LOB storage and
/// parameters are untouched; two connections to the same database may use different options.
/// </para>
/// <para>
/// Defaults are the reference-compatible shapes: BLOB and JSON read as <c>byte[]</c>, CLOB as
/// <c>string</c>, and ARRAY as a typed CLR array.
/// </para>
/// </remarks>
public interface IH2DataFormat
{
    /// <summary>
    /// Gets or sets how BLOB values are presented by <c>GetValue</c> and column metadata.
    /// </summary>
    /// <remarks>
    /// <see cref="H2BlobFormat.Stream"/> returns the LOB's own stream, so the value must be
    /// consumed before the reader advances or closes, exactly as with
    /// <c>DbDataReader.GetStream</c>. <c>ExecuteScalar</c> returns a detached
    /// <see cref="System.IO.MemoryStream"/> because its result is closed before the value is
    /// returned.
    /// </remarks>
    H2BlobFormat BlobFormat { get; set; }

    /// <summary>
    /// Gets or sets how CLOB values are presented by <c>GetValue</c> and column metadata.
    /// </summary>
    /// <remarks>
    /// <see cref="H2ClobFormat.TextReader"/> streams the value, so it must be consumed before
    /// the reader advances or closes. <c>ExecuteScalar</c> returns a detached
    /// <see cref="System.IO.StringReader"/>.
    /// </remarks>
    H2ClobFormat ClobFormat { get; set; }

    /// <summary>
    /// Gets or sets how JSON values are presented by <c>GetValue</c> and column metadata.
    /// </summary>
    /// <remarks>
    /// <see cref="H2JsonFormat.JsonDocument"/> creates an independent document on every read;
    /// the caller owns it and must dispose it. Reading a plain string back as a parameter is
    /// still an ordinary JSON string conversion, as in the reference; use
    /// <see cref="H2JsonFormat.JsonDocument"/> or the default <see cref="H2JsonFormat.ByteArray"/>
    /// when the value must survive a round trip as a document.
    /// </remarks>
    H2JsonFormat JsonFormat { get; set; }

    /// <summary>
    /// Gets or sets how SQL ARRAY values are presented by <c>GetValue</c> and column metadata.
    /// </summary>
    /// <remarks>
    /// <see cref="H2ArrayFormat.List"/> applies to the outer array only; elements that are
    /// themselves arrays or rows keep their reference shape. Arrays whose component is
    /// BINARY, VARBINARY, BLOB or JSON are excluded and stay CLR arrays (<c>byte[][]</c>).
    /// </remarks>
    H2ArrayFormat ArrayFormat { get; set; }

    /// <summary>
    /// Gets or sets the CLR shape of DATE values.
    /// </summary>
    /// <remarks>
    /// DateTime shapes keep midnight and only set <see cref="DateTime.Kind"/>; the date is
    /// not shifted. <see cref="H2TemporalFormat.DateTimeOffset"/> uses the session time zone
    /// captured when the connection opened.
    /// </remarks>
    H2TemporalFormat DateFormat { get; set; }

    /// <summary>
    /// Gets or sets the CLR shape of TIME values.
    /// </summary>
    /// <remarks>
    /// <see cref="H2TimeFormat.TimeSpan"/> is the reference-compatible default. DateTime
    /// shapes use <see cref="DateTime.MinValue"/> as the date and only set
    /// <see cref="DateTime.Kind"/>; <see cref="H2TimeFormat.DateTimeOffset"/> uses the
    /// session time zone captured when the connection opened.
    /// </remarks>
    H2TimeFormat TimeFormat { get; set; }

    /// <summary>
    /// Gets or sets the CLR shape of TIME WITH TIME ZONE values.
    /// </summary>
    /// <remarks>
    /// <see cref="H2TemporalFormat.DateTimeOffset"/> preserves the stored offset.
    /// DateTime shapes use <see cref="DateTime.MinValue"/> as the date;
    /// <see cref="H2TemporalFormat.DateTimeUnspecified"/> keeps the stored wall clock and
    /// drops the offset, while Utc and Local convert the instant.
    /// </remarks>
    H2TemporalFormat TimeTzFormat { get; set; }

    /// <summary>
    /// Gets or sets the CLR shape of TIMESTAMP values.
    /// </summary>
    /// <remarks>
    /// DateTime shapes keep the wall clock and only set <see cref="DateTime.Kind"/>; the
    /// value is not shifted. <see cref="H2TemporalFormat.DateTimeOffset"/> uses the session
    /// time zone captured when the connection opened, because a TIMESTAMP stores no zone.
    /// </remarks>
    H2TemporalFormat TimestampFormat { get; set; }

    /// <summary>
    /// Gets or sets the CLR shape of TIMESTAMP WITH TIME ZONE values.
    /// </summary>
    /// <remarks>
    /// <see cref="H2TemporalFormat.DateTimeOffset"/> preserves the stored offset.
    /// <see cref="H2TemporalFormat.DateTimeUnspecified"/> keeps the stored wall clock and
    /// drops the offset, while Utc and Local convert the instant.
    /// </remarks>
    H2TemporalFormat TimestampTzFormat { get; set; }
}

/// <summary>
/// The CLR shape of a BLOB value.
/// </summary>
public enum H2BlobFormat
{
    /// <summary>
    /// The reference-compatible default: a materialized <c>byte[]</c>.
    /// </summary>
    ByteArray,

    /// <summary>
    /// The LOB's own <see cref="System.IO.Stream"/>.
    /// </summary>
    Stream,
}

/// <summary>
/// The CLR shape of a CLOB value.
/// </summary>
public enum H2ClobFormat
{
    /// <summary>
    /// The reference-compatible default: a materialized <c>string</c>.
    /// </summary>
    String,

    /// <summary>
    /// A <see cref="System.IO.TextReader"/> over the value.
    /// </summary>
    TextReader,
}

/// <summary>
/// The CLR shape of a JSON value.
/// </summary>
public enum H2JsonFormat
{
    /// <summary>
    /// The reference-compatible default: UTF-8 <c>byte[]</c>.
    /// </summary>
    ByteArray,

    /// <summary>
    /// The JSON text as a <c>string</c>.
    /// </summary>
    String,

    /// <summary>
    /// A <see cref="System.IO.TextReader"/> over the JSON text.
    /// </summary>
    TextReader,

    /// <summary>
    /// A <see cref="System.IO.Stream"/> over the UTF-8 JSON bytes.
    /// </summary>
    Stream,

    /// <summary>
    /// An independent <see cref="System.Text.Json.JsonDocument"/> that the caller must dispose.
    /// </summary>
    JsonDocument,
}

/// <summary>
/// The CLR shape of a SQL ARRAY value.
/// </summary>
public enum H2ArrayFormat
{
    /// <summary>
    /// The reference-compatible default: a typed CLR array.
    /// </summary>
    Array,

    /// <summary>
    /// A <see cref="System.Collections.Generic.List{T}"/> over the outer array.
    /// </summary>
    List,
}

/// <summary>
/// The CLR shape of a DATE, TIME WITH TIME ZONE, TIMESTAMP or TIMESTAMP WITH TIME ZONE value.
/// </summary>
public enum H2TemporalFormat
{
    /// <summary>
    /// A <see cref="System.DateTime"/> whose <see cref="DateTime.Kind"/> is
    /// <see cref="DateTimeKind.Unspecified"/>.
    /// </summary>
    DateTimeUnspecified,

    /// <summary>
    /// A <see cref="System.DateTime"/> whose <see cref="DateTime.Kind"/> is
    /// <see cref="DateTimeKind.Utc"/>.
    /// </summary>
    DateTimeUtc,

    /// <summary>
    /// A <see cref="System.DateTime"/> whose <see cref="DateTime.Kind"/> is
    /// <see cref="DateTimeKind.Local"/>.
    /// </summary>
    DateTimeLocal,

    /// <summary>
    /// A <see cref="System.DateTimeOffset"/>.
    /// </summary>
    DateTimeOffset,
}

/// <summary>
/// The CLR shape of a TIME value.
/// </summary>
public enum H2TimeFormat
{
    /// <summary>
    /// The reference-compatible default: a <see cref="System.TimeSpan"/>.
    /// </summary>
    TimeSpan,

    /// <summary>
    /// A <see cref="System.DateTime"/> at <see cref="DateTime.MinValue"/> with
    /// <see cref="DateTimeKind.Unspecified"/>.
    /// </summary>
    DateTimeUnspecified,

    /// <summary>
    /// A <see cref="System.DateTime"/> at <see cref="DateTime.MinValue"/> with
    /// <see cref="DateTimeKind.Utc"/>.
    /// </summary>
    DateTimeUtc,

    /// <summary>
    /// A <see cref="System.DateTime"/> at <see cref="DateTime.MinValue"/> with
    /// <see cref="DateTimeKind.Local"/>.
    /// </summary>
    DateTimeLocal,

    /// <summary>
    /// A <see cref="System.DateTimeOffset"/> at <see cref="DateTime.MinValue"/> using the
    /// session time zone captured when the connection opened.
    /// </summary>
    DateTimeOffset,
}
