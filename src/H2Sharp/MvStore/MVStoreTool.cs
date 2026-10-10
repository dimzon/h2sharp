using System;
using H2Sharp.MvStore.Type;
using H2Sharp.Store.FileSystem;

namespace H2Sharp.MvStore;

/// <summary>
/// File-level tools. Only what the store itself needs lives here: the generic map builder behind
/// compaction and the atomic replace behind it. The rest of the reference's tool stayed out.
/// </summary>
public static class MVStoreTool
{
    /// <summary>
    /// A builder that reads anything persisted as raw bytes, for copying maps blind.
    /// </summary>
    internal static MVMap<object?, object?>.Builder<object?, object?> GetGenericMapBuilder()
    {
        var builder = new MVMap<object?, object?>.Builder<object?, object?>();
        builder.KeyType(GenericDataType.Instance);
        builder.ValueType(GenericDataType.Instance);
        return builder;
    }

    /// <summary>
    /// Moves a file over another, atomically where the file system allows it.
    /// </summary>
    /// <param name="sourceName">the old fully qualified file name</param>
    /// <param name="destinationName">the new fully qualified file name</param>
    public static void MoveAtomicReplace(string sourceName, string destinationName)
        => FilePath.Get(sourceName).MoveTo(FilePath.Get(destinationName), true);

    /// <summary>
    /// A data type that reads anything persisted as raw bytes, without understanding it.
    /// </summary>
    private sealed class GenericDataType : BasicDataType<object?>
    {
        internal static readonly GenericDataType Instance = new();

        private GenericDataType()
        {
        }

        public override bool IsMemoryEstimationAllowed() => false;

        public override int GetMemory(object? obj) => obj is byte[] bytes ? bytes.Length * 8 : 0;

        public override object?[] CreateStorage(int size) => new object?[size];

        public override void Write(WriteBuffer buff, object? obj)
        {
            if (obj is byte[] bytes)
            {
                buff.Put(bytes);
            }
        }

        public override object? Read(Span<byte> buff, ref int position)
        {
            int len = buff.Length - position;
            if (len == 0)
            {
                return null;
            }

            byte[] data = new byte[len];
            buff.Slice(position, len).CopyTo(data);
            position += len;
            return data;
        }
    }
}
