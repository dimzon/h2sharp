// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestLIRSMemoryConsumption.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the cache and its Config are the ported CacheLongKeyLIRS, and the memory
// walk is Utils.GetMemoryUsed and Utils.CollectGarbage. The reference's main() entry is
// the runner; xUnit runs Test() directly.
using H2Sharp.MvStore.Cache;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Class TestLIRSMemoryConsumption.
/// <UL>
/// <LI> 8/5/18 10:57 PM initial creation
/// </UL>
/// </summary>
public sealed class TestLIRSMemoryConsumption : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestMemoryConsumption();
        Console.WriteLine("-----------------------");
        TestMemoryConsumption();
        Console.WriteLine("-----------------------");
        TestMemoryConsumption();
    }

    private void TestMemoryConsumption()
    {
        int size = 1_000_000;
        Random rng = new();
        CacheLongKeyLIRS<object?>.Config config = new();
        for (int mb = 1; mb <= 16; mb *= 2)
        {
            config.MaxMemory = mb * 1024 * 1024;
            CacheLongKeyLIRS<object?> cache = new(config);
            long memoryUsedInitial = Utils.GetMemoryUsed();
            for (int i = 0; i < size; i++)
            {
                cache.Put(i, CreateValue(i), GetValueSize(i));
            }

            for (int i = 0; i < size; i++)
            {
                int key;
                int mode = rng.Next(4);
                switch (mode)
                {
                    default:
                    case 0:
                        key = rng.Next(10);
                        break;
                    case 1:
                        key = rng.Next(100);
                        break;
                    case 2:
                        key = rng.Next(10_000);
                        break;
                    case 3:
                        key = rng.Next(1_000_000);
                        break;
                }

                object? val = cache.Get(key);
                if (val is null)
                {
                    cache.Put(key, CreateValue(key), GetValueSize(key));
                }
            }

            Utils.CollectGarbage();
            cache.TrimNonResidentQueue();
            long memoryUsed = Utils.GetMemoryUsed();

            int sizeHot = cache.SizeHot();
            int sizeResident = cache.Size();
            int sizeNonResident = cache.SizeNonResident();
            long hits = cache.GetHits();
            long misses = cache.GetMisses();
            Console.WriteLine(mb + " | "
                + ((memoryUsed - memoryUsedInitial + 512) / 1024) + " | "
                + (sizeResident + sizeNonResident) + " | "
                + sizeHot + " | " + (sizeResident - sizeHot) + " | " + sizeNonResident
                + " | " + ((hits * 100) / (hits + misses)));
        }
    }

    private static object CreateValue(long key)
    {
        return new byte[2540];
    }

    private static int GetValueSize(long key)
    {
        return 2560;
    }
}
