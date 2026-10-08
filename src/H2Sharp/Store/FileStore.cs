/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.FileStore.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: encrypted databases are not ported, so the cipher overloads of open, the key, the salt
 * and the key hashing are gone; the header is still written and checked exactly as it is in the
 * reference, because all three blocks of it are in the file whether there is a cipher or not.
 *
 * The Java FileChannel is the store's own FileChannel here. The reference's extra assertion block —
 * that the length and the position agree with what this object thinks they are — is behind DEBUG,
 * which is what a Java assertion is behind: on, when assertions were asked for.
 */
using System;
using System.IO;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Store.FileSystem;
using H2Sharp.Util;

namespace H2Sharp.Store;

/// <summary>
/// A file of whole blocks, which is what a database file is.
/// </summary>
/// <remarks>
/// Every read, every write and every change of length is in whole blocks, and anything else is a
/// mistake worth failing over rather than something to round: a block that is half written is a
/// database that cannot be recovered.
/// </remarks>
public class FileStore
{
    /// <summary>
    /// The size of the file header in bytes.
    /// </summary>
    public const int HeaderLength = 3 * Constants.FileBlockSize;

    /// <summary>
    /// The magic file header, cut to a block.
    /// </summary>
    /// <remarks>
    /// The reference cuts the twenty-character text to <c>FILE_BLOCK_SIZE - 1</c> and adds a line
    /// feed, which is a block of sixteen. The cut is written defensively here because the reference
    /// would have thrown if the block were ever smaller than the text.
    /// </remarks>
    private static readonly byte[] Header = BuildHeader();

    private readonly DataHandler handler;

    private FileChannel? file;

    private long filePos;

    private long fileLength;

    private object? autoDeleteReference;

    private bool checkedWriting = true;

    private readonly string mode;

    /// <summary>
    /// Open a file.
    /// </summary>
    /// <param name="handler">the database this file belongs to</param>
    /// <param name="name">the file</param>
    /// <param name="mode">the access mode: <c>r</c>, or <c>rw</c> with an <c>s</c> or <c>d</c> after it</param>
    /// <exception cref="H2SharpException">if the file cannot be opened</exception>
    public static FileStore Open(DataHandler handler, string name, string mode)
        => new(handler, name, mode);

    /// <summary>
    /// Open a file.
    /// </summary>
    /// <param name="handler">the database this file belongs to</param>
    /// <param name="name">the file</param>
    /// <param name="mode">the access mode</param>
    /// <exception cref="H2SharpException">if the file cannot be opened</exception>
    protected FileStore(DataHandler handler, string name, string mode)
    {
        this.handler = handler;
        Name = name;
        try
        {
            bool exists = FileUtils.Exists(name);
            if (exists && !FileUtils.CanWrite(name))
            {
                // A file that cannot be written to is opened for reading, whatever was asked for.
                mode = "r";
            }
            else
            {
                FilePath? parent = FileUtils.GetParent(name);
                FileUtils.CreateDirectories(parent?.Name);
            }

            file = FileUtils.Open(name, mode);
            if (exists)
            {
                fileLength = file.Length;
            }
        }
        catch (IOException e)
        {
            throw DbException.ConvertIOException(e, "name: " + name + " mode: " + mode);
        }

        this.mode = mode;
    }

    /// <summary>
    /// The file name.
    /// </summary>
    protected string Name { get; private set; }

    /// <summary>
    /// The salt written into the header. Without encryption it is the header itself, which is what
    /// the reference's own class does as well — the block is in the file either way.
    /// </summary>
    /// <returns>the salt</returns>
    protected static byte[] GenerateSalt() => Header;

    /// <summary>
    /// Write or check the file header.
    /// </summary>
    /// <exception cref="H2SharpException">if the file is of another version or is not readable</exception>
    public void Init()
    {
        int len = Constants.FileBlockSize;
        byte[] salt;
        if (Length() < HeaderLength)
        {
            // A new file: the first block is the header in the clear, the second is the salt, and
            // the third is the header again. With a cipher the last one would be encrypted, and
            // nothing else about the layout would differ.
            checkedWriting = false;
            WriteDirect(Header, 0, len);
            salt = GenerateSalt();
            WriteDirect(salt, 0, len);
            Write(Header, 0, len);
            checkedWriting = true;
        }
        else
        {
            Seek(0);
            byte[] buffer = new byte[len];
            ReadFullyDirect(buffer, 0, len);
            if (!SameBytes(buffer, Header))
            {
                throw DbException.Get(ErrorCode.FileVersionError1, Name);
            }

            salt = new byte[len];
            ReadFullyDirect(salt, 0, len);
            ReadFully(buffer, 0, Constants.FileBlockSize);
            if (!SameBytes(buffer, Header))
            {
                throw DbException.Get(ErrorCode.FileEncryptionError1, Name);
            }
        }
    }

    /// <summary>
    /// Close the file.
    /// </summary>
    public void Close()
    {
        if (file is null)
        {
            return;
        }

        try
        {
            file.Dispose();
        }
        catch (IOException e)
        {
            throw DbException.ConvertIOException(e, Name);
        }
        finally
        {
            file = null;
        }
    }

    /// <summary>
    /// Close the file and do not mind if it fails.
    /// </summary>
    public void CloseSilently()
    {
        try
        {
            Close();
        }
        catch (Exception e) when (e is IOException or H2SharpException or ObjectDisposedException)
        {
            // Closing on a path where nobody is listening any more.
        }
    }

    /// <summary>
    /// Close the file, do not mind if it fails, and delete it.
    /// </summary>
    public void CloseAndDeleteSilently()
    {
        if (file is null)
        {
            return;
        }

        CloseSilently();
        handler.GetTempFileDeleter().DeleteFile(autoDeleteReference, Name);
        autoDeleteReference = null;
        Name = null!;
    }

    /// <summary>
    /// Whether every write is checked with the database first.
    /// </summary>
    /// <param name="value">true to ask before every write</param>
    public void SetCheckedWriting(bool value) => checkedWriting = value;

    /// <summary>
    /// Read bytes without decrypting them.
    /// </summary>
    /// <param name="b">where to read</param>
    /// <param name="off">the offset in the buffer</param>
    /// <param name="len">how many, in whole blocks</param>
    public void ReadFullyDirect(byte[] b, int off, int len) => ReadFully(b, off, len);

    /// <summary>
    /// Read bytes.
    /// </summary>
    /// <param name="b">where to read</param>
    /// <param name="off">the offset in the buffer</param>
    /// <param name="len">how many, in whole blocks</param>
    /// <exception cref="H2SharpException">if the length is not whole blocks</exception>
    public void ReadFully(byte[] b, int off, int len)
    {
        if (len < 0 || len % Constants.FileBlockSize != 0)
        {
            throw DbException.GetInternalError("unaligned read " + Name + " len " + len);
        }

        CheckPowerOff();
        try
        {
            FileUtils.ReadFully(Open(), b.AsSpan(off, len));
        }
        catch (IOException e)
        {
            throw DbException.ConvertIOException(e, Name);
        }

        filePos += len;
    }

    /// <summary>
    /// Go to a place in the file.
    /// </summary>
    /// <param name="pos">where, in whole blocks</param>
    /// <exception cref="H2SharpException">if the place is not a whole block</exception>
    public void Seek(long pos)
    {
        if (pos % Constants.FileBlockSize != 0)
        {
            throw DbException.GetInternalError("unaligned seek " + Name + " pos " + pos);
        }

        try
        {
            FileChannel open = Open();
            if (pos != filePos)
            {
                open.Position = pos;
                filePos = pos;
            }
        }
        catch (IOException e)
        {
            throw DbException.ConvertIOException(e, Name);
        }
    }

    /// <summary>
    /// Write bytes without encrypting them.
    /// </summary>
    /// <param name="b">what to write</param>
    /// <param name="off">the offset in the buffer</param>
    /// <param name="len">how many, in whole blocks</param>
    protected void WriteDirect(byte[] b, int off, int len) => Write(b, off, len);

    /// <summary>
    /// Write bytes.
    /// </summary>
    /// <param name="b">what to write</param>
    /// <param name="off">the offset in the buffer</param>
    /// <param name="len">how many, in whole blocks</param>
    /// <exception cref="H2SharpException">if the length is not whole blocks</exception>
    public void Write(byte[] b, int off, int len)
    {
        if (len < 0 || len % Constants.FileBlockSize != 0)
        {
            throw DbException.GetInternalError("unaligned write " + Name + " len " + len);
        }

        CheckWritingAllowed();
        CheckPowerOff();
        try
        {
            FileUtils.WriteFully(Open(), b.AsSpan(off, len));
        }
        catch (IOException e)
        {
            CloseFileSilently();
            throw DbException.ConvertIOException(e, Name);
        }

        filePos += len;
        fileLength = Math.Max(filePos, fileLength);
    }

    /// <summary>
    /// Make the file longer or shorter.
    /// </summary>
    /// <param name="newLength">the new length, in whole blocks</param>
    /// <exception cref="H2SharpException">if the length is not whole blocks</exception>
    public void SetLength(long newLength)
    {
        if (newLength % Constants.FileBlockSize != 0)
        {
            throw DbException.GetInternalError("unaligned setLength " + Name + " pos " + newLength);
        }

        CheckPowerOff();
        CheckWritingAllowed();
        try
        {
            FileChannel open = Open();
            if (newLength > fileLength)
            {
                // A byte past the end is what makes the file longer; the space in between is
                // zeros, and the reference does the same.
                long pos = filePos;
                open.Position = newLength - 1;
                FileUtils.WriteFully(open, [0]);
                open.Position = pos;
            }
            else
            {
                open.SetLength(newLength);
            }

            fileLength = newLength;
        }
        catch (IOException e)
        {
            CloseFileSilently();
            throw DbException.ConvertIOException(e, Name);
        }
    }

    /// <summary>
    /// How long the file is.
    /// </summary>
    /// <returns>the length in bytes</returns>
    public long Length()
    {
#if DEBUG
        // The reference keeps this behind a Java assertion, so it is here behind DEBUG for the same
        // reason: the length this object believes in must be the length on the disk, or the store is
        // about to write into a page that is not where it thinks it is.
        long len = fileLength;
        try
        {
            len = Open().Length;
            if (len != fileLength)
            {
                throw DbException.GetInternalError(
                    "file " + Name + " length " + len + " expected " + fileLength);
            }

            if (len % Constants.FileBlockSize != 0)
            {
                long newLength = len + Constants.FileBlockSize - (len % Constants.FileBlockSize);
                Open().SetLength(newLength);
                fileLength = newLength;
                throw DbException.GetInternalError("unaligned file length " + Name + " len " + len);
            }
        }
        catch (IOException e)
        {
            throw DbException.ConvertIOException(e, Name);
        }

        return len;
#else
        return fileLength;
#endif
    }

    /// <summary>
    /// Where the file pointer is.
    /// </summary>
    /// <returns>the position in bytes</returns>
    public long GetFilePointer() => filePos;

    /// <summary>
    /// Ask the system to write what has been written. Whether it really has is up to the system and
    /// the hardware.
    /// </summary>
    /// <exception cref="H2SharpException">if it cannot be done</exception>
    public void Sync()
    {
        try
        {
            Open().Flush(flushToDisk: true);
        }
        catch (IOException e)
        {
            CloseFileSilently();
            throw DbException.ConvertIOException(e, Name);
        }
    }

    /// <summary>
    /// Delete the file once nobody is using it any more.
    /// </summary>
    public void AutoDelete()
    {
        if (autoDeleteReference is null)
        {
            autoDeleteReference = handler.GetTempFileDeleter().AddFile(Name, this);
        }
    }

    /// <summary>
    /// Keep the file even though nobody is using it any more.
    /// </summary>
    public void StopAutoDelete()
    {
        handler.GetTempFileDeleter().StopAutoDelete(autoDeleteReference, Name);
        autoDeleteReference = null;
    }

    /// <summary>
    /// Close the file, so that it may be opened again.
    /// </summary>
    public void CloseFile()
    {
        file?.Dispose();
        file = null;
    }

    /// <summary>
    /// Open the file again, with the pointer where it was.
    /// </summary>
    public void OpenFile()
    {
        if (file is null)
        {
            file = FileUtils.Open(Name, mode);
            file.Position = filePos;
        }
    }

    /// <summary>
    /// Take the file, if nobody else has it.
    /// </summary>
    /// <remarks>
    /// The reference takes an operating system lock on the open file. Here the lock is the same
    /// thing the file system can answer: opening it with nobody else allowed to share. When the file
    /// cannot be locked the answer is no, and the store goes on to say the database is in use.
    /// </remarks>
    /// <returns>true if the lock was taken</returns>
    public bool TryLock()
    {
        if (locked)
        {
            return true;
        }

        try
        {
            if (file is null)
            {
                return false;
            }

            FileChannel exclusive = FileUtils.Open(Name, "rws");
            locked = true;
            lockChannel = exclusive;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
            or H2SharpException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Give the file back.
    /// </summary>
    public void ReleaseLock()
    {
        lockChannel?.Dispose();
        lockChannel = null;
        locked = false;
    }

    private FileChannel? lockChannel;

    private bool locked;

    private void CheckWritingAllowed()
    {
        if (checkedWriting)
        {
            handler.CheckWritingAllowed();
        }
    }

    private void CheckPowerOff() => handler.CheckPowerOff();

    private FileChannel Open()
        => file ?? throw DbException.ConvertIOException(new IOException("File is closed: " + Name), Name);

    /// <summary>
    /// Close the file after a write failed, keeping the reference so that nothing later trips over
    /// a null where it did not before.
    /// </summary>
    private void CloseFileSilently()
    {
        try
        {
            file?.Dispose();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // Already broken; the caller is about to be told about the failure that broke it.
        }
    }

    private static byte[] BuildHeader()
    {
        const string text = "-- H2 0.5/B --      ";
        int cut = Math.Min(text.Length, Constants.FileBlockSize - 1);
        return JavaUtf8.GetBytes(text[..cut] + "\n");
    }

    private static bool SameBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }
}
