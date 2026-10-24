/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mvstore.db.LobStorageMap (the two data types only).
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sliced to BlobReference and BlobMeta and their data types, which are the registry entries the
 * reference writes for its LOB maps. The LOB storage itself is not ported, but a store written by
 * the reference always names these two types in its type registry, and the registry is read whole:
 * without them a reference-written file cannot be opened at all.
 */
using System;
using H2Sharp.MvStore.Type;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// A map-based LOB storage. Only the two data types are ported.
/// </summary>
public sealed partial class LobStorageMap
{
    /// <summary>
    /// The reference to a LOB in the LOB data map.
    /// </summary>
    public sealed class BlobReference : IComparable<BlobReference>
    {
        /// <summary>
        /// The stream identifier.
        /// </summary>
        public readonly byte[] StreamStoreId;

        /// <summary>
        /// The LOB id.
        /// </summary>
        public readonly long LobId;

        /// <summary>
        /// Creates a LOB reference.
        /// </summary>
        /// <param name="streamStoreId">the stream identifier</param>
        /// <param name="lobId">the LOB id</param>
        public BlobReference(byte[] streamStoreId, long lobId)
        {
            StreamStoreId = streamStoreId;
            LobId = lobId;
        }

        /// <inheritdoc/>
        public int CompareTo(BlobReference? other)
        {
            ArgumentNullException.ThrowIfNull(other);
            int res = StreamStoreId.Length.CompareTo(other.StreamStoreId.Length);
            if (res == 0)
            {
                for (int i = 0; res == 0 && i < StreamStoreId.Length; i++)
                {
                    res = ((sbyte)StreamStoreId[i]).CompareTo((sbyte)other.StreamStoreId[i]);
                }

                if (res == 0)
                {
                    res = LobId.CompareTo(other.LobId);
                }
            }

            return res;
        }

        /// <summary>
        /// The data type of a LOB reference.
        /// </summary>
        public sealed class Type : BasicDataType<BlobReference>
        {
            /// <summary>
            /// The one instance.
            /// </summary>
            public static readonly Type Instance = new();

            private Type()
            {
            }

            /// <inheritdoc/>
            public override int GetMemory(BlobReference obj) => obj.StreamStoreId.Length + 8;

            /// <inheritdoc/>
            public override int Compare(BlobReference? a, BlobReference? b)
                => ReferenceEquals(a, b) ? 0 : a is null ? 1 : b is null ? -1 : a.CompareTo(b);

            /// <inheritdoc/>
            public override void Write(WriteBuffer buff, BlobReference obj)
            {
                _ = buff.PutVarInt(obj.StreamStoreId.Length);
                _ = buff.Put(obj.StreamStoreId);
                _ = buff.PutVarLong(obj.LobId);
            }

            /// <inheritdoc/>
            public override BlobReference Read(Span<byte> buff, ref int position)
            {
                int len = DataUtils.ReadVarInt(buff, ref position);
                byte[] streamStoreId = buff.Slice(position, len).ToArray();
                position += len;
                long lobId = DataUtils.ReadVarLong(buff, ref position);
                return new BlobReference(streamStoreId, lobId);
            }

            /// <inheritdoc/>
            public override BlobReference[] CreateStorage(int size) => new BlobReference[size];
        }
    }

    /// <summary>
    /// The meta data of a LOB.
    /// </summary>
    public sealed partial class BlobMeta
    {
        /// <summary>
        /// The stream identifier, used as a key in the LOB data map.
        /// </summary>
        public readonly byte[] StreamStoreId;

        internal readonly int TableId;

        internal readonly long ByteCount;

        internal readonly long Hash;

        /// <summary>
        /// Creates LOB meta data.
        /// </summary>
        /// <param name="streamStoreId">the stream identifier</param>
        /// <param name="tableId">the table id</param>
        /// <param name="byteCount">the number of bytes</param>
        /// <param name="hash">the hash</param>
        public BlobMeta(byte[] streamStoreId, int tableId, long byteCount, long hash)
        {
            StreamStoreId = streamStoreId;
            TableId = tableId;
            ByteCount = byteCount;
            Hash = hash;
        }

        /// <summary>
        /// The data type of LOB meta data.
        /// </summary>
        public sealed class Type : BasicDataType<BlobMeta>
        {
            /// <summary>
            /// The one instance.
            /// </summary>
            public static readonly Type Instance = new();

            private Type()
            {
            }

            /// <inheritdoc/>
            public override int GetMemory(BlobMeta obj) => obj.StreamStoreId.Length + 20;

            /// <inheritdoc/>
            public override void Write(WriteBuffer buff, BlobMeta obj)
            {
                _ = buff.PutVarInt(obj.StreamStoreId.Length);
                _ = buff.Put(obj.StreamStoreId);
                _ = buff.PutVarInt(obj.TableId);
                _ = buff.PutVarLong(obj.ByteCount);
                _ = buff.PutLong(obj.Hash);
            }

            /// <inheritdoc/>
            public override BlobMeta Read(Span<byte> buff, ref int position)
            {
                int len = DataUtils.ReadVarInt(buff, ref position);
                byte[] streamStoreId = buff.Slice(position, len).ToArray();
                position += len;
                int tableId = DataUtils.ReadVarInt(buff, ref position);
                long byteCount = DataUtils.ReadVarLong(buff, ref position);
                long hash = DataUtils.ReadFixedLong(buff, ref position);
                return new BlobMeta(streamStoreId, tableId, byteCount, hash);
            }

            /// <inheritdoc/>
            public override BlobMeta[] CreateStorage(int size) => new BlobMeta[size];
        }
    }
}
