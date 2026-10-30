/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.TableView, H2 2.5.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Util;

namespace H2Sharp.Tables;

/// <summary>
/// A view: a virtual table defined by a query.
/// </summary>
public sealed class TableView : QueryExpressionTable
{
    private readonly int id;

    private string querySQL;

    private Column[]? columnTemplates;

    private H2SharpException? createException;

    /// <summary>
    /// Creates a view.
    /// </summary>
    /// <param name="schema">the schema</param>
    /// <param name="id">the object id</param>
    /// <param name="name">the view name</param>
    /// <param name="querySQL">the query text</param>
    /// <param name="columnTemplates">the declared column names, or null</param>
    /// <param name="session">the session</param>
    public TableView(Schema schema, int id, string name, string querySQL, Column[]? columnTemplates,
        SessionLocal session)
        : base(schema, name)
    {
        this.id = id;
        this.querySQL = querySQL;
        this.columnTemplates = columnTemplates;
        InitColumnsAndTables(session);
    }

    /// <inheritdoc/>
    public override int GetId() => id;

    /// <summary>Replaces the query of this view and recompiles it.</summary>
    /// <remarks>
    /// A replace that breaks a dependent view is refused as a whole: the old query is
    /// restored and the old views recompiled, as the reference does.
    /// </remarks>
    public void Replace(string querySql, Column[]? newColumnTemplates, SessionLocal session, bool force)
    {
        string oldQuerySql = querySQL;
        Column[]? oldTemplates = columnTemplates;
        querySQL = querySql;
        columnTemplates = newColumnTemplates;
        H2SharpException? e = Recompile(session, force, true);
        if (e is not null)
        {
            querySQL = oldQuerySql;
            columnTemplates = oldTemplates;
            Recompile(session, true, false);
            throw e;
        }
    }

    /// <inheritdoc/>
    public override int GetTableType() => VIEW;

    /// <inheritdoc/>
    public override bool IsView() => true;

    /// <inheritdoc/>
    public override long GetMaxDataModificationId()
        => createException is not null || viewQuery is null ? long.MaxValue : base.GetMaxDataModificationId();

    /// <inheritdoc/>
    /// <remarks>
    /// A view keeps no data of its own, but it is still a database object:
    /// bumping the modification meta id is what invalidates the session command
    /// cache after a CREATE OR REPLACE VIEW.
    /// </remarks>
    public override void SetModified()
        => GetSchema()!.GetDatabase().GetNextModificationMetaId();

    /// <inheritdoc/>
    public override void Rename(string newName) => name = newName;

    /// <inheritdoc/>
    public override Index? GetScanIndex(SessionLocal session)
    {
        if (createException is not null)
        {
            throw DbException.Get(ErrorCode.ViewIsInvalid2, createException, GetSql(HasSql.TraceSqlFlags),
                createException.Message);
        }

        object cacheKey = this;
        SmallLRUCache<object, QueryExpressionIndex> cache = session.GetViewIndexCache();
        if (!cache.TryGetValue(cacheKey, out QueryExpressionIndex? index) || index.IsExpired())
        {
            index = new RegularQueryExpressionIndex(this, querySQL, null, session, null);
            cache.Put(cacheKey, index);
        }

        return index;
    }

    /// <inheritdoc/>
    public override Index? GetIndexFor(SessionLocal session, Result.SearchRow row)
        => GetScanIndex(session);

    /// <inheritdoc/>
    /// <remarks>QueryExpressionTable.getIndexes is empty: the session cache owns the index.</remarks>
    public override Index[] GetIndexes() => [];

    /// <inheritdoc/>
    public override Query? GetTopQuery() => null;

    /// <inheritdoc/>
    public override string? GetCreateSql() => GetCreateSql(false, true);

    /// <inheritdoc/>
    public override string? GetCreateSqlForMeta() => GetCreateSql();

    /// <summary>Generates the CREATE SQL for this view.</summary>
    public string GetCreateSql(bool orReplace, bool force)
        => GetCreateSql(orReplace, force, GetSql(HasSql.DefaultSqlFlags));

    private string GetCreateSql(bool orReplace, bool force, string quotedName)
    {
        StringBuilder builder = new("CREATE ");
        if (orReplace)
        {
            _ = builder.Append("OR REPLACE ");
        }

        if (force)
        {
            _ = builder.Append("FORCE ");
        }

        _ = builder.Append("VIEW ").Append(quotedName);
        if (GetComment() is not null)
        {
            _ = builder.Append(" COMMENT ");
            _ = StringUtils.QuoteStringSql(builder, GetComment()!);
        }

        if (columns.Length > 0)
        {
            _ = builder.Append('(');
            _ = Column.WriteColumns(builder, columns, HasSql.DefaultSqlFlags);
            _ = builder.Append(')');
        }
        else if (columnTemplates is not null)
        {
            _ = builder.Append('(');
            _ = Column.WriteColumns(builder, columnTemplates, HasSql.DefaultSqlFlags);
            _ = builder.Append(')');
        }

        return builder.Append(" AS\n").Append(querySQL).ToString();
    }

    /// <summary>The query text of this view.</summary>
    public string GetQuerySql() => querySQL;

    /// <summary>Recompiles this view and its dependent views.</summary>
    /// <remarks>
    /// The first dependent view whose recompilation fails answers that failure, unless
    /// force is set, which keeps every view invalid in place.
    /// </remarks>
    public H2SharpException? Recompile(SessionLocal session, bool force, bool clearIndexCache)
    {
        try
        {
            _ = CompileViewQuery(session, querySQL);
        }
        catch (H2SharpException e)
        {
            if (!force)
            {
                return e;
            }
        }

        InitColumnsAndTables(session);
        foreach (TableView dependent in GetDependentViews())
        {
            H2SharpException? e = dependent.Recompile(session, force, false);
            if (e is not null && !force)
            {
                return e;
            }
        }

        if (clearIndexCache)
        {
            ClearIndexCaches(GetSchema()!.GetDatabase());
        }

        return force ? null : createException;
    }

    private void InitColumnsAndTables(SessionLocal session)
    {
        RemoveCurrentViewFromOtherTables();
        try
        {
            Query compiled = CompileViewQuery(session, querySQL);
            querySQL = compiled.GetPlanSql(HasSql.DefaultSqlFlags);
            tables = [.. compiled.GetTables()];
            columns = InitColumns(session, columnTemplates, compiled, false, false);
            createException = null;
            viewQuery = compiled;
        }
        catch (H2SharpException e)
        {
            if (e.H2ErrorCode == ErrorCode.ColumnAliasIsNotSpecified1)
            {
                throw;
            }

            createException = e;
            tables = [];
            columns = [];
        }

        if (id != 0)
        {
            AddDependentViewToTables();
        }
    }

    private void RemoveCurrentViewFromOtherTables()
    {
        foreach (Table table in tables)
        {
            table.RemoveDependentView(this);
        }

        tables.Clear();
    }

    private void AddDependentViewToTables()
    {
        foreach (Table table in tables)
        {
            table.AddDependentView(this);
        }
    }

    private static Query CompileViewQuery(SessionLocal session, string sql)
    {
        session.SetParsingCreateView(true);
        try
        {
            return Command.Parser.CompileViewQuery(session, sql);
        }
        finally
        {
            session.SetParsingCreateView(false);
        }
    }

    /// <inheritdoc/>
    public override void RemoveChildrenAndResources(SessionLocal session)
    {
        RemoveCurrentViewFromOtherTables();
        base.RemoveChildrenAndResources(session);
        CreateException = null;
        ClearIndexCaches(GetSchema()!.GetDatabase());
        querySQL = string.Empty;
    }

    private static void ClearIndexCaches(Database database)
    {
        foreach (SessionLocal session in database.GetSessions()) session.ClearViewIndexCache();
    }

    private H2SharpException? CreateException
    {
        get => createException;
        set => createException = value;
    }

}
