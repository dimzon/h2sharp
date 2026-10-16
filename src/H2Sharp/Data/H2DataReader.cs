/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * A reader over the rows a query produced.
 *
 * JDBC's ResultSet and ADO.NET's DbDataReader ask the same questions — what columns are
 * there, what is in this cell — and the answers come from the same place: the engine's
 * result. NULL reads as DBNull, everything else as the CLR value the mapper gives, and a
 * cell read before Read or after the rows ran out is an InvalidOperationException rather
 * than a stale row, because the row the reader holds is only ever the current one.
 *
 * Typed getters convert between numeric CLR types and refuse anything else: an INT that
 * reads as a string would be a conversion the query did not ask for, and a DATE that
 * reads as an integer would be one the caller cannot use.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using H2Sharp.Values;

namespace H2Sharp.Data;

/// <summary>
/// Reads the rows of a query result.
/// </summary>
public sealed class H2DataReader : DbDataReader
{
    private Result.ResultInterface? result;

    private readonly Action? onClose;

    private readonly List<Command.CommandContainer> remaining;

    private readonly long maxrows;

    private Value?[]? current;

    private bool closed;

    private readonly long recordsAffected;

    /// <summary>
    /// Creates a reader over the result.
    /// </summary>
    /// <param name="result">the engine result</param>
    /// <param name="onClose">what to do when the reader closes, if anything</param>
    /// <param name="remaining">the statements after the first, for NextResult</param>
    /// <param name="maxrows">the row limit each result set runs with</param>
    internal H2DataReader(
        Result.ResultInterface result,
        Action? onClose = null,
        List<Command.CommandContainer>? remaining = null,
        long maxrows = -1)
    {
        this.result = result;
        this.onClose = onClose;
        this.remaining = remaining ?? [];
        this.maxrows = maxrows;
        recordsAffected = -1;
    }

    /// <summary>
    /// Creates a reader with no rows, for a statement that changed rows instead.
    /// </summary>
    /// <param name="recordsAffected">how many rows the statement changed</param>
    internal H2DataReader(long recordsAffected)
    {
        remaining = [];
        this.recordsAffected = recordsAffected;
    }

    /// <inheritdoc/>
    public override int Depth => 0;

    /// <inheritdoc/>
    public override int FieldCount
    {
        get
        {
            ThrowIfClosed();
            return result?.GetVisibleColumnCount() ?? 0;
        }
    }

    /// <inheritdoc/>
    public override bool HasRows => result?.HasNext() == true || current is not null;

    /// <inheritdoc/>
    public override bool IsClosed => closed;

    /// <inheritdoc/>
    public override int RecordsAffected => (int)Math.Min(recordsAffected, int.MaxValue);

    /// <inheritdoc/>
    public override object this[int ordinal] => GetValue(ordinal);

    /// <inheritdoc/>
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <inheritdoc/>
    public override bool Read()
    {
        ThrowIfClosed();
        if (result is not null && result.Next())
        {
            current = result.CurrentRow();
            return true;
        }

        current = null;
        return false;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The next statement runs here and not sooner: running the queue up front would
    /// execute every side effect whether the caller reads that far or not.
    /// </remarks>
    public override bool NextResult()
    {
        ThrowIfClosed();
        while (remaining.Count > 0)
        {
            Command.CommandContainer next = remaining[0];
            remaining.RemoveAt(0);
            result?.Close();
            current = null;
            if (!next.IsQuery())
            {
                _ = next.UpdateRows(false);
                continue;
            }

            result = next.Query(maxrows);
            return true;
        }

        result?.Close();
        current = null;
        return false;
    }

    /// <inheritdoc/>
    public override void Close()
    {
        if (!closed)
        {
            closed = true;
            current = null;
            result?.Close();
            result = null;
            remaining.Clear();
            onClose?.Invoke();
        }
    }

    /// <inheritdoc/>
    public override string GetName(int ordinal)
    {
        CheckOrdinal(ordinal);
        return result!.GetColumnName(ordinal);
    }

    /// <inheritdoc/>
    public override int GetOrdinal(string name)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(name);
        if (result is null)
        {
            throw new InvalidOperationException("The reader has no rows");
        }

        for (int i = 0, count = FieldCount; i < count; i++)
        {
            if (string.Equals(result.GetColumnName(i), name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new IndexOutOfRangeException("No column named '" + name + "'");
    }

    /// <inheritdoc/>
    public override Type GetFieldType(int ordinal)
    {
        CheckOrdinal(ordinal);
        return H2ValueMapper.GetFieldType(result!.GetColumnType(ordinal));
    }

    /// <inheritdoc/>
    public override string GetDataTypeName(int ordinal)
    {
        CheckOrdinal(ordinal);
        return Value.GetTypeName(result!.GetColumnType(ordinal).GetValueType());
    }

    /// <inheritdoc/>
    public override object GetValue(int ordinal) => H2ValueMapper.ToObject(Cell(ordinal)) ?? DBNull.Value;

    /// <inheritdoc/>
    public override int GetValues(object?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Value?[] row = Row();
        int count = Math.Min(values.Length, row.Length);
        for (int i = 0; i < count; i++)
        {
            values[i] = H2ValueMapper.ToObject(row[i]);
        }

        return count;
    }

    /// <inheritdoc/>
    public override bool IsDBNull(int ordinal)
    {
        Value? value = Cell(ordinal);
        return value is null or ValueNull;
    }

    /// <inheritdoc/>
    public override bool GetBoolean(int ordinal) => Get<bool>(ordinal);

    /// <inheritdoc/>
    public override byte GetByte(int ordinal) => Get<byte>(ordinal);

    /// <inheritdoc/>
    public override char GetChar(int ordinal) => Get<char>(ordinal);

    /// <inheritdoc/>
    public override DateTime GetDateTime(int ordinal) => Get<DateTime>(ordinal);

    /// <inheritdoc/>
    public override decimal GetDecimal(int ordinal) => Get<decimal>(ordinal);

    /// <inheritdoc/>
    public override double GetDouble(int ordinal) => Get<double>(ordinal);

    /// <inheritdoc/>
    public override float GetFloat(int ordinal) => Get<float>(ordinal);

    /// <inheritdoc/>
    public override Guid GetGuid(int ordinal) => Get<Guid>(ordinal);

    /// <inheritdoc/>
    public override short GetInt16(int ordinal) => Get<short>(ordinal);

    /// <inheritdoc/>
    public override int GetInt32(int ordinal) => Get<int>(ordinal);

    /// <inheritdoc/>
    public override long GetInt64(int ordinal) => Get<long>(ordinal);

    /// <inheritdoc/>
    /// <remarks>
    /// Any type reads as text in its H2 spelling: TRUE stays TRUE rather than becoming True,
    /// which is what a CLR conversion would print. That is JDBC's getString rule, and the
    /// engine already spells every value.
    /// </remarks>
    public override string GetString(int ordinal)
    {
        Value? cell = Cell(ordinal);
        if (cell is null or ValueNull)
        {
            throw new InvalidCastException("Cannot read NULL as String");
        }

        return cell.GetString()
            ?? throw new InvalidCastException("Cannot read column " + ordinal + " as String");
    }

    /// <inheritdoc/>
    public override long GetBytes(int ordinal, long fieldOffset, byte[]? buffer, int bufferOffset, int length)
    {
        byte[] bytes = Get<byte[]>(ordinal);
        return CopySlice(bytes, fieldOffset, buffer, bufferOffset, length);
    }

    /// <inheritdoc/>
    public override long GetChars(int ordinal, long fieldOffset, char[]? buffer, int bufferOffset, int length)
    {
        char[] chars = Get<string>(ordinal).ToCharArray();
        return CopySlice(chars, fieldOffset, buffer, bufferOffset, length);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A binary column reads as a stream, anything else refuses: a stream over text would
    /// encode it in an encoding the query did not name.
    /// </remarks>
    public override Stream GetStream(int ordinal)
    {
        Value? cell = Cell(ordinal);
        if (cell is null or ValueNull)
        {
            throw NullValueException("Stream");
        }

        if (H2ValueMapper.ToObject(cell) is byte[] bytes)
        {
            return new MemoryStream(bytes, writable: false);
        }

        throw new InvalidOperationException("Column " + ordinal + " is not binary");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A text column reads as a text reader, anything else refuses, for the same reason a
    /// stream refuses text: the encoding would be invented.
    /// </remarks>
    public override TextReader GetTextReader(int ordinal)
    {
        Value? cell = Cell(ordinal);
        if (cell is null or ValueNull)
        {
            throw NullValueException("TextReader");
        }

        if (H2ValueMapper.ToObject(cell) is string text)
        {
            return new StringReader(text);
        }

        throw new InvalidOperationException("Column " + ordinal + " is not text");
    }

    /// <inheritdoc/>
    public override T GetFieldValue<T>(int ordinal)
    {
        if (typeof(T) == typeof(Stream))
        {
            return (T)(object)GetStream(ordinal);
        }

        if (typeof(T) == typeof(TextReader))
        {
            return (T)(object)GetTextReader(ordinal);
        }

        return base.GetFieldValue<T>(ordinal);
    }

    private static H2SharpException NullValueException(string target)
        => Message.DbException.Get(Api.ErrorCode.DataConversionError1, "NULL", target);

    /// <inheritdoc/>
    /// <remarks>
    /// Synchronous underneath — the row is in memory — so cancellation only refuses
    /// before it starts. See OpenAsync for when this becomes real.
    /// </remarks>
    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<bool>(cancellationToken);
        }

        try
        {
            return Task.FromResult(IsDBNull(ordinal));
        }
        catch (Exception e)
        {
            return Task.FromException<bool>(e);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Same synchronicity note as <see cref="IsDBNullAsync"/>.
    /// </remarks>
    public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        try
        {
            return Task.FromResult(GetFieldValue<T>(ordinal));
        }
        catch (Exception e)
        {
            return Task.FromException<T>(e);
        }
    }

    /// <inheritdoc/>
    public override IEnumerator GetEnumerator()
    {
        while (Read())
        {
            yield return this;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Empty when there are no columns, as GetSchemaTable is null then: the two answer
    /// the same question, and disagreeing would make one of them a lie. The synchronous
    /// shape does not exist on DbDataReader, so this is the asynchronous one.
    /// </remarks>
    public override Task<System.Collections.ObjectModel.ReadOnlyCollection<DbColumn>> GetColumnSchemaAsync(
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<System.Collections.ObjectModel.ReadOnlyCollection<DbColumn>>(
                cancellationToken);
        }

        List<DbColumn> columns = [];
        Result.ResultInterface? source = result;
        if (source is not null)
        {
            for (int i = 0, count = source.GetVisibleColumnCount(); i < count; i++)
            {
                TypeInfo type = source.GetColumnType(i);
                columns.Add(new H2DbColumn(
                    source.GetColumnName(i),
                    i,
                    H2ValueMapper.GetFieldType(type),
                    Value.GetTypeName(type.GetValueType()),
                    type.GetPrecision() is >= 0 and <= int.MaxValue ? (int)type.GetPrecision() : -1,
                    type.GetScale(),
                    source.GetNullable(i) != H2Sharp.Tables.Column.NotNullable));
            }
        }

        return Task.FromResult<System.Collections.ObjectModel.ReadOnlyCollection<DbColumn>>(
            columns.AsReadOnly());
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Null when there are no columns: a table of no columns would claim a shape the
    /// statement never had.
    /// </remarks>
    public override DataTable? GetSchemaTable()
    {
        Result.ResultInterface? columns = result;
        if (columns is null || columns.GetVisibleColumnCount() == 0)
        {
            return null;
        }

        DataTable table = new("SchemaTable");
        table.Columns.Add("ColumnName", typeof(string));
        table.Columns.Add("ColumnOrdinal", typeof(int));
        table.Columns.Add("ColumnSize", typeof(int));
        table.Columns.Add("NumericPrecision", typeof(int));
        table.Columns.Add("NumericScale", typeof(int));
        table.Columns.Add("DataType", typeof(Type));
        table.Columns.Add("DataTypeName", typeof(string));
        table.Columns.Add("AllowDBNull", typeof(bool));
        for (int i = 0, count = columns.GetVisibleColumnCount(); i < count; i++)
        {
            TypeInfo type = columns.GetColumnType(i);
            DataRow row = table.NewRow();
            row["ColumnName"] = GetName(i);
            row["ColumnOrdinal"] = i;
            row["ColumnSize"] = type.GetPrecision() is >= 0 and <= int.MaxValue
                ? (int)type.GetPrecision()
                : -1;
            row["NumericPrecision"] = type.GetPrecision() is >= 0 and <= int.MaxValue
                ? (int)type.GetPrecision()
                : (object)DBNull.Value;
            row["NumericScale"] = type.GetScale() >= 0
                ? type.GetScale()
                : (object)DBNull.Value;
            row["DataType"] = GetFieldType(i);
            row["DataTypeName"] = GetDataTypeName(i);
            row["AllowDBNull"] = columns.GetNullable(i) != H2Sharp.Tables.Column.NotNullable;
            table.Rows.Add(row);
        }

        return table;
    }

    private T Get<T>(int ordinal)
        where T : notnull
    {
        if (typeof(T) == typeof(string))
        {
            return (T)(object)GetString(ordinal);
        }

        object? value = GetValue(ordinal);
        if (value is T matched)
        {
            return matched;
        }

        if (value is DBNull)
        {
            throw new InvalidCastException("Cannot read NULL as " + typeof(T).Name);
        }

        // A string parses into whatever it spells: Guid, timestamps, numbers, booleans.
        if (value is string text)
        {
            return ParseString<T>(text, ordinal);
        }

        // Numbers widen and narrow with overflow checks; anything else converts only
        // when the pair is exact below.
        if (value is IConvertible && IsNumericType(typeof(T)) && IsNumericType(value.GetType()))
        {
            try
            {
                return (T)Convert.ChangeType(value, typeof(T), null);
            }
            catch (Exception e) when (e is OverflowException or FormatException)
            {
                throw new InvalidCastException(
                    "Cannot read " + value.GetType().Name + " as " + typeof(T).Name, e);
            }
        }

        if (typeof(T) == typeof(Guid) && value is byte[] bytes && bytes.Length == 16)
        {
            return (T)(object)new Guid(bytes);
        }

        if (typeof(T) == typeof(byte[]) && value is Guid guid)
        {
            return (T)(object)guid.ToByteArray();
        }

        if (typeof(T) == typeof(DateTimeOffset) && value is DateTime dateTime)
        {
            return (T)(object)new DateTimeOffset(dateTime);
        }

        throw new InvalidCastException(
            "Cannot read " + value!.GetType().Name + " as " + typeof(T).Name);
    }

    private static T ParseString<T>(string text, int ordinal)
    {
        try
        {
            if (typeof(T) == typeof(Guid))
            {
                return (T)(object)Guid.Parse(text);
            }

            if (typeof(T) == typeof(DateTimeOffset))
            {
                return (T)(object)DateTimeOffset.Parse(
                    text, System.Globalization.CultureInfo.InvariantCulture);
            }

            return (T)Convert.ChangeType(
                text, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is FormatException || e is OverflowException
            || e is InvalidCastException)
        {
            throw new InvalidCastException(
                "Cannot read column " + ordinal + " as " + typeof(T).Name, e);
        }
    }

    private static bool IsNumericType(Type type)
        => type == typeof(sbyte) || type == typeof(byte) || type == typeof(short)
            || type == typeof(int) || type == typeof(long) || type == typeof(float)
            || type == typeof(double) || type == typeof(decimal);

    private static long CopySlice<T>(
        T[] source, long fieldOffset, T[]? buffer, int bufferOffset, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fieldOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(bufferOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (buffer is null)
        {
            // No buffer is a length question, not a read: the whole value, whatever
            // the offsets say.
            return source.Length;
        }

        if (bufferOffset > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferOffset));
        }

        if ((long)bufferOffset + length > buffer.Length)
        {
            throw new ArgumentException("The buffer is too small for the slice");
        }

        if (fieldOffset >= source.Length)
        {
            return 0;
        }

        int take = Math.Min(source.Length - (int)fieldOffset, length);
        Array.Copy(source, fieldOffset, buffer, bufferOffset, take);
        return take;
    }

    private Value? Cell(int ordinal)
    {
        Value?[] row = Row();
        if ((uint)ordinal >= (uint)row.Length)
        {
            throw new IndexOutOfRangeException("No column at ordinal " + ordinal);
        }

        return row[ordinal];
    }

    private Value?[] Row()
    {
        ThrowIfClosed();
        if (result is null)
        {
            throw new InvalidOperationException("The reader has no rows");
        }

        return current
            ?? throw new InvalidOperationException("Read was not called or has no more rows");
    }

    private void CheckOrdinal(int ordinal)
    {
        ThrowIfClosed();
        if (result is null)
        {
            throw new InvalidOperationException("The reader has no rows");
        }

        if ((uint)ordinal >= (uint)FieldCount)
        {
            throw new IndexOutOfRangeException("No column at ordinal " + ordinal);
        }
    }

    private void ThrowIfClosed()
    {
        if (closed)
        {
            throw new InvalidOperationException("The reader is closed");
        }
    }
}
