/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The table function that runs a registered .NET table UDF. It is the bridge between the
 * reference's table-function shape (a TableFunction that answers a template and a result)
 * and the host-provided implementation, which chooses its columns per call.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Extensions;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions.Table;

/// <summary>A table function backed by a registered .NET UDF.</summary>
internal sealed class UdfTableFunction : TableFunction
{
    private readonly TableUdfDefinition definition;

    internal UdfTableFunction(TableUdfDefinition definition, Expression[] args)
        : base(args)
        => this.definition = definition;

    /// <inheritdoc/>
    public override ResultInterface GetValue(SessionLocal session) => Build(session, template: false);

    /// <inheritdoc/>
    public override ResultInterface GetValueTemplate(SessionLocal session) => Build(session, template: true);

    /// <inheritdoc/>
    public override string GetName() => definition.Name;

    /// <inheritdoc/>
    public override bool IsDeterministic() => definition.Deterministic;

    private ResultInterface Build(SessionLocal session, bool template)
    {
        Value[] values = new Value[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            values[i] = args[i].GetValue(session);
        }

        H2Sharp.Data.Extensions.H2TableUdfResult data = definition.Invoke(values);
        int columnCount = data.Columns.Count;
        Expression[] header = new Expression[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            H2Sharp.Data.Extensions.H2TableUdfColumn column = data.Columns[i];
            header[i] = new ExpressionColumn(
                session.GetDatabase(), new Column(column.Name, ClrValueConverter.GetTypeInfo(column.Type)));
        }

        LocalResult result = new(session, header, columnCount, columnCount);
        if (!template)
        {
            foreach (object?[] row in data.Rows)
            {
                if (row.Length != columnCount)
                {
                    throw DbException.Get(Api.ErrorCode.ColumnCountDoesNotMatch);
                }

                Value[] values2 = new Value[columnCount];
                for (int i = 0; i < columnCount; i++)
                {
                    values2[i] = ClrValueConverter.ToValue(row[i]);
                }

                result.AddRow(values2);
            }
        }

        result.Done();
        return result;
    }
}
