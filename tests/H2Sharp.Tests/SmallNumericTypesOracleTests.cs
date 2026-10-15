/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for BOOLEAN, TINYINT and SMALLINT.
 *
 * Three small types, three different answers about how a value is made, and the corpus is where
 * they are told apart. A TINYINT builds all 256 of its values up front and can never overflow,
 * because every result of its arithmetic is inside the type by construction. A SMALLINT goes
 * through the value cache and has to check the result, which is where the overflow errors come from.
 * A BOOLEAN has two instances and no arithmetic at all, so its equality is the instance.
 *
 * The corpus is therefore built around the edges: the smallest and largest values of each type,
 * the results that leave the range in every direction, the division that has no answer, and the
 * bytes each type hands back, which are the bit patterns rather than the numbers.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Values;
using H2Sharp;
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class SmallNumericTypesOracleTests
{
    private static readonly string[] Tinyints =
    [
        "0", "-1", "1", "127", "-128", "64", "-64", "100", "-100", "2", "3", "-3", "5", "0",
    ];

    private static readonly string[] Smallints =
    [
        "0", "1", "-1", "32767", "-32768", "1234", "-1234", "100", "2", "3", "-3", "5", "0",
    ];

    private static readonly string[] Booleans = ["true", "false"];

    [JavaOracleFact]
    public void Tinyint_MatchesReference()
    {
        List<string> java = RunOracle(Tinyints, TinyintProgram);
        List<string> csharp = Tinyints.Select(s => DescribeTiny(sbyte.Parse(s, CultureInfo.InvariantCulture))).ToList();
        AssertSame(java, csharp, Tinyints);
    }

    [JavaOracleFact]
    public void Smallint_MatchesReference()
    {
        List<string> java = RunOracle(Smallints, SmallintProgram);
        List<string> csharp = Smallints.Select(s => DescribeSmall(short.Parse(s, CultureInfo.InvariantCulture))).ToList();
        AssertSame(java, csharp, Smallints);
    }

    [JavaOracleFact]
    public void Boolean_MatchesReference()
    {
        List<string> java = RunOracle(Booleans, BooleanProgram);
        List<string> csharp = Booleans.Select(s => DescribeBoolean(s == "true")).ToList();
        AssertSame(java, csharp, Booleans);
    }

    /// <summary>
    /// Every value of a TINYINT is the same object every time, and a boolean's identity is its
    /// value, so both are asked about directly rather than through the printed form.
    /// </summary>
    [Fact]
    public void TheValuesThatAreKeptAreTheSameObjects()
    {
        Assert.Same(ValueBoolean.True, ValueBoolean.Get(true));
        Assert.Same(ValueBoolean.False, ValueBoolean.Get(false));
        Assert.Same(ValueTinyint.Get(-128), ValueTinyint.Get(-128));
        Assert.Same(ValueTinyint.Get(127), ValueTinyint.Get(127));
        Assert.Same(ValueTinyint.Get(0), ValueTinyint.Get(0));
        Assert.Equal(0, ValueTinyint.Get(1).GetMemory());
        Assert.Equal(0, ValueBoolean.True.GetMemory());

        // A boolean that holds the same thing is the same object, so it is equal by identity.
        Assert.True(ValueBoolean.True.Equals(ValueBoolean.True));
        Assert.False(ValueBoolean.True.Equals(ValueBoolean.False));
        Assert.False(ValueBoolean.True.Equals(null));
    }

    /// <summary>
    /// A division by zero names the dividend in its trace SQL: the sites spell it through
    /// GetTraceSql rather than a throwaway builder, and this pins the text so a future
    /// change to the trace flags fails here instead of silently.
    /// </summary>
    [Fact]
    public void DivisionByZeroNamesTheDividend()
    {
        AssertDividendNamesItself(ValueTinyint.Get(5), ValueTinyint.Get(0));
        AssertDividendNamesItself(ValueSmallint.Get(300), ValueSmallint.Get(0));
    }

    private static void AssertDividendNamesItself(Value dividend, Value zero)
    {
        TypeInfo quotient = TypeInfo.GetTypeInfo(dividend.GetValueType(), 10, 0, null);
        H2SharpException divide = Assert.Throws<H2SharpException>(() => dividend.Divide(zero, quotient));
        Assert.Equal((int)ErrorCode.DivisionByZero1, divide.H2ErrorCode);
        Assert.Contains(dividend.GetTraceSql(), divide.Message, StringComparison.Ordinal);

        H2SharpException modulus = Assert.Throws<H2SharpException>(() => dividend.Modulus(zero));
        Assert.Equal((int)ErrorCode.DivisionByZero1, modulus.H2ErrorCode);
        Assert.Contains(dividend.GetTraceSql(), modulus.Message, StringComparison.Ordinal);
    }

    private static List<string> RunOracle(IReadOnlyList<string> inputs, string program)
        => OracleRunner.Run(
                inputs,
                program,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .Select(line => TraceSuffix.Replace(line, string.Empty))
            .ToList();

    private static void AssertSame(List<string> java, List<string> csharp, IReadOnlyList<string> inputs)
    {
        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == csharp[i],
                $"line {i} ({inputs[i]}):\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static string DescribeTiny(sbyte v)
    {
        Value a = ValueTinyint.Get(v);
        Value two = ValueTinyint.Get(2);
        Value three = ValueTinyint.Get(3);
        Value zero = ValueTinyint.Get(0);
        return Tail(a, two, three, zero);
    }

    private static string DescribeSmall(short v)
    {
        Value a = ValueSmallint.Get(v);
        Value two = ValueSmallint.Get(2);
        Value three = ValueSmallint.Get(3);
        Value zero = ValueSmallint.Get(0);

        return Tail(a, two, three, zero);
    }

    private static string Tail(Value a, Value two, Value three, Value zero)
        => new StringBuilder(Sql(a, 0)).Append('|')
            .Append(Sql(a, HasSql.NoCasts)).Append('|')
            .Append(HexText(a.GetString())).Append('|')
            .Append(HexBytes(a.GetBytes())).Append('|')
            .Append(a.GetTypeInfo().GetSql(0)).Append('|')
            .Append(a.GetValueType().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(a.GetSignum().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(a.GetHashCode().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(Get(() => a.GetByte().ToString(CultureInfo.InvariantCulture))).Append('|')
            .Append(a.GetShort().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(a.GetInt().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(a.GetLong().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(a.GetBigInteger().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(a.GetBigDecimal().ToString()).Append('|')
            .Append(JavaNumberFormat.FloatToString(a.GetFloat())).Append('|')
            .Append(JavaNumberFormat.DoubleToString(a.GetDouble())).Append('|')
            .Append(J(a.Equals(a))).Append('|')
            .Append(J(a.Equals(two))).Append('|')
            .Append(a.CompareTo(two, null, CompareMode.GetInstance(null, 0))
                .ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(Get(() => a.Add(two).GetString() ?? string.Empty)).Append('|')
            .Append(Get(() => a.Subtract(two).GetString() ?? string.Empty)).Append('|')
            .Append(Get(() => a.Multiply(two).GetString() ?? string.Empty)).Append('|')
            .Append(Get(() => a.Divide(three, TypeInfo.GetTypeInfo(a.GetValueType(), 10, 0, null)).GetString() ?? string.Empty))
            .Append('|')
            .Append(Get(() => a.Modulus(three).GetString() ?? string.Empty)).Append('|')
            .Append(Get(() => a.Negate().GetString() ?? string.Empty)).Append('|')
            .Append(Get(() => a.Divide(zero, TypeInfo.GetTypeInfo(a.GetValueType(), 10, 0, null)).GetString() ?? string.Empty))
            .Append('|')
            .Append(Get(() => a.Modulus(zero).GetString() ?? string.Empty))
            .ToString();

    private static string DescribeBoolean(bool b)
    {
        Value a = ValueBoolean.Get(b);
        return new StringBuilder(Sql(a, 0)).Append('|')
            .Append(Sql(a, HasSql.NoCasts)).Append('|')
            .Append(HexText(a.GetString())).Append('|')
            .Append("-").Append('|')
            .Append(a.GetTypeInfo().GetSql(0)).Append('|')
            .Append(a.GetValueType().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(a.GetHashCode().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(J(a.GetBoolean())).Append('|')
            .Append(Get(() => a.GetByte().ToString(CultureInfo.InvariantCulture))).Append('|')
            .Append(a.GetShort().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(a.GetInt().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(a.GetLong().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(a.GetBigInteger().ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(a.GetBigDecimal().ToString()).Append('|')
            .Append(JavaNumberFormat.FloatToString(a.GetFloat())).Append('|')
            .Append(JavaNumberFormat.DoubleToString(a.GetDouble())).Append('|')
            .Append(J(a.Equals(a))).Append('|')
            .Append(J(a.Equals(ValueBoolean.Get(!b)))).Append('|')
            .Append(a.CompareTo(ValueBoolean.Get(!b), null, CompareMode.GetInstance(null, 0))
                .ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(a.Negate().GetString() ?? string.Empty)
            .ToString();
    }

    private static string Sql(Value v, int flags) => v.GetSql(new StringBuilder(), flags).ToString();


    private static string Get(Func<string> f)
    {
        try
        {
            return f();
        }
        catch (H2SharpException e)
        {
            return "EX " + e.Message;
        }
    }

    private static string J(bool value) => value ? "true" : "false";

    private static string HexBytes(byte[]? bytes)
        => bytes is null || bytes.Length == 0 ? "-" : Hex.BytesToHex(bytes);

    private static string HexText(string? s)
    {
        if (s is null || s.Length == 0)
        {
            return "-";
        }

        StringBuilder b = new();
        foreach (char c in s)
        {
            _ = b.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
        }

        return b.ToString();
    }

    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        if (!only.All(static c => char.IsAsciiHexDigit(c) || c == '-' || c == ' '))
        {
            throw new InvalidOperationException($"Not hex from the oracle: '{hex}'");
        }

        if (only == "-")
        {
            return string.Empty;
        }

        StringBuilder b = new();
        for (int i = 0; i + 3 < only.Length; i += 4)
        {
            _ = b.Append((char)Convert.ToInt32(only.Substring(i, 4), 16));
        }

        return b.ToString();
    }

    private static readonly System.Text.RegularExpressions.Regex TraceSuffix =
        new(@"\s\[\d+-\d+\]", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <remarks>
    /// One Java program per type, without lambdas: a snippet that carries its own functions is a
    /// snippet whose types have to be inferred, and javac answers an inference failure as a cascade
    /// of errors about symbols that are perfectly fine.
    /// </remarks>
    private const string TinyintProgram = """
                      for (int i = 0; i < inputs.length; i++) {
                        org.h2.value.ValueTinyint v = org.h2.value.ValueTinyint.get(Byte.parseByte(inputs[i]));
                        org.h2.value.ValueTinyint two = org.h2.value.ValueTinyint.get((byte) 2);
                        org.h2.value.ValueTinyint three = org.h2.value.ValueTinyint.get((byte) 3);
                        org.h2.value.ValueTinyint zero = org.h2.value.ValueTinyint.get((byte) 0);
                        org.h2.value.TypeInfo t = org.h2.value.TypeInfo.getTypeInfo(
                            org.h2.value.Value.TINYINT, 10, 0, null);
                        StringBuilder b = new StringBuilder();
                        b.append(v.getSQL(new StringBuilder(), 0)).append('|');
                        b.append(v.getSQL(new StringBuilder(), org.h2.util.HasSQL.NO_CASTS)).append('|');
                        String text = v.getString();
                        if (text.length() == 0) {
                          b.append('-');
                        } else {
                          for (int j = 0; j < text.length(); j++) {
                            b.append(String.format("%04x", (int) text.charAt(j)));
                          }
                        }
                        b.append('|');
                        byte[] raw = v.getBytes();
                        for (int j = 0; j < raw.length; j++) {
                          b.append(String.format("%02x", raw[j] & 0xff));
                        }
                        b.append('|');
                        b.append(v.getType().getSQL(0)).append('|');
                        b.append(v.getValueType()).append('|');
                        b.append(v.getSignum()).append('|');
                        b.append(v.hashCode()).append('|');
                        try {
                          b.append(v.getByte()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        try {
                          b.append(v.getShort()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        b.append(v.getInt()).append('|');
                        b.append(v.getLong()).append('|');
                        b.append(v.getBigInteger()).append('|');
                        b.append(v.getBigDecimal()).append('|');
                        b.append(v.getFloat()).append('|');
                        b.append(v.getDouble()).append('|');
                        b.append(v.equals(v)).append('|');
                        b.append(v.equals(two)).append('|');
                        b.append(v.compareTo(two, null, org.h2.value.CompareMode.getInstance(null, 0))).append('|');
                        try {
                          b.append(v.add(two).getString()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        try {
                          b.append(v.subtract(two).getString()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        try {
                          b.append(v.multiply(two).getString()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        try {
                          b.append(v.divide(three, t).getString()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        try {
                          b.append(v.modulus(three).getString()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        try {
                          b.append(v.negate().getString()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        try {
                          b.append(v.divide(zero, t).getString()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        try {
                          b.append(v.modulus(zero).getString());
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage());
                        }
                        System.out.println(hex(b.toString()));
                      }
            """;


    private const string SmallintProgram = """
                      for (int i = 0; i < inputs.length; i++) {
                        org.h2.value.ValueSmallint v = org.h2.value.ValueSmallint.get(
                            Short.parseShort(inputs[i]));
                        org.h2.value.ValueSmallint two = org.h2.value.ValueSmallint.get((short) 2);
                        org.h2.value.ValueSmallint three = org.h2.value.ValueSmallint.get((short) 3);
                        org.h2.value.ValueSmallint zero = org.h2.value.ValueSmallint.get((short) 0);
                        org.h2.value.TypeInfo t = org.h2.value.TypeInfo.getTypeInfo(
                            org.h2.value.Value.SMALLINT, 10, 0, null);
                        StringBuilder b = new StringBuilder();
                        b.append(v.getSQL(new StringBuilder(), 0)).append('|');
                        b.append(v.getSQL(new StringBuilder(), org.h2.util.HasSQL.NO_CASTS)).append('|');
                        String text = v.getString();
                        if (text.length() == 0) {
                          b.append('-');
                        } else {
                          for (int j = 0; j < text.length(); j++) {
                            b.append(String.format("%04x", (int) text.charAt(j)));
                          }
                        }
                        b.append('|');
                        byte[] raw = v.getBytes();
                        for (int j = 0; j < raw.length; j++) {
                          b.append(String.format("%02x", raw[j] & 0xff));
                        }
                        b.append('|');
                        b.append(v.getType().getSQL(0)).append('|');
                        b.append(v.getValueType()).append('|');
                        b.append(v.getSignum()).append('|');
                        b.append(v.hashCode()).append('|');
                        try {
                          b.append(v.getByte()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        try {
                          b.append(v.getShort()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        b.append(v.getInt()).append('|');
                        b.append(v.getLong()).append('|');
                        b.append(v.getBigInteger()).append('|');
                        b.append(v.getBigDecimal()).append('|');
                        b.append(v.getFloat()).append('|');
                        b.append(v.getDouble()).append('|');
                        b.append(v.equals(v)).append('|');
                        b.append(v.equals(two)).append('|');
                        b.append(v.compareTo(two, null, org.h2.value.CompareMode.getInstance(null, 0))).append('|');
                        try {
                          b.append(v.add(two).getString()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        try {
                          b.append(v.subtract(two).getString()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        try {
                          b.append(v.multiply(two).getString()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        try {
                          b.append(v.divide(three, t).getString()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        try {
                          b.append(v.modulus(three).getString()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        try {
                          b.append(v.negate().getString()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        try {
                          b.append(v.divide(zero, t).getString()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        try {
                          b.append(v.modulus(zero).getString());
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage());
                        }
                        System.out.println(hex(b.toString()));
                      }
            """;

    private const string BooleanProgram = """
                      for (int i = 0; i < inputs.length; i++) {
                        org.h2.value.ValueBoolean v = org.h2.value.ValueBoolean.get(
                            Boolean.parseBoolean(inputs[i]));
                        org.h2.value.ValueBoolean other = org.h2.value.ValueBoolean.get(
                            !v.getBoolean());
                        StringBuilder b = new StringBuilder();
                        b.append(v.getSQL(new StringBuilder(), 0)).append('|');
                        b.append(v.getSQL(new StringBuilder(), org.h2.util.HasSQL.NO_CASTS)).append('|');
                        String text = v.getString();
                        if (text.length() == 0) {
                          b.append('-');
                        } else {
                          for (int j = 0; j < text.length(); j++) {
                            b.append(String.format("%04x", (int) text.charAt(j)));
                          }
                        }
                        b.append('|');
                        b.append('-').append('|');
                        b.append(v.getType().getSQL(0)).append('|');
                        b.append(v.getValueType()).append('|');
                        b.append(v.hashCode()).append('|');
                        b.append(v.getBoolean()).append('|');
                        try {
                          b.append(v.getByte()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        try {
                          b.append(v.getShort()).append('|');
                        } catch (org.h2.message.DbException e) {
                          b.append("EX ").append(e.getMessage()).append('|');
                        }
                        b.append(v.getInt()).append('|');
                        b.append(v.getLong()).append('|');
                        b.append(v.getBigInteger()).append('|');
                        b.append(v.getBigDecimal()).append('|');
                        b.append(v.getFloat()).append('|');
                        b.append(v.getDouble()).append('|');
                        b.append(v.equals(v)).append('|');
                        b.append(v.equals(other)).append('|');
                        b.append(v.compareTo(other, null, org.h2.value.CompareMode.getInstance(null, 0))).append('|');
                        b.append(v.negate().getString());
                        System.out.println(hex(b.toString()));
                      }
            """;
}
