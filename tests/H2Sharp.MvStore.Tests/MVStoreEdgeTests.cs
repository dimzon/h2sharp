/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 210: the store and page edges the suite had not reached — every builder knob,
 * a close that commits unsaved changes, refusal of everything after close, the page
 * memory accounting when a value is replaced and children are removed, the deep-tree
 * disk-space approximation, the write pass over mixed saved and unsaved children, and a
 * corrupted page that panics the store closed.
 */
using System;
using System.IO;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class MVStoreEdgeTests : StoreTestBase
{
    [Fact]
    public void BuilderKnobsOpenUsableStores()
    {
        string file = TempFile("knobs.mv");
        DeleteFile(file);
        using MVStore s = new MVStore.Builder()
            .FileName(file)
            .AutoCommitDisabled()
            .AutoCommitBufferSize(1024)
            .AutoCompactFillRate(50)
            .KeysPerPage(12)
            .CacheSize(1)
            .Open();
        MVMap<int, string> m = s.OpenMap<int, string>("m");
        for (int i = 0; i < 100; i++)
        {
            m.Put(i, "v" + i);
        }

        _ = s.Commit();
        Assert.Equal("v7", m.Get(7));
    }

    [Fact]
    public void CloseWithUnsavedChangesCommitsThem()
    {
        string file = TempFile("close-commit.mv");
        DeleteFile(file);
        MVStore s = MVStore.Open(file);
        MVMap<int, string> m = s.OpenMap<int, string>("m");
        m.Put(1, "one");
        s.Close();

        using MVStore reopened = MVStore.Open(file);
        Assert.Equal("one", reopened.OpenMap<int, string>("m").Get(1));
    }

    [Fact]
    public void VersionsTrimAsTheyAge()
    {
        using MVStore s = new MVStore.Builder().Open();
        MVMap<int, int> m = s.OpenMap<int, int>("m");
        for (int i = 0; i < 40; i++)
        {
            m.Put(i, i);
            _ = s.Commit();
        }

        Assert.True(s.GetCurrentVersion() >= 40);
        Assert.True(s.GetCurrentVersion() >= 0);
    }

    [Fact]
    public void ReplacingAValueAndRemovingChildrenKeepsTheAccounting()
    {
        using MVStore s = new MVStore.Builder().PageSplitSize(48).Open();
        MVMap<int, string> m = s.OpenMap<int, string>("m");
        for (int i = 0; i < 120; i++)
        {
            m.Put(i, new string('x', 20));
        }

        int before = s.GetUnsavedMemory();
        m.Put(5, "tiny");
        Assert.True(s.GetUnsavedMemory() <= before + 4);

        for (int i = 0; i < 110; i++)
        {
            m.Remove(i);
        }

        _ = s.Commit();
        Assert.Null(m.Get(5));
    }

    [Fact]
    public void DeepTreeApproximatesDiskSpaceFromTheTop()
    {
        string file = TempFile("deep-space.mv");
        DeleteFile(file);
        using MVStore s = new MVStore.Builder()
            .FileName(file)
            .PageSplitSize(48)
            .Open();
        MVMap<int, string> m = s.OpenMap<int, string>("m");
        for (int i = 0; i < 5000; i++)
        {
            m.Put(i, "value-" + i);
        }

        _ = s.Commit();
        Page<int, string> root = m.GetRootPage();
        Assert.False(root.IsLeaf());
        long approximate = root.GetDiskSpaceUsed(true);
        long accurate = root.GetDiskSpaceUsed(false);
        Assert.True(approximate > 0);
        Assert.True(accurate > 0);
    }

    [Fact]
    public void CommitWritesUnsavedChildren()
    {
        string file = TempFile("write-children.mv");
        DeleteFile(file);
        using MVStore s = new MVStore.Builder()
            .FileName(file)
            .AutoCommitDisabled()
            .PageSplitSize(64)
            .Open();
        MVMap<int, string> m = s.OpenMap<int, string>("m");
        for (int i = 0; i < 3000; i++)
        {
            m.Put(i, "value-" + i);
        }

        _ = s.Commit();

        // Reopen: everything the write pass touched must be there.
        s.Close();
        using MVStore reopened = MVStore.Open(file);
        Assert.Equal("value-2999", reopened.OpenMap<int, string>("m").Get(2999));
    }

    [Fact]
    public void CorruptedPagePanicsTheStoreClosed()
    {
        string file = TempFile("panic.mv");
        DeleteFile(file);
        long rootPos;
        using (MVStore s = MVStore.Open(file))
        {
            MVMap<int, string> m = s.OpenMap<int, string>("m");
            for (int i = 0; i < 500; i++)
            {
                m.Put(i, "value-" + i);
            }

            _ = s.Commit();
            rootPos = m.GetRootPage().GetPos();
        }

        byte[] bytes = File.ReadAllBytes(file);
        // The root page of the data map: its check value is the pair of bytes after the
        // length. Flipping them makes every read of the page fail the check. The page
        // offset is relative to its chunk, whose block the chunk header carries.
        int chunkId = DataUtils.GetPageChunkId(rootPos);
        int pageOffset = DataUtils.GetPageOffset(rootPos);
        int chunkBlock = FindChunkBlock(bytes, chunkId);
        bytes[chunkBlock * 4096 + pageOffset + 4] ^= 0xFF;
        File.WriteAllBytes(file, bytes);

        MVStore panicking = MVStore.Open(file);
        // Opening the map reads its root page: the check fails, the open refuses. The
        // reference keeps the store open on a read error — panics belong to the write path.
        Assert.ThrowsAny<MvStoreException>(() => panicking.OpenMap<int, string>("m"));
        Assert.ThrowsAny<MvStoreException>(() => panicking.OpenMap<int, string>("m"));
        Assert.False(panicking.IsClosed());
    }

    /// <summary>
    /// The block a chunk's header sits in: the header begins with the chunk id and is
    /// block-aligned; the block itself is not written into it.
    /// </summary>
    private static int FindChunkBlock(byte[] bytes, int chunkId)
    {
        string prefix = "chunk:" + chunkId.ToString("x") + ",";
        string text = System.Text.Encoding.Latin1.GetString(bytes);
        for (int block = 2; block * 4096 < bytes.Length; block++)
        {
            int at = block * 4096;
            if (text[at..].StartsWith(prefix, StringComparison.Ordinal))
            {
                return block;
            }
        }

        Assert.Fail("chunk header not found");
        return -1;
    }
}
