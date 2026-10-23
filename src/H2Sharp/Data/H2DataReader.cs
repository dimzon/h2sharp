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
                _ = next.ExecuteUpdate(false);
                continue;
            }

            result = next.ExecuteQuery(maxrows, 0, false);
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
        Result.ResultInterface source = State();
        for (int i = 0, count = source.GetVisibleColumnCount(); i < count; i++)
        {
            if (string.Equals(source.GetColumnName(i), name, StringComparison.OrdinalIgnoreCase))
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
        return result!.GetColumnType(ordinal).GetDeclaredTypeName();
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
        Value cell = NotNull(ordinal, "Boolean");
        if (cell.GetValueType() == Value.Boolean)
        {
            return cell.GetBoolean();
        }

        if (cell.GetValueType() is Value.Char or Value.Varchar or Value.VarcharIgnorecase or Value.Clob)
        {
            return Convert.ToBoolean(cell.GetString(), CultureInfo.InvariantCulture);
        }

        throw CastNotSupported(cell, "Boolean");
    }

    private static readonly Func<int, byte> byteFromInt32 = Convert.ToByte;
    private static readonly Func<long, byte> byteFromInt64 = Convert.ToByte;
    private static readonly Func<float, byte> byteFromSingle = Convert.ToByte;
    private static readonly Func<double, byte> byteFromDouble = Convert.ToByte;
    private static readonly Func<decimal, byte> byteFromDecimal = Convert.ToByte;
    private static readonly Func<string, byte> byteFromString = s => Convert.ToByte(s, CultureInfo.InvariantCulture);

    private static readonly Func<int, short> shortFromInt32 = Convert.ToInt16;
    private static readonly Func<long, short> shortFromInt64 = Convert.ToInt16;
    private static readonly Func<float, short> shortFromSingle = Convert.ToInt16;
    private static readonly Func<double, short> shortFromDouble = Convert.ToInt16;
    private static readonly Func<decimal, short> shortFromDecimal = Convert.ToInt16;
    private static readonly Func<string, short> shortFromString = s => Convert.ToInt16(s, CultureInfo.InvariantCulture);

    private static readonly Func<int, int> intFromInt32 = Convert.ToInt32;
    private static readonly Func<long, int> intFromInt64 = Convert.ToInt32;
    private static readonly Func<float, int> intFromSingle = Convert.ToInt32;
    private static readonly Func<double, int> intFromDouble = Convert.ToInt32;
    private static readonly Func<decimal, int> intFromDecimal = Convert.ToInt32;
    private static readonly Func<string, int> intFromString = s => Convert.ToInt32(s, CultureInfo.InvariantCulture);

    private static readonly Func<int, long> longFromInt32 = Convert.ToInt64;
    private static readonly Func<long, long> longFromInt64 = Convert.ToInt64;
    private static readonly Func<float, long> longFromSingle = Convert.ToInt64;
    private static readonly Func<double, long> longFromDouble = Convert.ToInt64;
    private static readonly Func<decimal, long> longFromDecimal = Convert.ToInt64;
    private static readonly Func<string, long> longFromString = s => Convert.ToInt64(s, CultureInfo.InvariantCulture);

    private static readonly Func<int, decimal> decimalFromInt32 = Convert.ToDecimal;
    private static readonly Func<long, decimal> decimalFromInt64 = Convert.ToDecimal;
    private static readonly Func<float, decimal> decimalFromSingle = Convert.ToDecimal;
    private static readonly Func<double, decimal> decimalFromDouble = Convert.ToDecimal;
    private static readonly Func<decimal, decimal> decimalFromDecimal = Convert.ToDecimal;
    private static readonly Func<string, decimal> decimalFromString = s => Convert.ToDecimal(s, CultureInfo.InvariantCulture);

    private static readonly Func<int, double> doubleFromInt32 = Convert.ToDouble;
    private static readonly Func<long, double> doubleFromInt64 = Convert.ToDouble;
    private static readonly Func<float, double> doubleFromSingle = Convert.ToDouble;
    private static readonly Func<double, double> doubleFromDouble = Convert.ToDouble;
    private static readonly Func<decimal, double> doubleFromDecimal = Convert.ToDouble;
    private static readonly Func<string, double> doubleFromString = s => Convert.ToDouble(s, CultureInfo.InvariantCulture);

    private static readonly Func<int, float> floatFromInt32 = Convert.ToSingle;
    private static readonly Func<long, float> floatFromInt64 = Convert.ToSingle;
    private static readonly Func<float, float> floatFromSingle = Convert.ToSingle;
    private static readonly Func<double, float> floatFromDouble = Convert.ToSingle;
    private static readonly Func<decimal, float> floatFromDecimal = Convert.ToSingle;
    private static readonly Func<string, float> floatFromString = s => Convert.ToSingle(s, CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public override byte GetByte(int ordinal)
        => ReadNumeric(ordinal, "Byte",
            byteFromInt32, byteFromInt64, byteFromSingle, byteFromDouble, byteFromDecimal,
            byteFromString);

    /// <inheritdoc/>
    public override char GetChar(int ordinal)
    {
        _ = NotNull(ordinal, "Char");
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
        Value cell = NotNull(ordinal, "DateTime");
        object? value = H2ValueMapper.ToObject(cell);
        if (value is DateTime dateTime)
        {
            return dateTime;
        }

        if (value is string text)
        {
            return Convert.ToDateTime(text, CultureInfo.InvariantCulture);
        }

        throw CastNotSupported(cell, "DateTime");
    }

    /// <inheritdoc/>
    public override decimal GetDecimal(int ordinal)
        => ReadNumeric(ordinal, "Decimal",
            decimalFromInt32, decimalFromInt64, decimalFromSingle, decimalFromDouble, decimalFromDecimal,
            decimalFromString);

    /// <inheritdoc/>
    public override double GetDouble(int ordinal)
        => ReadNumeric(ordinal, "Double",
            doubleFromInt32, doubleFromInt64, doubleFromSingle, doubleFromDouble, doubleFromDecimal,
            doubleFromString);

    /// <inheritdoc/>
    public override float GetFloat(int ordinal)
        => ReadNumeric(ordinal, "Single",
            floatFromInt32, floatFromInt64, floatFromSingle, floatFromDouble, floatFromDecimal,
            floatFromString);

    /// <inheritdoc/>
    public override Guid GetGuid(int ordinal)
    {
        Value cell = NotNull(ordinal, "Guid");
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

        throw CastNotSupported(cell, "Guid");
    }

    /// <inheritdoc/>
    public override short GetInt16(int ordinal)
        => ReadNumeric(ordinal, "Int16",
            shortFromInt32, shortFromInt64, shortFromSingle, shortFromDouble, shortFromDecimal,
            shortFromString);

    /// <inheritdoc/>
    public override int GetInt32(int ordinal)
        => ReadNumeric(ordinal, "Int32",
            intFromInt32, intFromInt64, intFromSingle, intFromDouble, intFromDecimal,
            intFromString);

    /// <inheritdoc/>
    public override long GetInt64(int ordinal)
        => ReadNumeric(ordinal, "Int64",
            longFromInt32, longFromInt64, longFromSingle, longFromDouble, longFromDecimal,
            longFromString);

    /// <inheritdoc/>
    /// <remarks>
    /// Any type reads as text in its H2 spelling: TRUE stays TRUE rather than becoming True,
    /// which is what a CLR conversion would print. That is JDBC's getString rule, and the
    /// engine already spells every value.
    /// </remarks>
    public override string GetString(int ordinal)
    {
        Value cell = NotNull(ordinal, "String");
        return cell.GetString()
            ?? throw new InvalidCastException("Cannot read column " + ordinal + " as String");
    }

    /// <inheritdoc/>
    public override long GetBytes(int ordinal, long fieldOffset, byte[]? buffer, int bufferOffset, int length)
    {
        Value cell = NotNull(ordinal, "Byte[]");
        if (cell.GetValueType() is Value.Binary or Value.Varbinary or Value.Blob or Value.Json
            && cell.GetBytes() is byte[] bytes)
        {
            return CopySlice(bytes, fieldOffset, buffer, bufferOffset, length);
        }

        throw CastNotSupported(cell, "Byte[]");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The window copies, never the whole text: the string stays where it is and only the
    /// asked slice moves, and a length question does not even do that.
    /// </remarks>
    public override long GetChars(int ordinal, long fieldOffset, char[]? buffer, int bufferOffset, int length)
        => CopySlice(GetString(ordinal), fieldOffset, buffer, bufferOffset, length);

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

        // JDBC getCharacterStream reads JSON through Value.getReader / getString;
        // its default object is byte[], but the explicit text path remains available.
        if (cell.GetValueType() == Value.Json)
        {
            return new StringReader(cell.GetString()!);
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
        // JDBC getObject(column, String.class) uses Value.getString independently of
        // the default JSON byte[] mapping. Keep the same explicit text conversion here.
        if (typeof(T) == typeof(string) && Cell(ordinal)?.GetValueType() == Value.Json)
        {
            return (T)(object)GetString(ordinal);
        }

        if (typeof(T).IsArray && Cell(ordinal) is ValueArray array)
        {
            return (T)(object)H2ValueMapper.ToArrayObject(array, typeof(T));
        }

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

    private Value NotNull(int ordinal, string target)
    {
        Value? cell = Cell(ordinal);
        if (cell is null or ValueNull)
        {
            throw new InvalidCastException("Cannot read NULL as " + target);
        }

        return cell;
    }

    private static InvalidCastException CastNotSupported(Value cell, string target)
        => new("Cannot read " + Value.GetTypeName(cell.GetValueType()) + " as " + target);

    /// <summary>
    /// The numeric getters differ only in the conversion overload (issue #12 C2): the source
    /// type picks the arm, and the caller names the conversion for each arm. Every arm answers
    /// exactly what the per-getter ladders answered, including the overflow checks of
    /// <see cref="Convert"/> on the narrowing arms.
    /// </summary>
    private T ReadNumeric<T>(int ordinal, string target,
        Func<int, T> fromInt32, Func<long, T> fromInt64, Func<float, T> fromSingle,
        Func<double, T> fromDouble, Func<decimal, T> fromDecimal, Func<string, T> fromString)
    {
        Value cell = NotNull(ordinal, target);
        return cell.GetValueType() switch
        {
            Value.Tinyint or Value.Smallint or Value.Integer => fromInt32(cell.GetInt()),
            Value.Bigint => fromInt64(cell.GetLong()),
            Value.Real => fromSingle(cell.GetFloat()),
            Value.Double => fromDouble(cell.GetDouble()),
            Value.Numeric or Value.Decfloat => fromDecimal(H2ValueMapper.ToDecimal(cell)),
            Value.Char or Value.Varchar or Value.VarcharIgnorecase or Value.Clob
                => fromString(cell.GetString()!),
            _ => throw CastNotSupported(cell, target),
        };
    }

    private static H2SharpException NullValueException(string target)
        => Message.DbException.Get(Api.ErrorCode.DataConversionError1, "NULL", target);

    /// <inheritdoc/>
    /// <remarks>
    /// Synchronous underneath — the row is in memory — so cancellation only refuses
    /// before it starts. See OpenAsync for when this becomes real.
    /// </remarks>
    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
        => SyncTask.Run(() => IsDBNull(ordinal), cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// Same synchronicity note as <see cref="IsDBNullAsync"/>.
    /// </remarks>
    public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
        => SyncTask.Run(() => GetFieldValue<T>(ordinal), cancellationToken);

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

        List<DbColumn> columns = new(ColumnDescriptions());
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
        List<H2DbColumn> columns = ColumnDescriptions();
        if (columns.Count == 0)
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
        foreach (H2DbColumn column in columns)
        {
            DataRow row = table.NewRow();
            row["ColumnName"] = column.ColumnName;
            row["ColumnOrdinal"] = column.ColumnOrdinal;
            row["ColumnSize"] = column.NumericPrecision ?? -1;
            row["NumericPrecision"] = column.NumericPrecision ?? (object)DBNull.Value;
            row["NumericScale"] = column.NumericScale ?? (object)DBNull.Value;
            row["DataType"] = column.DataType;
            row["DataTypeName"] = column.DataTypeName;
            row["AllowDBNull"] = column.AllowDBNull;
            table.Rows.Add(row);
        }

        return table;
    }

    /// <summary>
    /// Describes every visible column once, so the schema table and the column schema
    /// cannot disagree (issue #12 C10).
    /// </summary>
    /// <returns>the column descriptions, empty when there are no columns</returns>
    private List<H2DbColumn> ColumnDescriptions()
    {
        List<H2DbColumn> columns = [];
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

        return columns;
    }

    private static long CopySlice(
        ReadOnlySpan<char> source, long fieldOffset, char[]? buffer, int bufferOffset, int length)
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
        source.Slice((int)fieldOffset, take).CopyTo(buffer.AsSpan(bufferOffset));
        return take;
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
        _ = State();
        return current
            ?? throw new InvalidOperationException("Read was not called or has no more rows");
    }

    private void CheckOrdinal(int ordinal)
    {
        _ = State();
        if ((uint)ordinal >= (uint)FieldCount)
        {
            throw new IndexOutOfRangeException("No column at ordinal " + ordinal);
        }
    }

    private Result.ResultInterface State()
    {
        ThrowIfClosed();
        return result ?? throw new InvalidOperationException("The reader has no rows");
    }

    private void ThrowIfClosed()
    {
        if (closed)
        {
            throw new InvalidOperationException("The reader is closed");
        }
    }
}
