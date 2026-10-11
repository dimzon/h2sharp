/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.result.BatchResult.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What a batch said: how much each statement changed, and what went wrong where.
 *
 * One count per statement, in order, including the ones that failed: that is what lets a caller
 * report "the third statement of seven failed" instead of guessing, and it is why this is three
 * fields rather than a total.
 */
using System.Collections.Generic;

namespace H2Sharp.Result;

/// <summary>
/// Result of a batch execution.
/// </summary>
public class BatchResult
{
    private readonly long[] updateCounts;

    private readonly ResultInterface? generatedKeys;

    private readonly List<H2SharpException> exceptions;

    /// <summary>
    /// Creates a batch result.
    /// </summary>
    /// <param name="updateCounts">the count of each statement, in order</param>
    /// <param name="generatedKeys">the generated keys, or null</param>
    /// <param name="exceptions">the failures, in the order of the statements</param>
    public BatchResult(long[] updateCounts, ResultInterface? generatedKeys, List<H2SharpException> exceptions)
    {
        this.updateCounts = updateCounts;
        this.generatedKeys = generatedKeys;
        this.exceptions = exceptions;
    }

    /// <summary>
    /// Gets the number of rows each statement changed.
    /// </summary>
    /// <returns>the counts, in order</returns>
    public long[] GetUpdateCounts()
    {
        return updateCounts;
    }

    /// <summary>
    /// Gets the generated keys, or null.
    /// </summary>
    /// <returns>the generated keys, or null</returns>
    public ResultInterface? GetGeneratedKeys()
    {
        return generatedKeys;
    }

    /// <summary>
    /// Gets the failures of the statements that failed.
    /// </summary>
    /// <returns>the failures, in the order of the statements</returns>
    public List<H2SharpException> GetExceptions()
    {
        return exceptions;
    }
}
