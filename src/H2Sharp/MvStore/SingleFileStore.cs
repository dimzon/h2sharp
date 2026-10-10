using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using H2Sharp.Store.FileSystem;

namespace H2Sharp.MvStore;

/// <summary>
/// The default storage mechanism of the MVStore: data persists in one file, and this class owns
/// the channel while the free space lives in <see cref="RandomAccessStore"/>.
/// </summary>
public sealed class SingleFileStore : RandomAccessStore
{
    /// <summary>
    /// Files open in this process, by normalized name. A second open of the same file fails,
    /// read-only or not.
    /// </summary>
    /// <remarks>
    /// The reference takes an operating-system file lock on the channel. This port's channel
    /// abstraction deliberately has no locking, so same-process exclusion lives here instead.
    /// That is exactly what the open-close tests observe; exclusion across processes is not
    /// enforced.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, byte> OpenFiles = new(StringComparer.Ordinal);

    private FileChannel? fileChannel;

    private string? registryKey;

    private readonly IDictionary<string, object?> config;

    /// <summary>
    /// A single-file store over the given configuration.
    /// </summary>
    public SingleFileStore(IDictionary<string, object?> config)
        : base(config)
    {
        this.config = config;
    }

    /// <inheritdoc/>
    public override string ToString() => GetFileName() ?? "";

    /// <inheritdoc/>
    internal override byte[] ReadFully(Chunk? chunk, long pos, int len)
        => ReadFully(fileChannel!, pos, len);

    /// <inheritdoc/>
    protected override void WriteFully(Chunk? chunk, long pos, byte[] src, int offset, int length)
    {
        SetSize(Math.Max(Size(), pos + length));
        DataUtils.WriteFully(fileChannel!, pos, new ReadOnlyMemory<byte>(src, offset, length));
        _ = writeCount.IncrementAndGet();
        _ = writeBytes.AddAndGet(length);
    }

    /// <inheritdoc/>
    public override void Open(string fileName, bool readOnly, char[]? encryptionKey)
    {
        if (encryptionKey != null)
        {
            throw DataUtils.NewMvStoreException(
                DataUtils.ErrorUnsupportedFormat,
                "Encryption is not supported");
        }

        OpenImpl(fileName, readOnly);
    }

    /// <inheritdoc/>
    public override IFileStore Open(string fileName, bool readOnly)
    {
        var result = new SingleFileStore(config);
        result.OpenImpl(fileName, readOnly);
        return result;
    }

    private void OpenImpl(string fileName, bool readOnly)
    {
        if (fileChannel != null)
        {
            return;
        }

        FilePath f = FilePath.Get(fileName);
        FilePath? parent = f.GetParent();
        if (parent != null && !parent.Exists())
        {
            throw DataUtils.NewIllegalArgumentException(
                    "Directory does not exist: {0}", parent);
        }

        if (f.Exists() && !f.CanWrite())
        {
            readOnly = true;
        }

        Init(fileName, readOnly);
        FileChannel channel;
        try
        {
            channel = f.Open(readOnly ? "r" : "rw");
        }
        catch (Exception e) when (e is not MvStoreException)
        {
            throw DataUtils.NewMvStoreException(
                    DataUtils.ErrorReadingFailed,
                    "Could not open file {0}", fileName, e);
        }

        string key = NormalizeKey(fileName);
        if (!OpenFiles.TryAdd(key, 0))
        {
            channel.Dispose();
            throw DataUtils.NewMvStoreException(
                    DataUtils.ErrorFileLocked,
                    "The file is locked: {0}", fileName);
        }

        try
        {
            fileChannel = channel;
            registryKey = key;
            saveChunkLock.Lock();
            try
            {
                SetSize(channel.Length);
            }
            finally
            {
                saveChunkLock.Unlock();
            }
        }
        catch (Exception e) when (e is not MvStoreException)
        {
            OpenFiles.TryRemove(key, out _);
            registryKey = null;
            fileChannel = null;
            channel.Dispose();
            throw DataUtils.NewMvStoreException(
                    DataUtils.ErrorReadingFailed,
                    "Could not open file {0}", fileName, e);
        }
    }

    private static string NormalizeKey(string fileName)
    {
        try
        {
            return Path.GetFullPath(fileName);
        }
        catch (Exception)
        {
            return fileName;
        }
    }

    /// <inheritdoc/>
    public override void Close()
    {
        try
        {
            FileChannel? channel = fileChannel;
            fileChannel = null;
            channel?.Dispose();
        }
        catch (Exception e) when (e is not MvStoreException)
        {
            throw DataUtils.NewMvStoreException(
                    DataUtils.ErrorWritingFailed,
                    "Closing failed for file {0}", GetFileName(), e);
        }
        finally
        {
            if (registryKey != null)
            {
                OpenFiles.TryRemove(registryKey, out _);
                registryKey = null;
            }

            base.Close();
        }
    }

    /// <inheritdoc/>
    public override void Sync()
    {
        if (fileChannel != null)
        {
            try
            {
                fileChannel.Flush(true);
            }
            catch (IOException e)
            {
                throw DataUtils.NewMvStoreException(
                        DataUtils.ErrorWritingFailed,
                        "Could not sync file {0}", GetFileName(), e);
            }
        }
    }

    /// <inheritdoc/>
    protected override void Truncate(long size)
    {
        int attemptCount = 0;
        while (true)
        {
            try
            {
                _ = writeCount.IncrementAndGet();
                fileChannel!.SetLength(size);
                SetSize(Math.Min(Size(), size));
                return;
            }
            catch (IOException e)
            {
                if (++attemptCount == 10)
                {
                    throw DataUtils.NewMvStoreException(
                            DataUtils.ErrorWritingFailed,
                            "Could not truncate file {0} to size {1}",
                            GetFileName(), size.ToString(CultureInfo.InvariantCulture), e);
                }

                GC.Collect();
                Thread.Yield();
            }
        }
    }
}
