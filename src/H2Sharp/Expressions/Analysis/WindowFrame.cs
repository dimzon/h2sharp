/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.analysis.WindowFrame.
 * Java Iterator<Value[]> is represented by IEnumerable<Value?[]>; iterator helpers
 * preserve the reference's segment boundaries and ordering without copying row data. */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Analysis;

/// <summary>Window frame clause.</summary>
public sealed class WindowFrame
{
    private readonly WindowFrameUnits units;
    private readonly WindowFrameBound starting;
    private readonly WindowFrameBound? following;
    private readonly WindowFrameExclusion exclusion;
    /// <summary>Creates a frame.</summary>
    public WindowFrame(WindowFrameUnits units, WindowFrameBound starting, WindowFrameBound? following, WindowFrameExclusion exclusion)
    {
        this.units = units; this.starting = starting;
        this.following = following?.GetBoundType() == WindowFrameBoundType.CurrentRow ? null : following;
        this.exclusion = exclusion;
    }
    /// <summary>Iterates the selected frame or the default RANGE frame.</summary>
    public static IEnumerable<Value?[]> Iterator(Window over, SessionLocal session, List<Value?[]> rows, SortOrder? sort, int currentRow, bool reverse)
    {
        WindowFrame? frame = over.GetWindowFrame();
        if (frame is not null) return frame.Iterator(session, rows, sort, currentRow, reverse);
        int end = rows.Count - 1;
        return PlainIterator(rows, 0, over.GetOrderBy() is null ? end : ToGroupEnd(rows, sort!, currentRow, end), reverse);
    }
    /// <summary>Returns the inclusive frame end.</summary>
    public static int GetEndIndex(Window over, SessionLocal session, List<Value?[]> rows, SortOrder? sort, int currentRow)
    {
        WindowFrame? frame = over.GetWindowFrame();
        if (frame is not null) return frame.GetEndIndex(session, rows, sort, currentRow);
        int end = rows.Count - 1;
        return over.GetOrderBy() is null ? end : ToGroupEnd(rows, sort!, currentRow, end);
    }
    private static IEnumerable<Value?[]> PlainIterator(List<Value?[]> rows, int start, int end, bool reverse)
    {
        if (reverse) { for (int i = end; i >= start; i--) yield return rows[i]; }
        else { for (int i = start; i <= end; i++) yield return rows[i]; }
    }
    private static IEnumerable<Value?[]> BiIterator(List<Value?[]> rows, int start1, int end1, int start2, int end2, bool reverse)
    {
        if (reverse)
        {
            foreach (Value?[] row in PlainIterator(rows, start2, end2, true)) yield return row;
            foreach (Value?[] row in PlainIterator(rows, start1, end1, true)) yield return row;
        }
        else
        {
            foreach (Value?[] row in PlainIterator(rows, start1, end1, false)) yield return row;
            foreach (Value?[] row in PlainIterator(rows, start2, end2, false)) yield return row;
        }
    }
    private static IEnumerable<Value?[]> TriIterator(List<Value?[]> rows, int start1, int end1, int start2, int end2, int start3, int end3, bool reverse)
    {
        if (reverse)
        {
            foreach (Value?[] row in PlainIterator(rows, start3, end3, true)) yield return row;
            foreach (Value?[] row in BiIterator(rows, start1, end1, start2, end2, true)) yield return row;
        }
        else
        {
            foreach (Value?[] row in BiIterator(rows, start1, end1, start2, end2, false)) yield return row;
            foreach (Value?[] row in PlainIterator(rows, start3, end3, false)) yield return row;
        }
    }
    private static int ToGroupStart(List<Value?[]> rows, SortOrder sort, int offset, int min)
    {
        Value?[] row = rows[offset];
        while (offset > min && sort.Compare(row, rows[offset - 1]) == 0) offset--;
        return offset;
    }
    private static int ToGroupEnd(List<Value?[]> rows, SortOrder sort, int offset, int max)
    {
        Value?[] row = rows[offset];
        while (offset < max && sort.Compare(row, rows[offset + 1]) == 0) offset++;
        return offset;
    }
    private static int GetIntOffset(WindowFrameBound bound, Value?[] values, SessionLocal session)
    {
        Value v = bound.IsVariable() ? values[bound.GetExpressionIndex()]! : bound.GetValue()!.GetValue(session);
        if (v is ValueNull || v.GetInt() < 0) throw DbException.Get(Api.ErrorCode.InvalidPrecedingOrFollowing1, v.GetTraceSql());
        return v.GetInt();
    }
    private static Value GetValueOffset(WindowFrameBound bound, Value?[] values, SessionLocal session)
    {
        Value v = bound.IsVariable() ? values[bound.GetExpressionIndex()]! : bound.GetValue()!.GetValue(session);
        if (v is ValueNull || v.GetSignum() < 0) throw DbException.Get(Api.ErrorCode.InvalidPrecedingOrFollowing1, v.GetTraceSql());
        return v;
    }
    private static Value?[]? GetCompareRow(SessionLocal session, List<Value?[]> rows, SortOrder sort, int currentRow, WindowFrameBound bound, bool add)
    {
        int sortIndex = sort.GetQueryColumnIndexes()[0];
        Value?[] row = rows[currentRow];
        Value currentValue = row[sortIndex]!;
        int type = currentValue.GetValueType();
        Value range = GetValueOffset(bound, row, session), newValue;
        if (type == Value.Null) newValue = ValueNull.Instance;
        else if (DataType.IsNumericType(type) || DataType.IsDateTimeType(type) || DataType.IsIntervalType(type))
        {
            BinaryOperation.OpType op = add ^ ((sort.GetSortTypes()[0] & SortOrder.Descending) != 0)
                ? BinaryOperation.OpType.PLUS : BinaryOperation.OpType.MINUS;
            try
            {
                newValue = new BinaryOperation(op, ValueExpression.Get(currentValue), ValueExpression.Get(range))
                    .Optimize(session).GetValue(session).ConvertToForComparison(type);
            }
            catch (H2SharpException error) when (error.H2ErrorCode is Api.ErrorCode.NumericValueOutOfRange1 or Api.ErrorCode.NumericValueOutOfRange2)
            { return null; }
        }
        else throw DbException.GetInvalidValueException("unsupported type of sort key for RANGE units", currentValue.GetTraceSql());
        Value?[] result = (Value?[])row.Clone();
        result[sortIndex] = newValue;
        return result;
    }
    /// <summary>Returns units.</summary>
    public WindowFrameUnits GetUnits() => units;
    /// <summary>Returns starting bound.</summary>
    public WindowFrameBound GetStarting() => starting;
    /// <summary>Returns following bound.</summary>
    public WindowFrameBound? GetFollowing() => following;
    /// <summary>Returns exclusion.</summary>
    public WindowFrameExclusion GetExclusion() => exclusion;
    /// <summary>Validates ordering of bound kinds.</summary>
    public bool IsValid()
    {
        WindowFrameBoundType s = starting.GetBoundType(), f = following?.GetBoundType() ?? WindowFrameBoundType.CurrentRow;
        return s != WindowFrameBoundType.UnboundedFollowing && f != WindowFrameBoundType.UnboundedPreceding && s <= f;
    }
    /// <summary>Whether bounds contain row-varying expressions.</summary>
    public bool IsVariableBounds() => starting.IsVariable() || following?.IsVariable() == true;
    internal void MapColumns(ColumnResolver resolver, int level, int state)
    { starting.MapColumns(resolver, level, state); following?.MapColumns(resolver, level, state); }
    internal void Optimize(SessionLocal session) { starting.Optimize(session); following?.Optimize(session); }
    internal void UpdateAggregate(SessionLocal session, int stage) { starting.UpdateAggregate(session, stage); following?.UpdateAggregate(session, stage); }
    /// <summary>Iterates this frame.</summary>
    public IEnumerable<Value?[]> Iterator(SessionLocal session, List<Value?[]> rows, SortOrder? sort, int currentRow, bool reverse)
    {
        int start = GetIndex(session, rows, sort, currentRow, starting, false);
        int end = following is not null ? GetIndex(session, rows, sort, currentRow, following, true)
            : units == WindowFrameUnits.Rows ? currentRow : ToGroupEnd(rows, sort!, currentRow, rows.Count - 1);
        if (end < start || start >= rows.Count || end < 0) return Array.Empty<Value?[]>();
        start = Math.Max(0, start); end = Math.Min(rows.Count - 1, end);
        return exclusion != WindowFrameExclusion.ExcludeNoOthers
            ? ComplexIterator(rows, sort, currentRow, start, end, reverse) : PlainIterator(rows, start, end, reverse);
    }
    /// <summary>Returns inclusive start index without exclusion.</summary>
    public int GetStartIndex(SessionLocal session, List<Value?[]> rows, SortOrder? sort, int currentRow)
    {
        if (exclusion != WindowFrameExclusion.ExcludeNoOthers) throw new NotSupportedException();
        return Math.Max(0, GetIndex(session, rows, sort, currentRow, starting, false));
    }
    private int GetEndIndex(SessionLocal session, List<Value?[]> rows, SortOrder? sort, int currentRow)
    {
        if (exclusion != WindowFrameExclusion.ExcludeNoOthers) throw new NotSupportedException();
        int end = following is not null ? GetIndex(session, rows, sort, currentRow, following, true)
            : units == WindowFrameUnits.Rows ? currentRow : ToGroupEnd(rows, sort!, currentRow, rows.Count - 1);
        return Math.Min(rows.Count - 1, end);
    }
    private int GetIndex(SessionLocal session, List<Value?[]> rows, SortOrder? sort, int currentRow, WindowFrameBound bound, bool forFollowing)
    {
        int size = rows.Count, last = size - 1, index;
        switch (bound.GetBoundType())
        {
            case WindowFrameBoundType.UnboundedPreceding: return -1;
            case WindowFrameBoundType.Preceding:
                switch (units)
                {
                    case WindowFrameUnits.Rows:
                        int value = GetIntOffset(bound, rows[currentRow], session);
                        return value > currentRow ? -1 : currentRow - value;
                    case WindowFrameUnits.Groups:
                        int groups = GetIntOffset(bound, rows[currentRow], session);
                        if (!forFollowing)
                        {
                            index = ToGroupStart(rows, sort!, currentRow, 0);
                            while (groups > 0 && index > 0) { groups--; index = ToGroupStart(rows, sort!, index - 1, 0); }
                            if (groups > 0) index = -1;
                        }
                        else if (groups == 0) index = ToGroupEnd(rows, sort!, currentRow, last);
                        else
                        {
                            index = currentRow;
                            while (groups > 0 && index >= 0) { groups--; index = ToGroupStart(rows, sort!, index, 0) - 1; }
                        }
                        return index;
                    case WindowFrameUnits.Range:
                        Value?[]? row = GetCompareRow(session, rows, sort!, currentRow, bound, false);
                        if (row is null) return -1;
                        index = rows.BinarySearch(row, Comparer<Value?[]>.Create(sort!.Compare));
                        if (index >= 0)
                        {
                            if (!forFollowing) while (index > 0 && sort!.Compare(row, rows[index - 1]) == 0) index--;
                            else while (index < last && sort!.Compare(row, rows[index + 1]) == 0) index++;
                        }
                        else { index = ~index; if (!forFollowing) { if (index == 0) index = -1; } else index--; }
                        return index;
                    default: throw DbException.GetUnsupportedException("units=" + units);
                }
            case WindowFrameBoundType.CurrentRow:
                return units == WindowFrameUnits.Rows ? currentRow : forFollowing
                    ? ToGroupEnd(rows, sort!, currentRow, last) : ToGroupStart(rows, sort!, currentRow, 0);
            case WindowFrameBoundType.Following:
                switch (units)
                {
                    case WindowFrameUnits.Rows:
                        int value = GetIntOffset(bound, rows[currentRow], session);
                        return value > last - currentRow ? size : currentRow + value;
                    case WindowFrameUnits.Groups:
                        int groups = GetIntOffset(bound, rows[currentRow], session);
                        if (forFollowing)
                        {
                            index = ToGroupEnd(rows, sort!, currentRow, last);
                            while (groups > 0 && index < last) { groups--; index = ToGroupEnd(rows, sort!, index + 1, last); }
                            if (groups > 0) index = size;
                        }
                        else if (groups == 0) index = ToGroupStart(rows, sort!, currentRow, 0);
                        else
                        {
                            index = currentRow;
                            while (groups > 0 && index <= last) { groups--; index = ToGroupEnd(rows, sort!, index, last) + 1; }
                        }
                        return index;
                    case WindowFrameUnits.Range:
                        Value?[]? row = GetCompareRow(session, rows, sort!, currentRow, bound, true);
                        if (row is null) return size;
                        index = rows.BinarySearch(row, Comparer<Value?[]>.Create(sort!.Compare));
                        if (index >= 0)
                        {
                            if (forFollowing) while (index < last && sort!.Compare(row, rows[index + 1]) == 0) index++;
                            else while (index > 0 && sort!.Compare(row, rows[index - 1]) == 0) index--;
                        }
                        else { index = ~index; if (forFollowing && index != size) index--; }
                        return index;
                    default: throw DbException.GetUnsupportedException("units=" + units);
                }
            case WindowFrameBoundType.UnboundedFollowing: return size;
            default: throw DbException.GetUnsupportedException("window frame bound type=" + bound.GetBoundType());
        }
    }
    private IEnumerable<Value?[]> ComplexIterator(List<Value?[]> rows, SortOrder? sort, int currentRow, int start, int end, bool reverse)
    {
        if (exclusion == WindowFrameExclusion.ExcludeCurrentRow)
        {
            if (currentRow < start || currentRow > end) { }
            else if (currentRow == start) start++;
            else if (currentRow == end) end--;
            else return BiIterator(rows, start, currentRow - 1, currentRow + 1, end, reverse);
        }
        else
        {
            int exStart = ToGroupStart(rows, sort!, currentRow, start), exEnd = ToGroupEnd(rows, sort!, currentRow, end);
            bool includeCurrent = exclusion == WindowFrameExclusion.ExcludeTies;
            if (includeCurrent)
            {
                if (currentRow == exStart) { exStart++; includeCurrent = false; }
                else if (currentRow == exEnd) { exEnd--; includeCurrent = false; }
            }
            if (exStart > exEnd || exEnd < start || exStart > end) { }
            else if (includeCurrent)
            {
                if (start == exStart)
                    return end == exEnd ? PlainIterator(rows, currentRow, currentRow, reverse)
                        : BiIterator(rows, currentRow, currentRow, exEnd + 1, end, reverse);
                return end == exEnd ? BiIterator(rows, start, exStart - 1, currentRow, currentRow, reverse)
                    : TriIterator(rows, start, exStart - 1, currentRow, currentRow, exEnd + 1, end, reverse);
            }
            else if (start >= exStart) start = exEnd + 1;
            else if (end <= exEnd) end = exStart - 1;
            else return BiIterator(rows, start, exStart - 1, exEnd + 1, end, reverse);
        }
        return PlainIterator(rows, start, end, reverse);
    }
    /// <summary>Renders the frame.</summary>
    public StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        builder.Append(units.GetSql());
        if (following is null) starting.GetSql(builder.Append(' '), false, sqlFlags);
        else
        {
            starting.GetSql(builder.Append(" BETWEEN "), false, sqlFlags).Append(" AND ");
            following.GetSql(builder, true, sqlFlags);
        }
        if (exclusion != WindowFrameExclusion.ExcludeNoOthers) builder.Append(' ').Append(exclusion.GetSql());
        return builder;
    }
}
