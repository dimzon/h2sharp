using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace H2Sharp.MvStore;

/// <summary>
/// A cursor position: a path from the root of the tree down to a page, one link per level.
/// </summary>
/// <remarks>
/// The chain is built top down and read bottom up. Each link knows its page, the index inside it, and
/// the link above, which is what lets an operation that changed a page rebuild only the path to the
/// root instead of the whole tree.
/// </remarks>
/// <typeparam name="K">the key type</typeparam>
/// <typeparam name="V">the value type</typeparam>
public sealed class CursorPos<K, V> : ICursorLink
{
    /// <summary>
    /// The page this link stands on.
    /// </summary>
    public Page<K, V> page;

    /// <summary>
    /// The index inside the page.
    /// </summary>
    public int index;

    /// <summary>
    /// The link above, or null at the root.
    /// </summary>
    public CursorPos<K, V>? parent;

    /// <summary>
    /// A link on a page at an index, under a parent.
    /// </summary>
    public CursorPos(Page<K, V> page, int index, CursorPos<K, V>? parent)
    {
        this.page = page;
        this.index = index;
        this.parent = parent;
    }

    /// <summary>
    /// Walks from a page down to the leaf a key would land on, reusing the previous walk where the
    /// tree has not changed underneath it.
    /// </summary>
    /// <remarks>
    /// When the walk meets the same page it met last time, the subtree below has not been modified
    /// since, so the rest of the path is copied from the previous chain instead of searched again.
    /// When it meets a page with the same keys but a different identity, the index is still good and
    /// only the page is swapped. Only a page whose keys actually changed is searched again.
    /// </remarks>
    internal static CursorPos<K, V> TraverseDown(Page<K, V> page, K key, CursorPos<K, V>? existing)
    {
        if (existing is not null)
        {
            Debug.Assert(existing.page.IsLeaf());
            existing = existing.Reverse(null);
        }

        CursorPos<K, V>? cursorPos = null;
        while (true)
        {
            int index;
            if (existing is null)
            {
                index = page.CalculateTraversalIndex(key);
                cursorPos = new CursorPos<K, V>(page, index, cursorPos);
            }
            else
            {
                Page<K, V> existingPage = existing.page;
                if (ReferenceEquals(existingPage, page))
                {
                    // If we hit exactly the same page as previous time, that means that subtree
                    // under this page also hasn't been modified since last attempt. Further traversal
                    // therefore is going to follow exactly same path, so lets just copy it from
                    // existing chain.
                    cursorPos = existing.Reverse(cursorPos);
                    Debug.Assert(cursorPos.page.IsLeaf());
                    return cursorPos;
                }

                CursorPos<K, V>? temp = existing.parent;
                existing.parent = cursorPos;
                existing.page = page;
                cursorPos = existing;
                existing = temp;

                // if we hit page with exact set of keys, as last time,
                // there is no need to do a key search again, use previous result
                if (!page.SameKeys(existingPage))
                {
                    cursorPos.index = page.CalculateTraversalIndex(key);
                }

                index = cursorPos.index;
            }

            if (page.IsLeaf())
            {
                Debug.Assert(cursorPos!.page.IsLeaf());
                return cursorPos;
            }

            page = page.GetChildPage(index);
        }
    }

    /// <summary>
    /// Removes every page on this path, returning what was never saved.
    /// </summary>
    internal int ProcessRemovalInfo(long version)
    {
        int unsavedMemory = 0;
        for (CursorPos<K, V>? head = this; head is not null; head = head.parent)
        {
            unsavedMemory += head.page.RemovePage(version);
        }

        return unsavedMemory;
    }

    private CursorPos<K, V> Reverse(CursorPos<K, V>? alreadyReversed)
    {
        CursorPos<K, V> reversed = parent is null ? this : parent.Reverse(this);
        parent = alreadyReversed;
        return reversed;
    }

    /// <inheritdoc/>
    public override string ToString()
        => "CursorPos{" + "page=" + page + ", index=" + index + ", parent=" + parent + '}';

    /// <inheritdoc/>
    ICursorLink? ICursorLink.ParentLink => parent;

    /// <inheritdoc/>
    long ICursorLink.PagePos => page.GetPos();
}
