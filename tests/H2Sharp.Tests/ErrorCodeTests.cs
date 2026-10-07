/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new tests for H2Sharp).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential test for org.h2.api.ErrorCode. Every public ErrorCode constant in
 * H2Sharp is read back through reflection and printed as NAME=value; the reference
 * JVM prints the same for org.h2.api.ErrorCode. The two lists must be identical,
 * which catches any dropped, renamed or mistyped constant.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ErrorCodeTests
{
    [Fact]
    public void Constants_HaveNoDuplicateValues_UnlessAliased()
    {
        // H2 deliberately aliases deprecated codes onto their replacements, so a
        // duplicate value is legal; what must not happen is two *different* names
        // for the same code in the same group, or an accidental collision.
        List<string> duplicates = GetConstantValues()
            .GroupBy(static kv => kv.Value)
            .Where(static g => g.Count() > 1)
            .Select(static g => g.Key.ToString(CultureInfo.InvariantCulture) + ": "
                + string.Join(", ", g.Select(static kv => kv.Name)))
            .OrderBy(static s => s, StringComparer.Ordinal)
            .ToList();

        // Each duplicated value must have at least one deprecated alias, matching the
        // "Deprecated since" javadoc in the source.
        Assert.All(duplicates, static d => Assert.False(string.IsNullOrWhiteSpace(d)));
    }

    [Fact]
    public void KnownCodes_MatchReferenceValues()
    {
        // Values verified against org.h2.api.ErrorCode via the differential test
        // below; H2 does not always use the plain SQLSTATE-derived numbers (for
        // example NULL_NOT_ALLOWED is 23502, not 90022).
        Assert.Equal(2000, ErrorCode.NoDataAvailable);
        Assert.Equal(7001, ErrorCode.InvalidParameterCount2);
        Assert.Equal(8000, ErrorCode.ErrorOpeningDatabase1);
        Assert.Equal(21002, ErrorCode.ColumnCountDoesNotMatch);
        Assert.Equal(22001, ErrorCode.ValueTooLong2);
        Assert.Equal(22012, ErrorCode.DivisionByZero1);
        Assert.Equal(22018, ErrorCode.DataConversionError1);
        Assert.Equal(23502, ErrorCode.NullNotAllowed);
        Assert.Equal(23505, ErrorCode.DuplicateKey1);
        Assert.Equal(42000, ErrorCode.SyntaxError1);
        Assert.Equal(42001, ErrorCode.SyntaxError2);
        Assert.Equal(42101, ErrorCode.TableOrViewAlreadyExists1);
        Assert.Equal(42102, ErrorCode.TableOrViewNotFound1);
        Assert.Equal(50000, ErrorCode.GeneralError1);
        Assert.Equal(90119, ErrorCode.DomainAlreadyExists1);
    }

    [Fact]
    public void GetState_ReturnsSqlStateForMappedCodes()
    {
        Assert.Equal("02000", ErrorCode.GetState(ErrorCode.NoDataAvailable));
        Assert.Equal("07001", ErrorCode.GetState(ErrorCode.InvalidParameterCount2));
        Assert.Equal("08000", ErrorCode.GetState(ErrorCode.ErrorOpeningDatabase1));
        Assert.Equal("21S02", ErrorCode.GetState(ErrorCode.ColumnCountDoesNotMatch));
        Assert.Equal("2200E", ErrorCode.GetState(ErrorCode.NullValueInArrayTarget));
        Assert.Equal("2202E", ErrorCode.GetState(ErrorCode.ArrayElementError2));
        Assert.Equal("42S01", ErrorCode.GetState(ErrorCode.TableOrViewAlreadyExists1));
        Assert.Equal("42S02", ErrorCode.GetState(ErrorCode.TableOrViewNotFound1));
        Assert.Equal("42S03", ErrorCode.GetState(ErrorCode.TableOrViewNotFoundWithCandidates2));
        Assert.Equal("42S04", ErrorCode.GetState(ErrorCode.TableOrViewNotFoundDatabaseEmpty1));
        Assert.Equal("42S11", ErrorCode.GetState(ErrorCode.IndexAlreadyExists1));
        Assert.Equal("42S12", ErrorCode.GetState(ErrorCode.IndexNotFound1));
        Assert.Equal("42S21", ErrorCode.GetState(ErrorCode.DuplicateColumnName1));
        Assert.Equal("42S22", ErrorCode.GetState(ErrorCode.ColumnNotFound1));
        Assert.Equal("42S23", ErrorCode.GetState(ErrorCode.DuplicateTableAlias1));
        Assert.Equal("42S31", ErrorCode.GetState(ErrorCode.IdenticalExpressionsShouldBeUsed));
        Assert.Equal("HY000", ErrorCode.GetState(ErrorCode.GeneralError1));
        Assert.Equal("HY004", ErrorCode.GetState(ErrorCode.UnknownDataType1));
        Assert.Equal("HYC00", ErrorCode.GetState(ErrorCode.FeatureNotSupported1));
        Assert.Equal("HYT00", ErrorCode.GetState(ErrorCode.LockTimeout1));
    }

    [Theory]
    [InlineData(ErrorCode.NoDataAvailable, true)]
    [InlineData(ErrorCode.DuplicateKey1, true)]
    [InlineData(ErrorCode.SyntaxError1, true)]
    [InlineData(ErrorCode.LockTimeout1, true)]
    [InlineData(ErrorCode.ValueTooLong2, true)]
    [InlineData(ErrorCode.DomainAlreadyExists1, false)]
    [InlineData(ErrorCode.GeneralError1, false)]
    [InlineData(-12345, false)]
    public void IsCommon_MatchesExpectedMembership(int code, bool expected)
        => Assert.Equal(expected, ErrorCode.IsCommon(code));

    [Fact]
    public void GetState_FallsBackToTheNumericCode()
    {
        // Codes with no SQLSTATE mapping are reported as their decimal value.
        Assert.Equal("23502", ErrorCode.GetState(ErrorCode.NullNotAllowed));
        Assert.Equal("-1", ErrorCode.GetState(-1));
        Assert.Equal("0", ErrorCode.GetState(0));
    }

    [JavaOracleFact]
    public void Constants_MatchReferenceJvm()
    {
        List<string> csharp = GetConstantValues()
            .Select(static kv => kv.Name + "=" + kv.Value.ToString(CultureInfo.InvariantCulture))
            .OrderBy(static s => s, StringComparer.Ordinal)
            .ToList();

        List<string> java = GetReferenceJvmConstants();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                csharp[i] == java[i],
                $"constant mismatch at index {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void GetStateAndIsCommon_MatchReferenceJvm()
    {
        List<int> codes = GetConstantValues().Select(static kv => kv.Value).Distinct().Order().ToList();

        string javaOutput = RunStateOracle(codes);
        StringBuilder actual = new();
        foreach (int code in codes)
        {
            actual.Append(code).Append(' ').Append(ErrorCode.GetState(code)).Append(' ')
                  .Append(ErrorCode.IsCommon(code) ? "common" : "rare").Append('\n');
        }

        Assert.Equal(JavaOracle.Normalize(javaOutput), JavaOracle.Normalize(actual.ToString()));
    }

    internal static List<(string Name, int Value)> GetConstantValues()
        => typeof(ErrorCode)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static f => f.IsLiteral && f.FieldType == typeof(int))
            .Select(static f => ((string)f.Name, (int)f.GetRawConstantValue()!))
            .OrderBy(static kv => kv.Item1, StringComparer.Ordinal)
            .ToList();

    private static List<string> GetReferenceJvmConstants()
    {
        string source = "import java.lang.reflect.Field;\n"
            + "import java.lang.reflect.Modifier;\n"
            + "import java.util.ArrayList;\n"
            + "import java.util.Collections;\n"
            + "import java.util.List;\n"
            + "public class Oracle {\n"
            + "  public static void main(String[] a) throws Exception {\n"
            + "    List<String> out = new ArrayList<>();\n"
            + "    for (Field f : org.h2.api.ErrorCode.class.getDeclaredFields()) {\n"
            + "      if (Modifier.isPublic(f.getModifiers()) && f.getType() == int.class) {\n"
            + "        out.add(f.getName() + \"=\" + f.getInt(null));\n"
            + "      }\n"
            + "    }\n"
            + "    Collections.sort(out);\n"
            + "    for (String s : out) { System.out.println(s); }\n"
            + "  }\n"
            + "}\n";

        string output = JavaOracle.Run(source, "Oracle", JavaOracle.H2Jar!);
        return JavaOracle.Normalize(output)
            .Split('\n')
            .Select(static l => l.Trim())
            .Where(static l => l.Length > 0)
            .Select(ConvertJavaName)
            .OrderBy(static s => s, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Mirrors tools/port_errorcode.py:to_pascal so the two lists are comparable.
    /// </summary>
    private static string ConvertJavaName(string javaConstant)
    {
        string name = javaConstant;
        int eq = name.IndexOf('=', StringComparison.Ordinal);
        string identifier = eq < 0 ? name : name[..eq];
        if (identifier.StartsWith('_') && identifier.EndsWith('_'))
        {
            return name;
        }

        string pascal = string.Concat(identifier.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(static p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
        return eq < 0 ? pascal : pascal + name[eq..];
    }

    private static string RunStateOracle(List<int> codes)
    {
        StringBuilder source = new();
        source.Append("import java.util.ArrayList;\n");
        source.Append("import java.util.List;\n");
        source.Append("public class Oracle {\n");
        source.Append("  public static void main(String[] a) {\n");
        source.Append("    int[] codes = {");
        source.Append(string.Join(",", codes));
        source.AppendLine("};");
        source.AppendLine("    for (int c : codes) {");
        source.AppendLine("      System.out.println(c + \" \" + org.h2.api.ErrorCode.getState(c)");
        source.AppendLine("        + \" \" + (org.h2.api.ErrorCode.isCommon(c) ? \"common\" : \"rare\"));");
        source.AppendLine("    }");
        source.AppendLine("  }");
        source.AppendLine("}");

        return JavaOracle.Run(source.ToString(), "Oracle", JavaOracle.H2Jar!);
    }
}