/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.schema.Sequence.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A sequence: the numbers a column takes when nobody gives it one.
 *
 * The part of a sequence that is easy to miss is the base value and the margin. A sequence hands
 * out a block of numbers at once and remembers how far it got; a crash before the block is used
 * leaves the database claiming numbers it never gave, and the margin is the room that absorbs
 * that. That is why flush and flushWithoutMargin are two methods rather than one: the first runs
 * at the end of a transaction and may write a margin, the second runs at shutdown and must not,
 * because nothing more will be handed out afterwards and the margin would then be the gap.
 *
 * GetDefaultMinValue and GetDefaultMaxValue are ported because they are pure arithmetic over the
 * type and the increment, and they are where a wrong port shows up first: a sequence that goes
 * the wrong way is legal SQL that produces the wrong numbers.
 *
 * Not ported: handing out numbers and the bookkeeping around it — the store, the locks and the
 * cache of pre-read values all belong to the transaction layer.
 */
using System;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Command.Ddl;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Schemas;

/// <summary>
/// A sequence is created using the statement CREATE SEQUENCE.
/// </summary>
public sealed class Sequence : SchemaObject
{
    /// <summary>
    /// The default number of values a sequence hands out at once.
    /// </summary>
    public const int DefaultCacheSize = 32;

    private readonly TypeInfo dataType;

    private long increment;

    private long startValue;

    private long minValue;

    private long maxValue;

    private Cycle cycle;

    private long cacheSize;

    private bool belongsToTable;

    private long baseValue;

    /// <summary>
    /// How far the sequence has cached its values: the values up to here are already
    /// counted out and do not have to be written down again.
    /// </summary>
    private long margin;

    /// <summary>
    /// Creates a sequence.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="schema">the schema</param>
    /// <param name="id">the object id</param>
    /// <param name="name">the name</param>
    /// <param name="options">the options</param>
    /// <param name="belongsToTable">whether the sequence belongs to a table</param>
    public Sequence(SessionLocal session, Schema schema, int id, string name, SequenceOptions options, bool belongsToTable)
        : base(schema, id, name, Trace.Sequence)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        dataType = options.GetDataType();
        if (dataType is null)
        {
            dataType = session.GetMode().DecimalSequences ? TypeInfo.TypeNumericBigint : TypeInfo.TypeBigint;
            options.SetDataType(dataType);
        }

        long[] bounds = options.GetBounds();
        long? writtenIncrement = options.GetIncrement(session);
        increment = writtenIncrement ?? 1;
        long? start = options.GetStartValue(session);
        long? min = options.GetMinValue(null, session);
        long? max = options.GetMaxValue(null, session);
        minValue = min ?? GetDefaultMinValue(start, increment, bounds);
        maxValue = max ?? GetDefaultMaxValue(start, increment, bounds);
        startValue = start ?? (increment >= 0 ? minValue : maxValue);
        cycle = options.GetCycle();
        long? writtenCacheSize = options.GetCacheSize(session);
        bool mayAdjustCacheSize = writtenCacheSize is null;
        cacheSize = writtenCacheSize ?? DefaultCacheSize;
        baseValue = options.GetRestartValue(session, startValue) ?? startValue;
        if (cycle == Cycle.EXHAUSTED)
        {
            // An exhausted sequence starts over: the reference does the same, so that
            // ALTER SEQUENCE ... RESTART works on one.
            baseValue = startValue;
        }

        // The options are checked against each other: a sequence whose start is outside
        // its bounds, or whose cache does not fit its range, is not a sequence.
        cacheSize = CheckOptions(
            baseValue, startValue, minValue, maxValue, increment, cacheSize, mayAdjustCacheSize);
        margin = baseValue;
        this.belongsToTable = belongsToTable;
    }

    /// <summary>
    /// Changes the sequence.
    /// </summary>
    /// <remarks>
    /// The reference's modify: a value that is not given keeps its value, and the options
    /// are checked against each other, because a sequence whose start is outside its bounds
    /// is not a sequence.
    /// </remarks>
    /// <param name="newBaseValue">the new base value, or null</param>
    /// <param name="newStartValue">the new start value, or null</param>
    /// <param name="newMinValue">the new minimum, or null</param>
    /// <param name="newMaxValue">the new maximum, or null</param>
    /// <param name="newIncrement">the new increment, or null</param>
    /// <param name="newCycle">the new cycle, or null</param>
    /// <param name="newCacheSize">the new cache size, or null</param>
    public void Modify(
        long? newBaseValue,
        long? newStartValue,
        long? newMinValue,
        long? newMaxValue,
        long? newIncrement,
        Cycle? newCycle,
        long? newCacheSize)
    {
        long baseValueAsLong = newBaseValue ?? baseValue;
        long startValueAsLong = newStartValue ?? startValue;
        long minValueAsLong = newMinValue ?? minValue;
        long maxValueAsLong = newMaxValue ?? maxValue;
        long incrementAsLong = newIncrement ?? increment;
        long cacheSizeAsLong;
        bool mayAdjustCacheSize;
        if (newCacheSize is not null)
        {
            cacheSizeAsLong = newCacheSize.Value;
            mayAdjustCacheSize = false;
        }
        else
        {
            cacheSizeAsLong = cacheSize;
            mayAdjustCacheSize = true;
        }

        cacheSizeAsLong = CheckOptions(
            baseValueAsLong, startValueAsLong, minValueAsLong, maxValueAsLong, incrementAsLong,
            cacheSizeAsLong, mayAdjustCacheSize);
        Cycle cycle = newCycle ?? this.cycle;
        if (newCycle is null)
        {
            if (cycle == Cycle.EXHAUSTED && newBaseValue is not null)
            {
                cycle = Cycle.NO_CYCLE;
            }
        }
        else if (cycle == Cycle.EXHAUSTED)
        {
            baseValueAsLong = startValueAsLong;
        }

        margin = baseValue = baseValueAsLong;
        this.startValue = startValueAsLong;
        this.minValue = minValueAsLong;
        this.maxValue = maxValueAsLong;
        this.increment = incrementAsLong;
        this.cacheSize = cacheSizeAsLong;
        this.cycle = cycle;
    }

    /// <summary>
    /// Checks the options of the sequence against each other.
    /// </summary>
    /// <remarks>
    /// The reference's checkOptions. The base value and the start value must lie inside the
    /// bounds, the increment must not be zero, and the cache size is brought down to what
    /// the range allows.
    /// </remarks>
    /// <param name="baseValue">the base value</param>
    /// <param name="startValue">the start value</param>
    /// <param name="minValue">the minimum value</param>
    /// <param name="maxValue">the maximum value</param>
    /// <param name="increment">the increment</param>
    /// <param name="cacheSize">the cache size</param>
    /// <param name="mayAdjustCacheSize">whether the cache size may be brought down</param>
    /// <returns>the cache size</returns>
    private long CheckOptions(
        long baseValue,
        long startValue,
        long minValue,
        long maxValue,
        long increment,
        long cacheSize,
        bool mayAdjustCacheSize)
    {
        if (minValue <= baseValue && baseValue <= maxValue
            && minValue <= startValue && startValue <= maxValue
            && minValue < maxValue && increment != 0L)
        {
            long range = maxValue - minValue;
            long magnitude = increment == long.MinValue ? long.MinValue : Math.Abs(increment);
            if ((ulong)magnitude <= (ulong)range && cacheSize >= 0L)
            {
                if (cacheSize <= 1L)
                {
                    return 1L;
                }

                long maxCacheSize = GetMaxCacheSize(range, increment);
                if (cacheSize <= maxCacheSize)
                {
                    return cacheSize;
                }

                if (mayAdjustCacheSize)
                {
                    return maxCacheSize;
                }
            }
        }

        throw DbException.Get(
            ErrorCode.SequenceAttributesInvalid7,
            GetName(),
            baseValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            startValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            minValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            maxValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            increment.ToString(System.Globalization.CultureInfo.InvariantCulture),
            cacheSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The largest cache the range of the sequence allows.
    /// </summary>
    /// <param name="range">the range of the sequence</param>
    /// <param name="increment">the increment</param>
    /// <returns>the largest cache size</returns>
    private static long GetMaxCacheSize(long range, long increment)
    {
        if (increment > 0L)
        {
            if (range < 0)
            {
                range = long.MaxValue;
            }
            else
            {
                range += increment;
                if (range < 0)
                {
                    range = long.MaxValue;
                }
            }
        }
        else
        {
            range = -range;
            if (range > 0)
            {
                range = long.MinValue;
            }
            else
            {
                range += increment;
                if (range >= 0)
                {
                    range = long.MinValue;
                }
            }
        }

        return range / increment;
    }

    /// <summary>
    /// Gets the default minimum value of a sequence.
    /// </summary>
    /// <remarks>
    /// The first value a sequence hands out, when nothing else was written. A sequence that
    /// counts down starts at the bottom of its type; one that counts up starts at one, unless a
    /// start value was written that is below even that.
    /// </remarks>
    /// <param name="startValue">the start value, or null</param>
    /// <param name="increment">the increment</param>
    /// <param name="bounds">the bounds of the type</param>
    /// <returns>the minimum value</returns>
    public static long GetDefaultMinValue(long? startValue, long increment, long[] bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        long v = increment >= 0 ? 1 : bounds[0];
        if (startValue is not null && increment >= 0 && startValue.Value < v)
        {
            v = startValue.Value;
        }

        return v;
    }

    /// <summary>
    /// Gets the default maximum value of a sequence.
    /// </summary>
    /// <param name="startValue">the start value, or null</param>
    /// <param name="increment">the increment</param>
    /// <param name="bounds">the bounds of the type</param>
    /// <returns>the maximum value</returns>
    public static long GetDefaultMaxValue(long? startValue, long increment, long[] bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        long v = increment >= 0 ? bounds[1] : -1;
        if (startValue is not null && increment >= 0 && startValue.Value > v)
        {
            v = startValue.Value;
        }

        return v;
    }

    /// <summary>Gets whether this sequence belongs to a table.</summary>
    public bool GetBelongsToTable() => belongsToTable;

    /// <summary>Gets the data type of the sequence.</summary>
    public TypeInfo GetDataType() => dataType;

    /// <summary>
    /// Gets the effective precision of the data type: how many digits the sequence really has
    /// room for.
    /// </summary>
    /// <remarks>
    /// Not the same as the precision of the type. A NUMERIC whose whole part is wider than a long
    /// can hold has that whole part truncated down to what a long holds, and the scale comes back
    /// with it — the digits are moved, not dropped. DECFLOAT has no scale, so it is only capped.
    /// </remarks>
    /// <returns>the effective precision</returns>
    public int GetEffectivePrecision()
    {
        TypeInfo type = dataType;
        switch (type.GetValueType())
        {
            case Value.Numeric:
                int p = (int)type.GetPrecision();
                int s = type.GetScale();
                if (p - s > ValueBigint.DecimalPrecision)
                {
                    return ValueBigint.DecimalPrecision + s;
                }

                return p;
            case Value.Decfloat:
                return (int)Math.Min(type.GetPrecision(), ValueBigint.DecimalPrecision);
            default:
                return (int)type.GetPrecision();
        }
    }

    /// <summary>Gets the increment.</summary>
    public long GetIncrement() => increment;

    /// <summary>Gets the start value.</summary>
    public long GetStartValue() => startValue;

    /// <summary>Gets the minimum value.</summary>
    public long GetMinValue() => minValue;

    /// <summary>Gets the maximum value.</summary>
    public long GetMaxValue() => maxValue;

    /// <summary>Gets the cycle rule.</summary>
    public Cycle GetCycle() => cycle;

    /// <summary>Gets the SQL that drops this sequence.</summary>
    public override string GetDropSql()
    {
        StringBuilder builder = new("DROP SEQUENCE ");
        return GetSql(builder, 0).ToString();
    }

    /// <inheritdoc/>
    public override string? GetCreateSql()
    {
        StringBuilder builder = new("CREATE SEQUENCE ");
        _ = GetSql(builder, 0);
        StringBuilder options = new();
        _ = GetSequenceOptionsSql(options);
        return builder.Append(options).ToString();
    }

    /// <summary>
    /// Appends the options of this sequence, as they would be written.
    /// </summary>
    /// <param name="builder">the builder</param>
    /// <returns>the builder</returns>
    public StringBuilder GetSequenceOptionsSql(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        bool[] known = [true, startValue != 1, increment != 1, cacheSize != DefaultCacheSize,
            cycle != Cycle.NO_CYCLE, minValue != GetDefaultMinValue(null, increment, [0, 0]),
            maxValue != GetDefaultMaxValue(null, increment, [0, 0])];
        string[] commands =
        [
            "START WITH " + startValue,
            "INCREMENT BY " + increment,
            "CACHE " + cacheSize,
            cycle == Cycle.CYCLE ? "CYCLE" : "NO CYCLE",
            "MINVALUE " + minValue,
            "MAXVALUE " + maxValue,
        ];
        for (int i = 0; i < commands.Length; i++)
        {
            if (known[i])
            {
                _ = builder.Append(' ').Append(commands[i]);
            }
        }

        return builder;
    }

    /// <summary>Gets the next value.</summary>
    /// <param name="session">the session</param>
    /// <returns>the next value</returns>
    /// <summary>
    /// The next value of this sequence.
    /// </summary>
    /// <remarks>
    /// The reference's getNext. The values are counted out in blocks of the cache size, so
    /// that the block can be written down once; the margin is how far the block reaches. When
    /// the sequence runs out, it either starts over or is exhausted, and an exhausted
    /// sequence refuses to answer.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <returns>the value</returns>
    public Value GetNext(SessionLocal session)
    {
        long result;
        bool needsFlush;
        lock (this)
        {
            if (cycle == Cycle.EXHAUSTED)
            {
                throw DbException.Get(ErrorCode.SequenceExhausted, GetName());
            }

            result = baseValue;
            long newBase = result + increment;
            needsFlush = increment > 0 ? Increment(result, newBase) : Decrement(result, newBase);
        }

        if (needsFlush)
        {
            Flush(session);
        }

        return ValueBigint.Get(result).CastToValue(dataType, session.CastDataProvider);
    }

    /// <summary>
    /// Counts the sequence up, and starts it over when it runs out.
    /// </summary>
    /// <param name="oldBase">the value before</param>
    /// <param name="newBase">the value after</param>
    /// <returns>whether the block has to be written down</returns>
    private bool Increment(long oldBase, long newBase)
    {
        bool needsFlush = false;

        // An old base that is not negative and a new one that is means the counter
        // went over the end of the world.
        if (newBase > maxValue || (~oldBase & newBase) < 0)
        {
            newBase = minValue;
            needsFlush = true;
            if (cycle == Cycle.CYCLE)
            {
                margin = newBase + increment * (cacheSize - 1);
            }
            else
            {
                margin = newBase;
                cycle = Cycle.EXHAUSTED;
            }
        }
        else if (newBase > margin)
        {
            long newMargin = newBase + increment * (cacheSize - 1);
            if (newMargin > maxValue || (~newBase & newMargin) < 0)
            {
                // The values near the end of the sequence are not cached, for simplicity.
                newMargin = newBase;
            }

            margin = newMargin;
            needsFlush = true;
        }

        baseValue = newBase;
        return needsFlush;
    }

    /// <summary>
    /// Counts the sequence down, and starts it over when it runs out.
    /// </summary>
    /// <param name="oldBase">the value before</param>
    /// <param name="newBase">the value after</param>
    /// <returns>whether the block has to be written down</returns>
    private bool Decrement(long oldBase, long newBase)
    {
        bool needsFlush = false;

        // An old base that is negative and a new one that is not means the counter went
        // over the end of the world.
        if (newBase < minValue || (oldBase & ~newBase) < 0)
        {
            newBase = maxValue;
            needsFlush = true;
            if (cycle == Cycle.CYCLE)
            {
                margin = newBase + increment * (cacheSize - 1);
            }
            else
            {
                margin = newBase;
                cycle = Cycle.EXHAUSTED;
            }
        }
        else if (newBase < margin)
        {
            long newMargin = newBase + increment * (cacheSize - 1);
            if (newMargin < minValue || (newBase & ~newMargin) < 0)
            {
                // The values near the end of the sequence are not cached, for simplicity.
                newMargin = newBase;
            }

            margin = newMargin;
            needsFlush = true;
        }

        baseValue = newBase;
        return needsFlush;
    }

    /// <summary>
    /// Writes the current value without a margin.
    /// </summary>
    /// <remarks>
    /// The sequences of this port live in memory, so there is nothing to write down: the
    /// store of the sequences is not ported, and a database of this port is in memory
    /// anyway.
    /// </remarks>
    public void FlushWithoutMargin()
    {
    }

    /// <summary>
    /// Writes the current value.
    /// </summary>
    /// <remarks>
    /// Nothing to write down: the sequences of this port live in memory.
    /// </remarks>
    /// <param name="session">the session</param>
    public void Flush(SessionLocal session)
    {
    }

    /// <summary>Closes this sequence.</summary>
    public void Close()
        => throw new TODOException("Sequence.close: the store is not ported");

    /// <inheritdoc/>
    public override int GetTypeId() => DbObject.Sequence;

    /// <inheritdoc/>
    public override void RemoveChildrenAndResources(SessionLocal session)
    {
        // The sequences of this port live in memory, so there is nothing to remove: the
        // store of the sequences is not ported.
    }

    /// <summary>Gets the base value.</summary>
    public long GetBaseValue() => baseValue;

    /// <summary>Gets the current value.</summary>
    /// <remarks>
    /// The value that was handed out last, which is the base value less the increment.
    /// </remarks>
    /// <returns>the current value</returns>
    public long GetCurrentValue() => baseValue - increment;

    /// <summary>Sets whether this sequence belongs to a table.</summary>
    /// <param name="b">true if it does</param>
    public void SetBelongsToTable(bool b) => belongsToTable = b;

    /// <summary>Gets the number of values handed out at once.</summary>
    public long GetCacheSize() => cacheSize;

    /// <summary>
    /// Whether a sequence cycles.
    /// </summary>
    public enum Cycle
    {
        /// <summary>Sequence is cycled.</summary>
        CYCLE,

        /// <summary>Sequence is not cycled and isn't exhausted yet.</summary>
        NO_CYCLE,

        /// <summary>Sequence is not cycled and was already exhausted.</summary>
        EXHAUSTED,
    }
}
