/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.Parameter.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A parameter of a prepared statement.
 *
 * A parameter's type is not known until something happens: either a value is set, or the
 * parameter is matched to a column during optimisation. That is the whole reason this class
 * defers its type instead of declaring one — and it is why the SQL it prints is a positional
 * "?" and not a name, even when the statement was written with names. H2 rewrites named
 * parameters to positions at parse time, and a parameter that printed its own name would
 * print a name the database no longer has.
 *
 * An unset parameter reads as NULL rather than failing here. That is deliberate and it is what
 * allows a parameter inside a function table (a function evaluated per row, over a parameter
 * that the caller may or may not set); checkSet is the member that turns "unset" into an error,
 * and the places that need it call it.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions.Conditions;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>
/// A parameter of a prepared statement.
/// </summary>
/// <summary>
/// A parameter of a statement by position. It is a <see cref="ParameterInterface"/> in the
/// reference, and it is here too: the command layer hands its parameters to a caller to fill
/// in, and that only works if a parameter can be named as what it is rather than as an
/// expression.
/// </summary>
/// <remarks>
/// The port of <c>org.h2.expression.Parameter</c>, which in the reference extends
/// <c>Operation0</c> and implements <c>ParameterInterface</c>.
/// </remarks>
public sealed class Parameter : Expression, ParameterInterface
{
    private Value? value;

    private Column? column;

    private readonly int index;

    /// <summary>
    /// Creates a parameter.
    /// </summary>
    /// <param name="index">the 0-based index of this parameter in the statement</param>
    public Parameter(int index)
    {
        this.index = index;
    }

    /// <summary>
    /// Returns the maximum 1-based index.
    /// </summary>
    /// <param name="parameters">parameters</param>
    /// <returns>the maximum 1-based index, or -1</returns>
    public static int GetMaxIndex(IReadOnlyList<Parameter?> parameters)
    {
        int result = 0;
        foreach (Parameter? p in parameters)
        {
            if (p is not null)
            {
                int i = p.GetIndex() + 1;
                if (i > result)
                {
                    result = i;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the 0-based index of this parameter.
    /// </summary>
    public int GetIndex()
    {
        return index;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        return builder.Append('?').Append(index + 1);
    }

    /// <summary>
    /// Sets the value.
    /// </summary>
    /// <param name="v">the value</param>
    public void SetValue(Value v)
    {
        value = v;
    }

    /// <summary>
    /// Sets the value, closing the old one if it needs it.
    /// </summary>
    /// <param name="v">the value</param>
    /// <param name="closeOld">whether the old value must be closed</param>
    public void SetValue(Value v, bool closeOld)
    {
        // don't need to close the old value as temporary files are anyway removed
        value = v;
    }

    /// <summary>
    /// Gets the value, or NULL when none was set.
    /// </summary>
    /// <returns>the parameter value</returns>
    public Value GetParamValue()
    {
        // to allow parameters in function tables
        return value ?? ValueNull.Instance;
    }

    /// <inheritdoc/>
    public override bool IsValueSet()
    {
        return value is not null;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        return GetParamValue();
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        if (value is not null)
        {
            return value.GetTypeInfo();
        }

        if (column is not null)
        {
            return column.GetTypeInfo();
        }

        return TypeInfo.TypeUnknown;
    }

    /// <summary>
    /// Fails if no value was set for this parameter.
    /// </summary>
    public void CheckSet()
    {
        if (value is null)
        {
            throw DbException.Get(ErrorCode.ParameterNotSet1, "#" + (index + 1));
        }
    }

    /// <summary>
    /// Sets the column this parameter will be compared against, which gives the parameter its
    /// type when no value has been set.
    /// </summary>
    /// <param name="column">the column</param>
    public void SetColumn(Column? column)
    {
        this.column = column;
    }

    /// <summary>
    /// Gets the column this parameter is bound to, or null.
    /// </summary>
    /// <returns>the column</returns>
    public Column? GetColumn()
    {
        return column;
    }

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        if (column is null)
        {
            return this;
        }

        if (value is not null && !value.GetTypeInfo().Equals(column.GetTypeInfo()))
        {
            // The value has a type the column does not; the reference converts it once here
            // rather than on every row.
            value = value.ConvertToType(column.GetTypeInfo());
        }

        return this;
    }

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
        return visitor.GetVisitorType() != ExpressionVisitor.Independent || IsValueSet();
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        return 1;
    }
}
