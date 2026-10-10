namespace H2Sharp.MvStore.RTree;

/// <summary>
/// Boxes in 2+ dimensional space, where total ordering is not straight-forward. Usable as
/// keys for <see cref="MVRTreeMap{V}"/>.
/// </summary>
public interface Spatial
{
    /// <summary>
    /// The minimum value for the dimension.
    /// </summary>
    float Min(int dim);

    /// <summary>
    /// Sets the minimum value for the dimension.
    /// </summary>
    void SetMin(int dim, float x);

    /// <summary>
    /// The maximum value for the dimension.
    /// </summary>
    float Max(int dim);

    /// <summary>
    /// Sets the maximum value for the dimension.
    /// </summary>
    void SetMax(int dim, float x);

    /// <summary>
    /// A copy with a different id.
    /// </summary>
    Spatial Clone(long id);

    /// <summary>
    /// The id.
    /// </summary>
    long GetId();

    /// <summary>
    /// Whether this object has no value.
    /// </summary>
    bool IsNull();

    /// <summary>
    /// Whether the contents match, ignoring ids.
    /// </summary>
    bool EqualsIgnoringId(Spatial o);
}
