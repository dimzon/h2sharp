/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.util.Utils.
 * Licensed under the MPL 2.0 or the EPL 2.0 (see LICENSE-H2.txt).
 *
 * Modified: the following Java-only members are intentionally NOT ported and are
 * listed here so their absence is not mistaken for an oversight:
 *   - getMemoryUsed/getMemoryFree/getMemoryMax/collectGarbage:
 *     Runtime free/total/max memory has no .NET equivalent. GC.GetTotalMemory and
 *     GC.GetGCMemoryInfo are used instead where a value is genuinely needed.
 *   - getGarbageCollectionTime/getGarbageCollectionCount: GC.GetGCMemoryInfo is used.
 *   - getResource/loadResource: reads org.h2.util/data.zip, a build artifact of
 *     the Java module. H2Sharp embeds its resources directly instead.
 *   - callStaticMethod/callMethod/newInstance/getNonPrimitiveClass/match:
 *     reflective Java interop used by the OSGi activator and SourceCompiler;
 *     H2Sharp has no equivalent entry points.
 *   - H2_THREAD_GROUP / createBackgroundThread / isBackgroundThread:
 *     System.Threading.Thread lacks an addressable ThreadGroup. H2Sharp uses
 *     dedicated background Task instances; see H2Sharp.Util.BackgroundTaskQueue.
 *   - ClassFactory interface: OSGi-only.
 *
 * ThreadPoolExecutor-based helpers are ported to ThreadPoolExecutor equivalents.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace H2Sharp.Util;

/// <summary>
/// This utility class contains miscellaneous functions.
/// </summary>
public static class Utils
{
    /// <summary>
    /// A 0-size byte array.
    /// </summary>
    public static readonly byte[] EmptyBytes = [];

    /// <summary>
    /// A 0-size int array.
    /// </summary>
    public static readonly int[] EmptyIntArray = [];

    /// <summary>
    /// Calculate the index of the first occurrence of the pattern in the byte array,
    /// starting with the given index. This method returns -1 if the pattern has not
    /// been found, and the start position if the pattern is empty.
    /// </summary>
    /// <param name="bytes">the byte array</param>
    /// <param name="pattern">the pattern</param>
    /// <param name="start">the start index from where to search</param>
    /// <returns>the index</returns>
    public static int IndexOf(byte[] bytes, byte[] pattern, int start)
    {
        if (pattern.Length == 0)
        {
            return start;
        }

        if (start > bytes.Length)
        {
            return -1;
        }

        // One vectorized search instead of a slice plus compare per position: same first match.
        int found = bytes.AsSpan(start).IndexOf(pattern);
        return found < 0 ? -1 : start + found;
    }

    /// <summary>
    /// Calculate the hash code of the given byte array.
    /// </summary>
    /// <param name="value">the byte array</param>
    /// <returns>the hash code</returns>
    public static int GetByteArrayHash(byte[] value)
    {
        int len = value.Length;
        int h = len;
        if (len < 50)
        {
            for (int i = 0; i < len; i++)
            {
                h = 31 * h + value[i];
            }
        }
        else
        {
            int step = len / 16;
            for (int i = 0; i < 4; i++)
            {
                h = 31 * h + value[i];
                h = 31 * h + value[--len];
            }

            for (int i = 4 + step; i < len; i += step)
            {
                h = 31 * h + value[i];
            }
        }

        return h;
    }

    /// <summary>
    /// Compare two byte arrays. This method will always loop over all bytes and doesn't
    /// use conditional operations in the loop to make sure an attacker can not use a
    /// timing attack when trying out passwords.
    /// </summary>
    /// <param name="test">the first array</param>
    /// <param name="good">the second array</param>
    /// <returns>true if both byte arrays contain the same bytes</returns>
    public static bool CompareSecure(byte[]? test, byte[]? good)
    {
        if (test is null || good is null)
        {
            return test is null && good is null;
        }

        int len = test.Length;
        if (len != good.Length)
        {
            return false;
        }

        if (len == 0)
        {
            return true;
        }

        // don't use conditional operations inside the loop
        int bits = 0;
        for (int i = 0; i < len; i++)
        {
            // this will never reset any bits
            bits |= test[i] ^ good[i];
        }

        return bits == 0;
    }

    /// <summary>
    /// Copy the contents of the source array to the target array. If the size of the
    /// target array is too small, a larger array is created.
    /// </summary>
    /// <param name="source">the source array</param>
    /// <param name="target">the target array</param>
    /// <returns>the target array or a new one if the target array was too small</returns>
    public static byte[] Copy(byte[] source, byte[] target)
    {
        int len = source.Length;
        if (len > target.Length)
        {
            target = new byte[len];
        }

        Array.Copy(source, 0, target, 0, len);
        return target;
    }

    /// <summary>
    /// Create an array of bytes with the given size.
    /// </summary>
    /// <param name="len">the number of bytes requested</param>
    /// <returns>the byte array</returns>
    /// <exception cref="OutOfMemoryException">if the allocation was too large</exception>
    public static byte[] NewBytes(int len)
    {
        if (len == 0)
        {
            return EmptyBytes;
        }

        try
        {
            return new byte[len];
        }
        catch (OutOfMemoryException e)
        {
            throw new OutOfMemoryException("Requested memory: " + len, e);
        }
    }

    /// <summary>
    /// Creates a copy of array of bytes with the new size.
    /// </summary>
    /// <param name="bytes">source array</param>
    /// <param name="len">the number of bytes in the new array</param>
    /// <returns>the byte array</returns>
    /// <exception cref="OutOfMemoryException">if the allocation was too large</exception>
    public static byte[] CopyBytes(byte[] bytes, int len)
    {
        if (len == 0)
        {
            return EmptyBytes;
        }

        try
        {
            if (bytes.Length == len)
            {
                // Same copy without zeroing first: every byte is overwritten below.
                byte[] exact = GC.AllocateUninitializedArray<byte>(len);
                bytes.AsSpan().CopyTo(exact);
                return exact;
            }

            byte[] copy = new byte[len];
            Array.Copy(bytes, 0, copy, 0, Math.Min(bytes.Length, len));
            return copy;
        }
        catch (OutOfMemoryException e)
        {
            throw new OutOfMemoryException("Requested memory: " + len, e);
        }
    }

    /// <summary>
    /// Create a new byte array and copy all the data. If the size of the byte array is
    /// zero, the same empty array is returned.
    /// </summary>
    /// <remarks>
    /// The platform's <c>Array.Clone</c> is not used: it goes through a path that knows nothing
    /// about the element type, and it measured 26 ns for a three byte array where an uninitialised
    /// array and a span copy take two. This is on the way to every value of the byte array family,
    /// so the difference is not a detail.
    /// </remarks>
    /// <param name="b">the byte array (may not be null)</param>
    /// <returns>a new byte array</returns>
    public static byte[] CloneByteArray(byte[]? b)
    {
        if (b is null)
        {
            return null!;
        }

        int len = b.Length;
        if (len == 0)
        {
            return EmptyBytes;
        }

        byte[] copy = GC.AllocateUninitializedArray<byte>(len);
        b.AsSpan().CopyTo(copy);
        return copy;
    }

    /// <summary>
    /// Get the used memory in KB.
    /// </summary>
    /// <returns>the used memory</returns>
    public static long GetMemoryUsed()
    {
        CollectGarbage();
        return GC.GetTotalMemory(forceFullCollection: false) >> 10;
    }

    /// <summary>
    /// Get the maximum memory in KB.
    /// </summary>
    /// <returns>the maximum memory</returns>
    public static long GetMemoryMax()
    {
        GC.GetGCMemoryInfo();
        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes >> 10;
    }

    /// <summary>
    /// Get the total time spent in garbage collection, in milliseconds.
    /// </summary>
    /// <returns>the total GC time</returns>
    public static long GetGarbageCollectionTime()
    {
        long total = 0;
        foreach (TimeSpan duration in GC.GetGCMemoryInfo().PauseDurations)
        {
            total += (long)duration.TotalMilliseconds;
        }

        return total;
    }

    /// <summary>
    /// Run garbage collection.
    /// </summary>
    public static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// Create a new list with an initial capacity of 4.
    /// </summary>
    /// <typeparam name="T">the type</typeparam>
    /// <returns>the list</returns>
    public static List<T> NewSmallArrayList<T>() => new(4);

    /// <summary>
    /// Find the top limit values using the given comparer and place them as in a full
    /// array sort, in descending order.
    /// </summary>
    /// <typeparam name="X">the type of elements</typeparam>
    /// <param name="array">the array</param>
    /// <param name="fromInclusive">the start index, inclusive</param>
    /// <param name="toExclusive">the end index, exclusive</param>
    /// <param name="comp">the comparer</param>
    public static void SortTopN<X>(X[] array, int fromInclusive, int toExclusive, Comparison<X> comp)
    {
        int highInclusive = array.Length - 1;
        if (highInclusive > 0 && toExclusive > fromInclusive)
        {
            PartialQuickSort(array, 0, highInclusive, comp, fromInclusive, toExclusive - 1);
            // An index and a length: Java sorts from one index to another, and passing the end
            // as a length sorted past it. .NET has no ranged sort taking a Comparison, so the
            // wrapper stays; the range is what got fixed.
            Array.Sort(array, fromInclusive, toExclusive - fromInclusive, Comparer<X>.Create(comp));
        }
    }

    /// <summary>
    /// Partial quick sort.
    /// Works with elements from <paramref name="low"/> to <paramref name="high"/> indexes,
    /// inclusive. Moves smallest elements to low..start-1 positions and largest elements
    /// to end+1..high positions. Middle elements are placed into start..end positions.
    /// </summary>
    /// <typeparam name="X">the type of elements</typeparam>
    /// <param name="array">the array to sort</param>
    /// <param name="low">the lower index with data, inclusive</param>
    /// <param name="high">the higher index with data, inclusive, high &gt; low</param>
    /// <param name="comp">the comparer</param>
    /// <param name="start">the start index of requested region, inclusive</param>
    /// <param name="end">the end index of requested region, inclusive, end &gt;= start</param>
    private static void PartialQuickSort<X>(
        X[] array, int low, int high, Comparison<X> comp, int start, int end)
    {
        if (low >= start && high <= end)
        {
            // Don't sort blocks entirely contained in the middle region
            return;
        }

        int i = low, j = high;

        // use a random pivot to protect against the worst case order
        int p = low + MathUtils.RandomInt(high - low);
        X pivot = array[p];
        int m = (low + high) >>> 1;
        X temp = array[m];
        array[m] = pivot;
        array[p] = temp;

        while (i <= j)
        {
            while (comp(array[i], pivot) < 0)
            {
                i++;
            }

            while (comp(array[j], pivot) > 0)
            {
                j--;
            }

            if (i <= j)
            {
                temp = array[i];
                array[i++] = array[j];
                array[j--] = temp;
            }
        }

        if (low < j && /* Intersection with middle region */ start <= j)
        {
            PartialQuickSort(array, low, j, comp, start, end);
        }

        if (i < high && /* Intersection with middle region */ i <= end)
        {
            PartialQuickSort(array, i, high, comp, start, end);
        }
    }

    /// <summary>
    /// Parses the specified string to a boolean value.
    /// </summary>
    /// <param name="value">string to parse</param>
    /// <param name="defaultValue">value to return if value is null or on parsing error</param>
    /// <param name="throwException">throw exception on parsing error or return the default
    /// value instead</param>
    /// <returns>parsed or default value</returns>
    /// <exception cref="ArgumentException">on parsing error if
    /// <paramref name="throwException"/> is true</exception>
    public static bool ParseBoolean(string? value, bool defaultValue, bool throwException)
    {
        if (value is null)
        {
            return defaultValue;
        }

        switch (value.Length)
        {
            case 1:
                if (value is "1" || string.Equals(value, "t", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "y", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (value is "0" || string.Equals(value, "f", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "n", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                break;

            case 2:
                if (string.Equals(value, "no", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                break;

            case 3:
                if (string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                break;

            case 4:
                if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                break;

            case 5:
                if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                break;
        }

        if (throwException)
        {
            throw new ArgumentException(value);
        }

        return defaultValue;
    }

    /// <summary>
    /// Get the system property. If the system property is not set, the default value
    /// is returned.
    /// </summary>
    /// <param name="key">the key</param>
    /// <param name="defaultValue">the default value</param>
    /// <returns>the value</returns>
    public static string? GetProperty(string key, string? defaultValue)
    {
        try
        {
            return Environment.GetEnvironmentVariable(key) ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Get the system property. If the system property is not set, or cannot be parsed,
    /// the default value is returned.
    /// </summary>
    /// <param name="key">the key</param>
    /// <param name="defaultValue">the default value</param>
    /// <returns>the value</returns>
    public static int GetProperty(string key, int defaultValue)
    {
        string? s = GetProperty(key, null);
        return s is not null && TryParseInt(s, out int value) ? value : defaultValue;
    }

    /// <summary>
    /// Get the system property as a non-nullable string. Mirrors
    /// <c>Utils.getProperty(String, String)</c> for the common case where the default
    /// is a literal, so callers do not need a null-forgiving operator.
    /// </summary>
    /// <param name="key">the key</param>
    /// <param name="defaultValue">the default value; must not be null</param>
    /// <returns>the value, or <paramref name="defaultValue"/></returns>
    public static string GetPropertyOrDefault(string key, string defaultValue)
    {
        if (defaultValue is null)
        {
            throw new ArgumentNullException(nameof(defaultValue));
        }

        return GetProperty(key, (string?)defaultValue) ?? defaultValue;
    }

    /// <summary>
    /// Get the system property as a 64-bit integer. Java has no long overload of
    /// <c>Integer.decode</c>, so H2 assigns an int result to a long constant; this
    /// overload reproduces that behaviour.
    /// </summary>
    /// <param name="key">the key</param>
    /// <param name="defaultValue">the default value</param>
    /// <returns>the value</returns>
    public static long GetProperty(string key, long defaultValue)
    {
        string? s = GetProperty(key, null);
        return s is not null && TryParseInt(s, out int value) ? value : defaultValue;
    }

    /// <summary>
    /// Parses an integer using java.lang.Integer.decode syntax: optional
    /// <c>0x</c>/<c>0X</c> or <c>#</c> prefix for hexadecimal, a leading <c>0</c>
    /// (with at least one following digit) for octal, otherwise decimal.
    /// </summary>
    /// <param name="s">the text to parse</param>
    /// <param name="value">the parsed value on success</param>
    /// <returns>true if parsing succeeded</returns>
    public static bool TryParseInt(string s, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        try
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                value = int.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return true;
            }

            if (s.StartsWith('#'))
            {
                value = int.Parse(s[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return true;
            }

            if (s.Length > 1 && s[0] == '0')
            {
                value = Convert.ToInt32(s[1..], 8);
                return true;
            }

            // Java Integer.parseInt does not tolerate surrounding whitespace, so
            // NumberStyles.AllowLeadingSign (not NumberStyles.Integer) is used here.
            value = int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        {
            value = 0;
            return false;
        }
    }

    /// <summary>
    /// Get the system property as a boolean. If the system property is not set, the
    /// default value is returned.
    /// </summary>
    /// <param name="key">the key</param>
    /// <param name="defaultValue">the default value</param>
    /// <returns>the value</returns>
    public static bool GetProperty(string key, bool defaultValue)
        => ParseBoolean(GetProperty(key, null), defaultValue, false);

    /// <summary>
    /// Scale the value with the available memory. If 1 GB of RAM is available, the value
    /// is returned, if 2 GB are available, then twice the value, and so on.
    /// </summary>
    /// <param name="value">the value to scale</param>
    /// <returns>the scaled value</returns>
    public static int ScaleForAvailableMemory(int value)
    {
        GCMemoryInfo info = GC.GetGCMemoryInfo();
        long physicalMemorySize = info.TotalAvailableMemoryBytes;
        if (physicalMemorySize <= 0)
        {
            return value;
        }

        return (int)(value * physicalMemorySize / (1024L * 1024 * 1024));
    }

    /// <summary>
    /// Nanoseconds per timer tick.
    /// </summary>
    private static readonly long NanosPerTick = 1_000_000_000L / Stopwatch.Frequency;

    /// <summary>
    /// Returns the current value of the high-resolution time source.
    /// </summary>
    /// <remarks>
    /// The frequency does not change under a running process, so the scale is computed once
    /// rather than divided on every read.
    /// </remarks>
    /// <returns>time in nanoseconds, never equal to 0</returns>
    public static long CurrentNanoTime()
    {
        long time = Stopwatch.GetTimestamp() * NanosPerTick;
        return time == 0L ? 1L : time;
    }

    /// <summary>
    /// Returns the current value of the high-resolution time source plus the specified offset.
    /// </summary>
    /// <param name="ms">additional offset in milliseconds</param>
    /// <returns>time in nanoseconds, never equal to 0</returns>
    public static long CurrentNanoTimePlusMillis(int ms)
        => NanoTimePlusMillis(Stopwatch.GetTimestamp(), ms);

    /// <summary>
    /// Returns the given high-resolution time source value plus the specified offset.
    /// </summary>
    /// <param name="nanoTime">time in nanoseconds</param>
    /// <param name="ms">additional offset in milliseconds</param>
    /// <returns>time in nanoseconds, never equal to 0</returns>
    public static long NanoTimePlusMillis(long nanoTime, int ms)
    {
        long time = nanoTime + ms * 1_000_000L;
        return time == 0L ? 1L : time;
    }

    /// <summary>
    /// Create a single-threaded executor used for background maintenance.
    /// </summary>
    /// <param name="threadName">the thread name</param>
    /// <returns>the executor</returns>
    public static BackgroundExecutor CreateSingleThreadExecutor(string threadName)
        => new(threadName);

    /// <summary>
    /// Makes sure that all currently submitted tasks are processed before this method
    /// returns. It is assumed that there will be no new submissions to this executor,
    /// once this method has started.
    /// </summary>
    /// <param name="executor">the executor to flush</param>
    public static void FlushExecutor(BackgroundExecutor? executor)
    {
        if (executor is null)
        {
            return;
        }

        using ManualResetEventSlim done = new(false);
        try
        {
            executor.Execute(() => done.Set());
        }
        catch (InvalidOperationException)
        {
            // The executor was already shut down; nothing pending.
            return;
        }

        done.Wait();
    }

    /// <summary>
    /// Shut down the executor and wait for termination.
    /// </summary>
    /// <param name="executor">the executor</param>
    public static void ShutdownExecutor(BackgroundExecutor? executor)
    {
        if (executor is null)
        {
            return;
        }

        executor.Shutdown();
        executor.AwaitTermination(TimeSpan.FromDays(1));
    }

    /// <summary>
    /// A single-threaded executor backed by a dedicated background thread.
    /// The .NET replacement for Java's named <c>ThreadGroup</c> plus a
    /// single-worker <c>ThreadPoolExecutor</c>. System.Threading.Thread has no
    /// addressable thread group, so background/main-thread identity is tracked by
    /// this type instead.
    /// </summary>
    public sealed class BackgroundExecutor
    {
        private readonly System.Collections.Concurrent.BlockingCollection<Action> queue =
            new(new System.Collections.Concurrent.ConcurrentQueue<Action>());
        private readonly Thread thread;
        private readonly string name;
        private volatile bool shutdown;

        /// <summary>
        /// Creates and starts the executor.
        /// </summary>
        /// <param name="threadName">the thread name, also used for diagnostics</param>
        public BackgroundExecutor(string threadName)
        {
            name = threadName;
            thread = new Thread(Run)
            {
                IsBackground = true,
                Name = threadName,
            };
            BackgroundThreadRegistry.Register(thread, threadName);
            thread.Start();
        }

        /// <summary>
        /// The name of this executor's thread.
        /// </summary>
        public string Name => name;

        private void Run()
        {
            foreach (Action action in queue.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    MathUtils.Warn("Background task", e);
                }
            }
        }

        /// <summary>
        /// Submits work to the executor.
        /// </summary>
        /// <param name="action">the work</param>
        /// <exception cref="InvalidOperationException">if the executor is shut down</exception>
        public void Execute(Action action)
        {
            if (shutdown)
            {
                throw new InvalidOperationException("Executor '" + name + "' is shut down.");
            }

            queue.Add(action);
        }

        /// <summary>
        /// Submits work and answers a task for it. Unlike <see cref="Execute"/>, a failure of
        /// the work faults the task instead of being logged: the waiter owns the error, the way
        /// a <c>Future.get()</c> caller owns the <c>ExecutionException</c> in the reference.
        /// </summary>
        /// <param name="action">the work</param>
        /// <exception cref="InvalidOperationException">if the executor is shut down</exception>
        public Task Submit(Action action)
        {
            if (shutdown)
            {
                throw new InvalidOperationException("Executor '" + name + "' is shut down.");
            }

            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Add(() =>
            {
                try
                {
                    action();
                    completion.SetResult(null);
                }
                catch (Exception e)
                {
                    completion.SetException(e);
                }
            });
            return completion.Task;
        }

        /// <summary>
        /// How many submitted actions are still waiting for the worker.
        /// </summary>
        public int PendingCount => queue.Count;

        /// <summary>
        /// Stops accepting new work and lets queued work finish.
        /// </summary>
        public void Shutdown()
        {
            if (!shutdown)
            {
                shutdown = true;
                queue.CompleteAdding();
            }
        }

        /// <summary>
        /// Waits for the worker thread to finish.
        /// </summary>
        /// <param name="timeout">maximum time to wait</param>
        /// <returns>true if the worker finished within the timeout</returns>
        public bool AwaitTermination(TimeSpan timeout) => thread.Join(timeout);

        /// <summary>
        /// True if the caller is running on a thread owned by any
        /// <see cref="BackgroundExecutor"/>.
        /// </summary>
        public static bool IsBackgroundThread()
        {
            Thread? current = Thread.CurrentThread;
            return current is not null
                && current.IsBackground
                && BackgroundThreadRegistry.Contains(current);
        }
    }

    /// <summary>
    /// Tracks background threads created by <see cref="BackgroundExecutor"/>.
    /// </summary>
    private static class BackgroundThreadRegistry
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> Threads = new();

        internal static void Register(Thread thread, string name) => Threads[thread.ManagedThreadId] = name;

        internal static bool Contains(Thread thread) => Threads.ContainsKey(thread.ManagedThreadId);
    }
}
