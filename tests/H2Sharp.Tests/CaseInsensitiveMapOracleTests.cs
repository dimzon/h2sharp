using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The case insensitive maps against the reference.
/// </summary>
/// <remarks>
/// A schema is looked up by name without regard to case, and the fold is the one thing that decides
/// whether two names are the same. Getting it wrong is silent: the table is simply not found, or a
/// a second object with the same name is created.
/// <para>
/// The part worth checking is the part that is easy to get wrong. The reference folds on the way in
/// and on every lookup, and folds nowhere else — so the keys that come back out are the folded ones.
/// A port that folded on the way out as well would look more helpful and would not match: a caller
/// reading the keys would see the spelling it put in rather than the one the map holds.
/// </para>
/// <para>
/// The corpus is built out of names where case folding is not obvious: the dotted capital I, the
/// sharp S, the Greek final sigma, and the two i's that Turkish folds differently from everyone else.
/// </para>
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class CaseInsensitiveMapOracleTests
{
    private static readonly string[] Names =
    [
        "table", "TABLE", "TaBlE", "INDEX", "index", "İndex", "ındex", "straße", "STRASSE",
        "ΣΊΣΥΦΟΣ", "σίσυφος", "ﬁle", "FILE", "café", "CAFÉ", " ", "a b", "A B",
    ];

    [Fact]
    public void CaseInsensitiveMapAgreesWithTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Program(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en"])
            .ToList();

        List<string> csharp = [.. Lines()];

        Compare(java, csharp);
    }

    private static string Program() =>
        """
        {
        String[] names = { "table", "TABLE", "TaBlE", "INDEX", "index", "İndex", "ındex",
            "straße", "STRASSE", "ΣΊΣΥΦΟΣ", "σίσυφος", "ﬁle", "FILE", "café", "CAFÉ",
            " ", "a b", "A B" };

        for (int i = 0; i < names.length; i++) {
          org.h2.value.CaseInsensitiveMap<String> map = new org.h2.value.CaseInsensitiveMap<>();
          System.out.println("put|" + names[i] + "|" + map.put(names[i], "v" + i) + "|" + map.size());
        }

        org.h2.value.CaseInsensitiveMap<String> map = new org.h2.value.CaseInsensitiveMap<>();
        for (int i = 0; i < names.length; i++) {
          map.put(names[i], "v" + i);
        }
        System.out.println("size|" + map.size());
        java.util.TreeSet<String> k = new java.util.TreeSet<>(map.keySet());
        StringBuilder kb = new StringBuilder();
        for (String kk : k) { if (kb.length() > 0) { kb.append(", "); } kb.append(kk); }
        System.out.println("keys|" + kb);
        for (int i = 0; i < names.length; i++) {
          System.out.println("get|" + names[i] + "|" + map.get(names[i]) + "|" + map.containsKey(names[i]));
        }
        System.out.println("getmissing|" + map.get("nothing here") + "|" + map.containsKey("nothing here"));
        System.out.println("ifabsent1|" + map.putIfAbsent("nEw", "first"));
        System.out.println("ifabsent2|" + map.putIfAbsent("NEW", "second"));
        System.out.println("ifabsent|" + map.get("new"));
        System.out.println("remove|" + map.remove("NEw") + "|" + map.containsKey("NEW") + "|" + map.size());
        System.out.println("removemissing|" + map.remove("absent"));

        org.h2.value.CaseInsensitiveConcurrentMap<String> cm =
            new org.h2.value.CaseInsensitiveConcurrentMap<>();
        for (int i = 0; i < names.length; i++) {
          cm.put(names[i], "c" + i);
        }
        System.out.println("csize|" + cm.size());
        java.util.TreeSet<String> ck = new java.util.TreeSet<>(cm.keySet());
        StringBuilder cb = new StringBuilder();
        for (String kk : ck) { if (cb.length() > 0) { cb.append(", "); } cb.append(kk); }
        System.out.println("ckeys|" + cb);
        for (int i = 0; i < names.length; i++) {
          System.out.println("cget|" + names[i] + "|" + cm.get(names[i]) + "|" + cm.containsKey(names[i]));
        }
        System.out.println("cifabsent1|" + cm.putIfAbsent("other", "first"));
        System.out.println("cifabsent2|" + cm.putIfAbsent("OTHER", "second"));
        System.out.println("cifabsent|" + cm.get("OTHER"));
        System.out.println("cremove|" + cm.remove("OtHeR") + "|" + cm.containsKey("other"));
        }
        """;

    private static IEnumerable<string> Lines()
    {
        foreach (string name in Names)
        {
            CaseInsensitiveMap<string> fresh = new();
            string? before = fresh[name];
            fresh[name] = "v";
            yield return "put|" + Escape(name) + "|" + F(before) + "|" + fresh.Count;
        }

        CaseInsensitiveMap<string> map = new();
        for (int i = 0; i < Names.Length; i++)
        {
            map[Names[i]] = "v" + i.ToString(CultureInfo.InvariantCulture);
        }

        yield return "size|" + map.Count.ToString(CultureInfo.InvariantCulture);
        yield return "keys|" + string.Join(", ", map.Keys.Order(StringComparer.Ordinal));
        foreach (string name in Names)
        {
            yield return "get|" + Escape(name) + "|" + F(map[name]) + "|" + J(map.ContainsKey(name));
        }

        yield return "getmissing|" + F(map["nothing here"]) + "|" + J(map.ContainsKey("nothing here"));
        yield return "ifabsent1|" + F(map.PutIfAbsent("nEw", "first"));
        yield return "ifabsent2|" + F(map.PutIfAbsent("NEW", "second"));
        yield return "ifabsent|" + F(map["new"]);
        yield return "remove|" + F(map.Remove("NEw")) + "|" + J(map.ContainsKey("NEW")) + "|"
            + map.Count.ToString(CultureInfo.InvariantCulture);
        yield return "removemissing|" + F(map.Remove("absent"));

        CaseInsensitiveConcurrentMap<string> cm = new();
        for (int i = 0; i < Names.Length; i++)
        {
            cm[Names[i]] = "c" + i.ToString(CultureInfo.InvariantCulture);
        }

        yield return "csize|" + cm.Count.ToString(CultureInfo.InvariantCulture);
        yield return "ckeys|" + string.Join(", ", cm.Keys.Order(StringComparer.Ordinal));
        foreach (string name in Names)
        {
            yield return "cget|" + Escape(name) + "|" + F(cm[name]) + "|" + J(cm.ContainsKey(name));
        }

        yield return "cifabsent1|" + F(cm.PutIfAbsent("other", "first"));
        yield return "cifabsent2|" + F(cm.PutIfAbsent("OTHER", "second"));
        yield return "cifabsent|" + F(cm["OTHER"]);
        yield return "cremove|" + F(cm.Remove("OtHeR")) + "|" + J(cm.ContainsKey("other"));
    }

    /// <summary>
    /// A name as the reference prints it, so that a name with a comma in it cannot split a line.
    /// </summary>
    private static string Escape(string name) => name;

    private static string F<T>(T? value)
        => value is null ? "null" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";

    private static string J(bool value) => value ? "true" : "false";

    private static void Compare(List<string> java, List<string> csharp)
    {
        if (java.Count != csharp.Count)
        {
            int common = Math.Min(java.Count, csharp.Count);
            int first = -1;
            for (int i = 0; i < common; i++)
            {
                if (java[i] != csharp[i])
                {
                    first = i;
                    break;
                }
            }

            Assert.Fail(
                "строк: java=" + java.Count.ToString(CultureInfo.InvariantCulture)
                + " csharp=" + csharp.Count.ToString(CultureInfo.InvariantCulture)
                + "\n  первое различие: " + first.ToString(CultureInfo.InvariantCulture)
                + "\n    java:   " + (first >= 0 ? java[first] : "-")
                + "\n    csharp: " + (first >= 0 ? csharp[first] : "-"));
        }

        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }
}
