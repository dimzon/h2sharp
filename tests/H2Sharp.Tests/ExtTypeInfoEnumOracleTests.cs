/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the ENUM definition.
 *
 * An enum definition is validated when the type is built rather than when a value arrives,
 * so the interesting behaviour is all in the constructor: what is rejected, with which error
 * code and message, and what the surviving definition reports about itself. Rendering is
 * compared because the labels are quoted and escaped into stored metadata, and the hash is
 * compared because a type is hashed on every comparison.
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

public sealed class ExtTypeInfoEnumOracleTests
{
    /// <summary>
    /// The definitions tried, and the order they are tried in.
    /// </summary>
    /// <remarks>
    /// Written out on both sides rather than passed in, because the reference snippet has to
    /// build the same arrays and passing a null through the oracle harness is more trouble
    /// than spelling the case out. The cases cover the rejects (none, a null, an empty label,
    /// a blank label, two labels that are equal once trimmed and upper cased) and the
    /// survivals (quoting, padding, and the full-mapping cases that a simple upper case would
    /// get wrong). The runner pins the language, because the rejects carry a translated
    /// message and the reference would otherwise answer in the machine's locale.
    /// </remarks>
    private static readonly string?[]?[] Cases =
    [
        ["A"],
        ["a", "b"],
        [" a ", "b", "C"],
        ["A", "a"],
        ["it's"],
        ["''"],
        [""],
        [" "],
        Array.Empty<string?>(),
        null,
        ["ß", "SS"],
        ["i", "I", "ı"],
        ["é", "E"],
        [" x ", " x "],
    ];

    /// <summary>
    /// The definition every case is also compared against, so equality is checked in both
    /// directions rather than only for the identity case.
    /// </summary>
    private static readonly string[] Other = ["x", "y"];

    [JavaOracleFact]
    public void EnumDefinitions_MatchReference()
    {
        string body = """
                      String[][] cases = new String[][] {
                          new String[] { "A" },
                          new String[] { "a", "b" },
                          new String[] { " a ", "b", "C" },
                          new String[] { "A", "a" },
                          new String[] { "it's" },
                          new String[] { "''" },
                          new String[] { "" },
                          new String[] { " " },
                          new String[] {},
                          null,
                          new String[] { "\u00df", "SS" },
                          new String[] { "i", "I", "\u0131" },
                          new String[] { "\u00e9", "E" },
                          new String[] { " x ", " x " }
                      };
                      String[] other = new String[] { "x", "y" };
                      System.out.println(hex(String.valueOf(cases.length)));
                      for (int i = 0; i < cases.length; i++) {
                        String line;
                        try {
                          org.h2.value.ExtTypeInfoEnum e =
                              new org.h2.value.ExtTypeInfoEnum(cases[i]);
                          org.h2.value.TypeInfo t = org.h2.value.TypeInfo.getTypeInfo(
                              org.h2.value.Value.ENUM, -1L, -1, e);
                          org.h2.value.ExtTypeInfoEnum same =
                              new org.h2.value.ExtTypeInfoEnum(cases[i]);
                          line = "OK " + e.getCount() + " " + e.getEnumerator(0) + " "
                              + e.getSQL(0) + " " + t.getSQL(0) + " " + t.getPrecision() + " "
                              + t.getScale() + " " + e.hashCode() + " " + e.equals(same) + " "
                              + e.equals(new org.h2.value.ExtTypeInfoEnum(other)) + " "
                              + e.toString();
                        } catch (org.h2.message.DbException ex) {
                          line = "EX " + ex.getErrorCode() + " " + ex.getMessage();
                        } catch (RuntimeException ex) {
                          line = "EX -1 " + ex.getMessage();
                        }
                        System.out.println(hex(line));
                      }
            """;

        List<string> java = OracleRunner.Run(
            [],
            body,
            extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            // The reference appends its trace to every exception message, as
            // " [errorCode-traceId]", and the trace id is a counter the port does not have.
            // Only that exact suffix is removed, so a real difference in the message text
            // still shows.
            .Select(StripTrace)
            .ToList();

        // The first line is the case count, so a case added on one side only shows up as a
        // count difference instead of shifting every later line.
        Assert.Equal(Cases.Length.ToString(CultureInfo.InvariantCulture), java[0]);
        java.RemoveAt(0);

        List<string> csharp = [];
        foreach (string?[]? labels in Cases)
        {
            csharp.Add(Render(labels));
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"case {i} ({Describe(labels: Cases[i])}):\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static string Render(string?[]? labels)
    {
        // A null list is a case of its own: the reference rejects it as an empty enum, so
        // it must be passed through as null rather than normalised to an empty array.
        string?[] present = labels is null ? null! : labels;
        try
        {
            ExtTypeInfoEnum e = new(present);
            TypeInfo t = TypeInfo.GetTypeInfo(Value.Enum, -1L, -1, e);
            ExtTypeInfoEnum same = new(present);
            StringBuilder line = new();
            _ = line.Append("OK ")
                .Append(e.GetCount()).Append(' ').Append(e.GetEnumerator(0)).Append(' ')
                .Append(e.GetSql(0)).Append(' ').Append(t.GetSql(0)).Append(' ')
                .Append(t.GetPrecision().ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(t.GetScale().ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(e.GetHashCode().ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(J(e.Equals(same))).Append(' ')
                .Append(J(e.Equals(new ExtTypeInfoEnum(Other)))).Append(' ')
                .Append(e.ToString());
            return line.ToString();
        }
        catch (H2SharpException ex)
        {
            return $"EX {ex.H2ErrorCode} {ex.Message}";
        }
    }

    private static string J(bool value) => value ? "true" : "false";

    private static readonly System.Text.RegularExpressions.Regex TraceSuffix =
        new(@"\s\[\d+-\d+\]$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string StripTrace(string line) => TraceSuffix.Replace(line, string.Empty);

    private static string Describe(string?[]? labels)
        => labels is null ? "null" : "[" + string.Join(", ", labels) + "]";

    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        StringBuilder sb = new();
        for (int i = 0; i + 3 < only.Length; i += 4)
        {
            sb.Append((char)Convert.ToInt32(only.Substring(i, 4), 16));
        }

        return sb.ToString();
    }
}
