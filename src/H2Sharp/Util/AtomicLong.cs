using System.Threading;

namespace H2Sharp.Util;

/// <summary>
/// A long that is always read and written atomically, the small corner of
/// <c>java.util.concurrent.atomic.AtomicLong</c> the store actually uses.
/// </summary>
/// <remarks>
/// The reference keeps per-map size statistics in one of these and updates them with a
/// compare-and-set loop, so two threads sampling sizes at once never lose each other's update.
/// </remarks>
internal sealed class AtomicLong
{
    private long value;

    /// <summary>
    /// A zero counter.
    /// </summary>
    internal AtomicLong()
    {
    }

    /// <summary>
    /// The current value.
    /// </summary>
    internal long Get() => Interlocked.Read(ref value);

    /// <summary>
    /// Sets the value.
    /// </summary>
    internal void Set(long newValue) => Interlocked.Exchange(ref value, newValue);

    /// <summary>
    /// Sets the value when it still holds what the caller saw, answering whether it did.
    /// </summary>
    internal bool CompareAndSet(long expected, long updated)
        => Interlocked.CompareExchange(ref value, updated, expected) == expected;

    /// <summary>
    /// Adds one, answering the new value.
    /// </summary>
    internal long IncrementAndGet() => Interlocked.Increment(ref value);

    /// <summary>
    /// Adds the delta, answering the new value.
    /// </summary>
    internal long AddAndGet(long delta) => Interlocked.Add(ref value, delta);
}
