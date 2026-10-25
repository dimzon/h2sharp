/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mvstore.db.Store.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The database storage: the MVStore, its transaction layer, and the open tables.
 *
 * What is left out, and why:
 *
 * * The DbSettings half of the constructor (autoCompactFillRate, compressData,
 *   reuseSpace). The port has no DbSettings table (see docs/APPROVED_DEVIATIONS.md
 *   P-008), so those knobs do not exist; the store is built with the MVStore defaults
 *   for them. When DbSettings arrives, the branches come back here.
 * * The background-exception notification (Database.setBackgroundException,
 *   Utils.isBackgroundThread, shutdownImmediately). The database does not keep a
 *   background exception yet and the store runs no background thread
 *   (autoCommitDisabled), so the handler converts and rethrows as the reference does
 *   after notifying; the notification itself waits for its machinery.
 * * createTable/removeTable/getTable and the temporary-map bookkeeping: the port's DDL
 *   builds MVTable itself, and nothing opens a table by map name yet.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.MvStore.Tx;
using H2Sharp.MvStore.Type;
using H2Sharp.Store.FileSystem;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// A store with open tables.
/// </summary>
public sealed partial class Store : IDisposable
{
    /// <summary>
    /// Convert a password from byte[] to char[], as the reference's decodePassword does.
    /// </summary>
    /// <param name="key">the password as bytes</param>
    /// <returns>the password as characters</returns>
    internal static char[] DecodePassword(byte[] key)
    {
        char[] password = new char[key.Length / 2];
        for (int i = 0; i < password.Length; i++)
        {
            password[i] = (char)(((key[i + i] & 255) << 16) | (key[i + i + 1] & 255));
        }

        return password;
    }

    /// <summary>
    /// The store.
    /// </summary>
    private readonly MVStore mvStore;

    /// <summary>
    /// The transaction store.
    /// </summary>
    private readonly TransactionStore transactionStore;

    private int temporaryMapId;

    /// <summary>Gets a new temporary map name, as Store.nextTemporaryMapName.</summary>
    public string NextTemporaryMapName()
    {
        lock (this) return "temp." + temporaryMapId++;
    }

    private readonly bool encrypted;

    private readonly string? fileName;

    /// <summary>
    /// Creates the store.
    /// </summary>
    /// <param name="db">the database</param>
    /// <param name="key">the file encryption key, or null</param>
    public Store(Database db, byte[]? key)
    {
        string? dbPath = db.GetDatabasePath();
        MVStore.Builder builder = new();
        bool encrypted = false;
        if (dbPath is not null)
        {
            string fileName = dbPath + Constants.SuffixMvFile;
            this.fileName = fileName;
            MVStoreTool.CompactCleanUp(fileName);
            _ = builder.FileName(fileName);
            _ = builder.PageSplitSize(db.GetPageSize());
            if (db.IsReadOnly())
            {
                _ = builder.ReadOnly();
            }
            else
            {
                // possibly create the directory
                bool exists = FileUtils.Exists(fileName);
                if (exists && !FileUtils.CanWrite(fileName))
                {
                    // read only
                }
                else
                {
                    string? dir = FileUtils.GetParent(fileName)?.ToString();
                    FileUtils.CreateDirectories(dir);
                }
            }

            if (key is not null)
            {
                encrypted = true;
                _ = builder.EncryptionKey(DecodePassword(key));
            }

            _ = builder.BackgroundExceptionHandler((t, e) =>
            {
                // Reference: the database is told about the failure and, on a non-background
                // thread, shut down immediately. Neither the database-side hook nor the
                // background-thread check is ported, so the exception is converted and
                // rethrown; the store runs without a background thread here anyway.
                _ = t;
                throw e is MvStoreException storeException
                    ? ConvertMvStoreException(storeException, fileName, encrypted)
                    : DbException.Get(ErrorCode.GeneralError1, e, e.Message);
            });

            // always start without background thread first, and if necessary,
            // it will be set up later, after db has been fully started,
            // otherwise background thread would compete for store lock
            // with maps opening procedure
            _ = builder.AutoCommitDisabled();
        }
        else
        {
            fileName = null;
        }

        this.encrypted = encrypted;
        try
        {
            mvStore = builder.Open();
            mvStore.SetVersionsToKeep(0);
            transactionStore = new TransactionStore(
                mvStore,
                new MetaType<Database>(db, null),
                new ObjectMapDataType<Value>(new ValueDataType(db, db.GetCompareMode(), null, null)),
                db.GetLockTimeout());
        }
        catch (MvStoreException e)
        {
            throw ConvertMvStoreException(e, fileName, encrypted);
        }
    }

    /// <summary>
    /// Converts an MVStore exception to the database-level exception, as the reference's
    /// convertMVStoreException does.
    /// </summary>
    internal static H2SharpException ConvertMvStoreException(
        MvStoreException e, string? fileName, bool encrypted)
    {
        switch (e.GetErrorCode())
        {
            case DataUtils.ErrorClosed:
                return DbException.Get(ErrorCode.DatabaseIsClosed, e, fileName);
            case DataUtils.ErrorUnsupportedFormat:
                return DbException.Get(ErrorCode.FileVersionError1, e, fileName);
            case DataUtils.ErrorFileCorrupt:
                return encrypted
                    ? DbException.Get(ErrorCode.FileEncryptionError1, e, fileName)
                    : DbException.Get(ErrorCode.FileCorrupted1, e, fileName);
            case DataUtils.ErrorFileLocked:
                return DbException.Get(ErrorCode.DatabaseAlreadyOpen1, e, fileName);
            case DataUtils.ErrorReadingFailed:
            case DataUtils.ErrorWritingFailed:
                return DbException.Get(ErrorCode.IoException1, e, fileName);
            default:
                return DbException.Get(ErrorCode.GeneralError1, e, e.Message);
        }
    }

    /// <summary>
    /// Gets the MVStore.
    /// </summary>
    /// <returns>the store</returns>
    public MVStore GetMvStore() => mvStore;

    /// <summary>
    /// Gets the transaction store.
    /// </summary>
    /// <returns>the transaction store</returns>
    public TransactionStore GetTransactionStore() => transactionStore;

    /// <summary>
    /// Flushes the store.
    /// </summary>
    public void Flush() => mvStore.Commit();

    /// <summary>
    /// Closes the store immediately, without compaction.
    /// </summary>
    public void CloseImmediately() => mvStore.CloseImmediately();

    /// <summary>
    /// Closes the store.
    /// </summary>
    /// <param name="allowedCompactionTime">how long compaction may take, or 0 for none</param>
    public void Close(int allowedCompactionTime)
    {
        try
        {
            if (allowedCompactionTime == 0)
            {
                mvStore.CloseImmediately();
            }
            else
            {
                mvStore.Close(allowedCompactionTime);
            }
        }
        catch (MvStoreException e)
        {
            throw ConvertMvStoreException(e, fileName, encrypted);
        }
    }

    /// <summary>
    /// The error for an expression of a type a function cannot take, which names the offending
    /// expression when its type is not even known.
    /// </summary>
    /// <param name="param">the parameter the expression was passed to</param>
    /// <param name="e">the expression</param>
    /// <returns>the error, which is not thrown by this method</returns>
    public static H2SharpException GetInvalidExpressionTypeException(string param, Expression e)
    {
        TypeInfo type = e.GetTypeInfo();
        if (type.GetValueType() == Value.Unknown)
        {
            return DbException.Get(ErrorCode.UnknownDataType1, e.GetTraceSql());
        }

        return DbException.Get(ErrorCode.InvalidValue2, type.GetTraceSql(), param);
    }

    /// <inheritdoc/>
    public void Dispose() => Close(0);
}
