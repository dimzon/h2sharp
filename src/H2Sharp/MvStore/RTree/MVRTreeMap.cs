using System;
using System.Collections.Generic;
using System.Diagnostics;
using H2Sharp.MvStore.Type;

namespace H2Sharp.MvStore.RTree;

/// <summary>
/// An r-tree over spatial keys, with linear and quadratic split.
/// </summary>
/// <remarks>
/// Unlike a regular map, placement is by containment and area increase rather than key order:
/// operations walk down choosing (or splitting) children instead of traversing by comparison.
/// </remarks>
public sealed class MVRTreeMap<V> : MVMap<Spatial, V>
{
    /// <summary>
    /// The spatial key type.
    /// </summary>
    private readonly SpatialDataType keyType;

    private bool quadraticSplit;

    /// <summary>
    /// An r-tree map from a configuration.
    /// </summary>
    public MVRTreeMap(IDictionary<string, object?> config, SpatialDataType keyType, DataType<V> valueType)
        : base(config, keyType, valueType)
    {
        this.keyType = keyType;
        _ = config.TryGetValue("quadraticSplit", out object? qs);
        quadraticSplit = bool.TryParse(qs?.ToString(), out bool parsed) && parsed;
    }

    private MVRTreeMap(MVRTreeMap<V> source)
        : base(source)
    {
        keyType = source.keyType;
        quadraticSplit = source.quadraticSplit;
    }

    /// <inheritdoc/>
    protected override MVMap<Spatial, V> CloneIt() => new MVRTreeMap<V>(this);

    /// <summary>
    /// Iterates keys intersecting the rectangle.
    /// </summary>
    public RTreeCursor<V> FindIntersectingKeys(Spatial x) => new IntersectsRTreeCursor<V>(GetRootPage(), x, keyType);

    /// <summary>
    /// Iterates keys fully contained in the rectangle.
    /// </summary>
    public RTreeCursor<V> FindContainedKeys(Spatial x) => new ContainsRTreeCursor<V>(GetRootPage(), x, keyType);

    private bool Contains(Page<Spatial, V> p, int index, Spatial key)
        => keyType.Contains(p.GetKey(index), key);

    /// <summary>
    /// Gets by exact match, descending into containing children.
    /// </summary>
    public override V? Get(Page<Spatial, V> p, Spatial key)
    {
        int keyCount = p.GetKeyCount();
        if (!p.IsLeaf())
        {
            for (int i = 0; i < keyCount; i++)
            {
                if (Contains(p, i, key))
                {
                    V? o = Get(p.GetChildPage(i), key);
                    if (o is not null)
                    {
                        return o;
                    }
                }
            }
        }
        else
        {
            for (int i = 0; i < keyCount; i++)
            {
                if (keyType.Equals(p.GetKey(i), key))
                {
                    return p.GetValue(i);
                }
            }
        }

        return default;
    }

    /// <inheritdoc/>
    public override V? Remove(object? key) => Operate((Spatial)key!, default, DecisionMaker<V>.RemoveDecision());

    /// <inheritdoc/>
    public override V? Operate(Spatial key, V? value, DecisionMaker<V> decisionMaker)
    {
        int attempt = 0;
        List<Page<Spatial, V>>? removedPages = IsPersistent() ? new List<Page<Spatial, V>>() : null;
        while (true)
        {
            RootReference<Spatial, V> rootReference = FlushAndGetRoot();
            if (attempt++ == 0 && !rootReference.IsLockedByCurrentThread())
            {
                BeforeWrite();
            }

            Page<Spatial, V> p = rootReference.root;
            if (removedPages is not null && p.GetTotalCount() > 0)
            {
                removedPages.Add(p);
            }

            p = p.Copy();
            V? result = Operate(p, key, value, decisionMaker, removedPages);
            if (!p.IsLeaf() && p.GetTotalCount() == 0)
            {
                if (removedPages is not null)
                {
                    removedPages.Add(p);
                }

                p = CreateEmptyLeaf();
            }
            else if (p.GetKeyCount() > Store.GetKeysPerPage() || p.GetMemory() > Store.GetMaxPageSize()
                                                                 && p.GetKeyCount() > 3)
            {
                // only possible if this is the root, else we would have
                // split earlier (this requires pageSplitSize is fixed)
                long totalCount = p.GetTotalCount();
                Page<Spatial, V> split = Split(p);
                Spatial k1 = GetBounds(p);
                Spatial k2 = GetBounds(split);
                Spatial[] keys = p.CreateKeyStorage(2);
                keys[0] = k1;
                keys[1] = k2;
                Page<Spatial, V>.PageReference[] children = Page<Spatial, V>.CreateRefStorage(3);
                children[0] = new Page<Spatial, V>.PageReference(p);
                children[1] = new Page<Spatial, V>.PageReference(split);
                children[2] = Page<Spatial, V>.PageReference.Empty;
                p = Page<Spatial, V>.CreateNode(this, keys, children, totalCount, 0);
                RegisterUnsavedMemory(p.GetMemory());
            }

            if (removedPages is null)
            {
                if (UpdateRoot(rootReference, p, attempt))
                {
                    return result;
                }
            }
            else
            {
                RootReference<Spatial, V>? lockedRootReference = TryLock(rootReference, attempt);
                if (lockedRootReference is not null)
                {
                    try
                    {
                        long version = lockedRootReference.version;
                        int unsavedMemory = 0;
                        foreach (Page<Spatial, V> page in removedPages)
                        {
                            if (!page.IsRemoved())
                            {
                                unsavedMemory += page.RemovePage(version);
                            }
                        }

                        RegisterUnsavedMemory(unsavedMemory);
                    }
                    finally
                    {
                        UnlockRoot(p);
                    }

                    return result;
                }

                removedPages.Clear();
            }

            decisionMaker.Reset();
        }
    }

    private V? Operate(Page<Spatial, V> p, Spatial key, V? value, DecisionMaker<V> decisionMaker,
                        List<Page<Spatial, V>>? removedPages)
    {
        V? result;
        if (p.IsLeaf())
        {
            int index = -1;
            int keyCount = p.GetKeyCount();
            for (int i = 0; i < keyCount; i++)
            {
                if (keyType.Equals(p.GetKey(i), key))
                {
                    index = i;
                }
            }

            result = index < 0 ? default : p.GetValue(index);
            Decision decision = decisionMaker.Decide(result, value);
            switch (decision)
            {
                case Decision.Repeat:
                case Decision.Abort:
                    break;
                case Decision.Remove:
                    if (index >= 0)
                    {
                        p.Remove(index);
                    }

                    break;
                case Decision.Put:
                    value = decisionMaker.SelectValue(result, value);
                    if (index < 0)
                    {
                        p.InsertLeaf(p.GetKeyCount(), key, value!);
                    }
                    else
                    {
                        p.SetKey(index, key);
                        p.SetValue(index, value!);
                    }

                    break;
            }

            return result;
        }

        // p is an internal node
        int nodeIndex = -1;
        for (int i = 0; i < p.GetKeyCount(); i++)
        {
            if (Contains(p, i, key))
            {
                Page<Spatial, V> c = p.GetChildPage(i);
                if (Get(c, key) is not null)
                {
                    nodeIndex = i;
                    break;
                }

                if (nodeIndex < 0)
                {
                    nodeIndex = i;
                }
            }
        }

        if (nodeIndex < 0)
        {
            // a new entry, we don't know where to add yet
            float min = float.MaxValue;
            for (int i = 0; i < p.GetKeyCount(); i++)
            {
                Spatial k = p.GetKey(i);
                float areaIncrease = keyType.GetAreaIncrease(k, key);
                if (areaIncrease < min)
                {
                    nodeIndex = i;
                    min = areaIncrease;
                }
            }
        }

        Page<Spatial, V> child = p.GetChildPage(nodeIndex);
        if (removedPages is not null)
        {
            removedPages.Add(child);
        }

        child = child.Copy();
        if (child.GetKeyCount() > Store.GetKeysPerPage() || child.GetMemory() > Store.GetMaxPageSize()
                && child.GetKeyCount() > 4)
        {
            // split on the way down
            Page<Spatial, V> split = Split(child);
            p.SetKey(nodeIndex, GetBounds(child));
            p.SetChild(nodeIndex, child);
            p.InsertNode(nodeIndex, GetBounds(split), split);
            // now we are not sure where to add
            result = Operate(p, key, value, decisionMaker, removedPages);
        }
        else
        {
            result = Operate(child, key, value, decisionMaker, removedPages);
            Spatial bounds = p.GetKey(nodeIndex);
            if (!keyType.Contains(bounds, key))
            {
                bounds = keyType.CreateBoundingBox(bounds);
                keyType.IncreaseBounds(bounds, key);
                p.SetKey(nodeIndex, bounds);
            }

            if (child.GetTotalCount() > 0)
            {
                p.SetChild(nodeIndex, child);
            }
            else
            {
                p.Remove(nodeIndex);
            }
        }

        return result;
    }

    private Spatial GetBounds(Page<Spatial, V> x)
    {
        Spatial bounds = keyType.CreateBoundingBox(x.GetKey(0));
        int keyCount = x.GetKeyCount();
        for (int i = 1; i < keyCount; i++)
        {
            keyType.IncreaseBounds(bounds, x.GetKey(i));
        }

        return bounds;
    }

    /// <inheritdoc/>
    public override V? Put(Spatial key, V value) => Operate(key, value, DecisionMaker<V>.PutDecision());

    /// <summary>
    /// Adds an entry that must not exist yet.
    /// </summary>
    public void Add(Spatial key, V value) => Operate(key, value, DecisionMaker<V>.PutDecision());

    private Page<Spatial, V> Split(Page<Spatial, V> p)
        => quadraticSplit ? SplitQuadratic(p) : SplitLinear(p);

    private Page<Spatial, V> SplitLinear(Page<Spatial, V> p)
    {
        int keyCount = p.GetKeyCount();
        var keys = new List<Spatial>(keyCount);
        for (int i = 0; i < keyCount; i++)
        {
            keys.Add(p.GetKey(i));
        }

        int[]? extremes = keyType.GetExtremes(keys);
        if (extremes is null)
        {
            return SplitQuadratic(p);
        }

        Page<Spatial, V> splitA = NewPage(p.IsLeaf());
        Page<Spatial, V> splitB = NewPage(p.IsLeaf());
        Move(p, splitA, extremes[0]);
        if (extremes[1] > extremes[0])
        {
            extremes[1]--;
        }

        Move(p, splitB, extremes[1]);
        Spatial boundsA = keyType.CreateBoundingBox(splitA.GetKey(0));
        Spatial boundsB = keyType.CreateBoundingBox(splitB.GetKey(0));
        while (p.GetKeyCount() > 0)
        {
            Spatial o = p.GetKey(0);
            float a = keyType.GetAreaIncrease(boundsA, o);
            float b = keyType.GetAreaIncrease(boundsB, o);
            if (a < b)
            {
                keyType.IncreaseBounds(boundsA, o);
                Move(p, splitA, 0);
            }
            else
            {
                keyType.IncreaseBounds(boundsB, o);
                Move(p, splitB, 0);
            }
        }

        while (splitB.GetKeyCount() > 0)
        {
            Move(splitB, p, 0);
        }

        return splitA;
    }

    private Page<Spatial, V> SplitQuadratic(Page<Spatial, V> p)
    {
        Page<Spatial, V> splitA = NewPage(p.IsLeaf());
        Page<Spatial, V> splitB = NewPage(p.IsLeaf());
        // Java's Float.MIN_VALUE is the smallest positive float, not the most negative:
        // float.Epsilon is its counterpart here.
        float largest = float.Epsilon;
        int ia = 0, ib = 0;
        int keyCount = p.GetKeyCount();
        for (int a = 0; a < keyCount; a++)
        {
            Spatial objA = p.GetKey(a);
            for (int b = 0; b < keyCount; b++)
            {
                if (a == b)
                {
                    continue;
                }

                Spatial objB = p.GetKey(b);
                float area = keyType.GetCombinedArea(objA, objB);
                if (area > largest)
                {
                    largest = area;
                    ia = a;
                    ib = b;
                }
            }
        }

        Move(p, splitA, ia);
        if (ia < ib)
        {
            ib--;
        }

        Move(p, splitB, ib);
        Spatial boundsA = keyType.CreateBoundingBox(splitA.GetKey(0));
        Spatial boundsB = keyType.CreateBoundingBox(splitB.GetKey(0));
        while (p.GetKeyCount() > 0)
        {
            float diff = 0, bestA = 0, bestB = 0;
            int best = 0;
            keyCount = p.GetKeyCount();
            for (int i = 0; i < keyCount; i++)
            {
                Spatial o = p.GetKey(i);
                float incA = keyType.GetAreaIncrease(boundsA, o);
                float incB = keyType.GetAreaIncrease(boundsB, o);
                float d = Math.Abs(incA - incB);
                if (d > diff)
                {
                    diff = d;
                    bestA = incA;
                    bestB = incB;
                    best = i;
                }
            }

            if (bestA < bestB)
            {
                keyType.IncreaseBounds(boundsA, p.GetKey(best));
                Move(p, splitA, best);
            }
            else
            {
                keyType.IncreaseBounds(boundsB, p.GetKey(best));
                Move(p, splitB, best);
            }
        }

        while (splitB.GetKeyCount() > 0)
        {
            Move(splitB, p, 0);
        }

        return splitA;
    }

    private Page<Spatial, V> NewPage(bool leaf)
    {
        Page<Spatial, V> page = leaf ? CreateEmptyLeaf() : Page<Spatial, V>.CreateEmptyNode(this);
        RegisterUnsavedMemory(page.GetMemory());
        return page;
    }

    private static void Move(Page<Spatial, V> source, Page<Spatial, V> target, int sourceIndex)
    {
        Spatial k = source.GetKey(sourceIndex);
        if (source.IsLeaf())
        {
            V? v = source.GetValue(sourceIndex);
            target.InsertLeaf(0, k, v!);
        }
        else
        {
            Page<Spatial, V> c = source.GetChildPage(sourceIndex);
            target.InsertNode(0, k, c);
        }

        source.Remove(sourceIndex);
    }

    /// <summary>
    /// Adds all node keys (including internal bounds) to the list, for visualizing splits.
    /// </summary>
    public void AddNodeKeys(List<Spatial> list, Page<Spatial, V>? p)
    {
        if (p is not null && !p.IsLeaf())
        {
            int keyCount = p.GetKeyCount();
            for (int i = 0; i < keyCount; i++)
            {
                list.Add(p.GetKey(i));
                AddNodeKeys(list, p.GetChildPage(i));
            }
        }
    }

    /// <summary>
    /// Whether the quadratic split is used.
    /// </summary>
    public bool IsQuadraticSplit() => quadraticSplit;

    /// <summary>
    /// Sets the split algorithm.
    /// </summary>
    public void SetQuadraticSplit(bool quadraticSplit) => this.quadraticSplit = quadraticSplit;

    /// <inheritdoc/>
    protected internal override int GetChildPageCount(Page<Spatial, V> p) => p.GetRawChildPageCount() - 1;

    /// <summary>
    /// A cursor over matching keys.
    /// </summary>
    public abstract class RTreeCursor<VV>
    {
        private readonly Spatial filter;
        private CursorPos<Spatial, VV>? pos;
        private Spatial? current;
        private readonly Page<Spatial, VV> root;
        private bool initialized;

        /// <summary>
        /// A cursor from the root, matching the filter.
        /// </summary>
        protected RTreeCursor(Page<Spatial, VV> root, Spatial filter)
        {
            this.root = root;
            this.filter = filter;
        }

        /// <summary>
        /// Whether another key matches.
        /// </summary>
        public bool HasNext()
        {
            if (!initialized)
            {
                // init
                pos = new CursorPos<Spatial, VV>(root, 0, null);
                FetchNext();
                initialized = true;
            }

            return current is not null;
        }

        /// <summary>
        /// Skips entries. Fast even for many, for this map.
        /// </summary>
        public void Skip(long n)
        {
            while (HasNext() && n-- > 0)
            {
                FetchNext();
            }
        }

        /// <summary>
        /// The next matching key, or null.
        /// </summary>
        public Spatial? Next()
        {
            if (!HasNext())
            {
                return null;
            }

            Spatial? c = current;
            FetchNext();
            return c;
        }

        private void FetchNext()
        {
            while (pos is not null)
            {
                Page<Spatial, VV> p = pos.page;
                if (p.IsLeaf())
                {
                    while (pos.index < p.GetKeyCount())
                    {
                        Spatial c = p.GetKey(pos.index++);
                        if (filter is null || Check(true, c, filter))
                        {
                            current = c;
                            return;
                        }
                    }
                }
                else
                {
                    bool found = false;
                    while (pos.index < p.GetKeyCount())
                    {
                        int index = pos.index++;
                        Spatial c = p.GetKey(index);
                        if (filter is null || Check(false, c, filter))
                        {
                            Page<Spatial, VV> child = pos.page.GetChildPage(index);
                            pos = new CursorPos<Spatial, VV>(child, 0, pos);
                            found = true;
                            break;
                        }
                    }

                    if (found)
                    {
                        continue;
                    }
                }

                // parent
                pos = pos.parent;
            }

            current = null;
        }

        /// <summary>
        /// Whether a key matches.
        /// </summary>
        protected abstract bool Check(bool leaf, Spatial key, Spatial test);
    }

    private sealed class IntersectsRTreeCursor<VV> : RTreeCursor<VV>
    {
        private readonly SpatialDataType keyType;

        internal IntersectsRTreeCursor(Page<Spatial, VV> root, Spatial filter, SpatialDataType keyType)
            : base(root, filter)
        {
            this.keyType = keyType;
        }

        protected override bool Check(bool leaf, Spatial key, Spatial test)
            => keyType.IsOverlap(key, test);
    }

    private sealed class ContainsRTreeCursor<VV> : RTreeCursor<VV>
    {
        private readonly SpatialDataType keyType;

        internal ContainsRTreeCursor(Page<Spatial, VV> root, Spatial filter, SpatialDataType keyType)
            : base(root, filter)
        {
            this.keyType = keyType;
        }

        protected override bool Check(bool leaf, Spatial key, Spatial test)
            => leaf ? keyType.IsInside(key, test) : keyType.IsOverlap(key, test);
    }

    /// <inheritdoc/>
    protected internal override string? GetMapType() => "rtree";

    /// <summary>
    /// Builds r-tree maps.
    /// </summary>
    public sealed class Builder<VV> : MVMap<Spatial, VV>.BasicBuilder<MVRTreeMap<VV>, Spatial, VV>
    {
        private int dimensions = 2;

        /// <summary>
        /// A builder for 2-dimensional maps.
        /// </summary>
        public Builder()
        {
            SetKeyType(new SpatialDataType(dimensions));
        }

        /// <summary>
        /// Sets the dimensions.
        /// </summary>
        public Builder<VV> Dimensions(int dimensions)
        {
            this.dimensions = dimensions;
            SetKeyType(new SpatialDataType(dimensions));
            return this;
        }

        /// <summary>
        /// Sets the value type.
        /// </summary>
        public new Builder<VV> ValueType(DataType<VV> valueType)
        {
            SetValueType(valueType);
            return this;
        }

        /// <inheritdoc/>
        protected override MVRTreeMap<VV> Create(IDictionary<string, object?> config)
            => new MVRTreeMap<VV>(config, (SpatialDataType)GetKeyType()!, GetValueType()!);
    }
}
