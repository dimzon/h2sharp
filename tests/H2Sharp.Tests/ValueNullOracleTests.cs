/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the value surface and for the first concrete value.
 *
 * NULL is worth a test of its own even though it holds no data, because it is the value every
 * other conversion has to recognise: it is the one that is its own singleton, the one whose
 * equality is identity rather than data equality, and the one that is neither true nor false.
 * The arithmetic refusals are compared too, since a refusal that names the wrong type or the
 * wrong operator is a user-visible message.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ValueNullOracleTests
{
    [JavaOracleFact]
    public void Null_MatchesReference()
    {
        string body = """
                      org.h2.value.Value v = org.h2.value.ValueNull.INSTANCE;
                      // PRECISION and DISPLAY_SIZE are package private in the reference, and
                      // the snippet lives in the default package, so they are read reflectively
                      // rather than dropped from the comparison.
                      java.lang.reflect.Field precision =
                          org.h2.value.ValueNull.class.getDeclaredField("PRECISION");
                      precision.setAccessible(true);
                      java.lang.reflect.Field displaySize =
                          org.h2.value.ValueNull.class.getDeclaredField("DISPLAY_SIZE");
                      displaySize.setAccessible(true);
                      StringBuilder line = new StringBuilder();
                      line.append(v.getSQL(0)).append('|')
                          .append(v.getType().getSQL(0)).append('|')
                          .append(v.getValueType()).append('|')
                          .append(v.getMemory()).append('|')
                          .append(v.getString() == null).append('|')
                          .append(v.getReader() == null).append('|')
                          .append(v.getBytes() == null).append('|')
                          .append(v.containsNull()).append('|')
                          .append(v.hashCode()).append('|')
                          .append(v.equals(v)).append('|')
                          .append(v.equals(org.h2.value.ValueNull.INSTANCE)).append('|')
                          .append(v.equals("NULL")).append('|')
                          .append(v.toString()).append('|')
                          .append(precision.getInt(null)).append('|')
                          .append(displaySize.getInt(null));
                      System.out.println(hex(line.toString()));
                      // The truth tests are on the base class and go through getBoolean, which
                      // NULL refuses, so they are asked separately.
                      try {
                        v.isTrue();
                        System.out.println(hex("isTrue OK"));
                      } catch (RuntimeException ex) {
                        System.out.println(hex("isTrue " + ex.getClass().getSimpleName()));
                      }
                      try {
                        v.isFalse();
                        System.out.println(hex("isFalse OK"));
                      } catch (RuntimeException ex) {
                        System.out.println(hex("isFalse " + ex.getClass().getSimpleName()));
                      }
                      for (String op : new String[] { "+", "-", "*", "/", "%", "NEGATE" }) {
                        try {
                          if ("+".equals(op)) {
                            v.add(v);
                          } else if ("-".equals(op)) {
                            v.subtract(v);
                          } else if ("*".equals(op)) {
                            v.multiply(v);
                          } else if ("/".equals(op)) {
                            v.divide(v, org.h2.value.TypeInfo.TYPE_BIGINT);
                          } else if ("%".equals(op)) {
                            v.modulus(v);
                          } else {
                            v.negate();
                          }
                          System.out.println(hex(op + " OK"));
                        } catch (RuntimeException ex) {
                          System.out.println(hex(op + " " + ex.getMessage()));
                        }
                      }
            """;

        // The language is pinned because the refusals carry translated messages, and the
        // reference appends its trace to every exception message as " [code-id]", which is a
        // counter this port does not have.
        List<string> java = OracleRunner.Run(
                [],
                body,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .Select(line => TraceSuffix.Replace(line, string.Empty))
            .ToList();

        List<string> csharp =
        [
            Render(),
            Probe("isTrue"),
            Probe("isFalse"),
            .. new[] { "+", "-", "*", "/", "%", "NEGATE" }.Select(op => Arithmetic(op)),
        ];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <remarks>
    /// Every boolean goes through J, because Java concatenates a boolean as "true" and .NET
    /// as "True", and comparing them raw would report a difference on every line.
    /// </remarks>
    private static string Render()
    {
        Value v = ValueNull.Instance;
        StringBuilder line = new();
        _ = line.Append(v.GetSql(new StringBuilder(), 0)).Append('|')
            .Append(v.GetTypeInfo().GetSql(0)).Append('|')
            .Append(v.GetValueType()).Append('|')
            .Append(v.GetMemory()).Append('|')
            .Append(J(v.GetString() is null)).Append('|')
            .Append(J(v.GetReader() is null)).Append('|')
            .Append(J(v.GetBytes() is null)).Append('|')
            .Append(J(v.ContainsNull())).Append('|')
            .Append(v.GetHashCode()).Append('|')
            .Append(J(v.Equals(v))).Append('|')
            .Append(J(v.Equals(ValueNull.Instance))).Append('|')
            .Append(J(v.Equals("NULL"))).Append('|')
            .Append(v.ToString()).Append('|')
            .Append(ValueNull.Precision.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(ValueNull.DisplaySize.ToString(CultureInfo.InvariantCulture));
        return line.ToString();
    }

    private static readonly System.Text.RegularExpressions.Regex TraceSuffix =
        new(@"\s\[\d+-\d+\]$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string J(bool value) => value ? "true" : "false";

    private static string Probe(string what)
    {
        try
        {
            _ = what == "isTrue" ? ValueNull.Instance.IsTrue() : ValueNull.Instance.IsFalse();
            return $"{what} OK";
        }
        catch (Exception e)
        {
            return $"{what} {e.GetType().Name}";
        }
    }

    private static string Arithmetic(string op)
    {
        try
        {
            _ = op switch
            {
                "+" => ValueNull.Instance.Add(ValueNull.Instance),
                "-" => ValueNull.Instance.Subtract(ValueNull.Instance),
                "*" => ValueNull.Instance.Multiply(ValueNull.Instance),
                "/" => ValueNull.Instance.Divide(ValueNull.Instance, TypeInfo.TypeBigint),
                "%" => ValueNull.Instance.Modulus(ValueNull.Instance),
                _ => ValueNull.Instance.Negate(),
            };
            return $"{op} OK";
        }
        catch (Exception e)
        {
            return $"{op} {e.Message}";
        }
    }

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
