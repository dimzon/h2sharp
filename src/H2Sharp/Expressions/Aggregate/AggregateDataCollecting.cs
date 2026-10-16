using System.Collections;
using System.Collections.Generic;
using System.Linq;

using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

///
/// Data stored while calculating an aggregate that needs collecting of all
/// values or a distinct aggregate.
///
/// NULL values are not collected. <see cref="GetValue"/> never answers for this
/// class, use <see cref="GetArray"/> instead.
///
internal sealed class AggregateDataCollecting : AggregateData, IEnumerable<Value>
{
    ///
    /// NULL values collection mode.
    ///
    internal enum NullCollectionMode
    {
        ///
        /// Rows with NULL value are completely ignored.
        ///
        Ignored,

        ///
        /// Rows with NULL values are processed causing the result to be not
        /// NULL, but NULL values aren't collected.
        ///
        Excluded,

        ///
        /// Rows with NULL values are aggregated just like rows with any other
        /// values, should also be used when NULL values aren't passed in.
        ///
        UsedOrImpossible,
    }

    private readonly bool _distinct;

    private readonly bool _orderedWithOrder;

    private readonly NullCollectionMode _nullCollectionMode;

    private ICollection<Value>? _values;

    private Value? _shared;

    ///
    /// Creates new instance of data for collecting aggregates.
    ///
    /// <param name="distinct">if distinct is used</param>
    /// <param name="orderedWithOrder">if aggregate is an ordered aggregate with ORDER BY clause</param>
    /// <param name="nullCollectionMode">NULL values collection mode</param>
    internal AggregateDataCollecting(bool distinct, bool orderedWithOrder, NullCollectionMode nullCollectionMode)
    {
        _distinct = distinct;
        _orderedWithOrder = orderedWithOrder;
        _nullCollectionMode = nullCollectionMode;
    }

    internal override void Add(SessionLocal session, Value? v)
    {
        if (_nullCollectionMode == NullCollectionMode.Ignored && IsNull(v))
        {
            return;
        }

        ICollection<Value> c = _values ??= _distinct
            ? (ICollection<Value>)new SortedSet<Value>(new SessionComparer(session))
            : new List<Value>();
        if (_nullCollectionMode == NullCollectionMode.Excluded && IsNull(v))
        {
            return;
        }

        c.Add(v!);
    }

    private bool IsNull(Value? v) =>
        (_orderedWithOrder ? ((ValueRow)v!).GetList()[0] : v) == ValueNull.Instance;

    internal override Value GetValue(SessionLocal session) =>
        // The reference returns null here and every reader uses GetArray instead;
        // answering would be a lie, so the dead end refuses loudly.
        throw Message.DbException.GetInternalError("AggregateDataCollecting.getValue");

    ///
    /// Returns the count of values.
    ///
    /// <returns>the count of values</returns>
    internal int GetCount() => _values?.Count ?? 0;

    ///
    /// Returns array with values or null.
    ///
    /// <returns>array with values or null</returns>
    internal Value[]? GetArray() => _values?.ToArray();

    /// <inheritdoc/>
    public IEnumerator<Value> GetEnumerator() =>
        (_values ?? (ICollection<Value>)[]).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    ///
    /// Sets value of a shared argument.
    ///
    /// <param name="shared">the shared value</param>
    internal void SetSharedArgument(Value shared)
    {
        if (_shared is null)
        {
            _shared = shared;
        }
        else if (!_shared.Equals(shared))
        {
            throw Message.DbException.Get(
                ErrorCode.InvalidValue2,
                "Inverse distribution function argument",
                _shared.GetTraceSql() + "<>" + shared.GetTraceSql());
        }
    }

    ///
    /// Returns value of a shared argument.
    ///
    /// <returns>value of a shared argument</returns>
    internal Value? GetSharedArgument() => _shared;

    ///
    /// The session as a value comparer: DISTINCT deduplicates by session
    /// comparison order, the way the reference hands the session itself to the
    /// set. C# sets take an explicit comparer, so the session gets a thin skin.
    ///
    private sealed class SessionComparer(SessionLocal session) : IComparer<Value>
    {
        public int Compare(Value? x, Value? y) => session.Compare(x!, y!);
    }
}
