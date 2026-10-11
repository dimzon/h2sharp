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

    private readonly long increment;

    private readonly long startValue;

    private readonly long minValue;

    private readonly long maxValue;

    private readonly Cycle cycle;

    private readonly long cacheSize;

    private bool belongsToTable;

    private long baseValue;

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
        increment = options.GetIncrement(session);
        startValue = options.GetStartValue(session) ?? 1;
        minValue = options.GetMinValue(this, session);
        maxValue = options.GetMaxValue(this, session);
        cycle = options.GetCycle();
        cacheSize = options.GetCacheSize(session);
        baseValue = options.GetRestartValue(session, startValue);
        this.belongsToTable = belongsToTable;
    }

    /// <summary>
    /// Changes the sequence.
    /// </summary>
    /// <param name="baseValue">the new base value</param>
    /// <param name="startValue">the new start value</param>
    /// <param name="minValue">the new minimum</param>
    /// <param name="maxValue">the new maximum</param>
    /// <param name="increment">the new increment</param>
    public void Modify(long? baseValue, long? startValue, long? minValue, long? maxValue, long? increment)
        => throw new TODOException("Sequence.modify: sequences are not stored yet");

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
    public override string GetCreateSql()
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
    public Value GetNext(SessionLocal session)
        => throw new TODOException("Sequence.getNext: the store is not ported");

    /// <summary>Writes the current value without a margin.</summary>
    public void FlushWithoutMargin()
        => throw new TODOException("Sequence.flushWithoutMargin: the store is not ported");

    /// <summary>Writes the current value.</summary>
    /// <param name="session">the session</param>
    public void Flush(SessionLocal session)
        => throw new TODOException("Sequence.flush: the store is not ported");

    /// <summary>Closes this sequence.</summary>
    public void Close()
        => throw new TODOException("Sequence.close: the store is not ported");

    /// <inheritdoc/>
    public override int GetTypeId() => DbObject.Sequence;

    /// <inheritdoc/>
    public override void RemoveChildrenAndResources(SessionLocal session)
        => throw new TODOException("Sequence.removeChildrenAndResources: the store is not ported");

    /// <summary>Gets the base value.</summary>
    public long GetBaseValue() => baseValue;

    /// <summary>Gets the current value.</summary>
    public long GetCurrentValue()
        => throw new TODOException("Sequence.getCurrentValue: the store is not ported");

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

        /// <summary>Sequence is not cycled.</summary>
        NO_CYCLE,
    }
}
