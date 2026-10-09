using System;
using System.Globalization;
using System.Text;

namespace H2Sharp.MvStore.Type;

/// <summary>
/// Strings, held as a character count followed by the store's own compact UTF-8.
/// </summary>
/// <remarks>
/// Ordering is <see cref="string.CompareOrdinal(string)"/>, not a culture comparison. The store has to
/// order keys the same way on every machine, and a culture comparison would not.
/// </remarks>
public sealed class StringDataType : BasicDataType<string>
{
    /// <summary>
    /// The one instance.
    /// </summary>
    public static readonly StringDataType Instance = new();

    private StringDataType()
    {
    }

    /// <inheritdoc/>
    public override string[] CreateStorage(int size) => size == 0 ? [] : new string[size];

    /// <inheritdoc/>
    public override int Compare(string? a, string? b) => string.CompareOrdinal(a, b);

    /// <inheritdoc/>
    /// <remarks>
    /// The search is written out rather than inherited so that the comparison does not go through
    /// <see cref="Compare(T, T)"/>: in a page of a stored map this is the hottest comparison in the
    /// store, and an interface call per step is the difference that shows.
    /// </remarks>
    public override int BinarySearch(string key, string[] storage, int size, int initialGuess)
    {
        int low = 0;
        int high = size - 1;

        // the cached index minus one, so that
        // for the first time (when the cached value is 0),
        // the default value is used
        int x = initialGuess - 1;
        if (x < 0 || x > high)
        {
            x = high >> 1;
        }

        while (low <= high)
        {
            int compare = string.CompareOrdinal(key, storage[x]);
            if (compare > 0)
            {
                low = x + 1;
            }
            else if (compare < 0)
            {
                high = x - 1;
            }
            else
            {
                return x;
            }

            x = (low + high) >> 1;
        }

        return ~low;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 24 bytes for the object and two per character, which is what the reference counts. A .NET string
    /// is laid out differently, so this figure is an estimate here in a way it was not there; it is
    /// kept identical because the page tree divides by it and a different scale would move where pages
    /// split.
    /// </remarks>
    public override int GetMemory(string obj) => 24 + (2 * obj.Length);

    /// <inheritdoc/>
    public override string Read(Span<byte> buff, ref int position) => DataUtils.ReadString(buff, ref position);

    /// <inheritdoc/>
    public override void Write(WriteBuffer buff, string s)
    {
        int len = s.Length;
        _ = buff.PutVarInt(len);
        _ = buff.PutStringData(s, len);
    }
}
