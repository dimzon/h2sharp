/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.aggregate.AggregateDataGCD.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Numerics;

using H2Sharp.Engine;
using H2Sharp.Expressions.Functions;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

/// <summary>
/// Data stored while calculating GCD_AGG or LCM_AGG aggregate.
/// </summary>
internal sealed class AggregateDataGCD : AggregateData
{
    private readonly bool lcm;

    private bool skipRemaining, overflow;

    private BigInteger? bi;

    internal AggregateDataGCD(bool lcm)
    {
        this.lcm = lcm;
    }

    /// <inheritdoc/>
    internal override void Add(SessionLocal session, Value? v)
    {
        if (v is null or ValueNull || skipRemaining)
        {
            return;
        }

        BigInteger n = v.GetBigInteger();
        if (lcm)
        {
            if (n.Sign == 0)
            {
                bi = BigInteger.Zero;
                skipRemaining = true;
                overflow = false;
            }
            else
            {
                if (bi is null)
                {
                    bi = BigInteger.Abs(n);
                }
                else if (!overflow)
                {
                    bi = BigInteger.Abs(bi.Value * n) / BigInteger.GreatestCommonDivisor(bi.Value, n);
                    overflow = bi.Value.GetBitLength() > GCDFunction.MaxBitLength;
                }
            }
        }
        else
        {
            if (bi is null)
            {
                bi = BigInteger.Abs(n);
            }
            else if (n.Sign != 0)
            {
                bi = BigInteger.GreatestCommonDivisor(bi.Value, n);
            }
            else
            {
                return;
            }

            skipRemaining = bi == BigInteger.One;
        }
    }

    /// <inheritdoc/>
    internal override Value GetValue(SessionLocal session)
    {
        if (overflow)
        {
            throw DbException.GetValueTooLongException(
                "NUMERIC", "unknown least common multiple", -1);
        }

        return bi is not null ? ValueNumeric.Get(bi.Value) : ValueNull.Instance;
    }
}
