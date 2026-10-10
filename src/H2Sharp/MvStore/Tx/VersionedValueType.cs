using System;
using H2Sharp.Engine;
using H2Sharp.MvStore.Type;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Tx;

/// <summary>
/// The value type for a versioned value: a fast path for plain committed values, and a flagged
/// slow path carrying operation id, entry id, and both values.
/// </summary>
/// <remarks>
/// The factory round-trips through <see cref="Type.MetaType{D}"/> as <c>DataType&lt;object&gt;</c>,
/// so it only ever materializes with <c>T</c> as <c>object</c> — the only instantiation the
/// transaction store builds. Anything else fails at the cast, loudly rather than subtly.
/// </remarks>
public class VersionedValueType<T, D> : BasicDataType<VersionedValue<T>>, StatefulDataType<D>
{
    private readonly DataType<T> valueType;

    private readonly Factory factory = new();

    /// <summary>
    /// A versioned value type over the given inner type.
    /// </summary>
    public VersionedValueType(DataType<T> valueType)
    {
        this.valueType = valueType;
    }

    public override VersionedValue<T>[] CreateStorage(int size) => new VersionedValue<T>[size];

    public override int GetMemory(VersionedValue<T>? v)
    {
        if (v is null)
        {
            return 0;
        }

        int res = Constants.MemoryObject + 16 + 2 * Constants.MemoryPointer +
                GetValMemory(v.GetCurrentValue());
        if (v.GetOperationId() != VersionedValue<T>.NoOperationId)
        {
            res += GetValMemory(v.GetCommittedValue());
        }

        return res;
    }

    private int GetValMemory(T? obj) => obj is null ? 0 : valueType.GetMemory(obj);

    public override void Read(Span<byte> buff, VersionedValue<T>[] values, int len, ref int position)
    {
        if (buff[position++] == 0)
        {
            // fast path (no op ids or null entries)
            for (int i = 0; i < len; i++)
            {
                values[i] = VersionedValueCommitted<T>.GetInstance(valueType.Read(buff, ref position))!;
            }
        }
        else
        {
            // slow path (some entries may be null)
            for (int i = 0; i < len; i++)
            {
                values[i] = Read(buff, ref position);
            }
        }
    }

    public override VersionedValue<T> Read(Span<byte> buff, ref int position)
    {
        byte flags = buff[position++];

        long operationId = (flags & 1) != 0 ? DataUtils.ReadVarLong(buff, ref position) : VersionedValue<T>.NoOperationId;
        long entryId = (flags & 2) != 0 ? DataUtils.ReadVarLong(buff, ref position) : VersionedValue<T>.NoEntryId;
        T? value = (flags & 4) != 0 ? valueType.Read(buff, ref position) : default;
        T? committedValue = (flags & 8) != 0 ? valueType.Read(buff, ref position) : default;

        if (operationId == VersionedValue<T>.NoOperationId)
        {
            return VersionedValueCommitted<T>.GetInstance(value, entryId)!;
        }

        return VersionedValueUncommitted<T>.GetInstance(operationId, value, committedValue, entryId);
    }

    public override void Write(WriteBuffer buff, VersionedValue<T>[] storage, int len)
    {
        if (IsFastPath(storage, len))
        {
            _ = buff.Put((byte)0);
            for (int i = 0; i < len; i++)
            {
                valueType.Write(buff, storage[i].GetCurrentValue());
            }
        }
        else
        {
            // slow path:
            // store op ids, and some entries may be null
            _ = buff.Put((byte)1);
            for (int i = 0; i < len; i++)
            {
                Write(buff, storage[i]);
            }
        }
    }

    private bool IsFastPath(VersionedValue<T>[] storage, int len)
    {
        for (int i = 0; i < len; i++)
        {
            VersionedValue<T>? v = storage[i];
            if (v is null
                    || v.GetOperationId() != VersionedValue<T>.NoOperationId
                    || v.GetEntryId() != VersionedValue<T>.NoEntryId
                    || v.GetCurrentValue() is null)
            {
                return false;
            }
        }

        return true;
    }

    public override void Write(WriteBuffer buff, VersionedValue<T> v)
    {
        System.Diagnostics.Debug.Assert(v is not null);
        long operationId = v.GetOperationId();
        long entryId = v.GetEntryId();
        T? currentValue = v.GetCurrentValue();
        T? committedValue = operationId == VersionedValue<T>.NoOperationId ? default : v.GetCommittedValue();
        int flags = (operationId == VersionedValue<T>.NoOperationId ? 0 : 1)
                    | (entryId == VersionedValue<T>.NoEntryId ? 0 : 2)
                    | (currentValue is null ? 0 : 4)
                    | (committedValue is null ? 0 : 8);

        _ = buff.Put((byte)flags);

        if (operationId != VersionedValue<T>.NoOperationId)
        {
            _ = buff.PutVarLong(operationId);
        }

        if (entryId != VersionedValue<T>.NoEntryId)
        {
            _ = buff.PutVarLong(entryId);
        }

        if (currentValue is not null)
        {
            valueType.Write(buff, currentValue);
        }

        if (committedValue is not null)
        {
            valueType.Write(buff, committedValue);
        }
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(obj, this))
        {
            return true;
        }

        if (obj is not VersionedValueType<T, D> other)
        {
            return false;
        }

        return valueType.Equals(other.valueType);
    }

    public override int GetHashCode() => base.GetHashCode() ^ valueType.GetHashCode();

    public void Save(WriteBuffer buff, MetaType<D> metaType)
        => metaType.Write(buff, (DataType<object>)(object)valueType);

    public override int Compare(VersionedValue<T>? a, VersionedValue<T>? b)
        => valueType.Compare(a!.GetCurrentValue(), b!.GetCurrentValue());

    public StatefulDataType<D>.Factory GetFactory() => factory;

    /// <summary>
    /// Reads a versioned value type back through a meta type.
    /// </summary>
    public sealed class Factory : StatefulDataType<D>.Factory
    {
        /// <summary>
        /// A factory.
        /// </summary>
        public Factory()
        {
        }

        public DataType<object> Create(Span<byte> buff, ref int position, MetaType<D> metaType, D database)
        {
            DataType<object> inner = metaType.Read(buff, ref position);
            return (DataType<object>)(object)new VersionedValueType<T, D>((DataType<T>)(object)inner);
        }
    }
}
