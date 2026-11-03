/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Dialect value literals against the reference: both sides build the same value and print its
 * SQL literal, and the texts are compared line by line. This is a read-only look at the dialect:
 * any mismatch is reported, not fixed, because the dialect is consumed by the LINQ providers.
 * The cases known to diverge (null branches of the nullable overloads, E-strings, geometry,
 * row fields, clobs) are deliberately left out of this comparison and are listed in
 * MIGRATION_STATUS.md.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class DialectLiteralOracleTests
{
    private static readonly Guid Uuid = new("123e4567-e89b-12d3-a456-426614174000");

    [JavaOracleFact]
    public void ValueLiteralsMatchTheReference()
    {
        List<string> port =
        [
            "real_inf=" + Render(b => Dialect.AppendReal(b, float.PositiveInfinity)),
            "real_ninf=" + Render(b => Dialect.AppendReal(b, float.NegativeInfinity)),
            "real_nan=" + Render(b => Dialect.AppendReal(b, float.NaN)),
            "real_value=" + Render(b => Dialect.AppendReal(b, 1.5f)),
            "double_inf=" + Render(b => Dialect.AppendDouble(b, double.PositiveInfinity)),
            "double_ninf=" + Render(b => Dialect.AppendDouble(b, double.NegativeInfinity)),
            "double_nan=" + Render(b => Dialect.AppendDouble(b, double.NaN)),
            "double_value=" + Render(b => Dialect.AppendDouble(b, 2.25d)),
            "numeric_max=" + Render(b => Dialect.AppendNumeric(b, 79228162514264337593543950335m)),
            "string_value=" + Render(b => Dialect.AppendString(b, "it's")),
            "char_value=" + Render(b => Dialect.AppendChar(b, 'x')),
            "uuid_value=" + Render(b => Dialect.AppendUuid(b, Uuid)),
        ];

        string[] names =
        [
            "real_inf", "real_ninf", "real_nan", "real_value",
            "double_inf", "double_ninf", "double_nan", "double_value",
            "numeric_max",
            "string_value",
            "char_value",
            "uuid_value",
        ];

        List<string> java = OracleRunner.RunWithMembers(
            names,
            """
            print("real_inf", org.h2.value.ValueReal.get(Float.POSITIVE_INFINITY));
            print("real_ninf", org.h2.value.ValueReal.get(Float.NEGATIVE_INFINITY));
            print("real_nan", org.h2.value.ValueReal.get(Float.NaN));
            print("real_value", org.h2.value.ValueReal.get(1.5f));
            print("double_inf", org.h2.value.ValueDouble.get(Double.POSITIVE_INFINITY));
            print("double_ninf", org.h2.value.ValueDouble.get(Double.NEGATIVE_INFINITY));
            print("double_nan", org.h2.value.ValueDouble.get(Double.NaN));
            print("double_value", org.h2.value.ValueDouble.get(2.25d));
            print("numeric_max", org.h2.value.ValueNumeric.get(new java.math.BigDecimal("79228162514264337593543950335")));
            print("string_value", org.h2.value.ValueVarchar.get("it's", null));
            print("char_value", org.h2.value.ValueChar.get("x"));
            print("uuid_value", org.h2.value.ValueUuid.get(java.util.UUID.fromString("123e4567-e89b-12d3-a456-426614174000")));
            """,
            """
            private static void print(String name, org.h2.value.Value v) {
              StringBuilder b = new StringBuilder();
              v.getSQL(b, org.h2.util.HasSQL.DEFAULT_SQL_FLAGS);
              System.out.println(name + "=" + b);
            }
            """);
        Assert.Equal(java, port);
    }

    private static string Render(Func<StringBuilder, StringBuilder> build)
    {
        StringBuilder builder = new();
        _ = build(builder);
        return builder.ToString();
    }
}
