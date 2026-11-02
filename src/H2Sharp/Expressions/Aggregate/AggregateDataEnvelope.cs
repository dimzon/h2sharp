/*
 * Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.aggregate.AggregateDataEnvelope.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The accumulation path uses the existing geometry conversions and envelope union; the
 * column lookup hands the quick path of the aggregate to the spatial index.
 */
using H2Sharp.Engine;
using H2Sharp.MvStore.Db;
using H2Sharp.Tables;
using H2Sharp.Util.Geometry;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

/// <summary>Data stored while calculating ENVELOPE.</summary>
internal sealed class AggregateDataEnvelope : AggregateData
{
    private double[]? envelope;

    /// <summary>
    /// The index (if any) for the column specified in the geometry aggregate.
    /// </summary>
    /// <param name="on">the expression (usually a column expression)</param>
    /// <returns>the index, or null</returns>
    internal static H2Sharp.Indexes.Index? GetGeometryColumnIndex(Expression on)
    {
        if (on is ExpressionColumn col)
        {
            Column column = col.GetColumn()!;
            if (column.GetTypeInfo().GetValueType() == Value.Geometry)
            {
                TableFilter? filter = col.GetTableFilter();
                if (filter is not null)
                {
                    foreach (H2Sharp.Indexes.Index index in filter.GetTable()!.GetIndexes())
                    {
                        if (index is MVSpatialIndex && index.IsFirstColumn(column))
                        {
                            return index;
                        }
                    }
                }
            }
        }

        return null;
    }

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
