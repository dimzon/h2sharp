// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestFile.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (deferred, see MIGRATION_STATUS.md): the reference runs four combinations —
// memFS, memLZF, nioMemFS, nioMemLZF — but the port registers only the memFS scheme, so
// the LZF-compressed and nio variants wait for those file systems and this port runs the
// one combination the port has. The DataHandler answers are the reference's nulls.
using H2Sharp.Store;
using H2Sharp.Store.FileSystem;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Tests the in-memory file store.
/// </summary>
public sealed class TestFile : DataHandler
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public void Test()
    {
        // The reference also runs memLZF, nioMemFS and nioMemLZF; the port has memFS
        // alone, see the header.
        DoTest(false, false);
    }

    private void DoTest(bool nioMem, bool compress)
    {
        int len = 10000;
        Random random = new();
        FileStore? mem = null, file = null;
        byte[]? buffMem = null;
        byte[]? buffFile = null;
        string prefix = nioMem ? (compress ? "nioMemLZF:" : "nioMemFS:")
            : (compress ? "memLZF:" : "memFS:");
        FileUtils.NewFilePath(prefix + "test").Delete();
        FileUtils.NewFilePath("~/testFile").Delete();

        for (int i = 0; i < len; i++)
        {
            if (buffMem is null)
            {
                int l = 1 + random.Next(1000);
                buffMem = new byte[l];
                buffFile = new byte[l];
            }

            if (file is null)
            {
                mem = FileStore.Open(this, prefix + "test", "rw");
                file = FileStore.Open(this, "~/testFile", "rw");
            }

            AssertEquals(file.GetFilePointer(), mem!.GetFilePointer());
            AssertEquals(file.Length(), mem.Length());
            int x = random.Next(100);
            if ((x -= 20) < 0)
            {
                if (file.Length() > 0)
                {
                    long pos = random.Next((int)(file.Length() / 16)) * 16;
                    Trace("seek " + pos);
                    mem.Seek(pos);
                    file.Seek(pos);
                }
            }
            else if ((x -= 20) < 0)
            {
                Trace("close");
                mem.Close();
                file.Close();
                mem = null;
                file = null;
            }
            else if ((x -= 20) < 0)
            {
                if (buffFile!.Length > 16)
                {
                    random.NextBytes(buffFile);
                    Array.Copy(buffFile, 0, buffMem!, 0, buffFile.Length);
                    int off = random.Next(buffFile.Length - 16);
                    int l = random.Next((buffFile.Length - off) / 16) * 16;
                    Trace("write " + off + " " + l);
                    mem.Write(buffMem, off, l);
                    file.Write(buffFile, off, l);
                }
            }
            else if ((x -= 20) < 0)
            {
                if (buffFile!.Length > 16)
                {
                    int off = random.Next(buffFile.Length - 16);
                    int l = random.Next((buffFile.Length - off) / 16) * 16;
                    l = (int)Math.Min(l, file.Length() - file.GetFilePointer());
                    Trace("read " + off + " " + l);
                    Exception? a = null, b = null;
                    try
                    {
                        file.ReadFully(buffFile, off, l);
                    }
                    catch (Exception e)
                    {
                        a = e;
                    }

                    try
                    {
                        mem.ReadFully(buffMem!, off, l);
                    }
                    catch (Exception e)
                    {
                        b = e;
                    }

                    if (a is not null != (b is not null))
                    {
                        Fail("only one threw an exception");
                    }

                    AssertEquals(buffMem, buffFile);
                }
            }
            else if ((x -= 10) < 0)
            {
                Trace("reset buffers");
                buffMem = null;
                buffFile = null;
            }
            else
            {
                int l = random.Next(10000) * 16;
                long p = file.GetFilePointer();
                file.SetLength(l);
                mem.SetLength(l);
                Trace("setLength " + l);
                if (p > l)
                {
                    file.Seek(l);
                    mem.Seek(l);
                }
            }
        }

        if (mem is not null)
        {
            mem.Close();
            file!.Close();
        }

        FileUtils.NewFilePath(prefix + "test").Delete();
        FileUtils.NewFilePath("~/testFile").Delete();
    }

    /// <summary>
    /// The trace of the reference, writing nothing here: the operations it names are the
    /// test's own narration.
    /// </summary>
    /// <param name="s">the message</param>
    private static void Trace(string s)
    {
        _ = s;
    }

    private static void AssertEquals(long expected, long actual)
    {
        Assert.Equal(expected, actual);
    }

    private static void AssertEquals(byte[]? expected, byte[]? actual)
    {
        Assert.Equal(expected, actual);
    }

    private static void Fail(string message)
    {
        throw new Xunit.Sdk.XunitException(message);
    }

    /// <inheritdoc/>
    void DataHandler.CheckPowerOff()
    {
        // nothing to do
    }

    /// <inheritdoc/>
    void DataHandler.CheckWritingAllowed()
    {
        // nothing to do
    }

    /// <inheritdoc/>
    string DataHandler.GetDatabasePath()
    {
        return null!;
    }

    /// <inheritdoc/>
    object DataHandler.GetLobSyncObject()
    {
        return null!;
    }

    /// <inheritdoc/>
    int DataHandler.GetMaxLengthInplaceLob()
    {
        return 0;
    }

    /// <inheritdoc/>
    FileStore DataHandler.OpenFile(string name, string mode, bool mustExist)
    {
        return null!;
    }

    /// <inheritdoc/>
    SmallLRUCache<string, string[]>? DataHandler.GetLobFileListCache()
    {
        return null;
    }

    /// <inheritdoc/>
    TempFileDeleter DataHandler.GetTempFileDeleter()
    {
        return TempFileDeleter.GetInstance();
    }

    /// <inheritdoc/>
    CompareMode DataHandler.GetCompareMode()
    {
        return CompareMode.GetInstance(null, 0);
    }
}
