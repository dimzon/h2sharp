/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the index type vocabulary: IndexType's factories, its predicates and
 * the SQL snippet it prints.
 *
 * The corpus is the cross product of every way an index can be declared, and the SQL snippet is
 * asked for both with and without the nulls-distinct clause, because the clause is the only
 * place createUnique's one rewrite is visible: a single-column ALL_DISTINCT comes back as
 * DISTINCT, and a two-column ALL_DISTINCT comes back unchanged. A test that asked only for the
 * predicates would pass with that rewrite dropped.
 *
 * Both sides print the whole state of the object rather than a few fields: an index type is
 * small enough that its entire observable surface fits on one line per combination, and a
 * predicate nobody printed is a predicate nobody checked.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class IndexTypeOracleTests
{
    [JavaOracleFact]
    public void IndexType_MatchesReference()
    {
        string body = """
                      String[] kinds = { "pk", "pk-hash", "unique", "unique-hash", "nonunique",
                          "nonunique-hash", "nonunique-hash-spatial", "scan", "unique-all",
                          "unique-all-2", "unique-not" };
                      for (String kind : kinds) {
                        org.h2.index.IndexType t = create.apply(kind);
                        t.setBelongsToConstraint(kind.endsWith("hash"));
                        StringBuilder sb = new StringBuilder();
                        sb.append(kind).append("|");
                        sb.append(t.isPrimaryKey()).append("|");
                        sb.append(t.isUnique()).append("|");
                        sb.append(t.isHash()).append("|");
                        sb.append(t.isSpatial()).append("|");
                        sb.append(t.isPersistent()).append("|");
                        sb.append(t.isScan()).append("|");
                        sb.append(t.getBelongsToConstraint()).append("|");
                        sb.append(t.getNullsDistinct()).append("|");
                        sb.append(t.getEffectiveNullsDistinct()).append("|");
                        sb.append(t.getSQL(false)).append("|");
                        sb.append(t.getSQL(true));
                        System.out.println(sb);
                      }
            """;

        List<string> java = OracleRunner.Run([], Setup + body);
        List<string> csharp = Describe();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void NullsDistinctSql_MatchesReference()
    {
        string body = """
                      for (org.h2.engine.NullsDistinct n : org.h2.engine.NullsDistinct.values()) {
                        System.out.println(n.getSQL(new StringBuilder(), 0));
                      }
            """;

        List<string> java = OracleRunner.Run([], Setup + body);
        List<string> csharp = [.. Enum.GetValues<NullsDistinct>()
            .Select(n => NullsDistinctSql.GetSql(new StringBuilder(), n, 0).ToString())];

        Assert.Equal(java, csharp);
    }

    private static List<string> Describe()
    {
        List<string> lines = [];
        foreach ((string Kind, IndexType Type) t in Types())
        {
            StringBuilder line = new();
            _ = line.Append(t.Kind).Append('|')
                .Append(B(t.Type.IsPrimaryKey())).Append('|')
                .Append(B(t.Type.IsUnique())).Append('|')
                .Append(B(t.Type.IsHash())).Append('|')
                .Append(B(t.Type.IsSpatial())).Append('|')
                .Append(B(t.Type.IsPersistent())).Append('|')
                .Append(B(t.Type.IsScan())).Append('|');
            t.Type.SetBelongsToConstraint(t.Kind.EndsWith("hash", StringComparison.Ordinal));
            _ = line.Append(B(t.Type.GetBelongsToConstraint())).Append('|')
                .Append(t.Type.GetNullsDistinct() is NullsDistinct d ? JavaName(d) : "null").Append('|')
                .Append(t.Type.GetEffectiveNullsDistinct() is NullsDistinct e ? JavaName(e) : "null").Append('|')
                .Append(t.Type.GetSql(false)).Append('|')
                .Append(t.Type.GetSql(true));
            lines.Add(line.ToString());
        }

        return lines;
    }

    private static List<(string Kind, IndexType Type)> Types() =>
    [
        ("pk", IndexType.CreatePrimaryKey(true, false)),
        ("pk-hash", IndexType.CreatePrimaryKey(true, true)),
        ("unique", IndexType.CreateUnique(true, false, 1, NullsDistinct.Distinct)),
        ("unique-hash", IndexType.CreateUnique(true, true, 2, NullsDistinct.NotDistinct)),
        ("nonunique", IndexType.CreateNonUnique(true)),
        ("nonunique-hash", IndexType.CreateNonUnique(true, true, false)),
        ("nonunique-hash-spatial", IndexType.CreateNonUnique(false, true, true)),
        ("scan", IndexType.CreateScan(true)),
        ("unique-all", IndexType.CreateUnique(false, false, 1, NullsDistinct.AllDistinct)),
        ("unique-all-2", IndexType.CreateUnique(true, false, 2, NullsDistinct.AllDistinct)),
        ("unique-not", IndexType.CreateUnique(true, false, 3, NullsDistinct.NotDistinct)),
    ];

    /// <summary>A boolean the way Java prints one.</summary>
    private static string B(bool value) => value ? "true" : "false";

    /// <summary>
    /// The enum member under its reference name. The port names enum members in PascalCase by
    /// the project's own rule, so the words differ where the reference prints them; the SQL a
    /// user sees is uppercase either way, and that is what NullsDistinctSql pins.
    /// </summary>
    private static string JavaName(NullsDistinct value) => value switch
    {
        NullsDistinct.Distinct => "DISTINCT",
        NullsDistinct.AllDistinct => "ALL_DISTINCT",
        NullsDistinct.NotDistinct => "NOT_DISTINCT",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// <summary>
    /// The factories, spelled out as a lambda: the harness puts the snippet inside main, and
    /// Java has no local methods — the alternative is repeating the same switch in every test
    /// that needs an index type, which is how a corpus and its copy drift apart.
    /// </summary>
    private const string Setup = """
                      java.util.function.Function<String, org.h2.index.IndexType> create = kind -> {
                        switch (kind) {
                          case "pk": return org.h2.index.IndexType.createPrimaryKey(true, false);
                          case "pk-hash": return org.h2.index.IndexType.createPrimaryKey(true, true);
                          case "unique": return org.h2.index.IndexType.createUnique(true, false, 1,
                              org.h2.engine.NullsDistinct.DISTINCT);
                          case "unique-hash": return org.h2.index.IndexType.createUnique(true, true, 2,
                              org.h2.engine.NullsDistinct.NOT_DISTINCT);
                          case "nonunique": return org.h2.index.IndexType.createNonUnique(true);
                          case "nonunique-hash": return org.h2.index.IndexType.createNonUnique(true, true, false);
                          case "nonunique-hash-spatial":
                            return org.h2.index.IndexType.createNonUnique(false, true, true);
                          case "scan": return org.h2.index.IndexType.createScan(true);
                          case "unique-all": return org.h2.index.IndexType.createUnique(false, false, 1,
                              org.h2.engine.NullsDistinct.ALL_DISTINCT);
                          case "unique-all-2": return org.h2.index.IndexType.createUnique(true, false, 2,
                              org.h2.engine.NullsDistinct.ALL_DISTINCT);
                          case "unique-not": return org.h2.index.IndexType.createUnique(true, false, 3,
                              org.h2.engine.NullsDistinct.NOT_DISTINCT);
                          default: throw new IllegalArgumentException(kind);
                        }
                      };
            """;
}
