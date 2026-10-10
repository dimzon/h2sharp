using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace H2Sharp.MvStore;

/// <summary>
/// One version of a map's root: which page is the root, at which version, and who holds it.
/// </summary>
/// <remarks>
/// The root of a map is never mutated. Every change — a new page, a lock, a version bump — makes a
/// new one of these and swaps it in with a compare-and-set, and a swap that loses the race is simply
/// retried by the caller. That is the whole concurrency contract of the store: the data structure
/// under the reference never moves, only the reference to it does.
/// <para>
/// Holding is reentrant per thread and counted, so a thread that already holds the root can take it
/// again, and unlocking hands it back one hold at a time. The owner is a thread id and zero means
/// nobody, which is why a freshly made reference belongs to no one and a locked one belongs to
/// exactly one thread.
/// </para>
/// <para>
/// Versions form a chain through <c>previous</c>. A reference for version X is the last known root
/// for X and therefore the first known root for X+1, and dropping old versions walks the chain
/// cutting it behind the oldest version anyone still needs — but keeps one previous version past
/// that, because retaining the history of a version needs the last root of the one before it.
/// </para>
/// </remarks>
/// <typeparam name="K">the key type</typeparam>
/// <typeparam name="V">the value type</typeparam>
public sealed class RootReference<K, V> : IRootView
{
    /// <summary>
    /// The root page of this version.
    /// </summary>
    public readonly Page<K, V> root;

    /// <summary>
    /// Which version this root belongs to.
    /// </summary>
    public readonly long version;

    private readonly byte holdCount;

    private readonly long ownerId;

    internal volatile RootReference<K, V>? previous;

    internal readonly long updateCounter;

    internal readonly long updateAttemptCounter;

    private readonly byte appendCounter;

    /// <summary>
    /// A first root, and a read-only snapshot: owned by nobody.
    /// </summary>
    internal RootReference(Page<K, V> root, long version)
    {
        this.root = root;
        this.version = version;
        previous = null;
        updateCounter = 1;
        updateAttemptCounter = 1;
        holdCount = 0;
        ownerId = 0;
        appendCounter = 0;
    }

    private RootReference(RootReference<K, V> source, Page<K, V> root, long updateAttemptCounter)
    {
        this.root = root;
        version = source.version;
        previous = source.previous;
        updateCounter = source.updateCounter + 1;
        this.updateAttemptCounter = source.updateAttemptCounter + updateAttemptCounter;
        holdCount = 0;
        ownerId = 0;
        appendCounter = source.appendCounter;
    }

    /// <summary>
    /// A locked root, held once more by the current thread.
    /// </summary>
    private RootReference(RootReference<K, V> source, int attempt)
    {
        root = source.root;
        version = source.version;
        previous = source.previous;
        updateCounter = source.updateCounter + 1;
        updateAttemptCounter = source.updateAttemptCounter + attempt;
        Debug.Assert(source.holdCount == 0 || source.ownerId == Environment.CurrentManagedThreadId,
            Environment.CurrentManagedThreadId + " " + source);
        holdCount = (byte)(source.holdCount + 1);
        ownerId = Environment.CurrentManagedThreadId;
        appendCounter = source.appendCounter;
    }

    /// <summary>
    /// An unlocked root, holding once less.
    /// </summary>
    private RootReference(RootReference<K, V> source, Page<K, V> root, bool keepLocked, int appendCounter)
    {
        this.root = root;
        version = source.version;
        previous = source.previous;
        updateCounter = source.updateCounter;
        updateAttemptCounter = source.updateAttemptCounter;
        Debug.Assert(source.holdCount > 0 && source.ownerId == Environment.CurrentManagedThreadId,
            Environment.CurrentManagedThreadId + " " + source);
        holdCount = (byte)(source.holdCount - (keepLocked ? 0 : 1));
        ownerId = holdCount == 0 ? 0 : Environment.CurrentManagedThreadId;
        this.appendCounter = (byte)appendCounter;
    }

    /// <summary>
    /// A root moved to another version, keeping the chain behind it.
    /// </summary>
    private RootReference(RootReference<K, V> source, long version, int attempt)
    {
        RootReference<K, V>? prev = source;
        RootReference<K, V>? tmp;
        while ((tmp = prev.previous) is not null && ReferenceEquals(tmp.root, source.root))
        {
            prev = tmp;
        }

        root = source.root;
        this.version = version;
        previous = prev;
        updateCounter = source.updateCounter + 1;
        updateAttemptCounter = source.updateAttemptCounter + attempt;
        holdCount = source.holdCount == 0 ? (byte)0 : (byte)(source.holdCount - 1);
        ownerId = holdCount == 0 ? 0 : source.ownerId;
        Debug.Assert(source.appendCounter == 0);
        appendCounter = 0;
    }

    /// <summary>
    /// The same version over a new root page, when nobody holds it.
    /// </summary>
    internal RootReference<K, V>? UpdateRootPage(Page<K, V> newRootPage, long attemptCounter)
        => IsFree() ? TryUpdate(new RootReference<K, V>(this, newRootPage, attemptCounter)) : null;

    /// <summary>
    /// Takes the lock for the current thread, when nobody else holds it.
    /// </summary>
    internal RootReference<K, V>? TryLock(int attemptCounter)
        => CanUpdate() ? TryUpdate(new RootReference<K, V>(this, attemptCounter)) : null;

    /// <summary>
    /// Unlocks and moves to another version at once.
    /// </summary>
    internal RootReference<K, V>? TryUnlockAndUpdateVersion(long version, int attempt)
        => CanUpdate() ? TryUpdate(new RootReference<K, V>(this, version, attempt)) : null;

    /// <summary>
    /// Swaps the page and the lock state at once.
    /// </summary>
    internal RootReference<K, V>? UpdatePageAndLockedStatus(Page<K, V> page, bool keepLocked, int appendCounter)
        => CanUpdate() ? TryUpdate(new RootReference<K, V>(this, page, keepLocked, appendCounter)) : null;

    /// <summary>
    /// Cuts the chain behind the oldest version anyone still needs, keeping one previous version past
    /// it so that the history of a kept version stays reachable.
    /// </summary>
    internal void RemoveUnusedOldVersions(long oldestVersionToKeep)
    {
        // We need to keep at least one previous version (if any) here,
        // because in order to retain whole history of some version
        // we really need last root of the previous version.
        // Root labeled with version "X" is the LAST known root for that version
        // and therefore the FIRST known root for the version "X+1"
        for (RootReference<K, V>? rootRef = this; rootRef is not null; rootRef = rootRef.previous)
        {
            if (rootRef.version < oldestVersionToKeep)
            {
                RootReference<K, V>? prev = rootRef.previous;
                Debug.Assert(prev is null || prev.GetAppendCounter() == 0,
                    oldestVersionToKeep + " " + rootRef.previous);
                rootRef.previous = null;
            }
        }
    }

    /// <summary>
    /// Whether anyone holds this root.
    /// </summary>
    internal bool IsLocked() => holdCount != 0;

    private bool IsFree() => holdCount == 0;

    private bool CanUpdate() => IsFree() || ownerId == Environment.CurrentManagedThreadId;

    /// <summary>
    /// Whether the current thread holds this root.
    /// </summary>
    public bool IsLockedByCurrentThread()
        => holdCount != 0 && ownerId == Environment.CurrentManagedThreadId;

    private RootReference<K, V>? TryUpdate(RootReference<K, V> updated)
    {
        Debug.Assert(CanUpdate());
        return root.map.CompareAndSetRoot(this, updated) ? updated : null;
    }

    /// <summary>
    /// Which version this reference effectively answers for, walking back over appends.
    /// </summary>
    internal long GetVersion()
    {
        RootReference<K, V>? prev = previous;
        return prev is null || !ReferenceEquals(prev.root, root) || prev.appendCounter != appendCounter
            ? version
            : prev.GetVersion();
    }

    /// <summary>
    /// Whether anything changed since a version.
    /// </summary>
    internal bool HasChangesSince(long version, bool persistent)
        => (persistent && (root.IsSaved() ? GetAppendCounter() > 0 : GetTotalCount() > 0))
            || GetVersion() > version;

    /// <summary>
    /// How many appends happened under this reference.
    /// </summary>
    internal int GetAppendCounter() => appendCounter & 0xff;

    /// <summary>
    /// Whether there are appends waiting to be flushed.
    /// </summary>
    public bool NeedFlush() => appendCounter != 0;

    /// <summary>
    /// How many values live under this root, appends included.
    /// </summary>
    public long GetTotalCount() => root.GetTotalCount() + GetAppendCounter();

    /// <inheritdoc/>
    public override string ToString()
        => "RootReference(" + RuntimeHelpers.GetHashCode(root)
            + ", v=" + version
            + ", owner=" + ownerId + (ownerId == Environment.CurrentManagedThreadId ? "(current)" : "")
            + ", holdCnt=" + holdCount
            + ", keys=" + root.GetTotalCount()
            + ", append=" + GetAppendCounter()
            + ")";

    /// <inheritdoc/>
    long IRootView.Version => version;

    /// <inheritdoc/>
    object IRootView.RootPage => root;
}
