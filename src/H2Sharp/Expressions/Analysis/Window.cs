/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.analysis.Window. */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Values;
namespace H2Sharp.Expressions.Analysis;

/// <summary>Window specification.</summary>
public sealed class Window
{
    private List<Expression>? partitionBy;
    private List<QueryOrderBy>? orderBy;
    private WindowFrame? frame;
    private string? parent;
    /// <summary>Creates a window specification.</summary>
    public Window(string? parent, List<Expression>? partitionBy, List<QueryOrderBy>? orderBy, WindowFrame? frame)
    { this.parent = parent; this.partitionBy = partitionBy; this.orderBy = orderBy; this.frame = frame; }
    /// <summary>Appends an ORDER BY clause.</summary>
    public static void AppendOrderBy(StringBuilder builder, List<QueryOrderBy>? orderBy, int sqlFlags, bool forceOrderBy)
    {
        if (orderBy is not null && orderBy.Count > 0)
        {
            AppendOrderByStart(builder);
            for (int i = 0; i < orderBy.Count; i++)
            {
                if (i > 0) builder.Append(", ");
                orderBy[i].expression!.GetUnenclosedSql(builder, sqlFlags);
                Result.SortOrder.TypeToString(builder, orderBy[i].sortType);
            }
        }
        else if (forceOrderBy) { AppendOrderByStart(builder); builder.Append("NULL"); }
    }
    private static void AppendOrderByStart(StringBuilder builder)
    { if (builder[builder.Length - 1] != '(') builder.Append(' '); builder.Append("ORDER BY "); }
    /// <summary>Maps the specification's expressions.</summary>
    public void MapColumns(ColumnResolver resolver, int level)
    {
        ResolveWindows(resolver);
        if (partitionBy is not null) foreach (Expression e in partitionBy) e.MapColumns(resolver, level, Expression.MapInWindow);
        if (orderBy is not null) foreach (QueryOrderBy o in orderBy) o.expression!.MapColumns(resolver, level, Expression.MapInWindow);
        frame?.MapColumns(resolver, level, Expression.MapInWindow);
    }
    private void ResolveWindows(ColumnResolver resolver)
    {
        if (parent is null) return;
        Select? select = resolver.GetSelect();
        Window? p;
        while ((p = select?.GetWindow(parent)) is null)
        {
            select = select?.GetParentSelect();
            if (select is null) throw DbException.Get(Api.ErrorCode.WindowNotFound1, parent);
        }
        p.ResolveWindows(resolver);
        partitionBy ??= p.partitionBy; orderBy ??= p.orderBy; frame ??= p.frame; parent = null;
    }
    /// <summary>Optimizes expressions and drops constant keys.</summary>
    public void Optimize(SessionLocal session)
    {
        if (partitionBy is not null)
        {
            for (int i = 0; i < partitionBy.Count; i++)
            {
                Expression e = partitionBy[i].Optimize(session);
                if (e.IsConstant()) partitionBy.RemoveAt(i--); else partitionBy[i] = e;
            }
            if (partitionBy.Count == 0) partitionBy = null;
        }
        if (orderBy is not null)
        {
            for (int i = 0; i < orderBy.Count; i++)
            {
                Expression e = orderBy[i].expression!.Optimize(session);
                if (e.IsConstant()) orderBy.RemoveAt(i--); else orderBy[i].expression = e;
            }
            if (orderBy.Count == 0) orderBy = null;
        }
        frame?.Optimize(session);
    }
    /// <summary>Marks expression availability.</summary>
    public void SetEvaluatable(TableFilter tableFilter, bool value)
    {
        if (partitionBy is not null) foreach (Expression e in partitionBy) e.SetEvaluatable(tableFilter, value);
        if (orderBy is not null) foreach (QueryOrderBy o in orderBy) o.expression!.SetEvaluatable(tableFilter, value);
    }
    /// <summary>Returns ordering keys.</summary>
    public List<QueryOrderBy>? GetOrderBy() => orderBy;
    /// <summary>Returns the frame.</summary>
    public WindowFrame? GetWindowFrame() => frame;
    /// <summary>Whether per-row ordered results are required.</summary>
    public bool IsOrdered()
    {
        if (orderBy is not null) return true;
        if (frame is not null && frame.GetUnits() == WindowFrameUnits.Rows)
        {
            if (frame.GetStarting().GetBoundType() == WindowFrameBoundType.UnboundedPreceding
                && frame.GetFollowing()?.GetBoundType() == WindowFrameBoundType.UnboundedFollowing) return false;
            return true;
        }
        return false;
    }
    /// <summary>Evaluates a partition key.</summary>
    public Value? GetCurrentKey(SessionLocal session)
    {
        if (partitionBy is null) return null;
        if (partitionBy.Count == 1) return partitionBy[0].GetValue(session);
        Value[] values = new Value[partitionBy.Count];
        for (int i = 0; i < values.Length; i++) values[i] = partitionBy[i].GetValue(session);
        return ValueRow.Get(values);
    }
    /// <summary>Renders the OVER clause.</summary>
    public StringBuilder GetSql(StringBuilder builder, int sqlFlags, bool forceOrderBy)
    {
        builder.Append("OVER (");
        if (partitionBy is not null)
        {
            builder.Append("PARTITION BY ");
            for (int i = 0; i < partitionBy.Count; i++)
            { if (i > 0) builder.Append(", "); partitionBy[i].GetUnenclosedSql(builder, sqlFlags); }
        }
        AppendOrderBy(builder, orderBy, sqlFlags, forceOrderBy);
        if (frame is not null)
        { if (builder[builder.Length - 1] != '(') builder.Append(' '); frame.GetSql(builder, sqlFlags); }
        return builder.Append(')');
    }
    /// <summary>Updates expressions during aggregation.</summary>
    public void UpdateAggregate(SessionLocal session, int stage)
    {
        if (partitionBy is not null) foreach (Expression e in partitionBy) e.UpdateAggregate(session, stage);
        if (orderBy is not null) foreach (QueryOrderBy o in orderBy) o.expression!.UpdateAggregate(session, stage);
        frame?.UpdateAggregate(session, stage);
    }
    /// <inheritdoc/>
    public override string ToString() => GetSql(new StringBuilder(), Util.HasSql.TraceSqlFlags, false).ToString();
}
