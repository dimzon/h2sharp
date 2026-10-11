/*
 * Ported from org.h2.value.ValueUuid.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// The UUID value, which is a pair of 64-bit halves and nothing else.
/// </summary>
/// <remarks>
/// The text form is the one everybody writes, with the dashes, and it is read without them, with
/// any blanks in it and with either case: the reader counts sixteen hexadecimal digits and takes
/// the first sixteen as the high half. Everything else, including a UUID with the wrong number of
/// bytes, is a data conversion error rather than a null.
/// </remarks>
public sealed class ValueUuid : Value
{
    /// <summary>
    /// The precision of this value in number of bytes.
    /// </summary>
    private const int Precision = 16;

    /// <summary>
    /// How wide the text form is, dashes included.
    /// </summary>
    /// <remarks>
    /// For example cd38d882-7ada-4589-b5fb-7da0ca559d9a
    /// </remarks>
    private const int DisplaySize = 36;

    private readonly long high;
    private readonly long low;

    private ValueUuid(long high, long low)
    {
        this.high = high;
        this.low = low;
    }

    /// <inheritdoc/>
    public override int GetValueType() => Value.Uuid;

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeUuid;

    /// <summary>
    /// A new UUID from the pseudo random number generator.
    /// </summary>
    /// <remarks>
    /// Version 4 is two random halves. Version 7 is the time in milliseconds in the top half and a
    /// counter in what is left of it, so that the values come out in order and still are random.
    /// The value depends on the clock and on the random generator of the runtime, so nothing about
    /// it is compared with the reference; what is compared is its shape, which the version and the
    /// variant are a part of.
    /// </remarks>
    /// <param name="version">the version, which is 4 or 7</param>
    /// <returns>the new value</returns>
    public static ValueUuid GetNewRandom(int version)
    {
        long high;
        long low;
        switch (version)
        {
            case 4:
                high = MathUtils.SecureRandomLong();
                low = MathUtils.SecureRandomLong();
                break;

            case 7:
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    long nanos = (now.Ticks - TicksAtUnixEpoch) * 100L;
                    int sub = (int)(nanos % 1_000_000L) * 2_000 / 488_281;
                    high = (((now.ToUnixTimeSeconds() * 1_000L) + (nanos / 1_000_000L)) << 16) | (long)sub;
                    low = MathUtils.SecureRandomLong();
                    break;
                }

            default:
                throw DbException.GetInvalidValueException("RANDOM_UUID version", version);
        }

        return new ValueUuid(
            (high & ~0xF000L) | ((long)version << 12),

            // The variant 0b10, which is the sign bit of the low half read as an unsigned number.
            (low & 0x3FFF_FFFF_FFFF_FFFFL) | unchecked((long)0x8000_0000_0000_0000UL));
    }

    /// <summary>
    /// The ticks of the epoch, which is how a time is turned into the 100-nanosecond units that the
    /// version 7 layout is written in.
    /// </summary>
    private const long TicksAtUnixEpoch = 621_355_968_000_000_000L;

    /// <summary>
    /// A value for the sixteen bytes.
    /// </summary>
    /// <param name="binary">the bytes</param>
    /// <returns>the value</returns>
    public static ValueUuid Get(byte[] binary)
    {
        int length = binary.Length;
        if (length != 16)
        {
            throw DbException.Get(
                ErrorCode.DataConversionError1,
                "UUID requires 16 bytes, got " + length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return Get(Bits.LongFromBigEndian(binary, 0), Bits.LongFromBigEndian(binary, 8));
    }

    /// <summary>
    /// A value for the two halves.
    /// </summary>
    /// <param name="high">the most significant bits</param>
    /// <param name="low">the least significant bits</param>
    /// <returns>the value</returns>
    public static ValueUuid Get(long high, long low) => (ValueUuid)Cache(new ValueUuid(high, low));

    /// <summary>
    /// A value for the text.
    /// </summary>
    /// <remarks>
    /// The dashes are optional and any character up to a space is skipped, so the reader counts
    /// hexadecimal digits rather than positions: sixteen of them make a UUID, and the first sixteen
    /// are the high half.
    /// </remarks>
    /// <param name="s">the text</param>
    /// <returns>the value</returns>
    public static ValueUuid Get(string s)
    {
        long low = 0;
        long high = 0;
        int j = 0;
        for (int i = 0, length = s.Length; i < length; i++)
        {
            char c = s[i];
            if (c is >= '0' and <= '9')
            {
                low = (low << 4) | (long)(c - '0');
            }
            else if (c is >= 'a' and <= 'f')
            {
                low = (low << 4) | (long)(c - ('a' - 0xA));
            }
            else if (c == '-')
            {
                continue;
            }
            else if (c is >= 'A' and <= 'F')
            {
                low = (low << 4) | (long)(c - ('A' - 0xA));
            }
            else if (c <= ' ')
            {
                continue;
            }
            else
            {
                throw DbException.Get(ErrorCode.DataConversionError1, s);
            }

            if (++j == 16)
            {
                high = low;
                low = 0;
            }
        }

        return j == 32 ? Get(high, low) : throw DbException.Get(ErrorCode.DataConversionError1, s);
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => AddString(builder.Append("UUID '")).Append('\'');

    /// <inheritdoc/>
    public override int GetMemory() => 32;

    /// <inheritdoc/>
    public override string GetString() => AddString(new StringBuilder(DisplaySize)).ToString();

    /// <inheritdoc/>
    public override byte[] GetBytes() => Bits.UuidToBytes(high, low);

    private StringBuilder AddString(StringBuilder builder)
    {
        _ = Hex.AppendHex(builder, high >> 32, 4).Append('-');
        _ = Hex.AppendHex(builder, high >> 16, 2).Append('-');
        _ = Hex.AppendHex(builder, high, 2).Append('-');
        _ = Hex.AppendHex(builder, low >> 48, 2).Append('-');
        return Hex.AppendHex(builder, low, 6);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The halves are compared as unsigned, so a UUID whose high half has its top bit set sorts
    /// after one that does not, which signed comparison would get backwards.
    /// </remarks>
    public sealed override int CompareTypeSafe(Value o, CompareMode mode, CastDataProvider? provider)
    {
        if (ReferenceEquals(o, this))
        {
            return 0;
        }

        ValueUuid v = (ValueUuid)o;
        int cmp = UnsignedCompare(high, v.high);
        return cmp != 0 ? cmp : UnsignedCompare(low, v.low);
    }

    private static int UnsignedCompare(long a, long b)
        => (ulong)a < (ulong)b ? -1 : (ulong)a > (ulong)b ? 1 : 0;

    /// <inheritdoc/>
    public sealed override int GetHashCode() => (int)((high >> 32) ^ high ^ (low >> 32) ^ low);

    /// <inheritdoc/>
    /// <summary>
    /// Get or create a UUID value for the given UUID.
    /// </summary>
    /// <remarks>
    /// The reference's <c>ValueUuid.get(UUID)</c>, which takes the platform's UUID and takes it
    /// apart the same way <see cref="GetUuid"/> puts it back together. It is not a convenience
    /// wrapper: the UUID class keeps the number as two longs in *Java's* byte order, so a Guid
    /// that went in through <see cref="Get(byte[])"/> and came out through here would be a
    /// different UUID with the same text.
    /// </remarks>
    /// <param name="uuid">the UUID</param>
    /// <returns>the value</returns>
    public static ValueUuid Get(Guid uuid)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!uuid.TryWriteBytes(bytes, bigEndian: true, out _))
        {
            throw new ArgumentException("The UUID cannot be written", nameof(uuid));
        }

        return Get(bytes.ToArray());
    }

    /// <inheritdoc/>
    public override bool Equals(object? other)
        => other is ValueUuid v && high == v.high && low == v.low;

    /// <summary>
    /// The UUID as the platform spells it.
    /// </summary>
    /// <remarks>
    /// The platform keeps its UUID in a byte order and in a layout of its own, so the sixteen bytes
    /// have to be taken apart into the fields of the platform's constructor. Going through
    /// <see cref="GetBytes"/> would be shorter and would allocate the array twice over, and this is
    /// the path a driver takes whenever it hands the value to the outside world.
    /// </remarks>
    /// <returns>the UUID</returns>
    public Guid GetUuid()
        => new(
            (int)(high >> 32),
            (short)(high >> 16),
            (short)high,
            (byte)(low >> 56),
            (byte)(low >> 48),
            (byte)(low >> 40),
            (byte)(low >> 32),
            (byte)(low >> 24),
            (byte)(low >> 16),
            (byte)(low >> 8),
            (byte)low);

    /// <summary>
    /// The most significant half.
    /// </summary>
    /// <returns>the high half</returns>
    public long GetHigh() => high;

    /// <summary>
    /// The least significant half.
    /// </summary>
    /// <returns>the low half</returns>
    public long GetLow() => low;

    /// <inheritdoc/>
    public override long CharLength() => DisplaySize;

    /// <inheritdoc/>
    public override long OctetLength() => Precision;
}
