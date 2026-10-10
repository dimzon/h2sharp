/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The smallest possible TestBase: what the ported org.h2.test.store tests actually call.
 * The reference's TestBase opens databases, deletes files and prints progress; none of that
 * belongs here. What belongs here is the shape of the asserts — fail with a message, true with
 * an optional message, equal with an optional message — because xUnit's Assert has no message
 * overloads on Equal, and the ported tests lean on them for every comparison.
 */

using System;
using System.IO;
using H2Sharp.MvStore;
using H2Sharp.Store.FileSystem;
using Xunit;

namespace H2Sharp.MvStore.Tests;

/// <summary>
/// The asserts a ported store test needs, mirroring the reference's TestBase.
/// </summary>
public abstract class StoreTestBase : IDisposable
{
    private string? tempDir;

    /// <summary>
    /// A fresh temporary directory, made on first use.
    /// </summary>
    protected string TempDir
    {
        get
        {
            tempDir ??= Path.Combine(Path.GetTempPath(), "h2sharp-mvstore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            return tempDir;
        }
    }

    /// <summary>
    /// A file name inside the temporary directory.
    /// </summary>
    protected string TempFile(string name) => Path.Combine(TempDir, name);

    /// <summary>
    /// Deletes a file when it is there, like the reference's file delete.
    /// </summary>
    /// <remarks>
    /// Scheme-aware like the reference's <c>FileUtils.delete</c>: a <c>memFS:</c> name never
    /// touches the disk, so plain <see cref="File"/> calls would silently leave the previous
    /// seed's data behind and the next open would read it as its own.
    /// </remarks>
    protected static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
            return;
        }

        try
        {
            FilePath filePath = FilePath.Get(path);
            if (filePath.Exists())
            {
                filePath.Delete();
            }
        }
        catch (Exception)
        {
            // delete-if-there: an unparsable name is not there
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (tempDir is not null && Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Fails with a message.
    /// </summary>
    protected static void Fail(string message) => Assert.Fail(message);

    /// <summary>
    /// Holds.
    /// </summary>
    protected static void AssertTrue(bool condition) => Assert.True(condition);

    /// <summary>
    /// Holds, or fails with a message.
    /// </summary>
    protected static void AssertTrue(string message, bool condition) => Assert.True(condition, message);

    /// <summary>
    /// Does not hold.
    /// </summary>
    protected static void AssertFalse(bool condition) => Assert.False(condition);

    /// <summary>
    /// Does not hold, or fails with a message.
    /// </summary>
    protected static void AssertFalse(string message, bool condition) => Assert.False(condition, message);

    /// <summary>
    /// Two ints are the same.
    /// </summary>
    protected static void AssertEqual(int expected, int actual) => Assert.Equal(expected, actual);

    /// <summary>
    /// Two longs are the same.
    /// </summary>
    protected static void AssertEqual(long expected, long actual) => Assert.Equal(expected, actual);

    /// <summary>
    /// Two values are equal.
    /// </summary>
    protected static void AssertEqual(object? expected, object? actual) => Assert.Equal(expected, actual);

    /// <summary>
    /// Two ints are the same, or fails with a message.
    /// </summary>
    /// <remarks>
    /// xUnit's Equal takes no message; the message goes in front by hand, which reads worse than
    /// the reference but fails with the same words.
    /// </remarks>
    protected static void AssertEqual(string message, int expected, int actual)
        => Assert.True(expected == actual, message + ": expected " + expected + ", actual " + actual);

    /// <summary>
    /// An int key against an expected one that may be absent: value-type keys cannot spell a
    /// missing neighbor as null, so the map answers default and both sides must agree where a
    /// neighbor exists, while a missing neighbor must read as default.
    /// </summary>
    protected static void AssertEqualKey(int? expected, int actual)
    {
        if (expected is null)
        {
            Assert.Equal(default, actual);
        }
        else
        {
            Assert.Equal(expected.Value, actual);
        }
    }

    /// <summary>
    /// The cause is a store exception with a code.
    /// </summary>
    protected static void CheckErrorCode(int expected, Exception? cause)
        => Assert.Equal(expected, ((MvStoreException)cause!).GetErrorCode());
}
