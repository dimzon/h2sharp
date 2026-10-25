/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.SequenceOptions.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The options of a sequence, as they were written.
 *
 * Every option is an expression until it is read: "START WITH 1 + 1" is a legal start value and
 * only becomes a number when the sequence is created. That is why this class keeps expressions
 * and hands out values — getStartValue, getIncrement and the rest — and why the getters take a
 * session: an expression that reads "now" needs one.
 *
 * The bounds are the one part that is not an option. They come from the data type, and a
 * sequence of SMALLINT cannot hand out more than a SMALLINT holds however it was written. The
 * difference between that and a rejected statement is worth keeping: "write more than the type
 * holds" is refused as unsupported, while "write a range wider than the type" is clamped to it,
 * because a sequence whose START WITH is above the type's maximum is legal SQL with an obvious
 * meaning, and a sequence of a type that is not numeric is not.
 *
 * GetBounds is ported rather than refused because it is arithmetic over the type, not storage,
 * and it is the one member here that a differential test can pin exactly.
 */
using System;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Values;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// The options of a sequence.
/// </summary>
public class SequenceOptions
{
    private TypeInfo dataType = TypeInfo.TypeBigint;

    private long[]? bounds;

    private Expression? startValue;

    private Expression? restartValue;

    private Expression? increment;

    private Expression? maxValue;

    private Expression? minValue;

    private Expression? cacheSize;

    private Sequence.Cycle cycle = Sequence.Cycle.NO_CYCLE;

    /// <summary>
    /// Creates options with nothing set.
    /// </summary>
    public SequenceOptions()
    {
    }

    /// <summary>
    /// Creates options from an existing sequence and a data type.
    /// </summary>
    /// <param name="oldSequence">the sequence</param>
    /// <param name="dataType">the data type</param>
    public SequenceOptions(Sequence oldSequence, TypeInfo dataType)
    {
        ArgumentNullException.ThrowIfNull(oldSequence);
        this.dataType = dataType;
        bounds = SequenceOptions.GetBounds(dataType);
    }

    /// <summary>Gets the data type.</summary>
    /// <returns>the data type</returns>
    public TypeInfo GetDataType() => dataType;

    /// <summary>Sets the data type.</summary>
    /// <param name="dataType">the data type</param>
    public void SetDataType(TypeInfo dataType)
    {
        this.dataType = dataType;
        bounds = null;
    }

    /// <summary>Gets the start value.</summary>
    /// <param name="session">the session</param>
    /// <returns>the start value, or null when it was not written</returns>
    public long? GetStartValue(SessionLocal session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Check(GetLong(session, startValue));
    }

    /// <summary>Sets the start value.</summary>
    /// <param name="start">the expression</param>
    public void SetStartValue(Expression? start) => startValue = start;

    /// <summary>Gets the value to restart from.</summary>
    /// <remarks>
    /// The reference's getRestartValue: null when no restart was written, so that the
    /// sequence keeps the value it has; the start value when the restart was written
    /// without a value of its own; and the value of the expression otherwise.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="startValue">the start value</param>
    /// <returns>the restart value, or null when there is none</returns>
    public long? GetRestartValue(SessionLocal session, long startValue)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Check(ReferenceEquals(restartValue, Expressions.ValueExpression.Default)
            ? startValue
            : GetLong(session, restartValue));
    }

    /// <summary>Sets the value to restart from.</summary>
    /// <param name="restart">the expression</param>
    public void SetRestartValue(Expression? restart) => restartValue = restart;

    /// <summary>Gets the increment.</summary>
    /// <param name="session">the session</param>
    /// <returns>the increment</returns>
    public long? GetIncrement(SessionLocal session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Check(GetLong(session, increment));
    }

    /// <summary>Sets the increment.</summary>
    /// <param name="increment">the expression</param>
    public void SetIncrement(Expression? increment) => this.increment = increment;

    /// <summary>Gets the maximum value.</summary>
    /// <param name="sequence">the sequence</param>
    /// <param name="session">the session</param>
    /// <returns>the maximum value</returns>
    public long? GetMaxValue(Sequence? sequence, SessionLocal session)
    {
        ArgumentNullException.ThrowIfNull(session);
        long? v;
        if (ReferenceEquals(maxValue, Expressions.ValueExpression.Null) && sequence is not null)
        {
            v = Sequence.GetDefaultMaxValue(GetCurrentStart(sequence, session),
                increment is not null ? GetIncrement(session)!.Value : sequence.GetIncrement(), GetBounds());
        }
        else
        {
            v = GetLong(session, maxValue);
        }

        return Check(v);
    }

    /// <summary>Sets the maximum value.</summary>
    /// <param name="maxValue">the expression</param>
    public void SetMaxValue(Expression? maxValue) => this.maxValue = maxValue;

    /// <summary>Gets the minimum value.</summary>
    /// <param name="sequence">the sequence</param>
    /// <param name="session">the session</param>
    /// <returns>the minimum value</returns>
    public long? GetMinValue(Sequence? sequence, SessionLocal session)
    {
        ArgumentNullException.ThrowIfNull(session);
        long? v;
        if (ReferenceEquals(minValue, Expressions.ValueExpression.Null) && sequence is not null)
        {
            v = Sequence.GetDefaultMinValue(GetCurrentStart(sequence, session),
                increment is not null ? GetIncrement(session)!.Value : sequence.GetIncrement(), GetBounds());
        }
        else
        {
            v = GetLong(session, minValue);
        }

        return Check(v);
    }

    /// <summary>Sets the minimum value.</summary>
    /// <param name="minValue">the expression</param>
    public void SetMinValue(Expression? minValue) => this.minValue = minValue;

    /// <summary>Gets the bounds of the data type.</summary>
    /// <returns>the minimum and the maximum</returns>
    public long[] GetBounds()
    {
        long[] b = bounds ??= GetBounds(dataType);
        return b;
    }

    /// <summary>
    /// Gets the bounds (min, max) of a data type.
    /// </summary>
    /// <remarks>
    /// The float and double bounds are not the limits of the type and are not meant to be: a
    /// sequence of REAL is limited by what can be told apart, and the reference keeps those two
    /// constants rather than deriving them. The NUMERIC branch is different — it reads the
    /// precision, refuses a scale it cannot honour, and treats more than eighteen digits as the
    /// whole range of a long rather than computing a number past the point where multiplying by
    /// ten stops meaning anything.
    /// </remarks>
    /// <param name="dataType">the data type</param>
    /// <returns>the bounds (an array with 2 elements)</returns>
    public static long[] GetBounds(TypeInfo dataType)
    {
        ArgumentNullException.ThrowIfNull(dataType);
        long min;
        long max;
        switch (dataType.GetValueType())
        {
            case Value.Tinyint:
                min = sbyte.MinValue;
                max = sbyte.MaxValue;
                break;
            case Value.Smallint:
                min = short.MinValue;
                max = short.MaxValue;
                break;
            case Value.Integer:
                min = int.MinValue;
                max = int.MaxValue;
                break;
            case Value.Bigint:
                min = long.MinValue;
                max = long.MaxValue;
                break;
            case Value.Real:
                min = -0x100_0000;
                max = 0x100_0000;
                break;
            case Value.Double:
                min = -0x20_0000_0000_0000L;
                max = 0x20_0000_0000_0000L;
                break;
            case Value.Numeric:
                if (dataType.GetScale() != 0)
                {
                    throw DbException.GetUnsupportedException(dataType.GetTraceSql());
                }

                long p = dataType.GetPrecision() - dataType.GetScale();
                if (p <= 0)
                {
                    throw DbException.GetUnsupportedException(dataType.GetTraceSql());
                }

                if (p > 18)
                {
                    min = long.MinValue;
                    max = long.MaxValue;
                }
                else
                {
                    max = 10;
                    for (int i = 1; i < p; i++)
                    {
                        max *= 10;
                    }

                    // The reference writes this as `min = - --max`: a pre-decrement, so the
                    // bound is one below the largest number p digits can spell. Written out
                    // here because C# has no pre-decrement on anything but a variable.
                    max--;
                    min = -max;
                }

                break;
            case Value.Decfloat:
                long pd = dataType.GetPrecision();
                if (pd > 18)
                {
                    min = long.MinValue;
                    max = long.MaxValue;
                }
                else
                {
                    max = 10;
                    for (int i = 1; i < pd; i++)
                    {
                        max *= 10;
                    }

                    min = -max;
                }

                break;
            default:
                throw DbException.GetUnsupportedException(dataType.GetTraceSql());
        }

        return [min, max];
    }

    /// <summary>Gets the cycle rule.</summary>
    /// <returns>the cycle rule</returns>
    public Sequence.Cycle GetCycle() => cycle;

    /// <summary>Sets the cycle rule.</summary>
    /// <param name="cycle">the cycle rule</param>
    public void SetCycle(Sequence.Cycle cycle) => this.cycle = cycle;

    /// <summary>Gets the number of values handed out at once.</summary>
    /// <remarks>
    /// The reference's getCacheSize: null when the size was not written, so that the
    /// sequence can bring it down to what its range allows.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <returns>the cache size, or null when it was not written</returns>
    public long? GetCacheSize(SessionLocal session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return GetLong(session, cacheSize);
    }

    /// <summary>Sets the number of values handed out at once.</summary>
    /// <param name="cacheSize">the expression</param>
    public void SetCacheSize(Expression? cacheSize) => this.cacheSize = cacheSize;

    // H2 2.5 SequenceOptions.getLong: an option that was not written, or is NULL, has no value.
    private static long? GetLong(SessionLocal session, Expression? expression)
    {
        if (expression is null)
        {
            return null;
        }

        Value value = expression.Optimize(session).GetValue(session);
        return value == ValueNull.Instance ? null : value.GetLong();
    }

    // H2 2.5 SequenceOptions.check: an explicit option has to fit the sequence's data type.
    private long? Check(long? value)
    {
        if (value is null)
        {
            return null;
        }

        long[] limits = GetBounds();
        long v = value.Value;
        if (v < limits[0] || v > limits[1])
        {
            throw DbException.Get(Api.ErrorCode.NumericValueOutOfRange1,
                v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return value;
    }

    // H2 2.5 SequenceOptions.getCurrentStart.
    private long GetCurrentStart(Sequence sequence, SessionLocal session)
        => startValue is not null ? GetStartValue(session)!.Value : sequence.GetBaseValue();
}
