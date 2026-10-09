using System;
using System.Collections.Generic;
using System.Text;

namespace H2Sharp.MvStore.Type;

/// <summary>
/// How the store holds one kind of value: how to make an array of them, how to find one in it, and how
/// it travels to and from disk.
/// </summary>
/// <remarks>
/// This is the contract every page is written against, which is why it is worth reading before
/// touching the page tree. The reference uses <c>Object</c> for the storage array and unchecked casts
/// rather than an array type of its own; this port says <see cref="T"/>[] where the reference says
/// <c>Object</c>, which is the same contract with the cast moved to the one place that has to do it.
/// <para>
/// The read side takes a span and a position rather than a <c>ByteBuffer</c>. The reference hands
/// around a buffer with its own position, and every call site reads further along; a span plus a
/// <c>ref</c> position is the same thing without the shared cursor, and it means a type can never be
/// read from the wrong offset because a caller forgot to advance.
/// </para>
/// </remarks>
/// <typeparam name="T">the value type</typeparam>
public interface DataType<T> : IComparer<T>
{
    /// <summary>
    /// Find a key in a sorted array, starting the search near where the last one landed.
    /// </summary>
    /// <param name="key">the key to look for</param>
    /// <param name="storage">the array to search</param>
    /// <param name="size">how much of it is in use</param>
    /// <param name="initialGuess">
    /// where the previous search ended, minus one. Zero means "no idea", which starts in the middle.
    /// </param>
    /// <returns>
    /// the index of the key, or the bitwise complement of the index where it would have to be
    /// inserted
    /// </returns>
    int BinarySearch(T key, T[] storage, int size, int initialGuess);

    /// <summary>
    /// How many bytes a value costs.
    /// </summary>
    /// <param name="obj">the value</param>
    /// <returns>the memory</returns>
    int GetMemory(T obj);

    /// <summary>
    /// Whether <see cref="GetMemory"/> is exact, as opposed to a guess the page tree may divide.
    /// </summary>
    /// <returns>true when the figure is exact</returns>
    bool IsMemoryEstimationAllowed();

    /// <summary>
    /// Write one value.
    /// </summary>
    /// <param name="buff">where to write</param>
    /// <param name="obj">the value</param>
    void Write(WriteBuffer buff, T obj);

    /// <summary>
    /// Write several values in a row.
    /// </summary>
    /// <param name="buff">where to write</param>
    /// <param name="storage">the values</param>
    /// <param name="len">how many of them</param>
    void Write(WriteBuffer buff, T[] storage, int len);

    /// <summary>
    /// Read one value.
    /// </summary>
    /// <param name="buff">where to read</param>
    /// <param name="position">where in it, moved past what was read</param>
    /// <returns>the value</returns>
    T Read(Span<byte> buff, ref int position);

    /// <summary>
    /// Read several values into an array.
    /// </summary>
    /// <param name="buff">where to read</param>
    /// <param name="storage">where to put them</param>
    /// <param name="len">how many</param>
    /// <param name="position">where in the buffer, moved past what was read</param>
    void Read(Span<byte> buff, T[] storage, int len, ref int position);

    /// <summary>
    /// An array of the given size to hold values of this type.
    /// </summary>
    /// <param name="size">how many</param>
    /// <returns>the array</returns>
    T[] CreateStorage(int size);
}

/// <summary>
/// A data type that has no way of storing itself by itself, and so can be written into and read out of
/// only one value at a time.
/// </summary>
/// <remarks>
/// Everything except the bulk read and write is left to the subclass, which is what a type that
/// carries its own state needs. What it does get here is the binary search, and it is the same search
/// in every subclass, starting near where the last one finished.
/// </remarks>
/// <typeparam name="T">the value type</typeparam>
public abstract class BasicDataType<T> : DataType<T>
{
    /// <inheritdoc/>
    public abstract T[] CreateStorage(int size);

    /// <inheritdoc/>
    public abstract int GetMemory(T obj);

    /// <inheritdoc/>
    public abstract void Write(WriteBuffer buff, T obj);

    /// <inheritdoc/>
    public abstract T Read(Span<byte> buff, ref int position);

    /// <inheritdoc/>
    /// <remarks>
    /// The reference answers "cannot compare" rather than refusing at construction, so a type that
    /// does not order its values still reads and writes and only refuses when asked to search.
    /// </remarks>
    public virtual int Compare(T? a, T? b) => throw DataUtils.NewUnsupportedOperationException("Can not compare");

    /// <inheritdoc/>
    public virtual bool IsMemoryEstimationAllowed() => true;

    /// <inheritdoc/>
    public virtual int BinarySearch(T key, T[] storage, int size, int initialGuess)
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
            int compare = Compare(key, storage[x]);
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
    public virtual void Write(WriteBuffer buff, T[] storage, int len)
    {
        for (int i = 0; i < len; i++)
        {
            Write(buff, storage[i]);
        }
    }

    /// <inheritdoc/>
    public virtual void Read(Span<byte> buff, T[] storage, int len, ref int position)
    {
        for (int i = 0; i < len; i++)
        {
            storage[i] = Read(buff, ref position);
        }
    }

    /// <inheritdoc/>
    public override int GetHashCode() => GetType().Name.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is not null && GetType() == obj.GetType();
}

/// <summary>
/// A data type that carries state, and therefore has to be saved along with its class name.
/// </summary>
/// <typeparam name="D">what the state is</typeparam>
public interface StatefulDataType<D>
{
    /// <summary>
    /// Write the state that is not in the class name.
    /// </summary>
    /// <param name="buff">where to write</param>
    /// <param name="metaType">the meta type that is reading it back</param>
    void Save(WriteBuffer buff, MetaType<D> metaType);

    /// <summary>
    /// How to rebuild this type when it is read back.
    /// </summary>
    /// <returns>the factory</returns>
    Factory GetFactory();

    /// <summary>
    /// Rebuilds a data type from what was written after its class name.
    /// </summary>
    /// <remarks>
    /// The rebuilt type is <see cref="DataType{T}"/> of <see cref="object"/> whatever it really holds,
    /// which is the reference's <c>DataType&lt;?&gt;</c>: the meta type stores types of many different
    /// element types side by side and only needs enough of them to write them out again.
    /// </remarks>
    public interface Factory
    {
        /// <summary>
        /// Build the data type, reading whatever it saved.
        /// </summary>
        /// <param name="buff">where to read</param>
        /// <param name="position">where in it, moved past what was read</param>
        /// <param name="metaType">the meta type that is reading</param>
        /// <param name="database">what the state belongs to</param>
        /// <returns>the data type</returns>
        DataType<object> Create(Span<byte> buff, ref int position, MetaType<D> metaType, D database);
    }
}
