/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A LOB whose bytes live in a temporary file. The reference keeps four LOB locations;
 * of the file-backed ones this is the simplest: a temp file and a length. It is what
 * lets a parameter stream larger than memory (for example a BLOB parameter) flow into
 * the database without ever being copied whole into a byte array.
 */
using System;
using System.IO;
using H2Sharp.Util;

namespace H2Sharp.Values.Lobs;

/// <summary>
/// A LOB stored in a temporary file.
/// </summary>
public sealed class LobDataFile : LobData
{
    private readonly string fileName;

    private readonly object deletionToken;

    /// <summary>
    /// Creates a file-backed LOB.
    /// </summary>
    /// <param name="fileName">the temporary file holding the bytes</param>
    /// <param name="octetLength">how many bytes it holds</param>
    public LobDataFile(string fileName, long octetLength)
    {
        this.fileName = fileName;
        OctetLength = octetLength;
        // The file goes away with this object: the deleter watches the object itself.
        deletionToken = TempFileDeleter.GetInstance().AddFile(fileName, this);
    }

    /// <summary>
    /// How many bytes the file holds.
    /// </summary>
    public long OctetLength { get; }

    /// <summary>Gets the path of the temporary file.</summary>
    internal string FileName => fileName;

    /// <inheritdoc/>
    public override Stream GetInputStream(long precision)
        => new FileStream(
            fileName, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: false);

    /// <inheritdoc/>
    public override void Remove(ValueLob value)
        => TempFileDeleter.GetInstance().DeleteFile(deletionToken, fileName);
}
