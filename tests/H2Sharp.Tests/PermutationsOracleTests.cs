/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Every permutation the reference walks, in the order it walks them.
 *
 * The optimizer lives or dies by trying join orders, and the only thing this test pins is that
 * the orders tried here are the orders tried there: same count, same sequence, same partial
 * orders for m < n. A port that produced every permutation in a different order would still
 * find the cheapest plan -- eventually -- but it would measure costs in a different order, and
 * the genetic half that follows the brute-force half starts from where brute force stopped.
 */
using System.Collections.Generic;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class PermutationsOracleTests
{
    [JavaOracleFact]
    public void FullPermutations_MatchReference()
    {
        string body = """
                      for (int n = 1; n <= 4; n++) {
                        Integer[] inn = new Integer[n];
                        Integer[] out = new Integer[n];
                        for (int i = 0; i < n; i++) { inn[i] = i; }
                        org.h2.util.Permutations<Integer> p = org.h2.util.Permutations.create(inn, out);
                        System.out.println("n=" + n);
                        while (p.next()) {
                          StringBuilder b = new StringBuilder();
                          for (int i = 0; i < n; i++) { b.append(out[i]); }
                          System.out.println("  " + b);
                        }
                      }
            """;

        List<string> java = OracleRunner.Run([], body);

        List<string> csharp = [];
        for (int n = 1; n <= 4; n++)
        {
            int[] inn = new int[n];
            int[] outt = new int[n];
            for (int i = 0; i < n; i++)
            {
                inn[i] = i;
            }

            Permutations<int> p = Permutations<int>.Create(inn, outt);
            csharp.Add("n=" + n);
            while (p.Next())
            {
                System.Text.StringBuilder b = new();
                for (int i = 0; i < n; i++)
                {
                    _ = b.Append(outt[i]);
                }

                csharp.Add("  " + b);
            }
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void PartialPermutations_MatchReference()
    {
        string body = """
                      for (int m = 0; m <= 3; m++) {
                        Integer[] inn = { 0, 1, 2, 3 };
                        Integer[] out = new Integer[4];
                        org.h2.util.Permutations<Integer> p = org.h2.util.Permutations.create(inn, out, m);
                        System.out.println("m=" + m);
                        while (p.next()) {
                          StringBuilder b = new StringBuilder();
                          for (int i = 0; i < m; i++) { b.append(out[i]); }
                          System.out.println("  " + b);
                        }
                      }
            """;

        List<string> java = OracleRunner.Run([], body);

        List<string> csharp = [];
        for (int m = 0; m <= 3; m++)
        {
            int[] inn = [0, 1, 2, 3];
            int[] outt = new int[4];
            Permutations<int> p = Permutations<int>.Create(inn, outt, m);
            csharp.Add("m=" + m);
            while (p.Next())
            {
                System.Text.StringBuilder b = new();
                for (int i = 0; i < m; i++)
                {
                    _ = b.Append(outt[i]);
                }

                csharp.Add("  " + b);
            }
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [Fact]
    public void FewerElementsThanAskedIsAnInternalError()
    {
        // n < m cannot walk: there are no m distinct elements to take from n. The reference
        // refuses with an internal error rather than walking off the end of the array, and so
        // does this -- a caller that asked for more than there is was already mistaken.
        Assert.ThrowsAny<System.Exception>(() => Permutations<int>.Create([0], new int[1], 2));
    }
}
