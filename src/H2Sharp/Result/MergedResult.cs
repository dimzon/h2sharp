/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.result.MergedResult.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Several results, one result.
 *
 * A batch of generated keys, or anything else that arrives as a few results and has to be read
 * as one, is merged here. The columns are matched by alias and name and put in the order they
 * first appeared, so a caller reads the merged result by the names it already knows: the
 * requirement that the merged columns agree on their types is the caller's to meet, and is why
 * the class says so where it is declared.
 */
using System.Collections.Generic;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Result;

/// <summary>
/// Merged result. Used to combine several results into one. Merged result will contain rows from
/// all appended results. Results are not required to have the same lists of columns, but required
/// to have compatible column definitions, for example, if one result has a
/// <c>VARCHAR</c> column <c>NAME</c> then another result that has a <c>NAME</c> column should
/// also define it with the same type.
/// </summary>
public sealed class MergedResult
{
    private readonly List<Dictionary<SimpleResult.Column, Value?>> data = Utils.NewSmallArrayList<Dictionary<SimpleResult.Column, Value?>>();

    private readonly List<SimpleResult.Column> columns = Utils.NewSmallArrayList<SimpleResult.Column>();

    /// <summary>
    /// Appends a result.
    /// </summary>
    /// <param name="result">result to append</param>
    public void Add(ResultInterface result)
    {
        System.ArgumentNullException.ThrowIfNull(result);
        int count = result.GetVisibleColumnCount();
        if (count == 0)
        {
            return;
        }

        SimpleResult.Column[] cols = new SimpleResult.Column[count];
        for (int i = 0; i < count; i++)
        {
            SimpleResult.Column c = new(result.GetAlias(i), result.GetColumnName(i), result.GetColumnType(i));
            cols[i] = c;
            if (!columns.Contains(c))
            {
                columns.Add(c);
            }
        }

        while (result.Next())
        {
            if (count == 1)
            {
                data.Add(new Dictionary<SimpleResult.Column, Value?> { [cols[0]] = result.CurrentRow()[0] });
            }
            else
            {
                Dictionary<SimpleResult.Column, Value?> map = [];
                for (int i = 0; i < count; i++)
                {
                    SimpleResult.Column ci = cols[i];
                    map[ci] = result.CurrentRow()[i];
                }

                data.Add(map);
            }
        }
    }

    /// <summary>
    /// Returns merged results.
    /// </summary>
    /// <returns>result with rows from all appended result sets</returns>
    public SimpleResult GetResult()
    {
        SimpleResult result = new();
        foreach (SimpleResult.Column c in columns)
        {
            result.AddColumn(c);
        }

        foreach (Dictionary<SimpleResult.Column, Value?> map in data)
        {
            Value?[] row = new Value?[columns.Count];
            foreach (KeyValuePair<SimpleResult.Column, Value?> entry in map)
            {
                row[columns.IndexOf(entry.Key)] = entry.Value;
            }

            result.AddRow(row);
        }

        return result;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return string.Join(", ", columns) + ": " + data.Count;
    }
}
