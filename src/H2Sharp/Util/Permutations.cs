/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.util.Permutations.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * All permutations of an array, one at a time.
 *
 * The optimizer tries join orders, and trying them means enumerating permutations of the
 * filters: for seven filters that is already 5040 plans, which is why the optimizer switches
 * from brute force to genetic past that point. This class is the brute-force half — it walks
 * every order exactly once, and the m < n form walks every *partial* order, which is what the
 * optimizer needs when it only fixes the first tables and leaves the rest to chance.
 *
 * The walk is Tucker's dip-and-swap, and the comment on moveIndex is the reference's own
 * because the algorithm is easier to misremember than to re-derive: find the rightmost dip,
 * swap it with the least greater element to its right, reverse what follows. The two reverses
 * at the end are not one — the first keeps the dip in the interesting region, the second keeps
 * everything past m ascending right to left, and dropping either repeats or skips orders.
 *
 * The elements past m are kept ascending right to left from the start for the same reason:
 * without it the dip would wander into the region nobody reads, and the walk would produce
 * the same visible prefix twice with different invisible tails.
 */
using System;
using H2Sharp.Message;

namespace H2Sharp.Util;

/// <summary>
/// A class to iterate over all permutations of an array.
/// The algorithm is from Applied Combinatorics, by Alan Tucker.
/// </summary>
/// <typeparam name="T">the element type</typeparam>
public sealed class Permutations<T>
{
    private readonly T[] inn;

    private readonly T[] outt;

    private readonly int n;

    private readonly int m;

    private readonly int[] index;

    private bool hasNext = true;

    private Permutations(T[] inn, T[] outt, int m)
    {
        n = inn.Length;
        this.m = m;
        if (n < m || m < 0)
        {
            throw DbException.GetInternalError("n < m or m < 0");
        }

        this.inn = inn;
        this.outt = outt;
        index = new int[n];
        for (int i = 0; i < n; i++)
        {
            index[i] = i;
        }

        // The elements from m to n are always kept ascending right to left.
        // This keeps the dip in the interesting region.
        ReverseAfter(m - 1);
    }

    /// <summary>
    /// Create a new permutations object.
    /// </summary>
    /// <param name="inn">the source array</param>
    /// <param name="outt">the target array</param>
    /// <returns>the generated permutations object</returns>
    public static Permutations<T> Create(T[] inn, T[] outt)
    {
        ArgumentNullException.ThrowIfNull(inn);
        ArgumentNullException.ThrowIfNull(outt);
        return new Permutations<T>(inn, outt, inn.Length);
    }

    /// <summary>
    /// Create a new permutations object.
    /// </summary>
    /// <param name="inn">the source array</param>
    /// <param name="outt">the target array</param>
    /// <param name="m">the number of output elements to generate</param>
    /// <returns>the generated permutations object</returns>
    public static Permutations<T> Create(T[] inn, T[] outt, int m)
    {
        ArgumentNullException.ThrowIfNull(inn);
        ArgumentNullException.ThrowIfNull(outt);
        return new Permutations<T>(inn, outt, m);
    }

    /// <summary>
    /// Move the index forward a notch. The algorithm first finds the rightmost
    /// index that is less than its neighbor to the right. This is the dip point.
    /// The algorithm next finds the least element to the right of the dip that
    /// is greater than the dip. That element is switched with the dip. Finally,
    /// the list of elements to the right of the dip is reversed. For example,
    /// in a permutation of 5 items, the index may be {1, 2, 4, 3, 0}. The dip
    /// is 2 the rightmost element less than its neighbor on its right. The least
    /// element to the right of 2 that is greater than 2 is 3. These elements are
    /// swapped, yielding {1, 3, 4, 2, 0}, and the list right of the dip point is
    /// reversed, yielding {1, 3, 0, 2, 4}.
    /// </summary>
    private void MoveIndex()
    {
        // find the index of the first element that dips
        int i = RightmostDip();
        if (i < 0)
        {
            hasNext = false;
            return;
        }

        // find the least great element to the right of the dip
        int leastToRightIndex = i + 1;
        for (int j = i + 2; j < n; j++)
        {
            if (index[j] < index[leastToRightIndex] && index[j] > index[i])
            {
                leastToRightIndex = j;
            }
        }

        // switch dip element with the least great element to its right
        int t = index[i];
        index[i] = index[leastToRightIndex];
        index[leastToRightIndex] = t;

        if (m - 1 > i)
        {
            // reverse the elements to the right of the dip
            ReverseAfter(i);

            // reverse the elements to the right of m - 1
            ReverseAfter(m - 1);
        }
    }

    /// <summary>
    /// Get the index of the first element from the right that is less
    /// than its neighbor on the right.
    /// </summary>
    /// <returns>the index or -1 if none is found</returns>
    private int RightmostDip()
    {
        for (int i = n - 2; i >= 0; i--)
        {
            if (index[i] < index[i + 1])
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Reverse the elements to the right of the specified index.
    /// </summary>
    /// <param name="i">the index</param>
    private void ReverseAfter(int i)
    {
        int start = i + 1;
        int end = n - 1;
        while (start < end)
        {
            int t = index[start];
            index[start] = index[end];
            index[end] = t;
            start++;
            end--;
        }
    }

    /// <summary>
    /// Go to the next lineup, and if available, fill the target array.
    /// </summary>
    /// <returns>if a new lineup is available</returns>
    public bool Next()
    {
        if (!hasNext)
        {
            return false;
        }

        for (int i = 0; i < m; i++)
        {
            outt[i] = inn[index[i]];
        }

        MoveIndex();
        return true;
    }
}
