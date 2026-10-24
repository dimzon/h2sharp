/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.DataTypeSQLFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What is left out, and why:
 *
 * The CONSTANT, DOMAIN and ROUTINE object kinds need constants, domains and function
 * aliases, none of which is ported; they fall into the default branch and answer NULL,
 * which is what the reference answers when the lookup finds nothing — and here there is
 * nothing to find.
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// DATA_TYPE_SQL() function.
/// </summary>
public sealed class DataTypeSQLFunction : FunctionN
{
    /// <summary>
    /// Creates a DATA_TYPE_SQL function.
    /// </summary>
    /// <param name="objectSchema">the schema name expression</param>
    /// <param name="objectName">the object name expression</param>
    /// <param name="objectType">the object kind expression</param>
    /// <param name="typeIdentifier">the column ordinal or 'TYPE' expression</param>
    public DataTypeSQLFunction(Expression objectSchema, Expression objectName, Expression objectType, Expression typeIdentifier)
        : base([objectSchema, objectName, objectType, typeIdentifier])
    {
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value? v1, Value? v2, Value? v3)
    {
        Schemas.Schema? schema = session.GetDatabase().FindSchema(v1!.GetString()!);
        if (schema is null)
        {
            return ValueNull.Instance;
        }

        string objectName = v2!.GetString()!;
        string objectType = v3!.GetString()!;
        string? typeIdentifier = args[3].GetValue(session).GetString();
        if (typeIdentifier is null)
        {
            return ValueNull.Instance;
        }

        TypeInfo type;
        switch (objectType)
        {
            case "TABLE":
            {
                Tables.Table? table = schema.FindTableOrView(session, objectName);
                if (table is null)
                {
                    return ValueNull.Instance;
                }

                if (!int.TryParse(typeIdentifier, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int ordinal))
                {
                    return ValueNull.Instance;
                }

                Tables.Column[] columns = table.GetColumns();
                if (ordinal < 1 || ordinal > columns.Length)
                {
                    return ValueNull.Instance;
                }

                type = columns[ordinal - 1].GetTypeInfo();
                break;
            }

            default:
                return ValueNull.Instance;
        }

        return ValueVarchar.Get(type.GetSql(new StringBuilder(), HasSql.DefaultSqlFlags).ToString(), session);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        OptimizeArguments(session, allConst: false);
        type = TypeInfo.TypeVarchar;
        return this;
    }

    /// <inheritdoc/>
    public override string GetName() => "DATA_TYPE_SQL";
}
