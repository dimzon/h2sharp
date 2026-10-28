/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mvstore.db.MVTempResult.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A temporary result stored in its own MVStore.
 *
 * The store lives in a temporary file, and the file is removed when this result and all its
 * copies are closed. TempFileDeleter also removes it if a result is not closed properly.
 */
using System;
using System.Collections.Generic;
using System.IO;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.MvStore;
using H2Sharp.Result;
using H2Sharp.Store.FileSystem;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// Temporary result.
/// </summary>
internal abstract class MVTempResult : ResultExternal
{
    private sealed class CloseImpl : IDisposable
    {
        private readonly MVStore store;

        private readonly string fileName;

        internal CloseImpl(MVStore store, string fileName)
        {
            this.store = store;
            this.fileName = fileName;
        }

        public void Dispose()
        {
            store.CloseImmediately();
            try
            {
                FilePath.Get(fileName).Delete();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The process may already be tearing down the temporary directory.
            }
        }
    }

    /// <summary>
    /// Creates an MVStore-based temporary result.
    /// </summary>
    /// <param name="database">the database</param>
    /// <param name="expressions">the expressions</param>
    /// <param name="distinct">whether the result is DISTINCT</param>
    /// <param name="distinctIndexes">indexes of distinct columns for DISTINCT ON results</param>
    /// <param name="visibleColumnCount">count of visible columns</param>
    /// <param name="resultColumnCount">the number of columns including virtual columns</param>
    /// <param name="sort">the sort order, or null</param>
    /// <returns>the temporary result</returns>
    public static ResultExternal Of(
        Database database,
        Expression[] expressions,
        bool distinct,
        int[]? distinctIndexes,
        int visibleColumnCount,
        int resultColumnCount,
        SortOrder? sort)
    {
        return distinct || distinctIndexes is not null || sort is not null
            ? throw new TODOException("MVTempResult.of: MVSortedTempResult is not ported")
            : new MVPlainTempResult(database, expressions, visibleColumnCount, resultColumnCount);
    }

    /// <summary>
    /// The database.
    /// </summary>
    protected readonly Database database;

    /// <summary>
    /// The temporary store.
    /// </summary>
    internal readonly MVStore store;

    /// <summary>
    /// The column expressions.
    /// </summary>
    protected readonly Expression[] expressions;

    /// <summary>
    /// Count of visible columns.
    /// </summary>
    protected readonly int visibleColumnCount;

    /// <summary>
    /// Total count of columns.
    /// </summary>
    protected readonly int resultColumnCount;

    /// <summary>
    /// Count of rows. Used only in root results; copies always have 0.
    /// </summary>
    protected int rowCount;

    /// <summary>
    /// The parent store for copies, or null for a root result.
    /// </summary>
    protected readonly MVTempResult? parent;

    /// <summary>
    /// Count of child results.
    /// </summary>
    protected int childCount;

    /// <summary>
    /// Whether this result is closed.
    /// </summary>
    protected bool closed;

    private readonly TempFileDeleter? tempFileDeleter;

    private readonly CloseImpl? closeable;

    private readonly object? fileRef;

    /// <summary>
    /// Creates a shallow copy of the result.
    /// </summary>
    /// <param name="parent">the parent result</param>
    protected MVTempResult(MVTempResult parent)
    {
        this.parent = parent;
        database = parent.database;
        store = parent.store;
        expressions = parent.expressions;
        visibleColumnCount = parent.visibleColumnCount;
        resultColumnCount = parent.resultColumnCount;
        tempFileDeleter = null;
        closeable = null;
        fileRef = null;
    }

    /// <summary>
    /// Creates a new temporary result.
    /// </summary>
    /// <param name="database">the database</param>
    /// <param name="expressions">the column expressions</param>
    /// <param name="visibleColumnCount">count of visible columns</param>
    /// <param name="resultColumnCount">total count of columns</param>
    protected MVTempResult(
        Database database,
        Expression[] expressions,
        int visibleColumnCount,
        int resultColumnCount)
    {
        this.database = database;
        try
        {
            string fileName = FileUtils.CreateTempFile("h2tmp", Constants.SuffixTempFile, true);
            IFileStore fileStore = database.GetStore().GetMvStore().GetFileStore()
                ?? throw new TODOException("MVTempResult: the database has no file store");
            MVStore.Builder builder = new MVStore.Builder()
                .AdoptFileStore(fileStore.Open(fileName, false))
                .CacheSize(0)
                .AutoCommitDisabled();
            store = builder.Open();
            this.expressions = expressions;
            this.visibleColumnCount = visibleColumnCount;
            this.resultColumnCount = resultColumnCount;
            tempFileDeleter = database.GetTempFileDeleter();
            closeable = new CloseImpl(store, fileName);
            fileRef = tempFileDeleter.AddFile(closeable, this);
        }
        catch (IOException e)
        {
            throw DbException.Convert(e);
        }

        parent = null;
    }

    /// <inheritdoc/>
    public int AddRows(IEnumerable<Value?[]> rows)
    {
        foreach (Value?[] row in rows)
        {
            _ = AddRow(row);
        }

        return rowCount;
    }

    /// <inheritdoc/>
    public void Close()
    {
        lock (this)
        {
            if (closed)
            {
                return;
            }

            closed = true;
            if (parent is not null)
            {
                parent.CloseChild();
            }
            else if (childCount == 0)
            {
                Delete();
            }
        }
    }

    private void CloseChild()
    {
        lock (this)
        {
            if (--childCount == 0 && closed)
            {
                Delete();
            }
        }
    }

    private void Delete()
    {
        tempFileDeleter?.DeleteFile(fileRef, closeable);
    }

    /// <inheritdoc/>
    public abstract void Reset();

    /// <inheritdoc/>
    public abstract Value?[] Next();

    /// <inheritdoc/>
    public abstract int AddRow(Value?[] values);

    /// <inheritdoc/>
    public abstract int RemoveRow(Value?[] values);

    /// <inheritdoc/>
    public abstract bool Contains(Value?[] values);

    /// <inheritdoc/>
    public abstract ResultExternal? CreateShallowCopy();
}
