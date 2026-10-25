/*
 * Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.aggregate.AggregateDataEnvelope.
 * Copyright 2026 H2Sharp contributors.
 * The accumulation path uses the existing geometry conversions and envelope union.
 * The spatial-index lookup/quick path comes with MVSpatialIndex, which is not ported.
 */
using H2Sharp.Engine;
using H2Sharp.Util.Geometry;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

/// <summary>Data stored while calculating ENVELOPE.</summary>
internal sealed class AggregateDataEnvelope : AggregateData
{
    private double[]? envelope;

    /// <inheritdoc/>
    internal override void Add(SessionLocal session, Value? v)
    {
        if (v == ValueNull.Instance) return;
        ValueGeometry geometry = (ValueGeometry)v!.ConvertToType(TypeInfo.TypeGeometry);
        envelope = GeometryUtils.Union(envelope, geometry.GetEnvelopeNoCopy());
    }

    /// <inheritdoc/>
    internal override Value GetValue(SessionLocal session) => ValueGeometry.FromEnvelope(envelope);
}
