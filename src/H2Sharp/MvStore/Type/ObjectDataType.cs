using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using H2Sharp.Util;

namespace H2Sharp.MvStore.Type;

/// <summary>
/// Any value the store can hold, found by what it is rather than declared.
/// </summary>
/// <remarks>
/// Every value is written with a tag that says what follows, and the tag decides how many bytes and
/// in what shape. Small numbers, zero and one of everything, and short strings and byte strings get
/// their own single byte, because the metadata maps that go through here are full of them and every
/// byte saved is a byte that does not have to be checksummed and copied.
/// <para>
/// The .NET types stand in for the Java ones one for one: <c>sbyte</c> for <c>byte</c> (Java's byte
/// is signed), <c>char</c>, <c>short</c>, <c>int</c>, <c>long</c>, <c>float</c>, <c>double</c>,
/// <c>bool</c>, <c>BigInteger</c>, this port's <c>BigDecimal</c>, <c>string</c>, <c>Guid</c> for
/// <c>UUID</c>, <c>DateTime</c> for <c>java.util.Date</c> (kept as milliseconds since the epoch, which
/// is all a Date holds). What does not map is the Java-serialization fallback: there is no
/// equivalent here, and a value outside the table is refused with the same shape of error the
/// reference raises when it cannot serialize, rather than written in a format nothing else reads.
/// </para>
/// <para>
/// The inner types are not generic, although the reference declares them with a type argument. Java
/// erases the argument at runtime, so every one of them is an auto-detect type holding
/// <c>Object</c> by the time anything runs; declaring them per element type in C# would make them
/// unrelated types that cannot share the dispatch table, which is the opposite of what the reference
/// does. Casting inside, after the tag has already said what the value is, is the same behaviour.
/// </para>
/// </remarks>
public class ObjectDataType : BasicDataType<object?>
{
    internal const int TypeNull = 0;
    internal const int TypeBoolean = 1;
    internal const int TypeByte = 2;
    internal const int TypeShort = 3;
    internal const int TypeInt = 4;
    internal const int TypeLong = 5;
    internal const int TypeBigInteger = 6;
    internal const int TypeFloat = 7;
    internal const int TypeDouble = 8;
    internal const int TypeBigDecimal = 9;
    internal const int TypeChar = 10;
    internal const int TypeString = 11;
    internal const int TypeUuid = 12;
    internal const int TypeDate = 13;
    internal const int TypeArray = 14;
    internal const int TypeSerializedObject = 19;

    internal const int TagBooleanTrue = 32;
    internal const int TagIntegerNegative = 33;
    internal const int TagIntegerFixed = 34;
    internal const int TagLongNegative = 35;
    internal const int TagLongFixed = 36;
    internal const int TagBigInteger0 = 37;
    internal const int TagBigInteger1 = 38;
    internal const int TagBigIntegerSmall = 39;
    internal const int TagFloat0 = 40;
    internal const int TagFloat1 = 41;
    internal const int TagFloatFixed = 42;
    internal const int TagDouble0 = 43;
    internal const int TagDouble1 = 44;
    internal const int TagDoubleFixed = 45;
    internal const int TagBigDecimal0 = 46;
    internal const int TagBigDecimal1 = 47;
    internal const int TagBigDecimalSmall = 48;
    internal const int TagBigDecimalSmallScaled = 49;

    internal const int TagInteger015 = 64;
    internal const int TagLong07 = 80;
    internal const int TagString015 = 88;
    internal const int TagByteArray015 = 104;

    private static readonly int FloatZeroBits = BitConverter.SingleToInt32Bits(0.0f);
    private static readonly int FloatOneBits = BitConverter.SingleToInt32Bits(1.0f);
    private static readonly long DoubleZeroBits = BitConverter.DoubleToInt64Bits(0.0d);
    private static readonly long DoubleOneBits = BitConverter.DoubleToInt64Bits(1.0d);

    private static readonly System.Type[] CommonClasses =
    [
        typeof(bool), typeof(sbyte), typeof(short), typeof(char), typeof(int), typeof(long),
        typeof(float), typeof(double), typeof(object), typeof(bool), typeof(sbyte),
        typeof(short), typeof(char), typeof(int), typeof(long), typeof(BigInteger),
        typeof(float), typeof(double), typeof(Util.BigDecimal), typeof(string), typeof(Guid),
        typeof(DateTime),
    ];

    /// <summary>
    /// The class id of a common type, or null when it is not one.
    /// </summary>
    /// <remarks>
    /// The reference lists the primitive and the boxed class separately, because in Java
    /// <c>boolean.class</c> and <c>Boolean.class</c> are two different objects. Here they are one
    /// type, so the table keeps the first id for it, which is the primitive one: the table is read
    /// for array component types, and those are the primitive ones.
    /// </remarks>
    private static readonly Dictionary<System.Type, int> CommonClassesMap = BuildCommonClassesMap();

    private static Dictionary<System.Type, int> BuildCommonClassesMap()
    {
        Dictionary<System.Type, int> map = new();
        for (int i = 0; i < CommonClasses.Length; i++)
        {
            map.TryAdd(CommonClasses[i], i);
        }

        return map;
    }

    /// <summary>
    /// The class id of a common type, or null when it is not one.
    /// </summary>
    internal static int? GetCommonClassId(System.Type clazz)
        => CommonClassesMap.TryGetValue(clazz, out int id) ? id : null;

    private AutoDetectDataType last = SelectDataType(TypeNull);

    /// <inheritdoc/>
    public override object?[] CreateStorage(int size) => new object?[size];

    /// <inheritdoc/>
    public override int Compare(object? a, object? b)
    {
        int typeId = GetTypeId(a);
        int typeDiff = typeId - GetTypeId(b);
        if (typeDiff == 0)
        {
            return NewType(typeId).Compare(a, b);
        }

        return Math.Sign(typeDiff);
    }

    /// <inheritdoc/>
    public override int GetMemory(object? obj) => SwitchType(obj).GetMemory(obj);

    /// <inheritdoc/>
    public override void Write(WriteBuffer buff, object? obj) => SwitchType(obj).Write(buff, obj);

    private AutoDetectDataType NewType(int typeId)
    {
        if (typeId == last.TypeId)
        {
            return last;
        }

        return SelectDataType(typeId);
    }

    private static AutoDetectDataType SelectDataType(int typeId) => typeId switch
    {
        TypeNull => NullType.Instance,
        TypeBoolean => BooleanType.Instance,
        TypeByte => ByteType.Instance,
        TypeShort => ShortType.Instance,
        TypeChar => CharacterType.Instance,
        TypeInt => IntegerType.Instance,
        TypeLong => LongType.Instance,
        TypeFloat => FloatType.Instance,
        TypeDouble => DoubleType.Instance,
        TypeBigInteger => BigIntegerType.Instance,
        TypeBigDecimal => BigDecimalType.Instance,
        TypeString => StringType.Instance,
        TypeUuid => UuidType.Instance,
        TypeDate => DateType.Instance,
        TypeArray => new ObjectArrayType(),
        TypeSerializedObject => new SerializedObjectType(new ObjectDataType()),
        _ => throw DataUtils.NewMvStoreException(
            DataUtils.ErrorInternal, "Unsupported type {0}", typeId),
    };

    /// <inheritdoc/>
    public override object? Read(Span<byte> buff, ref int position)
    {
        int tag = buff[position++];
        int typeId;
        if (tag <= TypeSerializedObject)
        {
            typeId = tag;
        }
        else
        {
            switch (tag)
            {
                case TagBooleanTrue:
                    typeId = TypeBoolean;
                    break;
                case TagIntegerNegative:
                case TagIntegerFixed:
                    typeId = TypeInt;
                    break;
                case TagLongNegative:
                case TagLongFixed:
                    typeId = TypeLong;
                    break;
                case TagBigInteger0:
                case TagBigInteger1:
                case TagBigIntegerSmall:
                    typeId = TypeBigInteger;
                    break;
                case TagFloat0:
                case TagFloat1:
                case TagFloatFixed:
                    typeId = TypeFloat;
                    break;
                case TagDouble0:
                case TagDouble1:
                case TagDoubleFixed:
                    typeId = TypeDouble;
                    break;
                case TagBigDecimal0:
                case TagBigDecimal1:
                case TagBigDecimalSmall:
                case TagBigDecimalSmallScaled:
                    typeId = TypeBigDecimal;
                    break;
                default:
                    if (tag >= TagInteger015 && tag <= TagInteger015 + 15)
                    {
                        typeId = TypeInt;
                    }
                    else if (tag >= TagString015 && tag <= TagString015 + 15)
                    {
                        typeId = TypeString;
                    }
                    else if (tag >= TagLong07 && tag <= TagLong07 + 7)
                    {
                        typeId = TypeLong;
                    }
                    else if (tag >= TagByteArray015 && tag <= TagByteArray015 + 15)
                    {
                        typeId = TypeArray;
                    }
                    else
                    {
                        throw DataUtils.NewMvStoreException(
                            DataUtils.ErrorFileCorrupt, "Unknown tag {0}", tag);
                    }

                    break;
            }
        }

        AutoDetectDataType t = last;
        if (typeId != t.TypeId)
        {
            last = t = NewType(typeId);
        }

        return t.ReadTagged(buff, ref position, tag);
    }

    private static int GetTypeId(object? obj) => obj switch
    {
        int => TypeInt,
        string => TypeString,
        long => TypeLong,
        double => TypeDouble,
        float => TypeFloat,
        bool => TypeBoolean,
        Guid => TypeUuid,
        sbyte => TypeByte,
        short => TypeShort,
        char => TypeChar,
        null => TypeNull,
        DateTime => TypeDate,
        BigInteger => TypeBigInteger,
        Util.BigDecimal => TypeBigDecimal,
        Array => TypeArray,
        _ => TypeSerializedObject,
    };

    private AutoDetectDataType SwitchType(object? obj)
    {
        int typeId = GetTypeId(obj);
        AutoDetectDataType current = last;
        if (typeId != current.TypeId)
        {
            last = current = NewType(typeId);
        }

        return current;
    }

    private static bool IsDate(object? obj) => obj is DateTime;

    private static bool IsArray(object? obj) => obj is Array;

    /// <summary>
    /// Serialize an object the Java way, which has no equivalent here and is refused.
    /// </summary>
    public static byte[] Serialize(object? obj) => throw DataUtils.NewIllegalArgumentException("Could not serialize {0}", obj);

    /// <summary>
    /// Deserialize bytes the Java way, which has no equivalent here and is refused.
    /// </summary>
    public static object? Deserialize(byte[] data) => throw DataUtils.NewIllegalArgumentException(
        "Could not deserialize {0}", Hex.BytesToHex(data));

    /// <summary>
    /// Compare two byte strings as unsigned, then by length.
    /// </summary>
    public static int CompareNotNull(byte[] data1, byte[] data2)
    {
        if (ReferenceEquals(data1, data2))
        {
            return 0;
        }

        int len = Math.Min(data1.Length, data2.Length);
        for (int i = 0; i < len; i++)
        {
            if (data1[i] != data2[i])
            {
                return data1[i] > data2[i] ? 1 : -1;
            }
        }

        return Math.Sign(data1.Length - data2.Length);
    }

    /// <summary>
    /// A data type that finds its real type from the value.
    /// </summary>
    internal abstract class AutoDetectDataType : BasicDataType<object?>
    {
        private readonly ObjectDataType? owner;

        internal readonly int TypeId;

        internal AutoDetectDataType(int typeId) => TypeId = typeId;

        internal AutoDetectDataType(ObjectDataType owner, int typeId)
        {
            this.owner = owner;
            TypeId = typeId;
        }

        /// <inheritdoc/>
        public override object?[] CreateStorage(int size) => new object?[size];

        /// <summary>
        /// The real type for a value, through the owner that remembers the last one.
        /// </summary>
        internal DataType<object?> DetectType(object? o) => owner is null
            ? throw new InvalidOperationException("No owner to detect the type")
            : owner.SwitchType(o);

        /// <inheritdoc/>
        public override int GetMemory(object? o) => DetectType(o).GetMemory(o);

        /// <inheritdoc/>
        public override void Write(WriteBuffer buff, object? o) => DetectType(o).Write(buff, o);

        /// <summary>
        /// Read a value whose tag was already consumed.
        /// </summary>
        internal abstract object? ReadTagged(Span<byte> buff, ref int position, int tag);
    }

    private sealed class NullType : AutoDetectDataType
    {
        internal static readonly NullType Instance = new();

        private NullType()
            : base(TypeNull)
        {
        }

        public override int Compare(object? a, object? b) => 0;

        public override int GetMemory(object? obj) => 0;

        public override void Write(WriteBuffer buff, object? obj) => _ = buff.Put((byte)TypeNull);

        public override object? Read(Span<byte> buff, ref int position) => null;

        internal override object? ReadTagged(Span<byte> buff, ref int position, int tag) => null;
    }

    private sealed class BooleanType : AutoDetectDataType
    {
        internal static readonly BooleanType Instance = new();

        private BooleanType()
            : base(TypeBoolean)
        {
        }

        public override int Compare(object? a, object? b) => ((bool)a!).CompareTo((bool)b!);

        public override int GetMemory(object? obj) => 0;

        public override void Write(WriteBuffer buff, object? obj)
            => _ = buff.Put((byte)((bool)obj! ? TagBooleanTrue : TypeBoolean));

        public override object? Read(Span<byte> buff, ref int position)
            => ReadTagged(buff, ref position, buff[position++]);

        internal override object? ReadTagged(Span<byte> buff, ref int position, int tag)
            => tag == TypeBoolean ? (object)false : true;
    }

    private sealed class ByteType : AutoDetectDataType
    {
        internal static readonly ByteType Instance = new();

        private ByteType()
            : base(TypeByte)
        {
        }

        public override int Compare(object? a, object? b) => ((sbyte)a!).CompareTo((sbyte)b!);

        public override int GetMemory(object? obj) => 1;

        public override void Write(WriteBuffer buff, object? obj)
        {
            _ = buff.Put((byte)TypeByte);
            _ = buff.Put((byte)(sbyte)obj!);
        }

        public override object? Read(Span<byte> buff, ref int position)
            => ReadTagged(buff, ref position, buff[position++]);

        internal override object? ReadTagged(Span<byte> buff, ref int position, int tag)
            => (sbyte)buff[position++];
    }

    private sealed class ShortType : AutoDetectDataType
    {
        internal static readonly ShortType Instance = new();

        private ShortType()
            : base(TypeShort)
        {
        }

        public override int Compare(object? a, object? b) => ((short)a!).CompareTo((short)b!);

        public override int GetMemory(object? obj) => 24;

        public override void Write(WriteBuffer buff, object? obj)
        {
            _ = buff.Put((byte)TypeShort);
            _ = buff.PutShort((short)obj!);
        }

        public override object? Read(Span<byte> buff, ref int position)
            => ReadTagged(buff, ref position, buff[position++]);

        internal override object? ReadTagged(Span<byte> buff, ref int position, int tag)
        {
            short value = (short)((buff[position] << 8) | buff[position + 1]);
            position += 2;
            return value;
        }
    }

    private sealed class CharacterType : AutoDetectDataType
    {
        internal static readonly CharacterType Instance = new();

        private CharacterType()
            : base(TypeChar)
        {
        }

        public override int Compare(object? a, object? b) => ((char)a!).CompareTo((char)b!);

        public override int GetMemory(object? obj) => 24;

        public override void Write(WriteBuffer buff, object? obj)
        {
            _ = buff.Put((byte)TypeChar);
            _ = buff.PutChar((char)obj!);
        }

        public override object? Read(Span<byte> buff, ref int position)
        {
            char value = (char)((buff[position] << 8) | buff[position + 1]);
            position += 2;
            return value;
        }

        internal override object? ReadTagged(Span<byte> buff, ref int position, int tag)
            => Read(buff, ref position);
    }

    private sealed class IntegerType : AutoDetectDataType
    {
        internal static readonly IntegerType Instance = new();

        private IntegerType()
            : base(TypeInt)
        {
        }

        public override int Compare(object? a, object? b) => ((int)a!).CompareTo((int)b!);

        public override int GetMemory(object? obj) => 24;

        public override void Write(WriteBuffer buff, object? obj)
        {
            int x = (int)obj!;
            if (x < 0)
            {
                // -int.MinValue is smaller than 0
                if (-x < 0 || -x > DataUtils.CompressedVarIntMax)
                {
                    _ = buff.Put((byte)TagIntegerFixed).PutInt(x);
                }
                else
                {
                    _ = buff.Put((byte)TagIntegerNegative).PutVarInt(-x);
                }
            }
            else if (x <= 15)
            {
                _ = buff.Put((byte)(TagInteger015 + x));
            }
            else if (x <= DataUtils.CompressedVarIntMax)
            {
                _ = buff.Put((byte)TypeInt).PutVarInt(x);
            }
            else
            {
                _ = buff.Put((byte)TagIntegerFixed).PutInt(x);
            }
        }

        public override object? Read(Span<byte> buff, ref int position)
            => ReadTagged(buff, ref position, buff[position++]);

        internal override object? ReadTagged(Span<byte> buff, ref int position, int tag) => tag switch
        {
            TypeInt => DataUtils.ReadVarInt(buff, ref position),
            TagIntegerNegative => -DataUtils.ReadVarInt(buff, ref position),
            TagIntegerFixed => BinaryPrimitives.ReadInt32BigEndian(ReadBytes(buff, ref position, 4)),
            _ => tag - TagInteger015,
        };
    }

    private sealed class LongType : AutoDetectDataType
    {
        internal static readonly LongType Instance = new();

        private LongType()
            : base(TypeLong)
        {
        }

        public override int Compare(object? a, object? b) => ((long)a!).CompareTo((long)b!);

        public override int GetMemory(object? obj) => 30;

        public override void Write(WriteBuffer buff, object? obj)
        {
            long x = (long)obj!;
            if (x < 0)
            {
                // -long.MinValue is smaller than 0
                if (-x < 0 || -x > DataUtils.CompressedVarLongMax)
                {
                    _ = buff.Put((byte)TagLongFixed).PutLong(x);
                }
                else
                {
                    _ = buff.Put((byte)TagLongNegative).PutVarLong(-x);
                }
            }
            else if (x <= 7)
            {
                _ = buff.Put((byte)(TagLong07 + x));
            }
            else if (x <= DataUtils.CompressedVarLongMax)
            {
                _ = buff.Put((byte)TypeLong).PutVarLong(x);
            }
            else
            {
                _ = buff.Put((byte)TagLongFixed).PutLong(x);
            }
        }

        public override object? Read(Span<byte> buff, ref int position)
            => ReadTagged(buff, ref position, buff[position++]);

        internal override object? ReadTagged(Span<byte> buff, ref int position, int tag) => tag switch
        {
            TypeLong => DataUtils.ReadVarLong(buff, ref position),
            TagLongNegative => -DataUtils.ReadVarLong(buff, ref position),
            TagLongFixed => BinaryPrimitives.ReadInt64BigEndian(ReadBytes(buff, ref position, 8)),
            _ => (long)(tag - TagLong07),
        };
    }

    private sealed class FloatType : AutoDetectDataType
    {
        internal static readonly FloatType Instance = new();

        private FloatType()
            : base(TypeFloat)
        {
        }

        public override int Compare(object? a, object? b)
            => CompareFloat((float)a!, (float)b!);

        public override int GetMemory(object? obj) => 24;

        public override void Write(WriteBuffer buff, object? obj)
        {
            float x = (float)obj!;
            int f = BitConverter.SingleToInt32Bits(x);
            if (f == FloatZeroBits)
            {
                _ = buff.Put((byte)TagFloat0);
            }
            else if (f == FloatOneBits)
            {
                _ = buff.Put((byte)TagFloat1);
            }
            else
            {
                int value = ReverseBits(f);
                if (value >= 0 && value <= DataUtils.CompressedVarIntMax)
                {
                    _ = buff.Put((byte)TypeFloat).PutVarInt(value);
                }
                else
                {
                    _ = buff.Put((byte)TagFloatFixed).PutFloat(x);
                }
            }
        }

        public override object? Read(Span<byte> buff, ref int position)
            => ReadTagged(buff, ref position, buff[position++]);

        internal override object? ReadTagged(Span<byte> buff, ref int position, int tag) => tag switch
        {
            TagFloat0 => 0f,
            TagFloat1 => 1f,
            TagFloatFixed => BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32BigEndian(ReadBytes(buff, ref position, 4))),
            _ => BitConverter.Int32BitsToSingle(
                ReverseBits(DataUtils.ReadVarInt(buff, ref position))),
        };
    }

    private sealed class DoubleType : AutoDetectDataType
    {
        internal static readonly DoubleType Instance = new();

        private DoubleType()
            : base(TypeDouble)
        {
        }

        public override int Compare(object? a, object? b)
            => CompareDouble((double)a!, (double)b!);

        public override int GetMemory(object? obj) => 30;

        public override void Write(WriteBuffer buff, object? obj)
        {
            double x = (double)obj!;
            long d = BitConverter.DoubleToInt64Bits(x);
            if (d == DoubleZeroBits)
            {
                _ = buff.Put((byte)TagDouble0);
            }
            else if (d == DoubleOneBits)
            {
                _ = buff.Put((byte)TagDouble1);
            }
            else
            {
                long value = ReverseBits(d);
                if (value >= 0 && value <= DataUtils.CompressedVarLongMax)
                {
                    _ = buff.Put((byte)TypeDouble).PutVarLong(value);
                }
                else
                {
                    _ = buff.Put((byte)TagDoubleFixed).PutDouble(x);
                }
            }
        }

        public override object? Read(Span<byte> buff, ref int position)
            => ReadTagged(buff, ref position, buff[position++]);

        internal override object? ReadTagged(Span<byte> buff, ref int position, int tag) => tag switch
        {
            TagDouble0 => 0d,
            TagDouble1 => 1d,
            TagDoubleFixed => BitConverter.Int64BitsToDouble(
                BinaryPrimitives.ReadInt64BigEndian(ReadBytes(buff, ref position, 8))),
            _ => BitConverter.Int64BitsToDouble(
                ReverseBits(DataUtils.ReadVarLong(buff, ref position))),
        };
    }

    private sealed class BigIntegerType : AutoDetectDataType
    {
        internal static readonly BigIntegerType Instance = new();

        private BigIntegerType()
            : base(TypeBigInteger)
        {
        }

        public override int Compare(object? a, object? b) => ((BigInteger)a!).CompareTo((BigInteger)b!);

        public override int GetMemory(object? obj) => 100;

        public override void Write(WriteBuffer buff, object? obj)
        {
            BigInteger x = (BigInteger)obj!;
            if (x.IsZero)
            {
                _ = buff.Put((byte)TagBigInteger0);
            }
            else if (x.IsOne)
            {
                _ = buff.Put((byte)TagBigInteger1);
            }
            else
            {
                int bits = BitLength(x);
                if (bits <= 63)
                {
                    _ = buff.Put((byte)TagBigIntegerSmall).PutVarLong(Low64(x));
                }
                else
                {
                    byte[] bytes = ToBigEndian(x);
                    _ = buff.Put((byte)TypeBigInteger).PutVarInt(bytes.Length).Put(bytes);
                }
            }
        }

        public override object? Read(Span<byte> buff, ref int position)
            => ReadTagged(buff, ref position, buff[position++]);

        internal override object? ReadTagged(Span<byte> buff, ref int position, int tag) => tag switch
        {
            TagBigInteger0 => BigInteger.Zero,
            TagBigInteger1 => BigInteger.One,
            TagBigIntegerSmall => new BigInteger(DataUtils.ReadVarLong(buff, ref position)),
            _ => FromBigEndian(ReadBytes(buff, ref position, DataUtils.ReadVarInt(buff, ref position))),
        };
    }

    private sealed class BigDecimalType : AutoDetectDataType
    {
        internal static readonly BigDecimalType Instance = new();

        private BigDecimalType()
            : base(TypeBigDecimal)
        {
        }

        public override int Compare(object? a, object? b)
            => ((Util.BigDecimal)a!).CompareTo((Util.BigDecimal)b!);

        public override int GetMemory(object? obj) => 150;

        public override void Write(WriteBuffer buff, object? obj)
        {
            // This port's decimal keeps the same two halves the reference writes: a scale and an
            // unscaled integer. Writing them in the same tags is what makes the bytes match.
            Util.BigDecimal x = (Util.BigDecimal)obj!;
            if (x.UnscaledValue.IsZero)
            {
                _ = buff.Put((byte)TagBigDecimal0);
            }
            else if (x.UnscaledValue.IsOne && x.Scale == 0)
            {
                _ = buff.Put((byte)TagBigDecimal1);
            }
            else
            {
                int scale = x.Scale;
                BigInteger unscaled = x.UnscaledValue;
                int bits = BitLength(unscaled);
                if (bits < 64)
                {
                    if (scale == 0)
                    {
                        _ = buff.Put((byte)TagBigDecimalSmall);
                    }
                    else
                    {
                        _ = buff.Put((byte)TagBigDecimalSmallScaled).PutVarInt(scale);
                    }

                    _ = buff.PutVarLong(Low64(unscaled));
                }
                else
                {
                    byte[] bytes = ToBigEndian(unscaled);
                    _ = buff.Put((byte)TypeBigDecimal).PutVarInt(scale).PutVarInt(bytes.Length).Put(bytes);
                }
            }
        }

        public override object? Read(Span<byte> buff, ref int position)
            => ReadTagged(buff, ref position, buff[position++]);

        internal override object? ReadTagged(Span<byte> buff, ref int position, int tag) => tag switch
        {
            TagBigDecimal0 => Util.BigDecimal.Zero,
            TagBigDecimal1 => Util.BigDecimal.One,
            TagBigDecimalSmall => new Util.BigDecimal(
                new BigInteger(DataUtils.ReadVarLong(buff, ref position)), 0),
            TagBigDecimalSmallScaled => ReadScaled(buff, ref position),
            _ => ReadFull(buff, ref position),
        };

        private static Util.BigDecimal ReadScaled(Span<byte> buff, ref int position)
        {
            int scale = DataUtils.ReadVarInt(buff, ref position);
            return new Util.BigDecimal(new BigInteger(DataUtils.ReadVarLong(buff, ref position)), scale);
        }

        private static Util.BigDecimal ReadFull(Span<byte> buff, ref int position)
        {
            int scale = DataUtils.ReadVarInt(buff, ref position);
            int len = DataUtils.ReadVarInt(buff, ref position);
            return new Util.BigDecimal(FromBigEndian(ReadBytes(buff, ref position, len)), scale);
        }
    }

    private sealed class StringType : AutoDetectDataType
    {
        internal static readonly StringType Instance = new();

        private StringType()
            : base(TypeString)
        {
        }

        public override int Compare(object? a, object? b) => string.CompareOrdinal((string?)a, (string?)b);

        public override int GetMemory(object? obj) => 24 + (2 * ((string)obj!).Length);

        public override void Write(WriteBuffer buff, object? obj)
        {
            string s = (string)obj!;
            int len = s.Length;
            if (len <= 15)
            {
                _ = buff.Put((byte)(TagString015 + len));
            }
            else
            {
                _ = buff.Put((byte)TypeString).PutVarInt(len);
            }

            _ = buff.PutStringData(s, len);
        }

        public override object? Read(Span<byte> buff, ref int position)
            => ReadTagged(buff, ref position, buff[position++]);

        internal override object? ReadTagged(Span<byte> buff, ref int position, int tag)
        {
            int len = tag == TypeString ? DataUtils.ReadVarInt(buff, ref position) : tag - TagString015;
            return DataUtils.ReadString(buff, ref position, len);
        }
    }

    private sealed class UuidType : AutoDetectDataType
    {
        internal static readonly UuidType Instance = new();

        private UuidType()
            : base(TypeUuid)
        {
        }

        public override int Compare(object? a, object? b)
        {
            // Java orders by signed most bits, then signed least bits. Guid.CompareTo orders the
            // trailing bytes unsigned, which disagrees exactly when their signs differ.
            Guid ga = (Guid)a!;
            Guid gb = (Guid)b!;
            int c = Util.Bits.Msb(ga).CompareTo(Util.Bits.Msb(gb));
            return c != 0 ? c : Util.Bits.Lsb(ga).CompareTo(Util.Bits.Lsb(gb));
        }

        public override int GetMemory(object? obj) => 40;

        public override void Write(WriteBuffer buff, object? obj)
        {
            // The reference writes the most and least significant bits big endian, one after the
            // other. A Guid's own byte order is mixed endian, so the value goes out field by field in
            // the same order Java's UUID writes them, rather than as one copy of its bytes.
            _ = buff.Put((byte)TypeUuid);
            byte[] bytes = ((Guid)obj!).ToByteArray();
            byte[] ordered =
            [
                bytes[3], bytes[2], bytes[1], bytes[0],
                bytes[5], bytes[4], bytes[7], bytes[6],
                bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15],
            ];
            _ = buff.Put(ordered);
        }

        public override object? Read(Span<byte> buff, ref int position)
            => ReadTagged(buff, ref position, buff[position++]);

        internal override object? ReadTagged(Span<byte> buff, ref int position, int tag)
        {
            byte[] bytes = ReadBytes(buff, ref position, 16);
            byte[] ordered =
            [
                bytes[3], bytes[2], bytes[1], bytes[0],
                bytes[5], bytes[4], bytes[7], bytes[6],
                bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15],
            ];
            return new Guid(ordered);
        }
    }

    private sealed class DateType : AutoDetectDataType
    {
        internal static readonly DateType Instance = new();

        private DateType()
            : base(TypeDate)
        {
        }

        public override int Compare(object? a, object? b) => ((DateTime)a!).CompareTo((DateTime)b!);

        public override int GetMemory(object? obj) => 40;

        public override void Write(WriteBuffer buff, object? obj)
        {
            _ = buff.Put((byte)TypeDate);
            _ = buff.PutLong((long)(((DateTime)obj!).ToUniversalTime() - DateTime.UnixEpoch).TotalMilliseconds);
        }

        public override object? Read(Span<byte> buff, ref int position)
            => ReadTagged(buff, ref position, buff[position++]);

        internal override object? ReadTagged(Span<byte> buff, ref int position, int tag)
            => DateTime.UnixEpoch.AddMilliseconds(
                BinaryPrimitives.ReadInt64BigEndian(ReadBytes(buff, ref position, 8)));
    }

    private sealed class ObjectArrayType : AutoDetectDataType
    {
        private readonly ObjectDataType elementType = new();

        internal ObjectArrayType()
            : base(TypeArray)
        {
        }

        public override int GetMemory(object? obj)
        {
            if (obj is not Array array)
            {
                return base.GetMemory(obj);
            }

            int size = 64;
            System.Type component = array.GetType().GetElementType()!;
            if (component.IsPrimitive)
            {
                int len = array.Length;
                if (component == typeof(bool) || component == typeof(byte) || component == typeof(sbyte))
                {
                    size += len;
                }
                else if (component == typeof(char) || component == typeof(short) || component == typeof(ushort))
                {
                    size += len * 2;
                }
                else if (component == typeof(int) || component == typeof(uint) || component == typeof(float))
                {
                    size += len * 4;
                }
                else if (component == typeof(double) || component == typeof(long) || component == typeof(ulong))
                {
                    size += len * 8;
                }
            }
            else
            {
                // A value-type array (DateTime, Guid, decimal and the nullable kinds) does not
                // convert to object?[], so the elements go one by one through GetValue, which
                // boxes them. That is what Array.get does on the reference side.
                foreach (object? x in array)
                {
                    if (x is not null)
                    {
                        size += elementType.GetMemory(x);
                    }
                }
            }

            // they are said to be larger, because these objects use quite a lot of disk space
            return size * 2;
        }

        public override int Compare(object? a, object? b)
        {
            if (a is not Array aArray || b is not Array bArray)
            {
                return base.Compare(a, b);
            }

            if (ReferenceEquals(aArray, bArray))
            {
                return 0;
            }

            System.Type component = aArray.GetType().GetElementType()!;
            System.Type other = bArray.GetType().GetElementType()!;
            if (component != other)
            {
                int? idA = GetCommonClassId(component);
                int? idB = GetCommonClassId(other);
                if (idA.HasValue)
                {
                    return idB.HasValue ? idA.Value.CompareTo(idB.Value) : -1;
                }

                if (idB.HasValue)
                {
                    return 1;
                }

                return string.CompareOrdinal(component.FullName, other.FullName);
            }

            int len = Math.Min(aArray.Length, bArray.Length);
            if (component == typeof(byte))
            {
                return CompareNotNull((byte[])aArray, (byte[])bArray);
            }

            if (component.IsPrimitive)
            {
                for (int i = 0; i < len; i++)
                {
                    int x = CompareElement(component, aArray, bArray, i);
                    if (x != 0)
                    {
                        return x;
                    }
                }
            }
            else
            {
                for (int i = 0; i < len; i++)
                {
                    int compared = elementType.Compare(aArray.GetValue(i), bArray.GetValue(i));
                    if (compared != 0)
                    {
                        return compared;
                    }
                }
            }

            return aArray.Length.CompareTo(bArray.Length);
        }

        public override void Write(WriteBuffer buff, object? obj)
        {
            if (obj is not Array array)
            {
                base.Write(buff, obj);
                return;
            }

            System.Type component = array.GetType().GetElementType()!;
            int? classId = GetCommonClassId(component);

            // A byte array is the one unsigned type the store writes. Java's byte is signed and has
            // its own slot; here byte[] takes that slot, because the bytes on disk do not know
            // about signedness and the slot is what decides the tag.
            if (component == typeof(byte))
            {
                classId = GetCommonClassId(typeof(sbyte));
            }
            if (classId.HasValue)
            {
                if (component.IsPrimitive)
                {
                    if (component == typeof(byte))
                    {
                        byte[] data = (byte[])obj;
                        int len = data.Length;
                        if (len <= 15)
                        {
                            _ = buff.Put((byte)(TagByteArray015 + len));
                        }
                        else
                        {
                            _ = buff.Put((byte)TypeArray).Put((byte)classId.Value).PutVarInt(len);
                        }

                        _ = buff.Put(data);
                        return;
                    }

                    int length = array.Length;
                    _ = buff.Put((byte)TypeArray).Put((byte)classId.Value).PutVarInt(length);
                    for (int i = 0; i < length; i++)
                    {
                        if (component == typeof(bool))
                        {
                            _ = buff.Put((byte)(((bool[])obj)[i] ? 1 : 0));
                        }
                        else if (component == typeof(char))
                        {
                            _ = buff.PutChar(((char[])obj)[i]);
                        }
                        else if (component == typeof(short))
                        {
                            _ = buff.PutShort(((short[])obj)[i]);
                        }
                        else if (component == typeof(int))
                        {
                            _ = buff.PutInt(((int[])obj)[i]);
                        }
                        else if (component == typeof(float))
                        {
                            _ = buff.PutFloat(((float[])obj)[i]);
                        }
                        else if (component == typeof(double))
                        {
                            _ = buff.PutDouble(((double[])obj)[i]);
                        }
                        else if (component == typeof(sbyte))
                        {
                            _ = buff.Put((byte)((sbyte[])obj)[i]);
                        }
                        else
                        {
                            _ = buff.PutLong(((long[])obj)[i]);
                        }
                    }

                    return;
                }

                int count = array.Length;
                _ = buff.Put((byte)TypeArray).Put((byte)classId.Value);
            }
            else
            {
                _ = buff.Put((byte)TypeArray).Put(unchecked((byte)-1));
                string name = component.FullName ?? component.Name;
                StringDataType.Instance.Write(buff, name);
            }

            int total = array.Length;
            _ = buff.PutVarInt(total);
            for (int i = 0; i < total; i++)
            {
                elementType.Write(buff, array.GetValue(i));
            }
        }

        public override object? Read(Span<byte> buff, ref int position)
            => ReadTagged(buff, ref position, buff[position++]);

        internal override object? ReadTagged(Span<byte> buff, ref int position, int tag)
        {
            if (tag != TypeArray)
            {
                // A short byte array carries no class id; like every other byte array on disk it is
                // Java's signed bytes, which are sbyte here.
                byte[] raw = ReadBytes(buff, ref position, tag - TagByteArray015);
                sbyte[] signed = new sbyte[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                {
                    signed[i] = (sbyte)raw[i];
                }

                return signed;
            }

            int component = (sbyte)buff[position++];
            System.Type clazz;
            if (component == -1)
            {
                string componentType = StringDataType.Instance.Read(buff, ref position);
                clazz = System.Type.GetType(componentType, throwOnError: true)
                    ?? throw DataUtils.NewMvStoreException(
                        DataUtils.ErrorSerialization, "Could not get class {0}", componentType);
            }
            else
            {
                clazz = CommonClasses[component];
            }

            int len = DataUtils.ReadVarInt(buff, ref position);
            try
            {
                Array result = Array.CreateInstance(clazz, len);
                for (int i = 0; i < len; i++)
                {
                    if (clazz == typeof(bool))
                    {
                        ((bool[])result)[i] = buff[position++] == 1;
                    }
                    else if (clazz == typeof(sbyte))
                    {
                        ((sbyte[])result)[i] = (sbyte)buff[position++];
                    }
                    else if (clazz == typeof(byte))
                    {
                        ((byte[])result)[i] = buff[position++];
                    }
                    else if (clazz == typeof(char))
                    {
                        ((char[])result)[i] = (char)BinaryPrimitives.ReadUInt16BigEndian(
                            ReadBytes(buff, ref position, 2));
                    }
                    else if (clazz == typeof(short))
                    {
                        ((short[])result)[i] = BinaryPrimitives.ReadInt16BigEndian(
                            ReadBytes(buff, ref position, 2));
                    }
                    else if (clazz == typeof(int))
                    {
                        ((int[])result)[i] = BinaryPrimitives.ReadInt32BigEndian(
                            ReadBytes(buff, ref position, 4));
                    }
                    else if (clazz == typeof(float))
                    {
                        ((float[])result)[i] = BitConverter.Int32BitsToSingle(
                            BinaryPrimitives.ReadInt32BigEndian(ReadBytes(buff, ref position, 4)));
                    }
                    else if (clazz == typeof(double))
                    {
                        ((double[])result)[i] = BitConverter.Int64BitsToDouble(
                            BinaryPrimitives.ReadInt64BigEndian(ReadBytes(buff, ref position, 8)));
                    }
                    else if (clazz == typeof(long))
                    {
                        ((long[])result)[i] = BinaryPrimitives.ReadInt64BigEndian(
                            ReadBytes(buff, ref position, 8));
                    }
                    else
                    {
                        result.SetValue(elementType.Read(buff, ref position), i);
                    }
                }

                return result;
            }
            catch (Exception e) when (e is not MvStoreException)
            {
                throw DataUtils.NewMvStoreException(
                    DataUtils.ErrorSerialization,
                    "Could not create array of type {0} length {1}", clazz, len, e);
            }
        }

        private static int CompareElement(System.Type component, Array a, Array b, int i)
        {
            if (component == typeof(bool))
            {
                return Math.Sign((((bool[])a)[i] ? 1 : 0) - (((bool[])b)[i] ? 1 : 0));
            }

            if (component == typeof(char))
            {
                return Math.Sign(((char[])a)[i] - ((char[])b)[i]);
            }

            if (component == typeof(short))
            {
                return Math.Sign(((short[])a)[i] - ((short[])b)[i]);
            }

            if (component == typeof(int))
            {
                return ((int[])a)[i].CompareTo(((int[])b)[i]);
            }

            if (component == typeof(sbyte))
            {
                // Java's byte arrays order unsigned (compareNotNull masks with 255), while its
                // byte scalars order signed: [1] sorts before [-1] here, as byte[] does there.
                int av = ((sbyte[])a)[i] & 0xFF;
                int bv = ((sbyte[])b)[i] & 0xFF;
                return av == bv ? 0 : av > bv ? 1 : -1;
            }

            if (component == typeof(float))
            {
                return CompareFloat(((float[])a)[i], ((float[])b)[i]);
            }

            if (component == typeof(double))
            {
                return CompareDouble(((double[])a)[i], ((double[])b)[i]);
            }

            return ((long[])a)[i].CompareTo(((long[])b)[i]);
        }
    }

    private sealed class SerializedObjectType : AutoDetectDataType
    {
        private int averageSize = 10_000;

        private readonly ObjectDataType owner;

        internal SerializedObjectType(ObjectDataType owner)
            : base(owner, TypeSerializedObject) => this.owner = owner;

        public override int Compare(object? a, object? b)
        {
            if (ReferenceEquals(a, b))
            {
                return 0;
            }

            DataType<object?> typeA = owner.SwitchType(a);
            DataType<object?> typeB = owner.SwitchType(b);
            if (typeA != this || typeB != this)
            {
                return typeA == typeB ? typeA.Compare(a, b) : base.Compare(a, b);
            }

            if (a is IComparable<object?> comparable
                && (b is null || a.GetType().IsAssignableFrom(b.GetType())))
            {
                return comparable.CompareTo(b);
            }

            if (b is IComparable<object?> other
                && (a is null || b.GetType().IsAssignableFrom(a.GetType())))
            {
                return -other.CompareTo(a);
            }

            byte[] first = Serialize(a);
            byte[] second = Serialize(b);
            return CompareNotNull(first, second);
        }

        public override int GetMemory(object? obj)
        {
            DataType<object?> t = owner.SwitchType(obj);
            return t == this ? averageSize : t.GetMemory(obj);
        }

        public override void Write(WriteBuffer buff, object? obj)
        {
            DataType<object?> t = owner.SwitchType(obj);
            if (t != this)
            {
                t.Write(buff, obj);
                return;
            }

            byte[] data = Serialize(obj);

            // they are said to be larger, because these objects use quite a lot of disk space
            int size = data.Length * 2;

            // adjust the average size using an exponential moving average
            averageSize = (int)((size + (15L * averageSize)) / 16);
            _ = buff.Put((byte)TypeSerializedObject).PutVarInt(data.Length).Put(data);
        }

        public override object? Read(Span<byte> buff, ref int position)
            => ReadTagged(buff, ref position, buff[position++]);

        internal override object? ReadTagged(Span<byte> buff, ref int position, int tag)
        {
            int len = DataUtils.ReadVarInt(buff, ref position);
            byte[] data = ReadBytes(buff, ref position, len);
            int size = data.Length * 2;

            // adjust the average size using an exponential moving average
            averageSize = (int)((size + (15L * averageSize)) / 16);
            return Deserialize(data);
        }
    }

    private static byte[] ReadBytes(Span<byte> buff, ref int position, int count)
    {
        byte[] result = buff.Slice(position, count).ToArray();
        position += count;
        return result;
    }

    private static int ReverseBits(int value)
    {
        uint v = (uint)value;
        v = ((v & 0x55555555) << 1) | ((v >> 1) & 0x55555555);
        v = ((v & 0x33333333) << 2) | ((v >> 2) & 0x33333333);
        v = ((v & 0x0F0F0F0F) << 4) | ((v >> 4) & 0x0F0F0F0F);
        v = (v << 24) | ((v & 0xFF00) << 8) | ((v >> 8) & 0xFF00) | (v >> 24);
        return (int)v;
    }

    private static long ReverseBits(long value)
    {
        ulong v = (ulong)value;
        v = ((v & 0x5555555555555555UL) << 1) | ((v >> 1) & 0x5555555555555555UL);
        v = ((v & 0x3333333333333333UL) << 2) | ((v >> 2) & 0x3333333333333333UL);
        v = ((v & 0x0F0F0F0F0F0F0F0FUL) << 4) | ((v >> 4) & 0x0F0F0F0F0F0F0F0FUL);
        v = ((v & 0x00FF00FF00FF00FFUL) << 8) | ((v >> 8) & 0x00FF00FF00FF00FFUL);
        v = ((v & 0x0000FFFF0000FFFFUL) << 16) | ((v >> 16) & 0x0000FFFF0000FFFFUL);
        return (long)((v << 32) | (v >> 32));
    }

    private static int CompareFloat(float a, float b)
    {
        // Java's Float.compare: -0.0 sorts below 0.0 and every NaN is one canonical NaN.
        // CompareTo calls both zero, which would collapse two values the ordering keeps apart.
        if (a < b)
        {
            return -1;
        }

        if (a > b)
        {
            return 1;
        }

        int aBits = float.IsNaN(a) ? CanonicalFloatNaNBits : BitConverter.SingleToInt32Bits(a);
        int bBits = float.IsNaN(b) ? CanonicalFloatNaNBits : BitConverter.SingleToInt32Bits(b);
        return aBits == bBits ? 0 : aBits < bBits ? -1 : 1;
    }

    private static int CompareDouble(double a, double b)
    {
        // Java's Double.compare, same three rules as CompareFloat.
        if (a < b)
        {
            return -1;
        }

        if (a > b)
        {
            return 1;
        }

        long aBits = double.IsNaN(a) ? CanonicalDoubleNaNBits : BitConverter.DoubleToInt64Bits(a);
        long bBits = double.IsNaN(b) ? CanonicalDoubleNaNBits : BitConverter.DoubleToInt64Bits(b);
        return aBits == bBits ? 0 : aBits < bBits ? -1 : 1;
    }

    private const int CanonicalFloatNaNBits = 0x7FC00000;

    private const long CanonicalDoubleNaNBits = 0x7FF8000000000000L;

    private static int BitLength(BigInteger value)
    {
        if (value.IsZero)
        {
            return 0;
        }

        BigInteger magnitude = BigInteger.Abs(value);
        int bits = 0;
        while (magnitude > BigInteger.Zero)
        {
            magnitude >>= 1;
            bits++;
        }

        return value.Sign < 0 ? bits + 1 : bits;
    }

    private static long Low64(BigInteger value)
    {
        BigInteger mask = (BigInteger.One << 64) - 1;
        BigInteger low = value & mask;
        byte[] bytes = low.ToByteArray();
        Array.Resize(ref bytes, 8);
        return BitConverter.ToInt64(bytes, 0);
    }

    private static byte[] ToBigEndian(BigInteger value)
    {
        // Minimal two's complement big-endian, sign byte included, exactly what BigInteger
        // writes on disk: stripping a leading zero would flip the sign on the way back and
        // differ from the reference byte for byte.
        byte[] little = value.ToByteArray();
        Array.Reverse(little);
        return little;
    }

    private static BigInteger FromBigEndian(byte[] bytes)
    {
        byte[] little = (byte[])bytes.Clone();
        Array.Reverse(little);
        return new BigInteger(little);
    }
}

/// <summary>
/// The auto-detecting object type, fixed to one element type.
/// </summary>
/// <remarks>
/// The reference has no such class: its one ObjectDataType serves every map through erasure, and
/// an unchecked cast hides that a map of integers is really holding objects. C# generics are
/// invariant, so that cast is a runtime failure here instead of a silent one, and a map opened
/// without explicit types — the reference default — needs a type that truly is a
/// <see cref="DataType{T}"/> of its keys. This is that type: every operation boxes into the
/// shared engine and, on the way back, unboxes, so the bytes on disk are exactly the engine's.
/// </remarks>
/// <typeparam name="T">the element type</typeparam>
public sealed class ObjectDataType<T> : BasicDataType<T>
{
    private static readonly ObjectDataType Engine = new();

    /// <inheritdoc/>
    public override T[] CreateStorage(int size) => new T[size];

    /// <inheritdoc/>
    public override int GetMemory(T obj) => Engine.GetMemory(obj);

    /// <inheritdoc/>
    public override void Write(WriteBuffer buff, T obj) => Engine.Write(buff, obj);

    /// <inheritdoc/>
    public override T Read(Span<byte> buff, ref int position) => (T)Engine.Read(buff, ref position)!;

    /// <inheritdoc/>
    public override int Compare(T? a, T? b) => Engine.Compare(a, b);
}
