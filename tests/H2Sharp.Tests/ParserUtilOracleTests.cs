/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for util/ParserUtil against the reference JVM.
 *
 * The token values are a contract: the parser switches on them and H2 metadata stores
 * them, so an off-by-one in the generated table would silently change which SQL word is a
 * keyword. That is why the constants are compared wholesale by reflection instead of
 * through a hand-written list, which could drift the same way the generated file does.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ParserUtilOracleTests
{
    /// <summary>
    /// Every keyword the reference knows, the context-sensitive ones, both lengths around
    /// the 17 character cutoff, and identifiers that differ only by case, by one
    /// character, or by being a keyword with a suffix.
    /// </summary>
    private static readonly string[] Words =
    [
        "", " ", "A", "AB", "SELECT", "select", "SeLeCt", "TABLE", "table", "FROM",
        "WHERE", "GROUP", "ORDER", "BY", "VALUES", "VALUE", "SELECTED", "SELECTX",
        "CURRENT_TIMESTAMP", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_USER",
        "SESSION_USER", "SYSTEM_USER", "SESSION_SCHEMA", "CURRENT_SCHEMA",
        "CURRENT_ROLE", "CURRENT_CATALOG", "CURRENT_ROUTINE", "LOCALTIME",
        "LOCALTIMESTAMP", "ROWNUM", "_ROWID_", "BOTH", "ILIKE", "LEADING", "TRAILING",
        "OVER", "PARTITION", "RANGE", "REGEXP", "ROWS", "TOP", "GROUPS", "IDENTIFIER",
        "KEYWORD", "NULL", "TRUE", "FALSE", "UNKNOWN", "AND", "OR", "NOT", "IS", "IN",
        "LIKE", "BETWEEN", "EXISTS", "ALL", "ANY", "SOME", "ARRAY", "AS", "ASYMMETRIC",
        "AUTHORIZATION", "DISTINCT", "FOR", "FOREIGN", "FROMX", "JOIN", "LEFT", "RIGHT",
        "FULL", "INNER", "CROSS", "NATURAL", "ON", "USING", "PRIMARY", "KEY", "UNIQUE",
        "CHECK", "DEFAULT", "IF", "CASE", "WHEN", "THEN", "ELSE", "END", "LIMIT",
        "OFFSET", "FETCH", "NEXT", "FIRST", "TOPX", "MINUS", "HOUR", "MINUTE", "SECOND",
        "YEAR", "DAY", "MONTH", "WINDOW", "WITH", "INTERVAL", "QUALIFY", "SYMMETRIC",
        "UESCAPE", "USER", "CONSTRAINT", "COLUMN", "INDEX", "VIEW", "SEQUENCE",
        "TRIGGER", "SCHEMA", "DATABASE", "INSERT", "DELETE", "UPDATE", "MERGE", "GRANT",
        "REVOKE", "COMMIT", "ROLLBACK", "SAVEPOINT", "SET", "CALL", "EXPLAIN", "ANALYZE",
        "TRUNCATE", "COMMENT", "IDENTIFIERX", "AVERYLONGIDENTIFIER1", "x", "y", "z",
        "1", "_", "1A", "A1", "A_B", "_AB", "AB_", "selecta", "1select", "SELECT_",
        "SELECTED_", "ß", "\u0130", "\u0131", "caf\u00e9",
        new string('X', 1), new string('X', 2), new string('X', 16),
        new string('X', 17), new string('X', 18),
    ];

    /// <summary>
    /// (ignoreCase, additionalKeywords) combinations, as 0/1/2 in the Java snippet.
    /// </summary>
    private static readonly int[] Flags = [0, 1, 2];

    private static readonly int[] FlagSets = [0, 1, 2, 3, 4, 8];

    /// <summary>
    /// Java's <c>Boolean.toString</c> is lowercase, .NET's is not. The snippets print
    /// booleans directly, so the C# side has to match rather than compare.
    /// </summary>
    private static string JavaBool(bool value) => value ? "true" : "false";

    [JavaOracleFact]
    public void GetTokenType_MatchesReferenceJvm()
    {
        string body = """
                      for (String s : inputs) {
                        for (int f : nums) {
                          System.out.println(org.h2.util.ParserUtil.getTokenType(s, f == 0, f == 1));
                        }
                      }
            """;

        List<string> java = OracleRunner.Run(Words, body, Flags);

        List<int> csharp = [];
        foreach (string word in Words)
        {
            foreach (int f in Flags)
            {
                csharp.Add(ParserUtil.GetTokenType(word, f == 0, f == 1));
            }
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.Equal(int.Parse(java[i], CultureInfo.InvariantCulture), csharp[i]);
        }
    }

    [JavaOracleFact]
    public void IsKeyword_MatchesReferenceJvm()
    {
        string body = """
                      for (String s : inputs) {
                        System.out.println(org.h2.util.ParserUtil.isKeyword(s, true) + "|"
                            + org.h2.util.ParserUtil.isKeyword(s, false));
                      }
            """;

        List<string> java = OracleRunner.Run(Words, body);

        List<string> csharp =
        [
            .. Words.Select(static w =>
                JavaBool(ParserUtil.IsKeyword(w, true)) + "|" + JavaBool(ParserUtil.IsKeyword(w, false))),
        ];

        Assert.Equal(java, csharp);
    }

    [JavaOracleFact]
    public void IsSimpleIdentifier_MatchesReferenceJvm()
    {
        string body = """
                      for (String s : inputs) {
                        System.out.println(
                            org.h2.util.ParserUtil.isSimpleIdentifier(s, false, false) + "|"
                            + org.h2.util.ParserUtil.isSimpleIdentifier(s, true, false) + "|"
                            + org.h2.util.ParserUtil.isSimpleIdentifier(s, false, true));
                      }
            """;

        List<string> java = OracleRunner.Run(Words, body);

        List<string> csharp =
        [
            .. Words.Select(static w =>
                JavaBool(ParserUtil.IsSimpleIdentifier(w, false, false))
                + "|" + JavaBool(ParserUtil.IsSimpleIdentifier(w, true, false))
                + "|" + JavaBool(ParserUtil.IsSimpleIdentifier(w, false, true))),
        ];

        Assert.Equal(java, csharp);
    }

    [JavaOracleFact]
    public void QuoteIdentifier_MatchesReferenceJvm()
    {
        string body = """
                      int[] flagSets = nums;
                      for (String s : inputs) {
                        for (int flags : flagSets) {
                          System.out.println(hex(org.h2.util.ParserUtil.quoteIdentifier(
                              new StringBuilder(), s, flags).toString()));
                        }
                      }
                      for (int flags : flagSets) {
                        System.out.println(hex(org.h2.util.ParserUtil.quoteIdentifier(
                            new StringBuilder(), null, flags).toString()));
                      }
            """;

        List<string> java = OracleRunner.Run(Words, body, FlagSets);

        List<string> csharp = [];
        foreach (string word in Words)
        {
            foreach (int flags in FlagSets)
            {
                StringBuilder builder = new();
                ParserUtil.QuoteIdentifier(builder, word, flags);
                csharp.Add(OracleRunner.Hex(builder.ToString()));
            }
        }

        // A null identifier renders as an empty quoted identifier in Java, and must here.
        foreach (int flags in FlagSets)
        {
            StringBuilder builder = new();
            ParserUtil.QuoteIdentifier(builder, null, flags);
            csharp.Add(OracleRunner.Hex(builder.ToString()));
        }

        Assert.Equal(java, csharp);
    }

    /// <summary>
    /// Every token constant and its value, compared against the reference by reflection so
    /// that a renumbering cannot pass unnoticed.
    /// </summary>
    [JavaOracleFact]
    public void TokenConstants_MatchReferenceJvm()
    {
        string body = """
                      java.lang.reflect.Field[] fields = org.h2.util.ParserUtil.class.getFields();
                      java.util.TreeMap<String, Integer> sorted = new java.util.TreeMap<>();
                      for (java.lang.reflect.Field f : fields) {
                        if (f.getType() == int.class) {
                          sorted.put(f.getName(), (Integer) f.get(null));
                        }
                      }
                      for (java.util.Map.Entry<String, Integer> e : sorted.entrySet()) {
                        System.out.println(e.getKey() + "=" + e.getValue());
                      }
            """;

        List<string> java = OracleRunner.Run([], body);

        // The C# names are PascalCase and the Java names are UPPER_SNAKE, so normalise both
        // to upper case without underscores before comparing. Compare as sets: only the
        // name/value pairing is a contract, not the declaration order or the casing.
        static string Normalise(string name) =>
            name.Replace("_", "", StringComparison.Ordinal).ToUpperInvariant();

        List<string> csharp =
        [
            .. typeof(ParserUtil)
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
    public void HasSqlFlags_MatchTheReferenceValues()
    {
        // HasSql is hand written rather than generated, and these values are combined
        // into sqlFlags across the engine, so a typo here would be invisible.
        Assert.Equal(1, HasSql.QuoteOnlyWhenRequired);
        Assert.Equal(2, HasSql.ReplaceLobsForTrace);
        Assert.Equal(4, HasSql.NoCasts);
        Assert.Equal(8, HasSql.AddPlanInformation);
        Assert.Equal(0, HasSql.DefaultSqlFlags);
        Assert.Equal(3, HasSql.TraceSqlFlags);
    }

    [Fact]
    public void IsSimpleIdentifier_RejectsBothCaseFlags()
    {
        Assert.Throws<ArgumentException>(
            () => ParserUtil.IsSimpleIdentifier(
                "x", databaseToUpper: true, databaseToLower: true));
    }

    [Fact]
    public void GetTokenType_LengthFilterShortCircuitsBeforeTheTable()
    {
        // The 17 character cutoff is a contract with the longest keyword, "CURRENT_TIMESTAMP",
        // and the filter has to stay on the same side of it as the table.
        Assert.Equal(ParserUtil.Select, ParserUtil.GetTokenType("SELECT", true, false));
        Assert.Equal(
            ParserUtil.CurrentTimestamp,
            ParserUtil.GetTokenType("CURRENT_TIMESTAMP", true, false));

        // 18 characters is past the cutoff, so it cannot be in the table at all.
        Assert.Equal(
            ParserUtil.Identifier,
            ParserUtil.GetTokenType(new string('X', 18), true, false));

        // One or two characters are below it too: no keyword is that short.
        Assert.Equal(ParserUtil.Identifier, ParserUtil.GetTokenType("A", true, false));
        Assert.Equal(ParserUtil.Identifier, ParserUtil.GetTokenType("AB", true, false));
    }
}
