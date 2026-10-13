using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// A map whose keys are stored folded to upper case with the English rules, and looked up the same
/// way.
/// </summary>
/// <remarks>
/// The fold is applied on the way in and on every lookup. What comes back out through
/// <see cref="Dictionary{TKey, TValue}.Keys"/>, <c>Values</c> and <c>Entries</c> is the folded
/// spelling, because that is what is actually stored — the reference is a <c>HashMap</c> subclass
/// that folds on <c>put</c> and folds again on lookup, and folds nowhere else. A caller that put in
/// <c>"table"</c> and reads <c>Keys</c> gets <c>"TABLE"</c>, and that is not an oversight to tidy
/// away: it is what a schema looks like from the inside.
/// <para>
/// The fold is <see cref="StringUtils.ToUpperEnglish"/>, not the current culture. Under a Turkish
/// locale <c>i</c> folds to <c>İ</c>, which would give the same schema different names depending on
/// the machine it was opened on.
/// </para>
/// </remarks>
public class CaseInsensitiveMap<TValue> : Dictionary<string, TValue>
{
    /// <summary>
    /// An empty map.
    /// </summary>
    public CaseInsensitiveMap()
    {
    }

    /// <summary>
    /// An empty map with a starting capacity.
    /// </summary>
    /// <param name="initialCapacity">the starting capacity</param>
    public CaseInsensitiveMap(int initialCapacity)
        : base(initialCapacity)
    {
    }

    /// <inheritdoc/>
    public new TValue? this[string key]
    {
        get => base.TryGetValue(Fold(key), out TValue? value) ? value : default;
        set => base[Fold(key)] = value!;
    }

    /// <inheritdoc/>
    public new bool ContainsKey(string key) => base.ContainsKey(Fold(key));

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the value that was removed, as the reference does. <c>Dictionary.Remove</c> returns
    /// whether anything was there instead, which is why this hides it rather than overriding it.
    /// </remarks>
    public new TValue? Remove(string key)
    {
        // Folded once, like every other member: the fold is pure, so folding twice answers the
        // same string while paying the hash and the scan twice per remove.
        string folded = Fold(key);
        return base.TryGetValue(folded, out TValue? value) && base.Remove(folded) ? value : default;
    }

    /// <summary>
    /// Adds the value only if there is nothing under that name, folded.
    /// </summary>
    /// <param name="key">the name</param>
    /// <param name="value">the value</param>
    /// <returns>what was there, or nothing</returns>
    public TValue? PutIfAbsent(string key, TValue value)
    {
        string folded = Fold(key);
        if (base.TryGetValue(folded, out TValue? existing))
        {
            return existing;
        }

        base[folded] = value;
        return default;
    }

    private static string Fold(string key) => StringUtils.ToUpperEnglish(key);
}

/// <summary>
/// The same fold over a concurrent map, for the places where two threads look names up at once.
/// </summary>
/// <remarks>
/// Deliberately not a subclass of <see cref="CaseInsensitiveMap{TValue}"/>, which locks nothing:
/// inheriting from it would produce a map that looks concurrent and is not.
/// </remarks>
public class CaseInsensitiveConcurrentMap<TValue> : ConcurrentDictionary<string, TValue>
{
    /// <inheritdoc/>
    public new TValue? this[string key]
    {
        get => base.TryGetValue(Fold(key), out TValue? value) ? value : default;
        set => base[Fold(key)] = value!;
    }

    /// <inheritdoc/>
    public new bool ContainsKey(string key) => base.ContainsKey(Fold(key));

    /// <summary>
    /// Drops the name, folded, and says whether it was there.
    /// </summary>
    /// <param name="key">the name</param>
    /// <returns>true when it was there</returns>
    public TValue? Remove(string key) => base.TryRemove(Fold(key), out TValue? value) ? value : default;

    /// <summary>
    /// Adds the value only if there is nothing under that name, folded.
    /// </summary>
    /// <param name="key">the name</param>
    /// <param name="value">the value</param>
    /// <returns>what was there, or nothing</returns>
    public TValue? PutIfAbsent(string key, TValue value)
    {
        // The reference's putIfAbsent answers what was already there, which is nothing on the first
        // call. ConcurrentDictionary.GetOrAdd answers what it just stored instead, which is the
        // opposite and would report a value that was never there before this line.
        string folded = Fold(key);
        if (base.TryGetValue(folded, out TValue? existing))
        {
            return existing;
        }

        base[folded] = value;
        return default;
    }

    private static string Fold(string key) => StringUtils.ToUpperEnglish(key);
}
