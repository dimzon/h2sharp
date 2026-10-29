// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestPattern.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the reference's main() entry is the runner; xUnit runs Test() directly. The
// Collator.SECONDARY constant is its numeric value.
using H2Sharp.Expressions.Conditions;
using H2Sharp.Values;

using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Tests LIKE pattern matching.
/// </summary>
public sealed class TestPattern
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public void Test()
    {
        TestCompareModeReuse();
        TestPattern_();
    }

    private static void TestCompareModeReuse()
    {
        CompareMode mode1 = CompareMode.GetInstance(null, 0);
        CompareMode mode2 = CompareMode.GetInstance(null, 0);
        Assert.True(ReferenceEquals(mode1, mode2));

        // java.text.Collator.SECONDARY
        mode1 = CompareMode.GetInstance("DE", 1);
        Assert.False(ReferenceEquals(mode1, mode2));
        mode2 = CompareMode.GetInstance("DE", 1);
        Assert.True(ReferenceEquals(mode1, mode2));
    }

    private void TestPattern_()
    {
        CompareMode mode = CompareMode.GetInstance(null, 0);
        CompareLike comp = new(
            mode, "\\", null, false, false, null, null, CompareLike.LikeType.Like);
        TestLike(comp, "B", "%_");
        TestLike(comp, "A", "A%");
        TestLike(comp, "A", "A%%");
        TestLike(comp, "A_A", "%\\_%");

        for (int i = 0; i < 10000; i++)
        {
            string pattern = GetRandomPattern();
            string value = GetRandomValue();
            TestLike(comp, value, pattern);
        }
    }

    /// <summary>
    /// The reference's test(comp, value, pattern) overload; the rename keeps it apart from
    /// the xUnit entry of the same name.
    /// </summary>
    private static void TestLike(CompareLike comp, string value, string pattern)
    {
        string regexp = InitPatternRegexp(pattern, '\\');
        // The reference uses Pattern.matcher(...).matches(), a whole-string match; .NET's
        // Regex.IsMatch searches, so the pattern is anchored to the whole string.
        bool resultRegexp = System.Text.RegularExpressions.Regex.IsMatch(
            value, "\\A" + regexp + "\\z");
        bool result = comp.Test(pattern, value, '\\');
        if (result != resultRegexp)
        {
            throw new Xunit.Sdk.XunitException("Error: >" + value + "< LIKE >" + pattern
                + "< result=" + result + " resultReg=" + resultRegexp);
        }
    }

    private static string GetRandomValue()
    {
        System.Text.StringBuilder buff = new();
        int len = (int)(Random.Shared.NextDouble() * 10);
        string s = "AB_%\\";
        for (int i = 0; i < len; i++)
        {
            _ = buff.Append(s[(int)(Random.Shared.NextDouble() * s.Length)]);
        }

        return buff.ToString();
    }

    private static string GetRandomPattern()
    {
        System.Text.StringBuilder buff = new();
        int len = (int)(Random.Shared.NextDouble() * 4);
        string s = "A%_\\";
        for (int i = 0; i < len; i++)
        {
            char c = s[(int)(Random.Shared.NextDouble() * s.Length)];
            if ((c == '_' || c == '%') && Random.Shared.NextDouble() > 0.5)
            {
                _ = buff.Append('\\');
            }
            else if (c == '\\')
            {
                _ = buff.Append(c);
            }

            _ = buff.Append(c);
        }

        return buff.ToString();
    }

    private static string InitPatternRegexp(string pattern, char escape)
    {
        int len = pattern.Length;
        System.Text.StringBuilder buff = new();
        for (int i = 0; i < len; i++)
        {
            char c = pattern[i];
            if (escape == c)
            {
                if (i >= len - 1)
                {
                    Fail("escape can't be last char");
                }

                c = pattern[++i];
                if (c == '%' || c == '_')
                {
                    // The .NET regex engine has no \_ or \% escape: the escaped LIKE
                    // wildcard is the literal character itself.
                    _ = buff.Append(c);
                }
                else
                {
                    _ = buff.Append('\\');
                    _ = buff.Append(c);
                }
            }
            else if (c == '%')
            {
                _ = buff.Append(".*");
            }
            else if (c == '_')
            {
                _ = buff.Append('.');
            }
            else if (c == '\\')
            {
                _ = buff.Append("\\\\");
            }
            else
            {
                _ = buff.Append(c);
            }

            // TODO regexp: there are other chars that need escaping
        }

        return buff.ToString();
    }

    private static void Fail(string message)
    {
        throw new Xunit.Sdk.XunitException(message);
    }
}
