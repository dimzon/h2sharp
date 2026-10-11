/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The statement-kind numbers, checked one by one against the reference.
 *
 * These numbers are read by a prepared statement and written to INFORMATION_SCHEMA, so a
 * difference here is not a style question: a row written by the reference would come back with
 * another statement's kind. Every constant is compared by reflection — both the name and the
 * value — because a table that is one number off looks perfectly fine until a file moves between
 * the two implementations.
 *
 * The comparison is by name rather than by position, so the reference can grow a constant
 * without this test breaking for the wrong reason: what is asserted is that every constant we
 * declare has the reference's value, not that the two files happen to have the same length.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using H2Sharp.Command;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class CommandInterfaceTests
{
    [JavaOracleFact]
    public void EveryConstantHasTheReferenceValue()
    {
        Dictionary<string, int> ours = Ours();
        string body = """
                       Class<?> c = org.h2.command.CommandInterface.class;
                       for (java.lang.reflect.Field f : c.getDeclaredFields()) {
                         if (f.getType() == int.class && java.lang.reflect.Modifier.isStatic(f.getModifiers())) {
                           f.setAccessible(true);
                           System.out.println(f.getName() + "=" + f.getInt(null));
                         }
                       }
            """;

        List<string> java = OracleRunner.Run([], body);
        Dictionary<string, int> reference = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in java)
        {
            string[] parts = line.Split('=');
            if (parts.Length == 2)
            {
                reference[parts[0]] = int.Parse(parts[1]);
            }
        }

        Assert.NotEmpty(reference);
        Dictionary<string, int> byName = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, int value) in reference)
        {
            byName[Normalize(name)] = value;
        }

        foreach ((string name, int value) in ours)
        {
            Assert.True(byName.TryGetValue(Normalize(name), out int expected),
                $"the reference has no constant {name}");
            Assert.Equal(expected, value);
        }
    }

    /// <summary>
    /// Compares names with the underscores dropped. Our names are PascalCase and the reference's
    /// are UPPER_SNAKE, and the reference is not even consistent with itself there — it spells one
    /// RUNSCRIPT and the rest RUN_*. The numbers are the contract; the spelling is not, so the
    /// spelling is normalised away rather than transcribed into the test.
    /// </summary>
    private static string Normalize(string name)
    {
        return name.Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }

    [Fact]
    public void TheTwoKindsThatShareANumberAreBothThere()
    {
        // REPLACE and NO_OPERATION are both 63 in the reference, on purpose: a REPLACE that
        // changed no rows *is* a no-operation. A table that "fixed" the duplicate would break
        // the statement that relies on the two answers being one.
        Assert.Equal(CommandInterface.Replace, CommandInterface.NoOperation);
        Assert.Equal(63, CommandInterface.NoOperation);
    }

    private static Dictionary<string, int> Ours()
    {
        Dictionary<string, int> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (FieldInfo field in typeof(CommandInterface).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType == typeof(int))
            {
                result[field.Name] = (int)field.GetValue(null)!;
            }
        }

        return result;
    }
}
