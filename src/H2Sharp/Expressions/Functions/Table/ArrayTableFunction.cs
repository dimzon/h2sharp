/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.function.table.ArrayTableFunction. */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions.Table;

/// <summary>UNNEST, TABLE, and TABLE_DISTINCT.</summary>
public sealed class ArrayTableFunction : TableFunction
{
    /// <summary>UNNEST.</summary>
    public const int Unnest = 0;
    /// <summary>TABLE.</summary>
    public const int Table = 1;
    /// <summary>TABLE_DISTINCT.</summary>
    public const int TableDistinct = 2;
    private Column[] columns = null!;
    private static readonly string[] Names = ["UNNEST", "TABLE", "TABLE_DISTINCT"];
    private readonly int function;
    /// <summary>Creates a table function.</summary>
    public ArrayTableFunction(int function) : base(new Expression[1]) => this.function = function;
    /// <inheritdoc/>
    public override ResultInterface GetValue(SessionLocal session) => GetTable(session, false);
    /// <inheritdoc/>
    public override ResultInterface GetValueTemplate(SessionLocal session) => GetTable(session, true);
    /// <inheritdoc/>
    public override void Optimize(SessionLocal session)
    {
        base.Optimize(session);
        if (args.Length < 1) throw DbException.Get(Api.ErrorCode.InvalidParameterCount2, GetName(), ">0");
    }
    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        if (function == Unnest)
        {
            base.GetSql(builder, sqlFlags);
            if (args.Length < columns.Length) builder.Append(" WITH ORDINALITY");
        }
        else
        {
            builder.Append(GetName()).Append('(');
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) builder.Append(", ");
                builder.Append(columns[i].GetCreateSql()).Append('=');
                args[i].GetUnenclosedSql(builder, sqlFlags);
            }
            builder.Append(')');
        }
        return builder;
    }
    /// <summary>Sets output columns.</summary>
    public void SetColumns(List<Column> columns) => this.columns = [.. columns];
    private ResultInterface GetTable(SessionLocal session, bool onlyColumnList)
    {
        int totalColumns = columns.Length;
        Expression[] header = new Expression[totalColumns];
        for (int i = 0; i < totalColumns; i++)
            header[i] = new ExpressionColumn(session.GetDatabase(), columns[i]);
        LocalResult result = new(session, header, totalColumns, totalColumns);
        if (!onlyColumnList && function == TableDistinct) result.SetDistinct();
        if (!onlyColumnList)
        {
            bool unnest = function == Unnest;
            int len = unnest ? args.Length : totalColumns;
            bool addNumber = unnest && len < totalColumns;
            Value[][] list = new Value[len][];
            int rows = 0;
            for (int i = 0; i < len; i++)
            {
                Value v = args[i].GetValue(session);
                if (v is ValueNull) list[i] = Value.EmptyValues;
                else
                {
                    Value[] values;
                    switch (v.GetValueType())
                    {
                        case Value.Json:
                            Util.Json.JsonValue json = ((ValueJson)v).GetDecomposition();
                            values = json is Util.Json.JsonArray array ? array.GetArray<Value>(ValueJson.FromJson) : Value.EmptyValues;
                            break;
                        case Value.Array:
                        case Value.Row: values = ((ValueCollectionBase)v).GetList(); break;
                        default: values = [v]; break;
                    }
                    list[i] = values;
                    rows = Math.Max(rows, values.Length);
                }
            }
            for (int row = 0; row < rows; row++)
            {
                Value[] r = new Value[totalColumns];
                for (int j = 0; j < len; j++)
                {
                    Value[] values = list[j];
                    Value v = row >= values.Length ? ValueNull.Instance : values[row];
                    if (row < values.Length && !unnest) v = v.ConvertForAssignTo(columns[j].GetTypeInfo(), session, columns[j]);
                    r[j] = v;
                }
                if (addNumber) r[len] = ValueInteger.Get(row + 1);
                result.AddRow(r);
            }
        }
        result.Done();
        return result;
    }
    /// <inheritdoc/>
    public override string GetName() => Names[function];
    /// <inheritdoc/>
    public override bool IsDeterministic() => true;
    /// <summary>Gets the function kind.</summary>
    public int GetFunctionType() => function;
}
