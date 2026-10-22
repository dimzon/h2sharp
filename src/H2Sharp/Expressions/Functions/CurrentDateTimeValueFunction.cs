/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.CurrentDateTimeValueFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The current date and time functions.
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// The current date and time functions: CURRENT_DATE, CURRENT_TIME, LOCALTIME,
/// CURRENT_TIMESTAMP and LOCALTIMESTAMP.
/// </summary>
/// <remarks>
/// The reference's class extends <c>Operation0</c> — an operation with no subexpressions — and
/// implements <c>NamedExpression</c>. The port has no <c>Operation0</c> of its own: the
/// operations without subexpressions extend <see cref="Expression"/> here and answer the four
/// members the reference's <c>Operation0</c> answers, which is what the members below are.
/// </remarks>
public sealed class CurrentDateTimeValueFunction : Expression, INamedExpression
{
    /// <summary>The function CURRENT_DATE.</summary>
    public const int CurrentDate = 0;

    /// <summary>The function CURRENT_TIME.</summary>
    public const int CurrentTime = 1;

    /// <summary>The function LOCALTIME.</summary>
    public const int Localtime = 2;

    /// <summary>The function CURRENT_TIMESTAMP.</summary>
    public const int CurrentTimestamp = 3;

    /// <summary>The function LOCALTIMESTAMP.</summary>
    public const int Localtimestamp = 4;

    private static readonly int[] Types =
    [
        Value.Date,
        Value.TimeTz,
        Value.Time,
        Value.TimestampTz,
        Value.Timestamp,
    ];

    private static readonly string[] Names =
    [
        "CURRENT_DATE",
        "CURRENT_TIME",
        "LOCALTIME",
        "CURRENT_TIMESTAMP",
        "LOCALTIMESTAMP",
    ];

    private readonly int function;

    private readonly int scale;

    /// <summary>
    /// The type of the result.
    /// </summary>
    private readonly TypeInfo type;

    /// <summary>
    /// Creates the function.
    /// </summary>
    /// <param name="function">the function, one of the constants of this class</param>
    /// <param name="scale">the scale, or a negative number for the default of the type</param>
    public CurrentDateTimeValueFunction(int function, int scale)
    {
        this.function = function;
        this.scale = scale;

        // The reference keeps the scale as it was written and uses the default of the type
        // only for the type itself: the SQL of the function shows the scale that was written,
        // and a function written without one shows none.
        int typeScale = scale;
        if (typeScale < 0)
        {
            typeScale = function >= CurrentTimestamp ? ValueTimestamp.DefaultScale : ValueTime.DefaultScale;
        }

        type = TypeInfo.GetTypeInfo(Types[function], 0L, typeScale, null);
    }

    /// <summary>
    /// Gets the name of a function.
    /// </summary>
    /// <param name="function">the function</param>
    /// <returns>the name</returns>
    public static string GetName(int function) => Names[function];

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
        => session.CurrentTimestamp().CastToValue(type, session);

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => type;

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session) => this;

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        if (visitor.GetVisitorType() == ExpressionVisitor.Deterministic)
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override int GetCost() => 1;

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append(Names[function]);
        if (scale >= 0)
        {
            _ = builder.Append('(').Append(scale).Append(')');
        }

        return builder;
    }

    /// <inheritdoc/>
    public string GetName() => Names[function];
}
