/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mvstore.db.ValueDataType.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A row type: how a value is written into a page and read back.
 *
 * This is bytes on disk, and every byte here is a decision someone made to save a byte.
 *
 * A type tag, but small and common values have *no* payload after the tag: FALSE is one byte
 * (64), TRUE is one byte (65), and integers 0..15 are one byte each because the tag itself is
 * biased by the value (32 + x). A column of small integers costs one byte per row.
 *
 * Negative numbers get their own tag rather than a sign bit. INT_NEG (66) is followed by the
 * absolute value, so -1 costs two bytes and 1 costs one. Sign-magnitude would make 1 cost two.
 * BIGINT has the same trick one size up (0..7 inline), NUMERIC and REAL one size up again.
 *
 * The tags are numbers with holes in them (18, 23, 29..31, 49..55, 61, 69..99, 101..133) and the
 * holes are not an accident to tidy: 132 was SPATIAL_KEY_2D, 133 was CUSTOM_DATA_TYPE, and a file
 * written by a version that used them must still be readable. That is why the reference comments
 * them and why the comment is kept here.
 *
 * The kinds ported so far are the scalar ones: boolean, the numeric family, strings, binaries,
 * dates and times, char and uuid. ARRAY, ROW, INTERVAL, JSON, LOB, GEOGRAPHY and JAVA_OBJECT
 * refuse — and they refuse *inside the switch*, on both sides, so an unported kind cannot be
 * written by accident and then read back as something else.
 */
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Type;
using H2Sharp.Result;
using H2Sharp.Util;
using H2Sharp.Store;
using H2Sharp.Values;
using H2Sharp.Values.Lobs;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// A row type.
/// </summary>
public sealed class ValueDataType : BasicDataType<Value>
{
    private const byte Null = 0;
    private const byte Tinyint = 2;
    private const byte Smallint = 3;
    private const byte Integer = 4;
    private const byte Bigint = 5;
    private const byte Numeric = 6;
    private const byte Double = 7;
    private const byte Real = 8;
    private const byte Time = 9;
    private const byte Date = 10;
    private const byte Timestamp = 11;
    private const byte Varbinary = 12;
    private const byte Varchar = 13;
    private const byte VarcharIgnoreCase = 14;
    private const byte Blob = 15;
    private const byte Clob = 16;
    private const byte Array = 17;
    private const byte JavaObject = 19;
    private const byte Uuid = 20;
    private const byte Char = 21;
    private const byte Geometry = 22;
    private const byte Enum = 25;
    private const byte Interval = 26;
    private const byte Row = 27;
    /// <summary>
    /// The BINARY tag. It is not a byte: the reference declares it as an int because its
    /// number is 137, and a byte would not hold it — which is why the type here is int and not
    /// byte like the tags above.
    /// </summary>
    private const int Binary = 137;

    private const byte Int0To15 = 32;
    private const byte Bigint0To7 = 48;
    private const byte Numeric0To1 = 56;
    private const byte NumericSmall0 = 58;
    private const byte NumericSmall = 59;
    private const byte Double0To1 = 60;
    private const byte Real0To1 = 62;
    private const byte BooleanFalse = 64;
    private const byte BooleanTrue = 65;
    private const byte IntNeg = 66;
    private const byte BigintNeg = 67;

    /// <summary>
    /// VARCHAR of 0..31 characters: the tag carries the length. There is no short form for
    /// VARCHAR_IGNORECASE — the reference does not fold the text on the way in either.
    /// </summary>
    private const int Varchar0To31 = 68;

    /// <summary>
    /// VARBINARY of 0..31 bytes: the tag carries the length, and 132/133 below this block were
    /// SPATIAL_KEY_2D and CUSTOM_DATA_TYPE in older versions.
    /// </summary>
    private const int Varbinary0To31 = 100;

    private readonly CastDataProvider? provider;

    private readonly CompareMode compareMode;

    private readonly DataHandler? handler;

    private readonly int[]? sortTypes;

    private RowFactory? rowFactory;

    /// <summary>
    /// Creates the type.
    /// </summary>
    /// <param name="provider">where casts come from</param>
    /// <param name="compareMode">how values are compared</param>
    /// <param name="handler">where LOBs live</param>
    /// <param name="sortTypes">the sort order of each column, or null</param>
    public ValueDataType(CastDataProvider? provider, CompareMode compareMode, DataHandler? handler, int[]? sortTypes)
    {
        this.provider = provider;
        this.compareMode = compareMode;
        this.handler = handler;
        this.sortTypes = sortTypes;
    }

    /// <summary>
    /// Gets the row factory that builds rows while reading.
    /// </summary>
    public RowFactory? GetRowFactory()
    {
        return rowFactory;
    }

    /// <summary>
    /// Sets the row factory that builds rows while reading.
    /// </summary>
    /// <param name="rowFactory">the row factory</param>
    public void SetRowFactory(RowFactory? rowFactory)
    {
        this.rowFactory = rowFactory;
    }

    /// <inheritdoc/>
    public override Value[] CreateStorage(int size)
    {
        return new Value[size];
    }

    /// <inheritdoc/>
    public override int Compare(Value? one, Value? two)
    {
        if (ReferenceEquals(one, two))
        {
            return 0;
        }

        if (one is null || two is null)
        {
            return one is null ? -1 : 1;
        }

        if (one is SearchRow rowOne && two is SearchRow rowTwo)
        {
            return CompareRows(rowOne, rowTwo);
        }

        return CompareValues(one, two, SortOrder.Ascending);
    }

    private int CompareRows(SearchRow a, SearchRow b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        int[]? indexes = rowFactory?.GetIndexes();
        if (indexes is null)
        {
            int len = a.GetColumnCount();
            for (int i = 0; i < len; i++)
            {
                int comp = CompareValues(a.GetValue(i), b.GetValue(i), sortTypes![i]);
                if (comp != 0)
                {
                    return comp;
                }
            }

            return 0;
        }

        for (int i = 0; i < indexes.Length; i++)
        {
            int index = indexes[i];
            Value? v1 = a.GetValue(index);
            Value? v2 = b.GetValue(index);
            if (v1 is null || v2 is null)
            {
                // can't compare further
                break;
            }

            int comp = CompareValues(v1, v2, sortTypes![i]);
            if (comp != 0)
            {
                return comp;
            }
        }

        // The row key is the tie-breaker: two rows whose index columns are all equal are still
        // different rows, and the storage layer must be able to tell them apart.
        return a.GetKey().CompareTo(b.GetKey());
    }

    /// <summary>
    /// Compares two values.
    /// </summary>
    /// <remarks>
    /// Ported from org.h2.mvstore.db.ValueDataType.compareValues: indexes with nullable
    /// values carry an explicit NULL ordering, and descending is a bit in the mask, not the
    /// whole mask.
    /// </remarks>
    /// <param name="a">the first value</param>
    /// <param name="b">the second value</param>
    /// <param name="sortType">the sort type mask</param>
    /// <returns>negative, zero or positive</returns>
    public int CompareValues(Value? a, Value? b, int sortType)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        bool aNull = a is null || a is ValueNull;
        if (aNull || b is null || b is ValueNull)
        {
            return DefaultNullOrderingRules.CompareNull(DefaultNullOrdering.LOW, aNull, sortType);
        }

        // The provider is null here because a stored value never needs a cast to be compared: both
        // sides come from the same column, so the types already agree.
        int result = a!.CompareTo(b, provider, compareMode);
        return (sortType & SortOrder.Descending) != 0 ? -result : result;
    }

    /// <inheritdoc/>
    public override void Write(WriteBuffer buff, Value v)
    {
        if (v is null || v is ValueNull)
        {
            buff.Put(Null);
            return;
        }

        int type = v.GetValueType();
        switch (type)
        {
            case Value.Boolean:
                buff.Put(v.GetBoolean() ? BooleanTrue : BooleanFalse);
                break;

            case Value.Tinyint:
                buff.Put(Tinyint).Put((byte)v.GetByte());
                break;

            case Value.Smallint:
                buff.Put(Smallint).PutShort(v.GetShort());
                break;

            case Value.Enum:
            case Value.Integer:
                {
                    int x = v.GetInt();
                    if (x < 0)
                    {
                        buff.Put(IntNeg).PutVarInt(-x);
                    }
                    else if (x < 16)
                    {
                        buff.Put((byte)(Int0To15 + x));
                    }
                    else
                    {
                        buff.Put(type == Value.Integer ? Integer : Enum).PutVarInt(x);
                    }

                    break;
                }

            case Value.Bigint:
                WriteLong(buff, v.GetLong());
                break;

            case Value.Real:
                {
                    float x = v.GetFloat();
                    if (x == 1.0f)
                    {
                        buff.Put((byte)(Real0To1 + 1));
                    }
                    else
                    {
                        // The bits are reversed and written as a var int, not as four bytes: the
                        // most significant byte of a float is its sign and exponent, so most floats
                        // have few significant *trailing* bits and the var int is short. 1.5 is
                        // three bytes on disk instead of five.
                        int f = BitConverter.SingleToInt32Bits(x);
                        if (f == ValueReal.ZeroBits)
                        {
                            buff.Put(Real0To1);
                        }
                        else
                        {
                            buff.Put(Real).PutVarInt(Bits.Reverse(f));
                        }
                    }

                    break;
                }

            case Value.Double:
                {
                    double x = v.GetDouble();
                    if (x == 1.0d)
                    {
                        buff.Put((byte)(Double0To1 + 1));
                    }
                    else
                    {
                        long d = BitConverter.DoubleToInt64Bits(x);
                        if (d == ValueDouble.ZeroBits)
                        {
                            buff.Put(Double0To1);
                        }
                        else
                        {
                            buff.Put(Double).PutVarLong(Bits.Reverse(d));
                        }
                    }

                    break;
                }

            case Value.Varchar:
            case Value.VarcharIgnorecase:
                {
                    // Only VARCHAR has a short form: the reference stores VARCHAR_IGNORECASE with its
                    // own tag and an explicit length, and it stores the text **as it is** — the
                    // folding is a comparison rule, not a storage one. A block of short forms for
                    // VARCHAR_IGNORECASE would also land on 100, which is VARBINARY's.
                    string s = v.GetString() ?? string.Empty;
                    int len = s.Length;
                    if (type == Value.Varchar && len < 32)
                    {
                        buff.Put((byte)(Varchar0To31 + len)).PutStringData(s, len);
                    }
                    else
                    {
                        buff.Put(type == Value.Varchar ? Varchar : VarcharIgnoreCase);
                        WriteString(buff, s);
                    }

                    break;
                }

            case Value.Varbinary:
                {
                    // VARBINARY has a short form from 100 up, and unlike BINARY it is a plain block of
                    // bytes with no length repeated. This is also where the block I had once given to
                    // VARCHAR_IGNORECASE actually belongs.
                    byte[] bytes = v.GetBytesNoCopy() ?? [];
                    int len = bytes.Length;
                    if (len < 32)
                    {
                        buff.Put((byte)(Varbinary0To31 + len)).Put(bytes, 0, len);
                    }
                    else
                    {
                        buff.Put(Varbinary).PutVarInt(len).Put(bytes, 0, len);
                    }

                    break;
                }

            case Value.Binary:
                {
                    byte[] bytes = v.GetBytesNoCopy() ?? [];
                    buff.Put(Binary).PutVarInt(bytes.Length).Put(bytes, 0, bytes.Length);
                    break;
                }

            case Value.Char:
                {
                    buff.Put(Char);
                    WriteString(buff, v.GetString() ?? string.Empty);
                    break;
                }

            case Value.Date:
                buff.Put(Date).PutVarInt((int)((ValueDate)v).GetDateValue());
                break;

            case Value.Time:
                WriteTimestampTime(buff.Put(Time), ((ValueTime)v).GetNanos());
                break;

            case Value.Timestamp:
                {
                    ValueTimestamp ts = (ValueTimestamp)v;
                    WriteTimestampTime(buff.Put(Timestamp), DateTimeUtils.GetEpochSeconds(ts.GetDateValue(), ts.GetTimeNanos(), 0)
                        * 1_000_000_000L + (ts.GetTimeNanos() % 1_000_000_000L));
                    break;
                }

            case Value.Uuid:
                {
                    // Two longs as eight raw bytes each, high then low. Not the var int trick the
                    // numeric types use: a UUID is always sixteen bytes whatever the value, so there
                    // is nothing to compress.
                    ValueUuid uuid = (ValueUuid)v;
                    buff.Put(Uuid).PutLong(uuid.GetHigh()).PutLong(uuid.GetLow());
                    break;
                }

            case Value.Numeric:
                {
                    BigDecimal x = v.GetBigDecimal();
                    if (x.Signum() == 0)
                    {
                        buff.Put(Numeric0To1);
                    }
                    else if (x.CompareTo(BigDecimal.One) == 0)
                    {
                        buff.Put((byte)(Numeric0To1 + 1));
                    }
                    else
                    {
                        int scale = x.Scale;
                        BigInteger b = x.UnscaledValue;
                        int bits = (int)b.GetBitLength();
                        if (bits <= 63)
                        {
                            if (scale == 0)
                            {
                                buff.Put(NumericSmall0).PutVarLong((long)b);
                            }
                            else
                            {
                                buff.Put(NumericSmall).PutVarInt(scale).PutVarLong((long)b);
                            }
                        }
                        else
                        {
                            // Minimal two's complement big-endian, as java.math.BigInteger
                            // writes it: the row format and the object format agree byte for
                            // byte, and both agree with the reference.
                            byte[] bytes = Bits.ToBigEndian(b);
                            buff.Put(Numeric).PutVarInt(scale).PutVarInt(bytes.Length).Put(bytes, 0, bytes.Length);
                        }
                    }

                    break;
                }

            case Value.Array:
                throw new TODOException("ValueDataType.write: ARRAY");

            case Value.Row:
                throw new TODOException("ValueDataType.write: ROW");

            case Value.IntervalMinuteToSecond:
                throw new TODOException("ValueDataType.write: INTERVAL");

            case Value.Blob:
                {
                    buff.Put(Blob);
                    ValueBlob lob = (ValueBlob)v;
                    LobData lobData = lob.GetLobData();
                    if (lobData is LobDataInMemory memory)
                    {
                        byte[] small = memory.GetSmall()!;
                        buff.PutVarInt(small.Length).Put(small, 0, small.Length);
                    }
                    else
                    {
                        // A lob that lives in the database is written as a reference to it, and
                        // the storage of the lobs is not ported.
                        throw new TODOException("ValueDataType.write: LOB in the database");
                    }

                    break;
                }

            case Value.Clob:
                {
                    buff.Put(Clob);
                    ValueClob lob = (ValueClob)v;
                    LobData lobData = lob.GetLobData();
                    if (lobData is LobDataInMemory memory)
                    {
                        byte[] small = memory.GetSmall()!;
                        buff.PutVarInt(small.Length).Put(small, 0, small.Length)
                            .PutVarLong(lob.CharLength());
                    }
                    else
                    {
                        throw new TODOException("ValueDataType.write: LOB in the database");
                    }

                    break;
                }

            case Value.Geometry:
                throw new TODOException("ValueDataType.write: GEOMETRY");

            case Value.JavaObject:
                throw new TODOException("ValueDataType.write: JAVA_OBJECT");

            default:
                throw new ArgumentException("Unsupported value type " + type, nameof(v));
        }
    }

    /// <summary>
    /// Java's <c>Integer.reverse</c> and <c>Long.reverse</c>, carried by <see cref="Bits.Reverse(int)"/>
    /// and <see cref="Bits.Reverse(long)"/> — the calls the reference makes directly on its
    /// Integer and Long classes.
    /// </summary>
    /// <remarks>
    /// The reference writes a float or a double as its *bit pattern*, reversed, as a var int —
    /// the point being that a float's significant bits are its trailing ones, so a common value
    /// costs three bytes instead of five. The reversal is what makes that true, and it is not
    /// a byte swap: every bit moves, exactly what <see cref="Bits"/> reverses.
    /// </remarks>

    /// <summary>
    /// Writes a long with the same tricks as the integer: negative gets its own tag, 0..7 inline.
    /// </summary>
    /// <param name="buff">the buffer</param>
    /// <param name="x">the value</param>
    public static void WriteLong(WriteBuffer buff, long x)
    {
        if (x < 0)
        {
            buff.Put(BigintNeg).PutVarLong(-x);
        }
        else if (x < 8)
        {
            buff.Put((byte)(Bigint0To7 + x));
        }
        else
        {
            buff.Put(Bigint).PutVarLong(x);
        }
    }

    private static void WriteString(WriteBuffer buff, string s)
    {
        int len = s.Length;
        buff.PutVarInt(len).PutStringData(s, len);
    }

    private static void WriteTimestampTime(WriteBuffer buff, long nanos)
    {
        long millis = nanos / 1_000_000L;
        buff.PutVarLong(millis).PutVarInt((int)(nanos - millis * 1_000_000L));
    }

    /// <inheritdoc/>
    public override Value Read(Span<byte> buff, ref int position)
    {
        int type = buff[position++] & 255;
        switch (type)
        {
            case Null:
                return ValueNull.Instance;

            case BooleanTrue:
                return ValueBoolean.True;

            case BooleanFalse:
                return ValueBoolean.False;

            case IntNeg:
                return ValueInteger.Get(-DataUtils.ReadVarInt(buff, ref position));

            case Integer:
                return ValueInteger.Get(DataUtils.ReadVarInt(buff, ref position));

            case Enum:
                // The ordinal is on disk but the list of labels is not: an ENUM needs its
                // enumerator set to become a value, and that comes from the column type, which
                // the row reader is not given here. The reference reads it through the row
                // factory for the same reason.
                throw new TODOException("ValueDataType.read: ENUM needs the enumerator list");

            case BigintNeg:
                return ValueBigint.Get(-DataUtils.ReadVarLong(buff, ref position));

            case Bigint:
                return ValueBigint.Get(DataUtils.ReadVarLong(buff, ref position));

            case Tinyint:
                return ValueTinyint.Get((sbyte)buff[position++]);

            case Smallint:
                {
                    short s = BinaryPrimitives.ReadInt16BigEndian(buff.Slice(position, 2));
                    position += 2;
                    return ValueSmallint.Get(s);
                }

            case Numeric0To1:
                return ValueNumeric.Zero;

            case Numeric0To1 + 1:
                return ValueNumeric.One;

            case NumericSmall0:
                return ValueNumeric.Get(BigDecimal.FromLong(DataUtils.ReadVarLong(buff, ref position)));

            case NumericSmall:
                {
                    int scale = DataUtils.ReadVarInt(buff, ref position);
                    return ValueNumeric.Get(new BigDecimal(DataUtils.ReadVarLong(buff, ref position), scale));
                }

            case Numeric:
                {
                    int scale = DataUtils.ReadVarInt(buff, ref position);
                    int len = DataUtils.ReadVarInt(buff, ref position);
                    BigInteger b = Bits.FromBigEndian(buff.Slice(position, len));
                    position += len;
                    return ValueNumeric.Get(new BigDecimal(b, scale));
                }

            case Real:
                return ValueReal.Get(BitConverter.Int32BitsToSingle(
                    Bits.Reverse(DataUtils.ReadVarInt(buff, ref position))));

            case Real0To1:
                return ValueReal.Get(0f);

            case Real0To1 + 1:
                return ValueReal.Get(1f);

            case Double:
                return ValueDouble.Get(BitConverter.Int64BitsToDouble(
                    Bits.Reverse(DataUtils.ReadVarLong(buff, ref position))));

            case Double0To1:
                return ValueDouble.Get(0d);

            case Double0To1 + 1:
                return ValueDouble.Get(1d);

            case Varbinary:
                // One copy, not two: the array out of the buffer is private to this read, so it
                // goes straight into the value instead of through a second clone. When the cache
                // answers with another instance the array is garbage, which is what the clone was
                // before it.
                return ValueVarbinary.GetNoCopy(ReadBytes(buff, ref position));

            case Binary:
                return ValueBinary.GetNoCopy(ReadBytes(buff, ref position));

            case Char:
                return ValueChar.Get(ReadString(buff, ref position));

            case Date:
                return ValueDate.FromDateValue(DataUtils.ReadVarInt(buff, ref position));

            case Time:
                return ValueTime.FromNanos(ReadTimestampTime(buff, ref position));

            case Timestamp:
                {
                    long nanos = ReadTimestampTime(buff, ref position);
                    long seconds = nanos / 1_000_000_000L;
                    return ValueTimestamp.FromDateValueAndNanos(
                        DateTimeUtils.DateValueFromLocalSeconds(seconds), (int)(nanos % 1_000_000_000L));
                }

            case Uuid:
                return ValueUuid.Get(
                    DataUtils.ReadFixedLong(buff, ref position),
                    DataUtils.ReadFixedLong(buff, ref position));

            case Array:
                throw new TODOException("ValueDataType.read: ARRAY");

            case Row:
                throw new TODOException("ValueDataType.read: ROW");

            case Interval:
                throw new TODOException("ValueDataType.read: INTERVAL");

            case Blob:
                {
                    int smallLen = DataUtils.ReadVarInt(buff, ref position);
                    if (smallLen >= 0)
                    {
                        byte[] small = buff.Slice(position, smallLen).ToArray();
                        position += smallLen;
                        return ValueBlob.CreateSmall(small);
                    }

                    throw new TODOException("ValueDataType.read: LOB in the database");
                }

            case Clob:
                {
                    int smallLen = DataUtils.ReadVarInt(buff, ref position);
                    if (smallLen >= 0)
                    {
                        byte[] small = buff.Slice(position, smallLen).ToArray();
                        position += smallLen;
                        long charLength = DataUtils.ReadVarLong(buff, ref position);
                        return ValueClob.CreateSmall(small, charLength);
                    }

                    throw new TODOException("ValueDataType.read: LOB in the database");
                }

            case Geometry:
                throw new TODOException("ValueDataType.read: GEOMETRY");

            case JavaObject:
                throw new TODOException("ValueDataType.read: JAVA_OBJECT");

            default:
                {
                    // The compressed forms carry their value in the tag, and every one of them is a
                    // range rather than a value: INTEGER 0..15 is 32..47, BIGINT 0..7 is 48..55,
                    // VARCHAR under 32 characters is 68..99, VARCHAR_IGNORECASE the same is
                    // 100..131. NUMERIC and the reals are single cases and are handled above.
                    if (type >= Int0To15 && type < Int0To15 + 16)
                    {
                        return ValueInteger.Get(type - Int0To15);
                    }

                    if (type >= Bigint0To7 && type < Bigint0To7 + 8)
                    {
                        return ValueBigint.Get(type - Bigint0To7);
                    }

                    if (type >= Varchar0To31 && type < Varchar0To31 + 32)
                    {
                        return ValueVarchar.Get(ReadStringData(buff, ref position, type - Varchar0To31));
                    }

                    if (type >= Varbinary0To31 && type < Varbinary0To31 + 32)
                    {
                        int len = type - Varbinary0To31;
                        byte[] bytes = buff.Slice(position, len).ToArray();
                        position += len;
                        return ValueVarbinary.GetNoCopy(bytes);
                    }

                    if (type is Varchar or VarcharIgnoreCase)
                    {
                        string s = ReadString(buff, ref position);
                        return type == Varchar ? ValueVarchar.Get(s) : ValueVarcharIgnoreCase.Get(s);
                    }

                    throw new ArgumentException("Unknown value type tag " + type);
                }
        }
    }

    /// <summary>
    /// Reads back what <see cref="WriteLong"/> wrote: the tag is implied by the value, so the
    /// reader has to know which of the three forms it is looking at from the byte pattern.
    /// </summary>
    private static long ReadLong(Span<byte> buff, ref int position)
    {
        int tag = buff[position] & 255;
        if (tag == BigintNeg)
        {
            _ = position++;
            return -DataUtils.ReadVarLong(buff, ref position);
        }

        if (tag >= Bigint0To7 && tag < Bigint0To7 + 8)
        {
            _ = position++;
            return tag - Bigint0To7;
        }

        _ = position++;
        return DataUtils.ReadVarLong(buff, ref position);
    }

    private static string ReadString(Span<byte> buff, ref int position)
    {
        int len = DataUtils.ReadVarInt(buff, ref position);
        return ReadStringData(buff, ref position, len);
    }

    private static string ReadStringData(Span<byte> buff, ref int position, int len)
    {
        // The payload is one byte per character, written by PutStringData: a character that does
        // not fit was replaced on the way in, so the bytes read back are exactly what was
        // stored and nothing is re-encoded here. One widening pass instead of a concatenation
        // per character, which spent O(n²) copies on every string in a page leaf.
        string result = Encoding.Latin1.GetString(buff.Slice(position, len));
        position += len;
        return result;
    }

    /// <summary>
    /// Reads a byte string out of the buffer. The array is freshly allocated and private to the
    /// caller, so a value built with <c>GetNoCopy</c> takes it as is.
    /// </summary>
    /// <remarks>
    /// Left uninitialized on purpose: the copy below fills every byte, so zeroing first
    /// would be a memset for nothing on every BINARY read.
    /// </remarks>
    private static byte[] ReadBytes(Span<byte> buff, ref int position)
    {
        int len = DataUtils.ReadVarInt(buff, ref position);
        byte[] bytes = GC.AllocateUninitializedArray<byte>(len);
        buff.Slice(position, len).CopyTo(bytes);
        position += len;
        return bytes;
    }

    private static long ReadTimestampTime(Span<byte> buff, ref int position)
    {
        long millis = DataUtils.ReadVarLong(buff, ref position);
        int nanos = DataUtils.ReadVarInt(buff, ref position);
        return millis * 1_000_000L + nanos;
    }

    /// <inheritdoc/>
    public override int GetMemory(Value obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return obj.GetMemory();
    }
}
