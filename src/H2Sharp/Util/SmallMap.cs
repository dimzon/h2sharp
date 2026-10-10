using System;
using System.Collections.Generic;
using H2Sharp.Api;
using H2Sharp.Message;

namespace H2Sharp.Util;

/// <summary>
/// A map from small integer ids to objects, where the ids are handed out in increasing order and only
/// recent ones are worth keeping.
/// </summary>
/// <remarks>
/// The store keeps open objects this way, and the reason for the class is the eviction: once there
/// are twice as many entries as there should be, everything older than a window of
/// <c>maxElements</c> ids is dropped. That is possible only because the ids increase, and it is why
/// this is not a general map — it is an ordered one with a forgetful tail.
/// <para>
/// There is also a one-entry cache in front, because the store asks for the same object repeatedly
/// and a dictionary lookup for it is the one cost that shows in a profile.
/// </para>
/// </remarks>
public class SmallMap
{
    private readonly Dictionary<int, object?> map = [];

    private object? cache;

    private int cacheId = -1;

    private int lastId;

    private readonly int maxElements;

    /// <summary>
    /// A map that keeps a window of this many ids.
    /// </summary>
    /// <param name="maxElements">how many recent ids to keep</param>
    public SmallMap(int maxElements) => this.maxElements = maxElements;

    /// <summary>
    /// Store an object under an id.
    /// </summary>
    /// <param name="id">the id</param>
    /// <param name="o">the object</param>
    /// <returns>the id</returns>
    public int AddObject(int id, object? o)
    {
        if (map.Count > maxElements * 2)
        {
            Forget();
        }

        if (id > lastId)
        {
            lastId = id;
        }

        map[id] = o;
        cacheId = id;
        cache = o;
        return id;
    }

    /// <summary>
    /// Drop whatever is under an id.
    /// </summary>
    /// <param name="id">the id</param>
    public void FreeObject(int id)
    {
        if (cacheId == id)
        {
            cacheId = -1;
            cache = null;
        }

        _ = map.Remove(id);
    }

    /// <summary>
    /// The object under an id.
    /// </summary>
    /// <param name="id">the id</param>
    /// <param name="ifAvailable">
    /// whether a missing id is an answer. When it is false a missing id is refused, which is what the
    /// store wants: it means the object was closed and something still holds it.
    /// </param>
    /// <returns>the object, or null</returns>
    /// <exception cref="H2SharpException">the id is not there and was not allowed to be missing</exception>
    public object? GetObject(int id, bool ifAvailable)
    {
        if (id == cacheId)
        {
            return cache;
        }

        if (!map.TryGetValue(id, out object? obj))
        {
            if (!ifAvailable)
            {
                throw DbException.Get(ErrorCode.ObjectClosed);
            }

            return null;
        }

        return obj;
    }

    /// <summary>
    /// Drops every id older than the window of ids that is still worth keeping.
    /// </summary>
    private void Forget()
    {
        List<int> stale = [];
        foreach (int key in map.Keys)
        {
            if (key + maxElements < lastId)
            {
                stale.Add(key);
            }
        }

        foreach (int key in stale)
        {
            _ = map.Remove(key);
            if (cacheId == key)
            {
                cacheId = -1;
                cache = null;
            }
        }
    }
}
