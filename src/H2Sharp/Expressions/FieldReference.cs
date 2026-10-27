/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.FieldReference.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Util.Json;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>A field of a ROW value or a member of a JSON object.</summary>
public sealed class FieldReference : Operation1
{
    private readonly string fieldName;

    private int ordinal;

    /// <summary>Creates a field reference.</summary>
    public FieldReference(Expression arg, string fieldName) : base(arg)
        => this.fieldName = fieldName;

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
        => ParserUtil.QuoteIdentifier(arg.GetEnclosedSql(builder, sqlFlags).Append('.'), fieldName, sqlFlags);

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value value = arg.GetValue(session);
        if (value is ValueNull)
        {
            return ValueNull.Instance;
        }

        if (ordinal >= 0)
        {
            return ((ValueRow)value).GetList()[ordinal];
        }

        JsonValue decomposed = ((ValueJson)value.ConvertToType(TypeInfo.TypeJson, session)).GetDecomposition();
        if (decomposed is JsonObject jsonObject)
        {
            JsonValue? jsonValue = jsonObject.GetFirst(fieldName);
            if (jsonValue is not null)
            {
                return ValueJson.FromJson(jsonValue);
            }
        }

        return ValueNull.Instance;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        arg = arg.Optimize(session);
        TypeInfo argType = arg.GetTypeInfo();
        switch (argType.GetValueType())
        {
            case Value.Json:
                type = TypeInfo.TypeJson;
                ordinal = -1;
                break;
            case Value.Row:
                int fieldOrdinal = 0;
                foreach (KeyValuePair<string, TypeInfo> field in
                    ((ExtTypeInfoRow)argType.GetExtTypeInfo()!).GetFields())
                {
                    if (fieldName.Equals(field.Key, System.StringComparison.Ordinal))
                    {
                        type = field.Value;
                        ordinal = fieldOrdinal;
                        break;
                    }

                    fieldOrdinal++;
                }

                if (type is null)
                {
                    throw DbException.Get(ErrorCode.ColumnNotFound1, fieldName);
                }

                break;
            default:
                throw MvStore.Db.Store.GetInvalidExpressionTypeException("JSON | ROW", arg);
        }

        if (arg.IsConstant())
        {
            return TypedValueExpression.Get(GetValue(session), type!);
        }

        return this;
    }
}
