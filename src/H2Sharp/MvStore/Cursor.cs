using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace H2Sharp.MvStore;

/// <summary>
/// Walks the keys of a map between two points, in either direction.
/// </summary>
/// <remarks>
/// The cursor reuses its chain links: a link abandoned while climbing back up is kept and handed
/// out again on the way down, so a long walk never allocates once it is going. What it answers
/// with is always the last key seen — <see cref="Next"/> clears the current one, and the next
/// <see cref="HasNext"/> finds the one after.
/// <para>
/// Keys and values coming out of a map are never null, which is why <see cref="Next"/> answers
/// with a plain <c>K</c>. Whether there is a current key is a flag, not a null check, and an
/// absent bound is an <c>object?</c>, not a <c>K?</c>: for a value-type key, <c>K?</c> collapses
/// to <c>K</c> and can neither hold null nor take a null bound.
/// </para>
/// </remarks>
/// <typeparam name="K">the key type</typeparam>
/// <typeparam name="V">the value type</typeparam>
public sealed class Cursor<K, V> : IEnumerator<K>
{
    private readonly bool reverse;

    private readonly object? to;

    private CursorPos<K, V>? cursorPos;

    private CursorPos<K, V>? keeper;

    private bool hasCurrent;

    private K last = default!;

    private V? lastValue;

    private Page<K, V> lastPage;

    /// <summary>
    /// A cursor from a point to a point, going forward.
    /// </summary>
    public Cursor(RootReference<K, V> rootReference, object? from, object? to)
        : this(rootReference, from, to, false)
    {
    }

    /// <summary>
    /// A cursor from a point to a point, in either direction.
    /// </summary>
    public Cursor(RootReference<K, V> rootReference, object? from, object? to, bool reverse)
    {
        lastPage = rootReference.root;
        cursorPos = TraverseDown(lastPage, from, reverse);
        this.to = to;
        this.reverse = reverse;
    }

    /// <summary>
    /// Whether there is another key.
    /// </summary>
    public bool HasNext()
    {
        if (cursorPos is not null)
        {
            int increment = reverse ? -1 : 1;
            while (!hasCurrent)
            {
                Page<K, V> page = cursorPos.page;
                int index = cursorPos.index;
                if (reverse ? index < 0 : index >= UpperBound(page))
                {
                    // traversal of this page is over, going up a level or stop if at the root already
                    CursorPos<K, V> tmp = cursorPos;
                    cursorPos = cursorPos.parent;
                    if (cursorPos is null)
                    {
                        return false;
                    }

                    tmp.parent = keeper;
                    keeper = tmp;
                }
                else
                {
                    // traverse down to the leaf taking the leftmost path
                    while (!page.IsLeaf())
                    {
                        page = page.GetChildPage(index);
                        index = reverse ? UpperBound(page) - 1 : 0;
                        if (keeper is null)
                        {
                            cursorPos = new CursorPos<K, V>(page, index, cursorPos);
                        }
                        else
                        {
                            CursorPos<K, V> tmp = keeper;
                            keeper = keeper.parent;
                            tmp.parent = cursorPos;
                            tmp.page = page;
                            tmp.index = index;
                            cursorPos = tmp;
                        }
                    }

                    if (reverse ? index >= 0 : index < page.GetKeyCount())
                    {
                        K key = page.GetKey(index);
                        if (to is not null && Math.Sign(page.map.KeyType.Compare(key, (K)to)) == increment)
                        {
                            return false;
                        }

                        last = key;
                        hasCurrent = true;
                        lastValue = page.GetValue(index);
                        lastPage = page;
                    }
                }

                cursorPos.index += increment;
            }
        }

        return hasCurrent;
    }

    /// <summary>
    /// The next key.
    /// </summary>
    public K Next()
    {
        if (!HasNext())
        {
            throw new InvalidOperationException();
        }

        hasCurrent = false;
        return last;
    }

    /// <summary>
    /// The last key seen.
    /// </summary>
    public K GetKey() => last;

    /// <summary>
    /// The value of the last key seen.
    /// </summary>
    public V GetValue() => lastValue!;

    /// <summary>
    /// The page the last key came from.
    /// </summary>
    internal Page<K, V> GetPage() => lastPage;

    /// <summary>
    /// Removes the last key seen. Always refused: the reference inherits a throwing remove from
    /// its iterator, and so does this.
    /// </summary>
    public void Remove() => throw new NotSupportedException();

    /// <summary>
    /// Skips a number of keys: walking for a few, jumping by index for many.
    /// </summary>
    public void Skip(long n)
    {
        if (n < 10)
        {
            while (n-- > 0 && HasNext())
            {
                Next();
            }
        }
        else if (HasNext())
        {
            Debug.Assert(cursorPos is not null);
            CursorPos<K, V> cp = cursorPos;
            CursorPos<K, V>? parent;
            while ((parent = cp.parent) is not null)
            {
                cp = parent;
            }

            Page<K, V> root = cp.page;
            MVMap<K, V> map = root.map;
            long index = map.GetKeyIndex(Next());
            last = map.GetKey(index + (reverse ? -n : n))!;
            cursorPos = TraverseDown(root, last, reverse);
        }
    }

    /// <summary>
    /// The chain down to where a key would land, adjusted past the insertion point.
    /// </summary>
    internal static CursorPos<K, V> TraverseDown(Page<K, V> page, object? key, bool reverse)
    {
        CursorPos<K, V> cursorPos = key is null
            ? reverse ? page.GetAppendCursorPos(null) : page.GetPrependCursorPos(null)
            : CursorPos<K, V>.TraverseDown(page, (K)key, null);
        int index = cursorPos.index;
        if (index < 0)
        {
            index = ~index;
            if (reverse)
            {
                --index;
            }

            cursorPos.index = index;
        }

        return cursorPos;
    }

    private static int UpperBound(Page<K, V> page)
        => page.IsLeaf() ? page.GetKeyCount() : page.map.GetChildPageCount(page);

    /// <inheritdoc/>
    public K Current { get; private set; } = default!;

    /// <inheritdoc/>
    public bool MoveNext()
    {
        if (!HasNext())
        {
            return false;
        }

        Current = Next();
        return true;
    }

    /// <inheritdoc/>
    public void Reset() => throw new NotSupportedException();

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    /// <inheritdoc/>
    object IEnumerator.Current => Current!;
}
