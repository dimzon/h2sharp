/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 218: recovery edges — a store opened in recovery mode answers from an empty leaf
 * where a page's check, page number, map id or node type is corrupt; a closed store
 * refuses every operation; keeping two versions trims the older ones; a compacted file
 * with several maps keeps its meta names.
 */
using System;
using System.Globalization;
using System.IO;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class MvStoreRecoveryEdgesTests : StoreTestBase
{
    private const int BlockSize = 4 * 1024;

    [Fact]
    public void OperationsOnAClosedStoreRefuse()
    {
        string file = TempFile("closed.mv");
        DeleteFile(file);
        MVStore s = MVStore.Open(file);
        MVMap<int, string> m = s.OpenMap<int, string>("m");
        m.Put(1, "one");
        _ = s.Commit();
        s.Close();

        Assert.Throws<MvStoreException>(() => s.GetMapNames());
        Assert.Throws<MvStoreException>(() => s.GetStoreVersion());
        Assert.Throws<MvStoreException>(() => s.SetStoreVersion(1));
        Assert.Throws<MvStoreException>(() => s.RollbackTo(0));
    }

    [Fact]
    public void KeepingTwoVersionsTrimsTheOlderOnes()
    {
        string file = TempFile("versions.mv");
        DeleteFile(file);
        using MVStore s = new MVStore.Builder().FileName(file).Open();
        s.SetVersionsToKeep(2);
        MVMap<int, string> m = s.OpenMap<int, string>("m");
        for (int i = 0; i < 20; i++)
        {
            m.Put(i, "v" + i);
            _ = s.Commit();
        }

        Assert.Equal(2, s.GetVersionsToKeep());
        Assert.Equal("v19", m.Get(19));
    }

    [Fact]
    public void CompactingAFileWithSeveralMapsKeepsTheNames()
    {
        string file = TempFile("compact-maps.mv");
        DeleteFile(file);
        using (MVStore s = new MVStore.Builder().FileName(file).AutoCommitDisabled().Open())
        {
            MVMap<int, string> a = s.OpenMap<int, string>("alpha");
            MVMap<int, string> b = s.OpenMap<int, string>("beta");
            for (int i = 0; i < 300; i++)
            {
                a.Put(i, "a" + i);
                b.Put(i, "b" + i);
            }

            _ = s.Commit();
        }

        using (MVStore s = MVStore.Open(file))
        {
            if (s.GetFileStore() is RandomAccessStore fileStore)
            {
                fileStore.CompactMoveChunks(50, long.MaxValue, s);
            }

            _ = s.Commit();
        }

        using MVStore reopened = MVStore.Open(file);
        Assert.Equal("a299", reopened.OpenMap<int, string>("alpha").Get(299));
        Assert.Equal("b299", reopened.OpenMap<int, string>("beta").Get(299));
    }

    [Fact]
    public void RecoveryModeAnswersFromAnEmptyLeafForACorruptCheckValue()
        => AssertRecoveryModeReadsEmptyLeaf(PageField.Check);

    [Fact]
    public void RecoveryModeAnswersFromAnEmptyLeafForANegativePageNumber()
        => AssertRecoveryModeReadsEmptyLeaf(PageField.PageNo);

    [Fact]
    public void RecoveryModeAnswersFromAnEmptyLeafForAForeignMapId()
        => AssertRecoveryModeReadsEmptyLeaf(PageField.MapId);

    [Fact]
    public void RecoveryModeAnswersFromAnEmptyLeafForAForeignNodeType()
        => AssertRecoveryModeReadsEmptyLeaf(PageField.NodeType);

    [Fact]
    public void RecoveryModeAnswersFromAnEmptyLeafForABadPageLength()
        => AssertRecoveryModeReadsEmptyLeaf(PageField.Length);

    private enum PageField
    {
        Check,
        PageNo,
        MapId,
        NodeType,
        Length,
    }

    /// <summary>
    /// Writes a small store, corrupts one field of the data map's root page, and opens the
    /// file in recovery mode: the page read fails, and the recovery answers with an empty
    /// leaf instead of failing the query.
    /// </summary>
    private void AssertRecoveryModeReadsEmptyLeaf(PageField field)
    {
        string file = TempFile("recover-" + field + ".mv");
        DeleteFile(file);
        long rootPos;
        using (MVStore s = MVStore.Open(file))
        {
            MVMap<int, string> m = s.OpenMap<int, string>("m");
            for (int i = 0; i < 400; i++)
            {
                m.Put(i, "value-" + i);
            }

            _ = s.Commit();
            rootPos = m.GetRootPage().GetPos();
        }

        byte[] bytes = File.ReadAllBytes(file);
        int chunkId = DataUtils.GetPageChunkId(rootPos);
        int pageOffset = DataUtils.GetPageOffset(rootPos);
        int chunkBlock = FindChunkBlock(bytes, chunkId);
        int at = chunkBlock * BlockSize + pageOffset;
        switch (field)
        {
            case PageField.Check:
                bytes[at + 4] ^= 0xFF;
                break;
            case PageField.PageNo:
                // A five-byte varint whose top bits make the value negative.
                for (int i = 6; i < 11; i++)
                {
                    bytes[at + i] = 0xFF;
                }

                break;
            case PageField.MapId:
                bytes[at + 7] = 0x7F;
                break;
            case PageField.NodeType:
                bytes[at + 9] ^= 0x01;
                break;
            case PageField.Length:
                bytes[at] = 0x7F;
                bytes[at + 1] = 0xFF;
                break;
        }

        File.WriteAllBytes(file, bytes);

        using MVStore recovered = new MVStore.Builder().FileName(file).RecoveryMode().Open();
        MVMap<int, string> map = recovered.OpenMap<int, string>("m");
        _ = map.Get(7);
    }

    /// <summary>
    /// The block a chunk's header sits in: the header begins with the chunk id and is
    /// block-aligned; the block itself is not written into it.
    /// </summary>
    private static int FindChunkBlock(byte[] bytes, int chunkId)
    {
        string prefix = "chunk:" + chunkId.ToString("x", CultureInfo.InvariantCulture) + ",";
        string text = System.Text.Encoding.Latin1.GetString(bytes);
        for (int block = 2; block * BlockSize < bytes.Length; block++)
        {
            int at = block * BlockSize;
            if (text[at..].StartsWith(prefix, StringComparison.Ordinal))
            {
                return block;
            }
        }

        Assert.Fail("chunk header not found");
        return -1;
    }
}
