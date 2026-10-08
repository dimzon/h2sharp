/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The three small pieces the store leans on: a cache that forgets, a deleter of temporary files
 * that waits for its owner to go, and the stream that turns text into bytes and counts characters.
 *
 * Each of these has one thing that is easy to get wrong and that the tests here pin: which entry a
 * cache drops, that a temporary file is deleted when its owner becomes unreachable rather than when
 * nobody calls anything, and that the count of a stream is characters while its bytes are UTF-8.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using H2Sharp.Store;
using H2Sharp.Store.FileSystem;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class StoreUtilitiesTests : IDisposable
{
    private readonly string directory;

    public StoreUtilitiesTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "h2sharp-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Somebody still holds a file; the next run makes another directory.
        }
    }

    [Fact]
    public void AFullCacheForgetsTheOneThatWasUsedLongestAgo()
    {
        SmallLRUCache<string, int> cache = SmallLRUCache<string, int>.NewInstance(3);
        _ = cache.Put("a", 1);
        _ = cache.Put("b", 2);
        _ = cache.Put("c", 3);

        // Nothing has been read, so the oldest entry is the first one that was put in.
        _ = cache.Put("d", 4);
        Assert.Equal(3, cache.Count);
        Assert.False(cache.TryGetValue("a", out _));
        Assert.True(cache.TryGetValue("b", out int b));
        Assert.Equal(2, b);
    }

    [Fact]
    public void AReadCountsAsUsingTheEntry()
    {
        SmallLRUCache<string, int> cache = SmallLRUCache<string, int>.NewInstance(3);
        _ = cache.Put("a", 1);
        _ = cache.Put("b", 2);
        _ = cache.Put("c", 3);

        // Reading "a" makes "b" the oldest rather than "a".
        Assert.True(cache.TryGetValue("a", out int a));
        Assert.Equal(1, a);
        _ = cache.Put("d", 4);

        Assert.True(cache.TryGetValue("a", out _));
        Assert.False(cache.TryGetValue("b", out _));
    }

    [Fact]
    public void PuttingAnExistingKeyMovesItToTheFront()
    {
        SmallLRUCache<string, int> cache = SmallLRUCache<string, int>.NewInstance(2);
        _ = cache.Put("a", 1);
        _ = cache.Put("b", 2);
        Assert.Equal(1, cache.Put("a", 11));

        Assert.True(cache.TryGetValue("a", out int a));
        Assert.Equal(11, a);

        // "a" was used, so "b" is the one that goes.
        _ = cache.Put("c", 3);
        Assert.False(cache.TryGetValue("b", out _));
    }

    [Fact]
    public void ASmallerCacheForgetsWhatNoLongerFits()
    {
        SmallLRUCache<string, int> cache = SmallLRUCache<string, int>.NewInstance(4);
        _ = cache.Put("a", 1);
        _ = cache.Put("b", 2);
        _ = cache.Put("c", 3);
        cache.SetMaxSize(1);

        Assert.Equal(1, cache.Count);
        Assert.False(cache.TryGetValue("a", out _));
        Assert.False(cache.TryGetValue("b", out _));
        Assert.True(cache.TryGetValue("c", out _));
    }

    [Fact]
    public void TakingAnEntryOutTakesItOut()
    {
        SmallLRUCache<string, int> cache = SmallLRUCache<string, int>.NewInstance(2);
        _ = cache.Put("a", 1);
        Assert.Equal(1, cache.Remove("a"));
        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGetValue("a", out _));
    }

    [Fact]
    public void TheCacheWalksFromTheOldestToTheNewest()
    {
        SmallLRUCache<string, int> cache = SmallLRUCache<string, int>.NewInstance(3);
        _ = cache.Put("a", 1);
        _ = cache.Put("b", 2);
        _ = cache.Put("c", 3);

        List<string> seen = [];
        foreach (KeyValuePair<string, int> entry in cache)
        {
            seen.Add(entry.Key);
        }

        // The order they would be dropped in, which is the reverse of the order of the reference's
        // map: it iterates by access order and we iterate by age.
        Assert.Equal(["a", "b", "c"], seen);
    }

    [Fact]
    public void ATemporaryFileGoesWhenWhatOpenedItGoes()
    {
        string name = Path.Combine(directory, "temporary.bin").Replace('\\', '/');
        FileUtils.WriteAllBytes(FilePath.Get(name), [1, 2, 3]);

        TempFileDeleter deleter = TempFileDeleter.GetInstance();

        // The token is deliberately not kept: the token is the object whose finalizer does the
        // deleting, and a local that still holds it keeps it alive, which is the one thing that
        // stops it. The reference has no such trap because a phantom reference is not the holder of
        // anything.
        WatchAndForget(deleter, name);
        Assert.True(File.Exists(name));
        WaitForFileToGo(name);
    }

    [Fact]
    public void AStoppedWatchLeavesTheFileAlone()
    {
        string name = Path.Combine(directory, "kept.bin").Replace('\\', '/');
        FileUtils.WriteAllBytes(FilePath.Get(name), [1, 2, 3]);

        TempFileDeleter deleter = TempFileDeleter.GetInstance();
        object token = Watch(deleter, name);

        // Nothing is watching it any more, so even after a collection the file stays.
        deleter.StopAutoDelete(token, name);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Thread.Sleep(100);
        Assert.True(File.Exists(name));

        // And it is still ours to delete.
        deleter.DeleteFile(token, name);
        Assert.False(File.Exists(name));
    }

    [Fact]
    public void DeletingNowDoesNotWaitForAnything()
    {
        string name = Path.Combine(directory, "now.bin").Replace('\\', '/');
        FileUtils.WriteAllBytes(FilePath.Get(name), [1, 2, 3]);

        TempFileDeleter deleter = TempFileDeleter.GetInstance();
        object token = Watch(deleter, name);
        deleter.DeleteFile(token, name);

        Assert.False(File.Exists(name));
        deleter.StopAutoDelete(token, name);
    }

    [Fact]
    public void EverythingRegisteredCanBeDeletedAtOnce()
    {
        string first = Path.Combine(directory, "all1.bin").Replace('\\', '/');
        string second = Path.Combine(directory, "all2.bin").Replace('\\', '/');
        FileUtils.WriteAllBytes(FilePath.Get(first), [1]);
        FileUtils.WriteAllBytes(FilePath.Get(second), [2]);

        TempFileDeleter deleter = TempFileDeleter.GetInstance();
        _ = deleter.AddFile(first, new object());
        _ = deleter.AddFile(second, new object());

        deleter.DeleteAll();
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
    }

    [Fact]
    public void SomethingToCloseIsClosedWhenItsOwnerGoes()
    {
        Counting closable = new();
        TempFileDeleter deleter = TempFileDeleter.GetInstance();
        WatchAndForget(deleter, closable);
        Assert.False(closable.Closed);

        WaitFor(() => closable.Closed);
    }

    [Fact]
    public void SomethingThatIsNeitherANameNorSomethingToCloseIsRefused()
    {
        TempFileDeleter deleter = TempFileDeleter.GetInstance();
        Assert.ThrowsAny<Exception>(() => deleter.AddFile(42, new object()));
    }

    [Fact]
    public void TextOverAStreamIsEncodedAndCounted()
    {
        using CountingReaderInputStream stream = new(new StringReader("привет"), 0);
        byte[] bytes = ReadAll(stream);

        Assert.Equal(Encoding.UTF8.GetBytes("привет"), bytes);
        Assert.Equal(6, stream.GetLength());
    }

    [Fact]
    public void AStreamStopsAtTheNumberOfCharactersItWasGiven()
    {
        using CountingReaderInputStream stream = new(new StringReader("abcdefgh"), 3);
        Assert.Equal(Encoding.UTF8.GetBytes("abc"), ReadAll(stream));
        Assert.Equal(3, stream.GetLength());
    }

    [Fact]
    public void TheCountIsCharactersAndNotBytes()
    {
        // Four characters, twelve bytes: the count the store asks for is in characters, and getting
        // it wrong would cut a CLOB in the wrong place.
        using CountingReaderInputStream stream = new(new StringReader("日本語だ"), 0);
        byte[] bytes = ReadAll(stream);

        Assert.Equal(4, stream.GetLength());
        Assert.Equal(12, bytes.Length);
    }

    [Fact]
    public void WhatCannotBeEncodedBecomesAQuestionMark()
    {
        // A surrogate without its partner is not a character, and the reference's encoder writes a
        // question mark where .NET's would write the replacement character.
        using CountingReaderInputStream stream = new(new StringReader("a\uD800b"), 0);
        byte[] bytes = ReadAll(stream);

        Assert.Equal(Encoding.ASCII.GetBytes("a?b"), bytes);
        Assert.Equal(3, stream.GetLength());
    }

    [Fact]
    public void AnEmptyTextIsAnEmptyStream()
    {
        using CountingReaderInputStream stream = new(new StringReader(""), 0);
        Assert.Empty(ReadAll(stream));
        Assert.Equal(0, stream.GetLength());
        Assert.Equal(-1, stream.ReadByte());
    }

    [Fact]
    public void TheStreamIsReadOnlyAndCannotBeSeeked()
    {
        using CountingReaderInputStream stream = new(new StringReader("abc"), 0);
        Assert.True(stream.CanRead);
        Assert.False(stream.CanSeek);
        Assert.False(stream.CanWrite);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(1));
        Assert.Throws<NotSupportedException>(() => stream.Write([1], 0, 1));
    }

    [Fact]
    public void ClosingTheStreamClosesTheText()
    {
        StringReader text = new("abc");
        CountingReaderInputStream stream = new(text, 0);
        stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => text.Read());
    }




    /// <summary>
    /// Watch a resource and keep the token, for the tests that stop the watch or delete by hand.
    /// </summary>
    /// <param name="deleter">the deleter</param>
    /// <param name="resource">the file name or the thing to close</param>
    /// <returns>the token</returns>
    private static object Watch(TempFileDeleter deleter, object resource)
        => deleter.AddFile(resource, new object());

    private static void WatchAndForget(TempFileDeleter deleter, object resource)
    {
        // A separate method, and the result thrown away, so that nothing keeps the watched object
        // or the holder of its finalizer alive past the call.
        _ = deleter.AddFile(resource, new object());
    }

    private static void WaitForFileToGo(string name)
    {
        for (int i = 0; i < 50 && File.Exists(name); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(20);
        }

        Assert.False(File.Exists(name), "the temporary file is still there");
    }

    private static void WaitFor(Func<bool> done)
    {
        for (int i = 0; i < 50 && !done(); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(20);
        }

        Assert.True(done(), "it was not closed");
    }

    private static byte[] ReadAll(Stream stream)
    {
        using MemoryStream target = new();
        stream.CopyTo(target);
        return target.ToArray();
    }

    private sealed class Counting : IDisposable
    {
        public bool Closed { get; private set; }

        public void Dispose() => Closed = true;
    }
}
