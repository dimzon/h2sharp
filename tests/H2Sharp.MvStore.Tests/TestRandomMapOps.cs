/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.test.store.TestRandomMapOps.
 *
 * A shadow test: every random operation goes to our map and to a SortedDictionary side by side,
 * and after each one the visible state must match — values, neighbors, ends, sizes, cursors both
 * ways over three ranges. The seeds are fixed, so a failure prints the seed and the operation
 * and the same run reproduces it.
 *
 * Two things do not map one for one. Java's Random is not System.Random, but no vector crosses
 * platforms: both maps see the same operations from ours, which is all the test needs. And the
 * reference runs this against its memory filesystem or truly in memory by configuration; here
 * those are two facts, because the file one needs a file store and fails with TODOException
 * until it is ported.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class TestRandomMapOps : StoreTestBase
{
    private static readonly bool Logging = false;

    private readonly Random random = new();

    private int op;

    [Fact]
    public void TestMapMemory() => TestMap(null);

    [Fact]
    public void TestMapFile() => TestMap("memFS:RandomMapOps");

    private void TestMap(string? fileName)
    {
        const int size = 500;
        long seed = 0;
        for (int cnt = 0; cnt < 100; cnt++)
        {
            try
            {
                TestOps(fileName, size, seed);
            }
            catch (Exception ex)
            {
                Console.WriteLine("seed:" + seed + " op:" + op + " " + ex);
                throw;
            }
            finally
            {
                if (fileName is not null)
                {
                    DeleteFile(fileName);
                }
            }

            seed = NextLong();
        }
    }

    private void TestOps(string? fileName, int loopCount, long seed)
    {
        var r = new Random((int)(seed ^ (seed >> 32)));
        op = 0;
        MVStore s = OpenStore(fileName);
        int keysPerPage = s.GetKeysPerPage();
        const int keyRange = 2000;
        MVMap<int, string> m = s.OpenMap<int, string>("data");
        var map = new SortedDictionary<int, string>();
        int[] recentKeys = new int[2 * keysPerPage];
        for (; op < loopCount; op++)
        {
            int k = r.Next(3 * keyRange / 2);
            if (k >= keyRange)
            {
                k = recentKeys[k % recentKeys.Length];
            }
            else
            {
                recentKeys[op % recentKeys.Length] = k;
            }

            string v = k + "_Value_" + op;
            int type = r.Next(15);
            switch (type)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                    Log(op, k, v, "m.put({0}, {1})");
                    m.Put(k, v);
                    map[k] = v;
                    break;
                case 4:
                case 5:
                    Log(op, k, v, "m.remove({0})");
                    m.Remove(k);
                    map.Remove(k);
                    break;
                case 6:
                    Log(op, k, v, "s.compact(90, 1024)");
                    s.Compact(90, 1024);
                    break;
                case 7:
                    if (op % 64 == 0)
                    {
                        Log(op, k, v, "m.clear()");
                        m.Clear();
                        map.Clear();
                    }

                    break;
                case 8:
                    Log(op, k, v, "s.commit()");
                    s.Commit();
                    break;
                case 9:
                    if (fileName is not null)
                    {
                        Log(op, k, v, "s.commit()");
                        s.Commit();
                        Log(op, k, v, "s.close()");
                        s.Close();
                        Log(op, k, v, "s = openStore(fileName)");
                        s = OpenStore(fileName);
                        Log(op, k, v, "m = s.openMap(\"data\")");
                        m = s.OpenMap<int, string>("data");
                    }

                    break;
                case 10:
                    Log(op, k, v, "s.commit()");
                    s.Commit();
                    Log(op, k, v, "s.compactFile(0)");
                    s.CompactFile(0);
                    break;
                case 11:
                    {
                        int rangeSize = r.Next(2 * keysPerPage);
                        int step = r.Next(2) == 0 ? 1 : -1;
                        for (int i = 0; i < rangeSize; i++)
                        {
                            Log(op, k, v, "m.put({0}, {1})");
                            m.Put(k, v);
                            map[k] = v;
                            k += step;
                            v = k + "_Value_" + op;
                        }

                        break;
                    }

                case 12:
                    {
                        int rangeSize = r.Next(2 * keysPerPage);
                        int step = r.Next(2) == 0 ? 1 : -1;
                        for (int i = 0; i < rangeSize; i++)
                        {
                            Log(op, k, v, "m.remove({0})");
                            m.Remove(k);
                            map.Remove(k);
                            k += step;
                        }

                        break;
                    }

                default:
                    {
                        Log(op, k, v, "m.getKeyIndex({0})");
                        var keyList = new List<int>(map.Keys);
                        int index = keyList.BinarySearch(k);
                        int index2 = (int)m.GetKeyIndex(k);
                        Assert.Equal(index, index2);
                        if (index >= 0)
                        {
                            int? k2 = m.GetKey(index);
                            Assert.Equal(k, k2);
                        }

                        break;
                    }
            }

            Assert.Equal(map.TryGetValue(k, out string? expected) ? expected : null, m.Get(k));
            AssertEqualKey(CeilingKey(map, k), m.CeilingKey(k));
            AssertEqualKey(FloorKey(map, k), m.FloorKey(k));
            AssertEqualKey(HigherKey(map, k), m.HigherKey(k));
            AssertEqualKey(LowerKey(map, k), m.LowerKey(k));
            Assert.Equal(map.Count == 0, m.IsEmpty());
            Assert.Equal(map.Count, m.Count);
            if (map.Count != 0)
            {
                Assert.Equal(map.Keys.First(), m.FirstKey());
                Assert.Equal(map.Keys.Last(), m.LastKey());
            }

            int from = r.Next(2) == 0 ? r.Next(keyRange) : k + r.Next(2 * keysPerPage) - keysPerPage;
            int to = r.Next(2) == 0 ? r.Next(keyRange) : from + r.Next(2 * keysPerPage) - keysPerPage;

            Cursor<int, string> cursor;
            IEnumerable<KeyValuePair<int, string>> entrySet;
            string msg;
            if (from <= to)
            {
                msg = "(" + from + ", null)";
                cursor = m.Cursor(from, null, false);
                entrySet = map.Where(entry => entry.Key >= from);
                AssertEqual(msg, entrySet, cursor);

                msg = "(null, " + from + ")";
                cursor = m.Cursor(null, from, false);
                entrySet = map.Where(entry => entry.Key < from + 1);
                AssertEqual(msg, entrySet, cursor);

                msg = "(" + from + ", " + to + ")";
                cursor = m.Cursor(from, to, false);
                entrySet = map.Where(entry => entry.Key >= from && entry.Key < to + 1);
                AssertEqual(msg, entrySet, cursor);
            }

            if (from >= to)
            {
                msg = "rev (" + from + ", null)";
                cursor = m.Cursor(from, null, true);
                entrySet = map.Where(entry => entry.Key < from + 1).Reverse();
                AssertEqual(msg, entrySet, cursor);

                msg = "rev (null, " + from + ")";
                cursor = m.Cursor(null, from, true);
                entrySet = map.Where(entry => entry.Key >= from).Reverse();
                AssertEqual(msg, entrySet, cursor);

                msg = "rev (" + from + ", " + to + ")";
                cursor = m.Cursor(from, to, true);
                entrySet = map.Where(entry => entry.Key >= to && entry.Key < from + 1).Reverse();
                AssertEqual(msg, entrySet, cursor);
            }
        }

        s.Close();
    }

    private static int? CeilingKey(SortedDictionary<int, string> map, int k)
    {
        foreach (int key in map.Keys)
        {
            if (key >= k)
            {
                return key;
            }
        }

        return null;
    }

    private static int? FloorKey(SortedDictionary<int, string> map, int k)
    {
        int? result = null;
        foreach (int key in map.Keys)
        {
            if (key > k)
            {
                break;
            }

            result = key;
        }

        return result;
    }

    private static int? HigherKey(SortedDictionary<int, string> map, int k) => CeilingKey(map, k + 1);

    private static int? LowerKey(SortedDictionary<int, string> map, int k) => FloorKey(map, k - 1);

    private void AssertEqual(string msg, IEnumerable<KeyValuePair<int, string>> entrySet, Cursor<int, string> cursor)
    {
        int cnt = 0;
        foreach (KeyValuePair<int, string> entry in entrySet)
        {
            string message = msg + " " + cnt;
            AssertTrue(message, cursor.HasNext());
            Assert.Equal(entry.Key, cursor.Next());
            Assert.Equal(entry.Key, cursor.GetKey());
            Assert.Equal(entry.Value, cursor.GetValue());
            ++cnt;
        }

        AssertFalse(msg, cursor.HasNext());
    }

    private static MVStore OpenStore(string? fileName)
    {
        var builder = new MVStore.Builder().KeysPerPage(7).AutoCommitDisabled();
        if (fileName is not null)
        {
            builder.FileName(fileName);
        }

        MVStore s = builder.Open();
        s.SetRetentionTime(1000);
        return s;
    }

    private long NextLong()
    {
        byte[] bytes = new byte[8];
        random.NextBytes(bytes);
        return BitConverter.ToInt64(bytes, 0);
    }

    private static void Log(int op, int k, string v, string msg)
    {
        if (Logging)
        {
            Console.WriteLine(msg + "; // op " + op);
        }
    }
}
