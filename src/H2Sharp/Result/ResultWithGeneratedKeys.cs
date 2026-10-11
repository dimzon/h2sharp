/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.result.ResultWithGeneratedKeys.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What an updating statement returned: a count, and sometimes the keys it made up.
 *
 * Most statements only have the count, and asking for the keys is rare enough that it should not
 * cost the common case. So there are two shapes rather than one with a field that is usually null:
 * Of() for a plain count, and the nested WithKeys for the other. The difference is visible to the
 * caller through GetGeneratedKeys(), which answers null rather than pretending there were none.
 */
namespace H2Sharp.Result;

/// <summary>
/// Result of update command with optional generated keys.
/// </summary>
public class ResultWithGeneratedKeys
{
    /// <summary>
    /// Result of update command with generated keys.
    /// </summary>
    public sealed class WithKeys : ResultWithGeneratedKeys
    {
        private readonly ResultInterface generatedKeys;

        /// <summary>
        /// Creates a result with update count and generated keys.
        /// </summary>
        /// <param name="updateCount">update count</param>
        /// <param name="generatedKeys">generated keys</param>
        public WithKeys(long updateCount, ResultInterface generatedKeys)
            : base(updateCount)
        {
            this.generatedKeys = generatedKeys;
        }

        /// <inheritdoc/>
        public override ResultInterface GetGeneratedKeys()
        {
            return generatedKeys;
        }
    }

    /// <summary>
    /// Returns a result with only update count.
    /// </summary>
    /// <param name="updateCount">update count</param>
    /// <returns>the result</returns>
    public static ResultWithGeneratedKeys Of(long updateCount)
    {
        return new ResultWithGeneratedKeys(updateCount);
    }

    private readonly long updateCount;

    /// <summary>
    /// Creates a result with only update count.
    /// </summary>
    /// <param name="updateCount">update count</param>
    protected ResultWithGeneratedKeys(long updateCount)
    {
        this.updateCount = updateCount;
    }

    /// <summary>
    /// Returns generated keys, or <c>null</c>.
    /// </summary>
    /// <returns>generated keys, or <c>null</c></returns>
    public virtual ResultInterface? GetGeneratedKeys()
    {
        return null;
    }

    /// <summary>
    /// Returns update count.
    /// </summary>
    /// <returns>update count</returns>
    public long GetUpdateCount()
    {
        return updateCount;
    }
}
