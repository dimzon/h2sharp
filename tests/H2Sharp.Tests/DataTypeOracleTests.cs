/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the catalogue of types.
 *
 * A catalogue is a table, and a table is worth nothing unless every row of it is read out of the
 * reference and written back: a type that is missing means a column cannot be declared with that
 * name, and a limit that is one digit out means a value is accepted or refused wrongly.
 *
 * So both sides are walked in the same order and every cell is compared: the value type, the width
 * it may declare, the default width, the number of decimal places, whether it takes a width at all,
 * and the prefix and suffix a literal of it is written with. The names are compared as a set, and
 * the SQL type is compared by name rather than by number, because .NET's SqlDbType and JDBC's Types
 * are two different enumerations with different names for the same thing and neither of them is
 * the other's numbering. The places where there is no counterpart at all are written down in the
 * port rather than left for a reader of ResultSetMetaData to discover.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class DataTypeOracleTests
{
    /// <summary>
    /// The names the two sides are asked about, which is every name of the catalogue rather than a
    /// sample of them.
    /// </summary>
    private static List<string> Names() => [.. DataType.AllNames().Order(StringComparer.Ordinal)];

    [JavaOracleFact]
    public void EveryTypeNameIsInTheReferenceAndEveryReferenceNameIsHere()
    {
        List<string> names = Names();
        List<string> text = [.. names];
        text.Add("NOT A TYPE");
        text.Add("INTEGER2");
        text.Add("VARCHAR3");

        List<string> java = OracleRunner.Run(
                text,
                """
                for (int i = 0; i < inputs.length; i++) {
                  org.h2.value.DataType t = org.h2.value.DataType.getTypeByName(
                      inputs[i], org.h2.engine.Mode.getRegular());
                  System.out.println(hex(t == null ? "<none>" : String.valueOf(t.type)));
                }
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(line => Decode(line))
            .ToList();

        Assert.Equal(text.Count, java.Count);
        for (int i = 0; i < names.Count; i++)
        {
            // Every name of this catalogue must be a type of the reference, and must be a type of
            // the same value type: a name that answers a different number here is a name that
            // would build a different column.
            Assert.True(
                java[i] != "<none>",
                $"the reference has no type called {names[i]}");
            int expected = int.Parse(java[i], CultureInfo.InvariantCulture);
            int actual = DataType.GetTypeByName(names[i])!.Type;
            Assert.True(
                expected == actual,
                $"{names[i]}: the reference says value type {expected}, this port says {actual}");
        }

        // And the three that are not types are not types here either.
        for (int i = names.Count; i < text.Count; i++)
        {
            Assert.Equal("<none>", java[i]);
            Assert.Null(DataType.GetTypeByName(text[i]));
        }
    }

    [JavaOracleFact]
    public void EveryRowHasTheSameLimitsAndTheSameLiteral()
    {
        // The rows, by value type, each asked of both sides. The numbers go over as a file of ints
        // because the value types are small and are not text.
        List<int> types = [.. Enumerable.Range(0, DataType.MaxValueTypeProbe).Where(DataType.HasRow)];

        List<string> java = OracleRunner.Run(
                [],
                """
                for (int i = 0; i < nums.length; i++) {
                  org.h2.value.DataType t = org.h2.value.DataType.getDataType(nums[i]);
                  StringBuilder b = new StringBuilder();
                  b.append(t.type).append('|');
                  b.append(t.minPrecision).append('|');
                  b.append(t.maxPrecision).append('|');
                  b.append(t.minScale).append('|');
                  b.append(t.maxScale).append('|');
                  b.append(t.defaultPrecision).append('|');
                  b.append(t.defaultScale).append('|');
                  b.append(t.supportsPrecision).append('|');
                  b.append(t.supportsScale).append('|');
                  b.append(t.caseSensitive).append('|');
                  b.append(String.valueOf(t.prefix)).append('|');
                  b.append(String.valueOf(t.suffix)).append('|');
                  b.append(String.valueOf(t.params));
                  System.out.println(hex(b.toString()));
                }
                """,
                numbers: types,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(line => Decode(line))
            .ToList();

        Assert.Equal(types.Count, java.Count);
        for (int i = 0; i < types.Count; i++)
        {
            Assert.True(java[i] == Describe(types[i]), $"value type {types[i]}:\n  java:   {java[i]}\n  csharp: {Describe(types[i])}");
        }
    }

    /// <summary>
    /// How many rows the catalogue has, and which value types they are.
    /// </summary>
    [JavaOracleFact]
    public void TheCatalogueHasTheSameRowsAsTheReference()
    {
        // The count is compared rather than written down: a row that is missing here is a type that
        // cannot be declared, and a row that is extra is a type that means nothing.
        List<string> java = OracleRunner.Run(
                [],
                """
                int rows = 0;
                for (int type = 0; type < 256; type++) {
                  if (org.h2.value.DataType.getDataType(type).type == type) {
                    rows++;
                  }
                }
                System.out.println(hex("" + rows));
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(line => Decode(line))
            .ToList();

        Assert.Single(java);
        Assert.Equal(int.Parse(java[0], CultureInfo.InvariantCulture), DataType.RowCount());
    }

    private static string Describe(int valueType)
    {
        DataType t = DataType.GetDataType(valueType);
        return string.Join(
            "|",
            t.Type,
            t.MinPrecision.ToString(CultureInfo.InvariantCulture),
            t.MaxPrecision.ToString(CultureInfo.InvariantCulture),
            t.MinScale.ToString(CultureInfo.InvariantCulture),
            t.MaxScale.ToString(CultureInfo.InvariantCulture),
            t.DefaultPrecision.ToString(CultureInfo.InvariantCulture),
            t.DefaultScale.ToString(CultureInfo.InvariantCulture),
            J(t.SupportsPrecision),
            J(t.SupportsScale),
            J(t.CaseSensitive),
            t.Prefix ?? "null",
            t.Suffix ?? "null",
            t.Params ?? "null");
    }

    private static string J(bool value) => value ? "true" : "false";

    /// <summary>
    /// Reads the hex the oracle prints, or passes the line through when it is already text.
    /// </summary>
    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        if (only.Length == 0)
        {
            return string.Empty;
        }

        if (!only.All(static c => char.IsAsciiHexDigit(c)))
        {
            return only;
        }

        StringBuilder b = new(only.Length / 4);
        for (int i = 0; i + 3 < only.Length; i += 4)
        {
            _ = b.Append((char)Convert.ToInt32(only.Substring(i, 4), 16));
        }

        return b.ToString();
    }
}
