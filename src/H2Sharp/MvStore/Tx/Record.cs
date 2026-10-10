using System;
using H2Sharp.MvStore.Type;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Tx;

/// <summary>
/// An undo log entry: one change of one map, carrying the value before the change.
/// Null when the entry did not exist before (an addition).
/// </summary>
internal sealed class Record<K, V>
{
    /// <summary>
    /// The map the change belongs to.
    /// </summary>
    internal readonly int MapId;

    /// <summary>
    /// The changed entry's key.
    /// </summary>
    internal readonly K Key;

    /// <summary>
    /// The entry's value before the change.
    /// </summary>
    internal readonly VersionedValue<V>? OldValue;

    internal Record(int commitOrder)
        : this(commitOrder, default!, null)
    {
    }

    internal Record(int mapId, K key, VersionedValue<V>? oldValue)
    {
        MapId = mapId;
        Key = key;
        OldValue = oldValue;
    }

    public override string ToString() => "mapId=" + MapId + ", key=" + Key + ", value=" + OldValue;

    /// <summary>
    /// The data type for undo log values.
    /// </summary>
    internal sealed class Type : BasicDataType<Record<K, V>>
    {
        private readonly TransactionStore transactionStore;

        internal Type(TransactionStore transactionStore)
        {
            this.transactionStore = transactionStore;
        }

        public override int GetMemory(Record<K, V> record)
        {
            int result = Engine.Constants.MemoryObject + 4 + 3 * Engine.Constants.MemoryPointer;
            if (record.MapId >= 0)
            {
                MVMap<K, VersionedValue<V>> map =
                    (MVMap<K, VersionedValue<V>>)(object)transactionStore.GetVersionedMap(record.MapId);
                System.Diagnostics.Debug.Assert(map is not null);
                result += map!.GetKeyType().GetMemory(record.Key) +
                        map.GetValueType().GetMemory(record.OldValue!);
            }

            return result;
        }

        public override int Compare(Record<K, V>? a, Record<K, V>? b) => throw new NotSupportedException();

        public override void Write(WriteBuffer buff, Record<K, V> record)
        {
            _ = buff.PutVarInt(record.MapId);
            if (record.MapId >= 0)
            {
                MVMap<K, VersionedValue<V>> map =
                    (MVMap<K, VersionedValue<V>>)(object)transactionStore.GetVersionedMap(record.MapId);
                System.Diagnostics.Debug.Assert(map is not null);
                map!.GetKeyType().Write(buff, record.Key);
                VersionedValue<V>? oldValue = record.OldValue;
                if (oldValue is null)
                {
                    _ = buff.Put((byte)0);
                }
                else
                {
                    _ = buff.Put((byte)1);
                    map.GetValueType().Write(buff, oldValue);
                }
            }
        }

        public override Record<K, V> Read(Span<byte> buff, ref int position)
        {
            int mapId = DataUtils.ReadVarInt(buff, ref position);
            if (mapId < 0)
            {
                return new Record<K, V>(mapId);
            }

            MVMap<K, VersionedValue<V>> map =
                (MVMap<K, VersionedValue<V>>)(object)transactionStore.GetVersionedMap(mapId);
            System.Diagnostics.Debug.Assert(map is not null);
            K key = map!.GetKeyType().Read(buff, ref position);
            VersionedValue<V>? oldValue = null;
            if (buff[position++] == 1)
            {
                oldValue = map.GetValueType().Read(buff, ref position);
            }

            return new Record<K, V>(mapId, key, oldValue);
        }

        public override Record<K, V>[] CreateStorage(int size) => new Record<K, V>[size];
    }
}
