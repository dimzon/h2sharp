using H2Sharp.MvStore.Type;

namespace H2Sharp.Util;

/// <summary>
/// Guesses how much memory values take without measuring every one of them.
/// </summary>
/// <remarks>
/// Measuring each value would cost more than the answer is worth, so the first few hundred values
/// are measured exactly and everything after that reuses a sliding average — until the average
/// drifts, at which point the window reopens and measures again. All the packed state lives in one
/// long so concurrent samplers update it with a single compare-and-set:
/// <list type="bullet">
/// <item>bits 0–7 count down the requests that may skip measuring,</item>
/// <item>bits 8–23 total the skips between the last 256 measurements, for the sampling percentage,</item>
/// <item>bit 24 says whether the window has filled once,</item>
/// <item>bits 32–63 hold the sliding sum of the estimates.</item>
/// </list>
/// <para>
/// The pointer size folded into every estimate is 8, the reference's <c>MEMORY_POINTER</c>.
/// </para>
/// </remarks>
internal static class MemoryEstimator
{
    private const int SkipSumShift = 8;

    private const int CounterMask = (1 << SkipSumShift) - 1;

    private const int SkipSumMask = 0xFFFF;

    private const int InitBitShift = 24;

    private const long InitBit = 1L << InitBitShift;

    private const int WindowShift = 8;

    private const int MagnitudeLimit = WindowShift - 1;

    private const int WindowSize = 1 << WindowShift;

    private const int WindowHalfSize = WindowSize >> 1;

    private const int SumShift = 32;

    private const int MemoryPointer = 8;

    /// <summary>
    /// The estimated memory of one value, measuring it only when the window says to.
    /// </summary>
    internal static int EstimateMemory<T>(AtomicLong stats, DataType<T> dataType, T? data)
    {
        long statsData = stats.Get();
        int counter = GetCounter(statsData);
        int skipSum = GetSkipSum(statsData);
        long initialized = statsData & InitBit;
        long sum = (long)((ulong)statsData >> SumShift);
        int mem = 0;
        int cnt = 0;
        if (initialized == 0 || counter-- == 0)
        {
            cnt = 1;
            mem = data is T exact ? dataType.GetMemory(exact) : 0;
            long delta = ((long)mem << WindowShift) - sum;
            if (initialized == 0)
            {
                if (++counter == WindowSize)
                {
                    initialized = InitBit;
                }

                sum = (sum * counter + delta + (counter >> 1)) / counter;
            }
            else
            {
                long absDelta = delta >= 0 ? delta : -delta;
                int magnitude = CalculateMagnitude(sum, absDelta);
                sum += ((delta >> (MagnitudeLimit - magnitude)) + 1) >> 1;
                counter = ((1 << magnitude) - 1) & CounterMask;

                delta = (counter << WindowShift) - skipSum;
                skipSum += (int)((delta + WindowHalfSize) >> WindowShift);
            }
        }

        long updated = UpdateStatsData(stats, statsData, counter, skipSum, initialized, sum, cnt, mem);
        return GetAverage(updated);
    }

    /// <summary>
    /// The estimated memory of a run of values, measuring only the window's share of them.
    /// </summary>
    internal static int EstimateMemory<T>(AtomicLong stats, DataType<T> dataType, T[] storage, int count)
    {
        long statsData = stats.Get();
        int counter = GetCounter(statsData);
        int skipSum = GetSkipSum(statsData);
        long initialized = statsData & InitBit;
        long sum = (long)((ulong)statsData >> SumShift);
        int index = 0;
        int memSum = 0;
        if (initialized != 0 && counter >= count)
        {
            counter -= count;
        }
        else
        {
            int cnt = count;
            while (cnt-- > 0)
            {
                T? data = storage[index++];
                int mem = data is T exact ? dataType.GetMemory(exact) : 0;
                memSum += mem;
                long delta = ((long)mem << WindowShift) - sum;
                if (initialized == 0)
                {
                    if (++counter == WindowSize)
                    {
                        initialized = InitBit;
                    }

                    sum = (sum * counter + delta + (counter >> 1)) / counter;
                }
                else
                {
                    cnt -= counter;
                    long absDelta = delta >= 0 ? delta : -delta;
                    int magnitude = CalculateMagnitude(sum, absDelta);
                    sum += ((delta >> (MagnitudeLimit - magnitude)) + 1) >> 1;
                    counter += ((1 << magnitude) - 1) & CounterMask;

                    delta = ((long)counter << WindowShift) - skipSum;
                    skipSum += (int)((delta + WindowHalfSize) >> WindowShift);
                }
            }
        }

        long updated = UpdateStatsData(stats, statsData, counter, skipSum, initialized, sum, index, memSum);
        return (GetAverage(updated) + MemoryPointer) * count;
    }

    /// <summary>
    /// What share of the requests still gets measured, in percent.
    /// </summary>
    internal static int SamplingPct(AtomicLong stats)
    {
        long statsData = stats.Get();
        int count = (statsData & InitBit) == 0 ? GetCounter(statsData) : WindowSize;
        int total = GetSkipSum(statsData) + count;
        return (count * 100 + (total >> 1)) / total;
    }

    private static int CalculateMagnitude(long sum, long absDelta)
    {
        int magnitude = 0;
        while (absDelta < sum && magnitude < MagnitudeLimit)
        {
            ++magnitude;
            absDelta <<= 1;
        }

        return magnitude;
    }

    private static long UpdateStatsData(AtomicLong stats, long statsData,
        int counter, int skipSum, long initialized, long sum,
        int itemsCount, int itemsMem)
        => UpdateStatsData(stats, statsData,
            ConstructStatsData(sum, initialized, skipSum, counter), itemsCount, itemsMem);

    private static long ConstructStatsData(long sum, long initialized, int skipSum, int counter)
        => (sum << SumShift) | initialized | ((long)skipSum << SkipSumShift) | (long)counter;

    private static long UpdateStatsData(AtomicLong stats, long statsData, long updatedStatsData,
        int itemsCount, int itemsMem)
    {
        while (!stats.CompareAndSet(statsData, updatedStatsData))
        {
            statsData = stats.Get();
            long sum = (long)((ulong)statsData >> SumShift);
            if (itemsCount > 0)
            {
                sum += itemsMem - ((sum * itemsCount + WindowHalfSize) >> WindowShift);
            }

            updatedStatsData = (sum << SumShift)
                | (statsData & (InitBit | (long)SkipSumMask | (long)CounterMask));
        }

        return updatedStatsData;
    }

    private static int GetCounter(long statsData) => (int)(statsData & CounterMask);

    private static int GetSkipSum(long statsData) => (int)((ulong)statsData >> SkipSumShift & SkipSumMask);

    private static int GetAverage(long updatedStatsData)
        => (int)((ulong)updatedStatsData >> (SumShift + WindowShift));
}
