/*
 * Sprint 239: the spatial type's id comparison and null-bounds area rules.
 */
using H2Sharp.MvStore.RTree;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class SpatialDataTypeSurfaceTests
{
    private sealed class TestDataType : SpatialDataType
    {
        internal TestDataType(int dimensions) : base(dimensions) { }

        internal Spatial New(long id, params float[] minMax) => Create(id, minMax);
    }

    [Fact]
    public void ComparesByTheObjectId()
    {
        TestDataType type = new(2);
        Spatial a = type.New(1, 0, 1, 0, 1);
        Spatial b = type.New(2, 0, 2, 0, 2);
        Assert.Equal(0, type.Compare(a, a));
        Assert.True(type.Compare(a, b) < 0);
        Assert.True(type.Compare(b, a) > 0);
        Assert.True(type.Compare(null, a) < 0);
        Assert.True(type.Compare(a, null) > 0);
    }

    [Fact]
    public void NullBoundsDoNotIncreaseTheArea()
    {
        TestDataType type = new(1);
        Spatial nullBounds = type.New(0);
        Spatial box = type.New(1, 0, 1);
        Assert.Equal(0f, type.GetAreaIncrease(nullBounds, box));
        Assert.Equal(0f, type.GetAreaIncrease(box, nullBounds));
    }
}
