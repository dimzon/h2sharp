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
using System.Globalization;
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
    /// <remarks>
    /// Each getter converts from the cell, never through a boxed value: widening and
    /// narrowing go through the <see cref="Convert"/> primitive overloads, so a long that
    /// does not fit still overflows rather than truncating, exactly as the boxed road did.
    /// Strings parse the way they did, and anything else refuses.
    /// </remarks>
    public override bool GetBoolean(int ordinal)
    {
        Value? cell = Cell(ordinal);
        if (cell is null or ValueNull)
        {
            throw new InvalidCastException("Cannot read NULL as Boolean");
        }

        if (cell.GetValueType() == Value.Boolean)
        {
            return cell.GetBoolean();
        }

        if (cell.GetValueType() is Value.Char or Value.Varchar or Value.VarcharIgnorecase or Value.Clob)
        {
            return Convert.ToBoolean(cell.GetString(), CultureInfo.InvariantCulture);
        }

        throw new InvalidCastException(
            "Cannot read " + Value.GetTypeName(cell.GetValueType()) + " as Boolean");
    }

    /// <inheritdoc/>
    public override byte GetByte(int ordinal)
    {
        Value? cell = Cell(ordinal);
        if (cell is null or ValueNull)
        {
            throw new InvalidCastException("Cannot read NULL as Byte");
        }

        return cell.GetValueType() switch
        {
            Value.Tinyint or Value.Smallint or Value.Integer => Convert.ToByte(cell.GetInt()),
            Value.Bigint => Convert.ToByte(cell.GetLong()),
            Value.Real => Convert.ToByte(cell.GetFloat()),
            Value.Double => Convert.ToByte(cell.GetDouble()),
            Value.Numeric or Value.Decfloat => Convert.ToByte(H2ValueMapper.ToDecimal(cell)),
            Value.Char or Value.Varchar or Value.VarcharIgnorecase or Value.Clob
                => Convert.ToByte(cell.GetString(), CultureInfo.InvariantCulture),
            _ => throw new InvalidCastException(
                "Cannot read " + Value.GetTypeName(cell.GetValueType()) + " as Byte"),
        };
    }

    /// <inheritdoc/>
    public override char GetChar(int ordinal)
    {
        Value? cell = Cell(ordinal);
        if (cell is null or ValueNull)
        {
            throw new InvalidCastException("Cannot read NULL as Char");
        }

        string text = GetString(ordinal);
        if (text.Length == 1)
        {
            return text[0];
        }

        throw new InvalidCastException("Cannot read column " + ordinal + " as Char");
    }

    /// <inheritdoc/>
    public override DateTime GetDateTime(int ordinal)
    {
        Value? cell = Cell(ordinal);
        if (cell is null or ValueNull)
        {
            throw new InvalidCastException("Cannot read NULL as DateTime");
        }

        object? value = H2ValueMapper.ToObject(cell);
        if (value is DateTime dateTime)
        {
            return dateTime;
        }

        if (value is string text)
        {
            return Convert.ToDateTime(text, CultureInfo.InvariantCulture);
        }

        throw new InvalidCastException(
            "Cannot read " + Value.GetTypeName(cell.GetValueType()) + " as DateTime");
    }

    /// <inheritdoc/>
    public override decimal GetDecimal(int ordinal)
    {
        Value? cell = Cell(ordinal);
        if (cell is null or ValueNull)
        {
            throw new InvalidCastException("Cannot read NULL as Decimal");
        }

        return cell.GetValueType() switch
        {
            Value.Tinyint or Value.Smallint or Value.Integer => cell.GetInt(),
            Value.Bigint => cell.GetLong(),
            Value.Real => Convert.ToDecimal(cell.GetFloat()),
            Value.Double => Convert.ToDecimal(cell.GetDouble()),
            Value.Numeric or Value.Decfloat => H2ValueMapper.ToDecimal(cell),
            Value.Char or Value.Varchar or Value.VarcharIgnorecase or Value.Clob
                => Convert.ToDecimal(cell.GetString(), CultureInfo.InvariantCulture),
            _ => throw new InvalidCastException(
                "Cannot read " + Value.GetTypeName(cell.GetValueType()) + " as Decimal"),
        };
    }

    /// <inheritdoc/>
    public override double GetDouble(int ordinal)
    {
        Value? cell = Cell(ordinal);
        if (cell is null or ValueNull)
        {
            throw new InvalidCastException("Cannot read NULL as Double");
        }

        return cell.GetValueType() switch
        {
            Value.Tinyint or Value.Smallint or Value.Integer => cell.GetInt(),
            Value.Bigint => cell.GetLong(),
            Value.Real => cell.GetFloat(),
            Value.Double => cell.GetDouble(),
            Value.Numeric or Value.Decfloat => Convert.ToDouble(H2ValueMapper.ToDecimal(cell)),
            Value.Char or Value.Varchar or Value.VarcharIgnorecase or Value.Clob
                => Convert.ToDouble(cell.GetString(), CultureInfo.InvariantCulture),
            _ => throw new InvalidCastException(
                "Cannot read " + Value.GetTypeName(cell.GetValueType()) + " as Double"),
        };
    }

    /// <inheritdoc/>
    public override float GetFloat(int ordinal)
    {
        Value? cell = Cell(ordinal);
        if (cell is null or ValueNull)
        {
            throw new InvalidCastException("Cannot read NULL as Single");
        }

        return cell.GetValueType() switch
        {
            Value.Tinyint or Value.Smallint or Value.Integer => cell.GetInt(),
            Value.Bigint => cell.GetLong(),
            Value.Real => cell.GetFloat(),
            Value.Double => Convert.ToSingle(cell.GetDouble()),
            Value.Numeric or Value.Decfloat => Convert.ToSingle(H2ValueMapper.ToDecimal(cell)),
            Value.Char or Value.Varchar or Value.VarcharIgnorecase or Value.Clob
                => Convert.ToSingle(cell.GetString(), CultureInfo.InvariantCulture),
            _ => throw new InvalidCastException(
                "Cannot read " + Value.GetTypeName(cell.GetValueType()) + " as Single"),
        };
    }

    /// <inheritdoc/>
    public override Guid GetGuid(int ordinal)
    {
        Value? cell = Cell(ordinal);
        if (cell is null or ValueNull)
        {
            throw new InvalidCastException("Cannot read NULL as Guid");
        }

        if (cell.GetValueType() == Value.Uuid)
        {
            return ((ValueUuid)cell).GetUuid();
        }

        if (cell.GetValueType() is Value.Binary or Value.Varbinary or Value.Blob
            && cell.GetBytes() is byte[] bytes && bytes.Length == 16)
        {
            return new Guid(bytes);
        }

        if (cell.GetValueType() is Value.Char or Value.Varchar or Value.VarcharIgnorecase or Value.Clob)
        {
            return Guid.Parse(cell.GetString()!);
        }

        throw new InvalidCastException(
            "Cannot read " + Value.GetTypeName(cell.GetValueType()) + " as Guid");
    }

    /// <inheritdoc/>
    public override short GetInt16(int ordinal)
    {
        Value? cell = Cell(ordinal);
        if (cell is null or ValueNull)
        {
            throw new InvalidCastException("Cannot read NULL as Int16");
        }

        return cell.GetValueType() switch
        {
            Value.Tinyint or Value.Smallint or Value.Integer => Convert.ToInt16(cell.GetInt()),
            Value.Bigint => Convert.ToInt16(cell.GetLong()),
            Value.Real => Convert.ToInt16(cell.GetFloat()),
            Value.Double => Convert.ToInt16(cell.GetDouble()),
            Value.Numeric or Value.Decfloat => Convert.ToInt16(H2ValueMapper.ToDecimal(cell)),
            Value.Char or Value.Varchar or Value.VarcharIgnorecase or Value.Clob
                => Convert.ToInt16(cell.GetString(), CultureInfo.InvariantCulture),
            _ => throw new InvalidCastException(
                "Cannot read " + Value.GetTypeName(cell.GetValueType()) + " as Int16"),
        };
    }

    /// <inheritdoc/>
    public override int GetInt32(int ordinal)
    {
        Value? cell = Cell(ordinal);
        if (cell is null or ValueNull)
        {
            throw new InvalidCastException("Cannot read NULL as Int32");
        }

        return cell.GetValueType() switch
        {
            Value.Tinyint or Value.Smallint or Value.Integer => cell.GetInt(),
            Value.Bigint => Convert.ToInt32(cell.GetLong()),
            Value.Real => Convert.ToInt32(cell.GetFloat()),
            Value.Double => Convert.ToInt32(cell.GetDouble()),
            Value.Numeric or Value.Decfloat => Convert.ToInt32(H2ValueMapper.ToDecimal(cell)),
            Value.Char or Value.Varchar or Value.VarcharIgnorecase or Value.Clob
                => Convert.ToInt32(cell.GetString(), CultureInfo.InvariantCulture),
            _ => throw new InvalidCastException(
                "Cannot read " + Value.GetTypeName(cell.GetValueType()) + " as Int32"),
        };
    }

    /// <inheritdoc/>
    public override long GetInt64(int ordinal)
    {
        Value? cell = Cell(ordinal);
        if (cell is null or ValueNull)
        {
            throw new InvalidCastException("Cannot read NULL as Int64");
        }

        return cell.GetValueType() switch
        {
            Value.Tinyint or Value.Smallint or Value.Integer => cell.GetInt(),
            Value.Bigint => cell.GetLong(),
            Value.Real => Convert.ToInt64(cell.GetFloat()),
            Value.Double => Convert.ToInt64(cell.GetDouble()),
            Value.Numeric or Value.Decfloat => Convert.ToInt64(H2ValueMapper.ToDecimal(cell)),
            Value.Char or Value.Varchar or Value.VarcharIgnorecase or Value.Clob
                => Convert.ToInt64(cell.GetString(), CultureInfo.InvariantCulture),
            _ => throw new InvalidCastException(
                "Cannot read " + Value.GetTypeName(cell.GetValueType()) + " as Int64"),
        };
    }

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
        Value? cell = Cell(ordinal);
        if (cell is null or ValueNull)
        {
            throw new InvalidCastException("Cannot read NULL as Byte[]");
        }

        if (cell.GetValueType() is Value.Binary or Value.Varbinary or Value.Blob
            && cell.GetBytes() is byte[] bytes)
        {
            return CopySlice(bytes, fieldOffset, buffer, bufferOffset, length);
        }

        throw new InvalidCastException(
            "Cannot read " + Value.GetTypeName(cell.GetValueType()) + " as Byte[]");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The window copies, never the whole text: the string stays where it is and only the
    /// asked slice moves, and a length question does not even do that.
    /// </remarks>
    public override long GetChars(int ordinal, long fieldOffset, char[]? buffer, int bufferOffset, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fieldOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(bufferOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        string text = GetString(ordinal);
        if (buffer is null)
        {
            return text.Length;
        }

        if (bufferOffset > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferOffset));
        }

        if ((long)bufferOffset + length > buffer.Length)
        {
            throw new ArgumentException("The buffer is too small for the slice");
        }

        if (fieldOffset >= text.Length)
        {
            return 0;
        }

        int take = Math.Min(text.Length - (int)fieldOffset, length);
        text.AsSpan((int)fieldOffset, take).CopyTo(buffer.AsSpan(bufferOffset));
        return take;
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
