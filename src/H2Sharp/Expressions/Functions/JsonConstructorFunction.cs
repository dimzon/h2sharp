/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.function.JsonConstructorFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sliced to the static flags-SQL helper the JSON aggregates call. The rest of the reference
 * class — the JSON_OBJECT/JSON_ARRAY scalar functions — is not ported: no reader builds one.
 */
using System.Text;

using H2Sharp.Util.Json;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// The JSON constructor functions.
/// </summary>
public static class JsonConstructorFunction
{
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
}
