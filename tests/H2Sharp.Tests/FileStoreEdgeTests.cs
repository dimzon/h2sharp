/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 109: the file store's edges — opening an already open store, an already open
 * file store instance, a missing parent directory, a read-only file, an invalid path
 * and the encryption refusal.
 */
using System;
using System.IO;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.Tests;

public sealed class FileStoreEdgeTests
{
    [Fact]
    public void TheSameFileCannotBeOpenedTwice()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fs_edge_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "s.mv");
        try
        {
            using (MVStore store = new MVStore.Builder().FileName(file).Open())
            {
                MVMap<int, int> map = store.OpenMap<int, int>("m");
                map.Put(1, 1);
                _ = store.Commit();

                // Opening the same file store instance again is a no-op.
                IFileStore fileStore = store.GetFileStore()!;
                fileStore.Open(file, readOnly: true, encryptionKey: null);

                // A second store over the same file is refused as locked.
                _ = Assert.ThrowsAny<MvStoreException>(
                    () => new MVStore.Builder().FileName(file).Open());
            }
        }
        finally
        {
            DeleteDirectory(dir);
        }
    }

    [Fact]
    public void MissingDirectoryReadOnlyAndInvalidPathsAreHandled()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fs_edge_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "s.mv");
        try
        {
            // A parent that does not exist is refused before any file is created.
            _ = Assert.ThrowsAny<ArgumentException>(
                () => new MVStore.Builder().FileName(Path.Combine(dir, "missing", "s.mv")).Open());

            using (MVStore store = new MVStore.Builder().FileName(file).Open())
            {
                MVMap<int, int> map = store.OpenMap<int, int>("m");
                map.Put(1, 1);
                _ = store.Commit();
            }

            // A read-only file opens read-only instead of failing.
            File.SetAttributes(file, FileAttributes.ReadOnly);
            try
            {
                using MVStore readOnly = new MVStore.Builder().FileName(file).Open();
                Assert.True(readOnly.IsReadOnly());
                MVMap<int, int> map = readOnly.OpenMap<int, int>("m");
                Assert.Equal(1, map.Get(1));
            }
            finally
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            // Encryption is not supported by the file store.
            _ = Assert.ThrowsAny<MvStoreException>(
                () => new MVStore.Builder().FileName(file).EncryptionKey("key".ToCharArray()).Open());
        }
        finally
        {
            DeleteDirectory(dir);
        }
    }

    private static void DeleteDirectory(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // A handle may outlive the closed store for a moment on Windows.
        }
    }
}
