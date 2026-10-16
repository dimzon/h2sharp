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
using System.Data;
using System.Data.Common;
using H2Sharp.Values;

namespace H2Sharp.Data;

/// <summary>
/// Reads the rows of a query result.
/// </summary>
public sealed class H2DataReader : DbDataReader
{
    private readonly Result.ResultInterface result;

    private readonly Action? onClose;

    private Value?[]? current;

    private bool closed;

    /// <summary>
    /// Creates a reader over the result.
    /// </summary>
    /// <param name="result">the engine result</param>
    /// <param name="onClose">what to do when the reader closes, if anything</param>
    internal H2DataReader(Result.ResultInterface result, Action? onClose = null)
    {
        this.result = result;
        this.onClose = onClose;
    }

    /// <inheritdoc/>
    public override int Depth => 0;

    /// <inheritdoc/>
    public override int FieldCount => result.GetVisibleColumnCount();

    /// <inheritdoc/>
    public override bool HasRows => result.HasNext() || current is not null;

    /// <inheritdoc/>
    public override bool IsClosed => closed;

    /// <inheritdoc/>
    public override int RecordsAffected => -1;

    /// <inheritdoc/>
    public override object this[int ordinal] => GetValue(ordinal);

    /// <inheritdoc/>
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <inheritdoc/>
    public override bool Read()
    {
        ThrowIfClosed();
        if (result.Next())
        {
            current = result.CurrentRow();
            return true;
        }

        current = null;
        return false;
    }

    /// <inheritdoc/>
    public override bool NextResult() => false;

    /// <inheritdoc/>
    public override void Close()
    {
        if (!closed)
        {
            closed = true;
            current = null;
            result.Close();
            onClose?.Invoke();
        }
    }

    /// <inheritdoc/>
    public override string GetName(int ordinal)
    {
        CheckOrdinal(ordinal);
        return result.GetColumnName(ordinal);
    }

    /// <inheritdoc/>
    public override int GetOrdinal(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
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
        return H2ValueMapper.GetFieldType(result.GetColumnType(ordinal));
    }

    /// <inheritdoc/>
    public override string GetDataTypeName(int ordinal)
    {
        CheckOrdinal(ordinal);
        return Value.GetTypeName(result.GetColumnType(ordinal).GetValueType());
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
    public override string GetString(int ordinal) => Get<string>(ordinal);

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
    public override IEnumerator GetEnumerator()
    {
        while (Read())
        {
            yield return this;
        }
    }

    /// <inheritdoc/>
    public override DataTable? GetSchemaTable()
    {
        DataTable table = new("SchemaTable");
        table.Columns.Add("ColumnName", typeof(string));
        table.Columns.Add("ColumnOrdinal", typeof(int));
        table.Columns.Add("ColumnSize", typeof(int));
        table.Columns.Add("NumericPrecision", typeof(int));
        table.Columns.Add("NumericScale", typeof(int));
        table.Columns.Add("DataType", typeof(Type));
        table.Columns.Add("DataTypeName", typeof(string));
        table.Columns.Add("AllowDBNull", typeof(bool));
        for (int i = 0, count = FieldCount; i < count; i++)
        {
            TypeInfo type = result.GetColumnType(i);
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
            row["AllowDBNull"] = result.GetNullable(i) != H2Sharp.Tables.Column.NotNullable;
            table.Rows.Add(row);
        }

        return table;
    }

    private T Get<T>(int ordinal)
        where T : notnull
    {
        object? value = GetValue(ordinal);
        if (value is T matched)
        {
            return matched;
        }

        if (value is DBNull)
        {
            throw new InvalidCastException("Cannot read NULL as " + typeof(T).Name);
        }

        if (value is IConvertible && typeof(T) != typeof(string) && typeof(T) != typeof(bool)
            && typeof(T) != typeof(DateTime) && typeof(T) != typeof(Guid))
        {
            return (T)Convert.ChangeType(value, typeof(T), null);
        }

        throw new InvalidCastException(
            "Cannot read " + value!.GetType().Name + " as " + typeof(T).Name);
    }

    private static long CopySlice<T>(
        T[] source, long fieldOffset, T[]? buffer, int bufferOffset, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fieldOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(bufferOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (buffer is not null)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(bufferOffset, buffer.Length);
        }

        if (fieldOffset >= source.Length)
        {
            return 0;
        }

        int available = source.Length - (int)fieldOffset;
        int take = Math.Min(available, length);
        if (buffer is not null)
        {
            take = Math.Min(take, buffer.Length - bufferOffset);
            Array.Copy(source, fieldOffset, buffer, bufferOffset, take);
        }

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
        return current
            ?? throw new InvalidOperationException("Read was not called or has no more rows");
    }

    private void CheckOrdinal(int ordinal)
    {
        ThrowIfClosed();
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
