/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.function.JsonConstructorFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Uses the existing JSON constructor/value infrastructure. No JSON parser, writer or encoder
 * is introduced here; the System.Text.Json integration and canonical-byte targets are retained.
 */
using System.IO;
using System.Text;

using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util.Json;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// The JSON constructor functions.
/// </summary>
public sealed class JsonConstructorFunction : OperationN, ExpressionWithFlags, INamedExpression
{
    private readonly bool array;
    private int flags;

    /// <summary>Creates JSON_ARRAY or JSON_OBJECT.</summary>
    /// <param name="array">true for JSON_ARRAY, false for JSON_OBJECT</param>
    public JsonConstructorFunction(bool array) : base(new Expression[4]) => this.array = array;

    /// <inheritdoc/>
    public void SetFlags(int flags) => this.flags = flags;

    /// <inheritdoc/>
    public int GetFlags() => flags;

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session) => array ? JsonArray(session, args) : JsonObject(session, args);

    private Value JsonObject(SessionLocal session, Expression[] args)
    {
        using MemoryStream output = new();
        output.WriteByte((byte)'{');
        for (int i = 0; i < args.Length;)
        {
            string? name = args[i++].GetValue(session).GetString();
            if (name is null) throw DbException.GetInvalidValueException("JSON_OBJECT key", "NULL");
            Value value = args[i++].GetValue(session);
            if (value == ValueNull.Instance || value == ValueJson.Null)
            {
                if ((flags & JsonConstructorUtils.JsonAbsentOnNull) != 0) continue;
                value = ValueJson.Null;
            }
            JsonConstructorUtils.JsonObjectAppend(output, name, value);
        }
        return JsonConstructorUtils.JsonObjectFinish(output, flags);
    }

    private Value JsonArray(SessionLocal session, Expression[] args)
    {
        using MemoryStream output = new();
        output.WriteByte((byte)'[');
        // Java's labelled block only skips the ordinary argument loop after a subquery.
        // No query is evaluated as a scalar when JSON_ARRAY consumes all of its rows.
        if (args.Length == 1 && args[0] is Subquery query)
        {
            foreach (Value value in query.GetAllRows(session)) JsonConstructorUtils.JsonArrayAppend(output, value, flags);
        }
        else if (args.Length == 1 && args[0] is Format format && format.GetSubexpression(0) is Subquery formattedQuery)
        {
            foreach (Value value in formattedQuery.GetAllRows(session))
                JsonConstructorUtils.JsonArrayAppend(output, format.GetValue(value), flags);
        }
        else
        {
            foreach (Expression arg in args) JsonConstructorUtils.JsonArrayAppend(output, arg.GetValue(session), flags);
        }
        output.WriteByte((byte)']');
        return ValueJson.GetInternal(output.ToArray());
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        bool allConst = OptimizeArguments(session, true);
        type = TypeInfo.TypeJson;
        return allConst ? TypedValueExpression.GetTypedIfNull(GetValue(session), type) : this;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        builder.Append(GetName()).Append('(');
        if (array) WriteExpressions(builder, args, sqlFlags);
        else
        {
            for (int i = 0; i < args.Length;)
            {
                if (i > 0) builder.Append(", ");
                args[i++].GetUnenclosedSql(builder, sqlFlags).Append(": ");
                args[i++].GetUnenclosedSql(builder, sqlFlags);
            }
        }
        return GetJsonFunctionFlagsSql(builder, flags, array).Append(')');
    }

    /// <summary>
    /// Appends flags of a JSON function to the specified builder.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="flags">flags to append</param>
    /// <param name="forArray">whether the function is an array function</param>
    /// <returns>the specified builder</returns>
    public static StringBuilder GetJsonFunctionFlagsSql(StringBuilder builder, int flags, bool forArray)
    {
        if ((flags & JsonConstructorUtils.JsonAbsentOnNull) != 0)
        {
            if (!forArray)
            {
                _ = builder.Append(" ABSENT ON NULL");
            }
        }
        else if (forArray)
        {
            _ = builder.Append(" NULL ON NULL");
        }

        if (!forArray && (flags & JsonConstructorUtils.JsonWithUniqueKeys) != 0)
        {
            _ = builder.Append(" WITH UNIQUE KEYS");
        }

        return builder;
    }

    /// <inheritdoc/>
    public string GetName() => array ? "JSON_ARRAY" : "JSON_OBJECT";
}
