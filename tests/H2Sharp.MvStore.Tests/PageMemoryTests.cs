/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of Page memory accounting: key replacement (SetKey) and key
 * removal (Remove(int)) when the map does not estimate memory.
 */
using System;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Type;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class PageMemoryTests : StoreTestBase
{
    /// <summary>
    /// A data type that refuses memory estimation, so the page tree takes the
    /// non-estimation branches in <see cref="Page{K, V}.SetKey"/> and
    /// <see cref="Page{K, V}.Remove(int)"/>.
    /// </summary>
    private sealed class NoMemIntType : BasicDataType<int>
    {
        public override int[] CreateStorage(int size) => new int[size];

        public override int GetMemory(int obj) => 4;

        public override void Write(WriteBuffer buff, int obj) => buff.PutInt(obj);

        public override int Read(Span<byte> buff, ref int position)
        {
            int value = BitConverter.ToInt32(buff.Slice(position, 4));
            position += 4;
            return value;
        }

        public override bool IsMemoryEstimationAllowed() => false;

        public override int Compare(int a, int b) => a.CompareTo(b);
    }

    [Fact]
    public void SetKeyAndRemoveAccountMemoryWithoutEstimation()
    {
        string file = TempFile("page-mem.mv");
        DeleteFile(file);
        using MVStore s = MVStore.Open(file);
        var builder = new MVMap<int, int>.Builder<int, int>()
            .KeyType(new NoMemIntType())
            .ValueType(new NoMemIntType());
        MVMap<int, int> m = s.OpenMap("mem", builder);
        for (int i = 0; i < 10; i++)
        {
            m.Put(i, i * 10);
        }
        s.Commit();

        // The root page is a leaf holding the keys; replace its first key to reach
        // Page.SetKey's non-estimation memory branch.
        Page<int, int> root = m.GetRootPage();
        root.SetKey(0, 100);

        // Remove the first key to reach Page.Remove's non-estimation memory branch.
        root.Remove(0);

        s.Commit();
    }
}
