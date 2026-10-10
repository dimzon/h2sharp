/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.test.store.SequenceMap.
 *
 * A map whose keys and values are always 1 through 10, whatever is stored: the custom map type
 * behind the custom-type test. Only the key set is custom; everything else is the map it extends.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Type;

namespace H2Sharp.MvStore.Tests;

/// <summary>
/// A custom map returning the keys and values 1 through 10.
/// </summary>
public class SequenceMap : MVMap<long, long>
{
    private int min = 1;

    private int max = 10;

    /// <summary>
    /// A sequence map from a configuration.
    /// </summary>
    public SequenceMap(IDictionary<string, object?> config, DataType<long> keyType, DataType<long> valueType)
        : base(config, keyType, valueType)
    {
    }

    /// <inheritdoc/>
    public override ICollection<long> KeySet() => new SequenceKeys(this);

    private sealed class SequenceKeys(SequenceMap map) : ICollection<long>
    {
        public int Count => map.max - map.min + 1;

        public bool IsReadOnly => true;

        public IEnumerator<long> GetEnumerator()
        {
            for (long x = map.min; x <= map.max; x++)
            {
                yield return x;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Contains(long item) => item >= map.min && item <= map.max;

        public void CopyTo(long[] array, int arrayIndex)
        {
            foreach (long x in this)
            {
                array[arrayIndex++] = x;
            }
        }

        public void Add(long item) => throw new NotSupportedException();

        public bool Remove(long item) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();
    }

    /// <summary>
    /// A builder for this class.
    /// </summary>
    public sealed class Builder : MVMap<long, long>.Builder<long, long>
    {
        /// <inheritdoc/>
        protected override MVMap<long, long> Create(IDictionary<string, object?> config)
            => new SequenceMap(config, GetKeyType()!, GetValueType()!);
    }
}
