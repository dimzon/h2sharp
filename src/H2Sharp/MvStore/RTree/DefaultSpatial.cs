using System;

namespace H2Sharp.MvStore.RTree;

/// <summary>
/// The plain spatial key: an id plus min/max floats per dimension.
/// </summary>
/// <remarks>
/// Public, unlike the reference's package-private twin: the test suite needs a spatial key
/// but not the engine's value-based one, and this is the implementation the data type itself
/// creates.
/// </remarks>
public sealed class DefaultSpatial : Spatial, IEquatable<DefaultSpatial?>
{
    private readonly long id;
    private readonly float[] minMax;

    /// <summary>
    /// A key: min x, max x, min y, max y, and so on.
    /// </summary>
    public DefaultSpatial(long id, params float[] minMax)
    {
        this.id = id;
        this.minMax = minMax;
    }

    private DefaultSpatial(long id, DefaultSpatial other)
    {
        this.id = id;
        minMax = (float[])other.minMax.Clone();
    }

    /// <inheritdoc/>
    public float Min(int dim) => minMax[dim + dim];

    /// <inheritdoc/>
    public void SetMin(int dim, float x) => minMax[dim + dim] = x;

    /// <inheritdoc/>
    public float Max(int dim) => minMax[dim + dim + 1];

    /// <inheritdoc/>
    public void SetMax(int dim, float x) => minMax[dim + dim + 1] = x;

    /// <inheritdoc/>
    public Spatial Clone(long id) => new DefaultSpatial(id, this);

    /// <inheritdoc/>
    public long GetId() => id;

    /// <inheritdoc/>
    public bool IsNull() => minMax.Length == 0;

    /// <inheritdoc/>
    public bool EqualsIgnoringId(Spatial o) => o is DefaultSpatial other && minMax.AsSpan().SequenceEqual(other.minMax);

    /// <inheritdoc/>
    public bool Equals(DefaultSpatial? other) => other is not null && id == other.id && EqualsIgnoringId(other);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as DefaultSpatial);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(id);
}
