/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.aggregate.JavaAggregate.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The host-provided aggregate wrapper. The lifecycle is the CLR one (Init /
 * Accumulate / GetResult), while DISTINCT, FILTER and OVER stay on the reference's
 * execution paths.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Command.Query;
using H2Sharp.Data.Extensions;
using H2Sharp.Engine;
using H2Sharp.Extensions;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

/// <summary>
/// Wraps a host-provided aggregate.
/// </summary>
internal sealed class ClrAggregate : AbstractAggregate
{
    private readonly AggregateDefinition definition;
    private readonly int baseCost;
    private readonly bool rendersDistinct;

    internal ClrAggregate(
        AggregateDefinition definition,
        Expression[] args,
        Select select,
        bool distinct,
        int baseCost = 5,
        bool rendersDistinct = false)
        : base(select, args, distinct)
    {
        this.definition = definition;
        this.baseCost = baseCost;
        this.rendersDistinct = rendersDistinct;
    }

    private bool EffectiveDistinct => distinct && !definition.Options.InvariantToDuplicates;

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        base.Optimize(session);
        TypeInfo[] inputTypes = new TypeInfo[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            inputTypes[i] = args[i].GetTypeInfo();
        }

        IH2Aggregate probe = CreateInstance();
        type = Call(() => probe.GetResultType(inputTypes));
        return this;
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        if (!base.IsEverything(visitor))
        {
            return false;
        }

        switch (visitor.GetVisitorType())
        {
            case ExpressionVisitor.Deterministic:
                if (!definition.Options.Deterministic)
                {
                    return false;
                }

                break;
            case ExpressionVisitor.OptimizableAggregate:
                return false;
            default:
                break;
        }

        foreach (Expression arg in args)
        {
            if (!arg.IsEverything(visitor))
            {
                return false;
            }
        }

        return filterCondition is null || filterCondition.IsEverything(visitor);
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        int cost = baseCost;
        foreach (Expression arg in args)
        {
            cost += arg.GetCost();
        }

        if (filterCondition is not null)
        {
            cost += filterCondition.GetCost();
        }

        return cost;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        ParserUtil.QuoteIdentifier(builder, definition.Name, sqlFlags).Append('(');
        if (rendersDistinct && distinct)
        {
            builder.Append("DISTINCT ");
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            args[i].GetUnenclosedSql(builder, sqlFlags);
        }

        builder.Append(')');
        return AppendTailConditions(builder, sqlFlags, false);
    }

    /// <inheritdoc/>
    protected override object CreateAggregateData()
    {
        return EffectiveDistinct
            ? new AggregateDataCollecting(true, false, AggregateDataCollecting.NullCollectionMode.Ignored)
            : CreateInstance();
    }

    /// <inheritdoc/>
    protected override Value GetAggregatedValue(SessionLocal session, object? aggregateData)
    {
        IH2Aggregate? aggregate = null;
        bool transient = false;
        try
        {
            if (EffectiveDistinct)
            {
                aggregate = CreateInstance();
                transient = true;
                if (aggregateData is AggregateDataCollecting data)
                {
                    Value[]? values = data.GetArray();
                    if (values is not null)
                    {
                        foreach (Value value in values)
                        {
                            if (args.Length == 1)
                            {
                                aggregate.Accumulate(Call(() => AggregateValueConverter.ToDefaultObject(value)));
                            }
                            else
                            {
                                Value[] row = ((ValueRow)value).GetList();
                                object?[] argValues = new object?[args.Length];
                                for (int i = 0; i < args.Length; i++)
                                {
                                    int index = i;
                                    argValues[i] = Call(() => AggregateValueConverter.ToDefaultObject(row[index]));
                                }

                                aggregate.Accumulate(Call(() => (object?)argValues));
                            }
                        }
                    }
                }
            }
            else if (aggregateData is IH2Aggregate stored)
            {
                aggregate = stored;
            }
            else
            {
                aggregate = CreateInstance();
                transient = true;
            }

            object? result = Call(aggregate.GetResult);
            return result is null
                ? ValueNull.Instance
                : Call(() => AggregateValueConverter.ToValue(result, type!, session));
        }
        finally
        {
            if (transient)
            {
                Call(() =>
                {
                    DisposeIfNeeded(aggregate);
                    return 0;
                });
            }
        }
    }

    /// <inheritdoc/>
    protected override void UpdateAggregate(SessionLocal session, object aggregateData)
    {
        UpdateData(session, aggregateData, null);
    }

    /// <inheritdoc/>
    protected override void UpdateFromExpressions(SessionLocal session, object aggregateData, Value?[] array)
    {
        if (filterCondition is not null && !array[GetNumExpressions() - 1]!.IsTrue())
        {
            return;
        }

        UpdateData(session, aggregateData, array);
    }

    /// <inheritdoc/>
    protected override void UpdateGroupAggregates(SessionLocal session, int stage)
    {
        base.UpdateGroupAggregates(session, stage);
        foreach (Expression arg in args)
        {
            arg.UpdateAggregate(session, stage);
        }
    }

    /// <inheritdoc/>
    protected override int GetNumExpressions()
    {
        return args.Length + (filterCondition is not null ? 1 : 0);
    }

    /// <inheritdoc/>
    protected override void RememberExpressions(SessionLocal session, Value?[] array)
    {
        int i = 0;
        for (; i < args.Length; i++)
        {
            array[i] = args[i].GetValue(session);
        }

        if (filterCondition is not null)
        {
            array[i] = ValueBoolean.Get(filterCondition.GetBooleanValue(session));
        }
    }

    private void UpdateData(SessionLocal session, object aggregateData, Value?[]? remembered)
    {
        if (EffectiveDistinct)
        {
            Value[] argValues = new Value[args.Length];
            Value? arg = null;
            for (int i = 0; i < args.Length; i++)
            {
                arg = remembered is null ? args[i].GetValue(session) : remembered[i];
                argValues[i] = arg ?? ValueNull.Instance;
            }

            AggregateDataCollecting data = (AggregateDataCollecting)aggregateData;
            Call(() =>
            {
                data.Add(session, args.Length == 1 ? arg : ValueRow.Get(argValues));
                return 0;
            });
            return;
        }

        IH2Aggregate aggregate = (IH2Aggregate)aggregateData;
        object?[] values = new object?[args.Length];
        object? value = null;
        for (int i = 0; i < args.Length; i++)
        {
            Value v = remembered is null ? args[i].GetValue(session) : remembered[i]!;
            value = Call(() => AggregateValueConverter.ToDefaultObject(v));
            values[i] = value;
        }

        object? argument = args.Length == 1 ? value : values;
        Call(() =>
        {
            aggregate.Accumulate(argument);
            return 0;
        });
    }

    private IH2Aggregate CreateInstance()
    {
        return Call(() =>
        {
            IH2Aggregate aggregate = definition.Provider.CreateAggregate();
            aggregate.Init();
            return aggregate;
        });
    }

    private static void DisposeIfNeeded(object? value)
    {
        if (value is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private T Call<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (H2SharpException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw DbException.Get(ErrorCode.ExceptionInFunction1, e, definition.Name);
        }
    }
}
