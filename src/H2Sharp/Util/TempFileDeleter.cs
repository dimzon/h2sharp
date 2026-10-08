/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.util.TempFileDeleter.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: the reference watches an object with a phantom reference, and the file goes when the
 * object is collected. .NET has no phantom reference and no queue to empty — what it has is a
 * finalizer, and a finalizer runs when its own object becomes unreachable. So the object that
 * carries the finalizer has to become unreachable at the same moment the watched object does, and
 * the only structure that guarantees that is a ConditionalWeakTable: it holds the watcher only as
 * long as the watched one is there.
 *
 * A holder kept in a list of its own would never be finalized, because the list would keep it alive
 * for as long as the process lived. That is the whole difference between this port and a
 * translation of the reference, and it is why the list here is weak.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Store.FileSystem;

namespace H2Sharp.Util;

/// <summary>
/// Deletes temporary files and closes temporary things when whatever made them is gone.
/// </summary>
/// <remarks>
/// A store opens a file, works with it, and lets it go: the file is temporary and nobody is going to
/// say when it may be deleted, so it is deleted when the thing that opened it becomes unreachable.
/// </remarks>
public sealed class TempFileDeleter
{
    /// <summary>
    /// What is being watched, and what to do when it is not there any more.
    /// </summary>
    private static readonly ConditionalWeakTable<object, Holder> Watching = [];

    /// <summary>
    /// Every holder, weakly, so that all of them can be cleaned up on request without being kept
    /// alive in the meantime.
    /// </summary>
    private readonly List<WeakReference<Holder>> all = [];

    private readonly object sync = new();

    private TempFileDeleter()
    {
    }

    /// <summary>
    /// A deleter of its own. The reference returns a new one every time, because what it keeps is
    /// the list of what is being watched and nobody may share that; here the table is shared on
    /// purpose, because a resource may only be watched once by anyone.
    /// </summary>
    /// <returns>the deleter</returns>
    public static TempFileDeleter GetInstance() => new();

    /// <summary>
    /// Delete a file, or close something, once whatever is being watched is gone.
    /// </summary>
    /// <param name="resource">
    /// the name of the file, or something to close when the time comes
    /// </param>
    /// <param name="monitor">what to watch; the resource goes when this becomes unreachable</param>
    /// <returns>a token to stop watching, or to delete it now</returns>
    /// <exception cref="H2SharpException">if the resource is neither a name nor something closable</exception>
    public object AddFile(object resource, object monitor)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(monitor);

        Holder holder = resource switch
        {
            string => new Holder(this, resource),
            IDisposable => new Holder(this, resource),
            _ => throw DbException.GetUnsupportedException("Unsupported resource " + resource),
        };

        // The table keeps the holder only while the watched object is alive, which is the whole
        // mechanism: when the object goes, the holder goes with it and its finalizer runs.
        Watching.AddOrUpdate(monitor, holder);
        lock (sync)
        {
            all.Add(new WeakReference<Holder>(holder));
        }

        return holder;
    }

    /// <summary>
    /// Delete a file, or close something, now.
    /// </summary>
    /// <param name="token">
    /// what <see cref="AddFile(object, object)"/> returned, or null if there is nothing registered
    /// </param>
    /// <param name="resource">the file name or the thing to close</param>
    public void DeleteFile(object? token, object? resource)
    {
        if (token is Holder holder)
        {
            resource = holder.Resource;
        }

        switch (resource)
        {
            case string fileName:
                DeleteFileByName(fileName);
                break;

            case IDisposable closable:
                CloseQuietly(closable);
                break;
        }

        if (token is Holder stopped)
        {
            // Done now, and once for all: the finalizer must not do it a second time.
            GC.SuppressFinalize(stopped);
        }
    }

    /// <summary>
    /// Delete everything that is registered.
    /// </summary>
    public void DeleteAll()
    {
        WeakReference<Holder>[] snapshot;
        lock (sync)
        {
            snapshot = [.. all];
        }

        foreach (WeakReference<Holder> weak in snapshot)
        {
            if (weak.TryGetTarget(out Holder? holder))
            {
                DeleteFile(holder, holder.Resource);
            }
        }
    }

    /// <summary>
    /// Stop watching, so the resource outlives whatever was being watched.
    /// </summary>
    /// <param name="token">what <see cref="AddFile(object, object)"/> returned</param>
    /// <param name="resource">the file name or the thing, which must be the one that was registered</param>
    public void StopAutoDelete(object? token, object resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (token is not Holder holder)
        {
            return;
        }

        if (SysProperties.Check && !Equals(holder.Resource, resource))
        {
            throw DbException.GetInternalError("f2:" + holder.Resource + " f:" + resource);
        }

        // A suppressed finalizer never runs, whether or not the holder is collected afterwards.
        GC.SuppressFinalize(holder);
    }

    private static void DeleteFileByName(string fileName)
    {
        FilePath path = FilePath.Get(fileName);
        if (!path.Exists())
        {
            return;
        }

        try
        {
            path.Delete();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SystemException)
        {
            // A temporary file that will not go is not worth an error: the process is ending, and the
            // directory it was in may already be gone.
        }
    }

    private static void CloseQuietly(IDisposable closable)
    {
        try
        {
            closable.Dispose();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // Closing is the last thing that happens and there is nothing left to spoil.
        }
    }

    /// <summary>
    /// One watched thing: what to delete once what is being watched is gone.
    /// </summary>
    private sealed class Holder
    {
        private readonly TempFileDeleter deleter;

        internal Holder(TempFileDeleter deleter, object resource)
        {
            this.deleter = deleter;
            Resource = resource;
        }

        internal object Resource { get; }

        ~Holder()
        {
            DeleteFile(Resource);

            lock (deleter.sync)
            {
                Holder self = this;
                _ = deleter.all.RemoveAll(weak =>
                    weak.TryGetTarget(out Holder? target) && ReferenceEquals(target, self));
            }
        }

        private void DeleteFile(object resource)
        {
            switch (resource)
            {
                case string fileName:
                    DeleteFileByName(fileName);
                    break;

                case IDisposable closable:
                    CloseQuietly(closable);
                    break;
            }
        }
    }
}
