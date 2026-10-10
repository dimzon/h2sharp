using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using H2Sharp.MvStore.Type;

namespace H2Sharp.MvStore.RTree;

/// <summary>
/// A spatial data type: up to 31 dimensions of min/max float pairs. The maximum is stored
/// only when it differs from the minimum.
/// </summary>
public class SpatialDataType : BasicDataType<Spatial>
{
    private readonly int dimensions;

    /// <summary>
    /// A spatial data type with the given dimension count (1 to 31).
    /// </summary>
    public SpatialDataType(int dimensions)
    {
        // Because of how we are storing the
        // min-max-flag in the read/write method
        // the number of dimensions must be < 32.
        DataUtils.CheckArgument(
                dimensions >= 1 && dimensions < 32,
                "Dimensions must be between 1 and 31, is {0}", dimensions);
        this.dimensions = dimensions;
    }

    /// <summary>
    /// Creates a spatial object with the given parameters.
    /// </summary>
    protected Spatial Create(long id, params float[] minMax) => new DefaultSpatial(id, minMax);

    /// <inheritdoc/>
    public override Spatial[] CreateStorage(int size) => new Spatial[size];

    /// <inheritdoc/>
    public override int Compare(Spatial? a, Spatial? b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }
        else if (a is null)
        {
            return -1;
        }
        else if (b is null)
        {
            return 1;
        }

        long la = a.GetId();
        long lb = b.GetId();
        return la.CompareTo(lb);
    }

    /// <summary>
    /// Whether two spatial values are equal (by id).
    /// </summary>
    public bool Equals(Spatial? a, Spatial? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }
        else if (a is null || b is null)
        {
            return false;
        }

        return a.GetId() == b.GetId();
    }

    /// <inheritdoc/>
    public override int GetMemory(Spatial obj) => 40 + dimensions * 4;

    /// <inheritdoc/>
    public override void Write(WriteBuffer buff, Spatial k)
    {
        if (k.IsNull())
        {
            _ = buff.PutVarInt(-1);
            _ = buff.PutVarLong(k.GetId());
            return;
        }

        int flags = 0;
        for (int i = 0; i < dimensions; i++)
        {
            if (k.Min(i) == k.Max(i))
            {
                flags |= 1 << i;
            }
        }

        _ = buff.PutVarInt(flags);
        for (int i = 0; i < dimensions; i++)
        {
            _ = buff.PutFloat(k.Min(i));
            if ((flags & (1 << i)) == 0)
            {
                _ = buff.PutFloat(k.Max(i));
            }
        }

        _ = buff.PutVarLong(k.GetId());
    }

    /// <inheritdoc/>
    public override Spatial Read(Span<byte> buff, ref int position)
    {
        int flags = DataUtils.ReadVarInt(buff, ref position);
        if (flags == -1)
        {
            long nullId = DataUtils.ReadVarLong(buff, ref position);
            return Create(nullId);
        }

        float[] minMax = new float[dimensions * 2];
        for (int i = 0; i < dimensions; i++)
        {
            float min = BinaryPrimitives.ReadSingleBigEndian(buff.Slice(position, 4));
            position += 4;
            float max;
            if ((flags & (1 << i)) != 0)
            {
                max = min;
            }
            else
            {
                max = BinaryPrimitives.ReadSingleBigEndian(buff.Slice(position, 4));
                position += 4;
            }

            minMax[i + i] = min;
            minMax[i + i + 1] = max;
        }

        long id = DataUtils.ReadVarLong(buff, ref position);
        return Create(id, minMax);
    }

    /// <summary>
    /// Whether the two objects overlap.
    /// </summary>
    public bool IsOverlap(Spatial a, Spatial b)
    {
        if (a.IsNull() || b.IsNull())
        {
            return false;
        }

        for (int i = 0; i < dimensions; i++)
        {
            if (a.Max(i) < b.Min(i) || a.Min(i) > b.Max(i))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Extends the bounds to contain the added object.
    /// </summary>
    public void IncreaseBounds(Spatial bounds, Spatial add)
    {
        if (add.IsNull() || bounds.IsNull())
        {
            return;
        }

        for (int i = 0; i < dimensions; i++)
        {
            float v = add.Min(i);
            if (v < bounds.Min(i))
            {
                bounds.SetMin(i, v);
            }

            v = add.Max(i);
            if (v > bounds.Max(i))
            {
                bounds.SetMax(i, v);
            }
        }
    }

    /// <summary>
    /// The area increase from extending the bounds to contain the added object.
    /// </summary>
    public float GetAreaIncrease(Spatial bounds, Spatial add)
    {
        if (bounds.IsNull() || add.IsNull())
        {
            return 0;
        }

        float min = bounds.Min(0);
        float max = bounds.Max(0);
        float areaOld = max - min;
        min = Math.Min(min, add.Min(0));
        max = Math.Max(max, add.Max(0));
        float areaNew = max - min;
        for (int i = 1; i < dimensions; i++)
        {
            min = bounds.Min(i);
            max = bounds.Max(i);
            areaOld *= max - min;
            min = Math.Min(min, add.Min(i));
            max = Math.Max(max, add.Max(i));
            areaNew *= max - min;
        }

        return areaNew - areaOld;
    }

    /// <summary>
    /// The combined area of both objects.
    /// </summary>
    internal float GetCombinedArea(Spatial a, Spatial b)
    {
        if (a.IsNull())
        {
            return GetArea(b);
        }
        else if (b.IsNull())
        {
            return GetArea(a);
        }

        float area = 1;
        for (int i = 0; i < dimensions; i++)
        {
            float min = Math.Min(a.Min(i), b.Min(i));
            float max = Math.Max(a.Max(i), b.Max(i));
            area *= max - min;
        }

        return area;
    }

    private float GetArea(Spatial a)
    {
        if (a.IsNull())
        {
            return 0;
        }

        float area = 1;
        for (int i = 0; i < dimensions; i++)
        {
            area *= a.Max(i) - a.Min(i);
        }

        return area;
    }

    /// <summary>
    /// Whether the bounds contain the object.
    /// </summary>
    public bool Contains(Spatial bounds, Spatial @object)
    {
        if (bounds.IsNull() || @object.IsNull())
        {
            return false;
        }

        for (int i = 0; i < dimensions; i++)
        {
            if (bounds.Min(i) > @object.Min(i) || bounds.Max(i) < @object.Max(i))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the object is completely inside the bounds without touching them.
    /// </summary>
    public bool IsInside(Spatial @object, Spatial bounds)
    {
        if (@object.IsNull() || bounds.IsNull())
        {
            return false;
        }

        for (int i = 0; i < dimensions; i++)
        {
            if (@object.Min(i) <= bounds.Min(i) || @object.Max(i) >= bounds.Max(i))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A bounding box starting with the given object.
    /// </summary>
    internal Spatial CreateBoundingBox(Spatial @object)
        => @object.IsNull() ? @object : @object.Clone(0);

    /// <summary>
    /// The most extreme pair, for a linear split. Null when no extremes exist.
    /// </summary>
    public int[]? GetExtremes(List<Spatial> list)
    {
        list = GetNotNull(list);
        if (list.Count == 0)
        {
            return null;
        }

        Spatial bounds = CreateBoundingBox(list[0]);
        Spatial boundsInner = CreateBoundingBox(bounds);
        for (int i = 0; i < dimensions; i++)
        {
            float t = boundsInner.Min(i);
            boundsInner.SetMin(i, boundsInner.Max(i));
            boundsInner.SetMax(i, t);
        }

        foreach (Spatial o in list)
        {
            IncreaseBounds(bounds, o);
            IncreaseMaxInnerBounds(boundsInner, o);
        }

        double best = 0;
        int bestDim = 0;
        for (int i = 0; i < dimensions; i++)
        {
            float inner = boundsInner.Max(i) - boundsInner.Min(i);
            if (inner < 0)
            {
                continue;
            }

            float outer = bounds.Max(i) - bounds.Min(i);
            float d = inner / outer;
            if (d > best)
            {
                best = d;
                bestDim = i;
            }
        }

        if (best <= 0)
        {
            return null;
        }

        float min = boundsInner.Min(bestDim);
        float max = boundsInner.Max(bestDim);
        int firstIndex = -1, lastIndex = -1;
        for (int i = 0; i < list.Count &&
                (firstIndex < 0 || lastIndex < 0); i++)
        {
            Spatial o = list[i];
            if (firstIndex < 0 && o.Max(bestDim) == min)
            {
                firstIndex = i;
            }
            else if (lastIndex < 0 && o.Min(bestDim) == max)
            {
                lastIndex = i;
            }
        }

        return new int[] { firstIndex, lastIndex };
    }

    private static List<Spatial> GetNotNull(List<Spatial> list)
    {
        bool foundNull = false;
        foreach (Spatial o in list)
        {
            if (o.IsNull())
            {
                foundNull = true;
                break;
            }
        }

        if (!foundNull)
        {
            return list;
        }

        var result = new List<Spatial>();
        foreach (Spatial o in list)
        {
            if (!o.IsNull())
            {
                result.Add(o);
            }
        }

        return result;
    }

    private void IncreaseMaxInnerBounds(Spatial bounds, Spatial add)
    {
        for (int i = 0; i < dimensions; i++)
        {
            bounds.SetMin(i, Math.Min(bounds.Min(i), add.Max(i)));
            bounds.SetMax(i, Math.Max(bounds.Max(i), add.Min(i)));
        }
    }
}
