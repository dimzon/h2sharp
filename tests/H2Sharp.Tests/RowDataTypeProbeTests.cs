/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The probe path, not just the answer: with duplicate keys every matching index is valid,
 * but the files must still lay out byte-identically, so the found index is the reference's
 * exact one. Both lines below diverge when the midpoints do.
 */
using System.Collections.Generic;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Db;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class RowDataTypeProbeTests
{
    private const string JavaBody = """
        org.h2.mvstore.db.RowDataType t = new org.h2.mvstore.db.RowDataType(null,
            org.h2.value.CompareMode.getInstance(null, 0), null, new int[]{ 0 }, new int[]{ 0 }, 2, false);
        int[] keys = new int[]{ 0, 10, 10, 10, 20, 30, 40, 50 };
        org.h2.result.SearchRow[] rows = new org.h2.result.SearchRow[8];
        for (int i = 0; i < 8; i++) {
            rows[i] = new org.h2.result.DefaultRow(new org.h2.value.Value[]{
                org.h2.value.ValueInteger.get(keys[i]), org.h2.value.ValueInteger.get(0) });
        }
        org.h2.result.SearchRow key = new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(10), org.h2.value.ValueInteger.get(0) });
        System.out.println("=" + t.binarySearch(key, rows, 8, 0));
        System.out.println("=" + t.binarySearch(key, rows, 8, 5));
        """;

    [JavaOracleFact]
    public void DuplicateKeysFindTheSameIndex()
    {
        RowDataType type = new(null, CompareMode.GetInstance(null, 0), null, [0], [0], 2, false);
        int[] keys = [0, 10, 10, 10, 20, 30, 40, 50];
        SearchRow[] rows = new SearchRow[8];
        for (int i = 0; i < 8; i++)
        {
            rows[i] = new ProbeRow([ValueInteger.Get(keys[i]), ValueInteger.Get(0)]);
        }

        SearchRow key = new ProbeRow([ValueInteger.Get(10), ValueInteger.Get(0)]);
        List<string> actual =
        [
            "=" + type.BinarySearch(key, rows, 8, 0),
            "=" + type.BinarySearch(key, rows, 8, 5),
        ];

        List<string> java = OracleRunner.Run([], JavaBody, null, null, null);
        Assert.Equal(java, actual);
    }

    /// <summary>
    /// A row of plain values; the key never matters because the type does not store it.
    /// </summary>
    private sealed class ProbeRow : SearchRow
    {
        private readonly Value[] values;

        internal ProbeRow(Value[] values) => this.values = values;

        public override int GetColumnCount() => values.Length;

        public override Value? GetValue(int index) => values[index];

        public override void SetValue(int index, Value v) => values[index] = v;

        public override int GetMemory() => 0;

        public override void CopyFrom(SearchRow source)
        {
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = source.GetValue(i)!;
            }

            SetKey(source.GetKey());
        }
    }
}
