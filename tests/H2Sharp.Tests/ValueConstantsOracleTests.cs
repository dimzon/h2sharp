/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The value type numbers are the most load bearing constants in the port: TypeInfo
 * switches on them, DataType is indexed by them, the tokenizer recognises literals by them
 * and they travel in stored rows. A single wrong number makes the engine disagree with
 * itself, not merely with Java. So the whole block is compared against the reference by
 * reflection, which catches a renumbering that a hand written list of expectations would
 * happily agree with.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class ValueConstantsOracleTests
{
    [JavaOracleFact]
    public void ValueTypeNumbers_MatchReferenceJvm()
    {
        string body = """
                      // All declared int fields, including package-private ones: the
                      // generated C# exposes the whole block as public constants, and a
                      // private or package-private field is unreadable through reflection
                      // without setAccessible. Filtering by visibility here would hide a
                      // dropped constant instead of comparing it.
                      java.lang.reflect.Field[] fields =
                          org.h2.value.Value.class.getDeclaredFields();
                      java.util.TreeMap<String, Integer> sorted = new java.util.TreeMap<>();
                      for (java.lang.reflect.Field f : fields) {
                        if (f.getType() == int.class) {
                          f.setAccessible(true);
                          sorted.put(f.getName(), (Integer) f.get(null));
                        }
                      }
                      for (java.util.Map.Entry<String, Integer> e : sorted.entrySet()) {
                        System.out.println(e.getKey() + "=" + e.getValue());
                      }
            """;

        List<string> java = OracleRunner.Run([], body);

        // The C# names are PascalCase, the Java names are UPPER_SNAKE, so both sides are
        // normalised. Compared as a set: only the name/value pairing is a contract.
        static string Normalise(string name) =>
            name.Replace("_", "", StringComparison.Ordinal).ToUpperInvariant();

        List<string> csharp =
        [
            .. typeof(Value)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(static f => f.IsLiteral && f.FieldType == typeof(int))
                .Select(static f => Normalise(f.Name) + "="
                    + Convert.ToInt32(f.GetRawConstantValue(), CultureInfo.InvariantCulture)),
        ];

        List<string> javaNormalised =
        [
            .. java.Select(static line =>
            {
                int equals = line.IndexOf('=', StringComparison.Ordinal);
                return Normalise(line[..equals]) + line[equals..];
            }),
        ];

        javaNormalised.Sort(StringComparer.Ordinal);
        csharp.Sort(StringComparer.Ordinal);
        Assert.Equal(javaNormalised, csharp);
    }

    [Fact]
    public void TypeNumbersCoverEveryIndexFromNullToTypeCount()
    {
        // The reference builds the numbering by chaining `PREV + 1` from UNKNOWN = -1, so
        // the numbers are dense and TYPE_COUNT is ROW + 1. TypeInfo and DataType index
        // arrays with them, which means a gap or a duplicate would be a silent out of range
        // read rather than a compile error, so the coverage is checked directly.
        Assert.Equal(-1, Value.Unknown);
        Assert.Equal(0, Value.Null);

        int[] assigned = typeof(Value)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static f => f.IsLiteral && f.FieldType == typeof(int))
            .Select(static f => Convert.ToInt32(f.GetRawConstantValue(), CultureInfo.InvariantCulture))
            .Where(static v => v >= Value.Null && v < Value.TypeCount)
            .ToArray();

        Assert.Equal(
            Value.TypeCount - Value.Null,
            assigned.Distinct().Count());

        for (int type = Value.Null; type < Value.TypeCount; type++)
        {
            Assert.Contains(type, assigned);
        }

        // TYPE_COUNT is derived, not declared independently, so the last type plus one is
        // the bound everything else indexes against.
        Assert.Equal(Value.Row + 1, Value.TypeCount);
    }
}
