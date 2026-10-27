/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.TableSynonym.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;
using H2Sharp.Command.Ddl;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Util;

namespace H2Sharp.Tables;

/// <summary>
/// Synonym for an existing table or view. All DML requests are forwarded to the backing table.
/// Adding indices to a synonym or altering the table is not supported.
/// </summary>
public sealed class TableSynonym : SchemaObject
{
    private CreateSynonymData data;

    private Table? synonymFor;

    /// <summary>Creates the synonym.</summary>
    public TableSynonym(CreateSynonymData data)
        : base(data.Schema, data.Id, data.SynonymName, Trace.Table)
    {
        this.data = data;
    }

    /// <summary>Gets the table this is a synonym for.</summary>
    public Table GetSynonymFor()
        => synonymFor ?? throw DbException.GetInternalError("synonym is not resolved");

    /// <summary>Updates the data.</summary>
    public void UpdateData(CreateSynonymData data) => this.data = data;

    /// <inheritdoc/>
    public override int GetTypeId() => DbObject.Synonym;

    /// <inheritdoc/>
    public override string? GetCreateSql()
    {
        StringBuilder builder = new("CREATE SYNONYM ");
        _ = GetSql(builder, HasSql.DefaultSqlFlags).Append(" FOR ");
        _ = ParserUtil.QuoteIdentifier(builder, data.SynonymForSchema.GetName(), HasSql.DefaultSqlFlags)
            .Append('.');
        _ = ParserUtil.QuoteIdentifier(builder, data.SynonymFor, HasSql.DefaultSqlFlags);
        return builder.ToString();
    }

    /// <inheritdoc/>
    public override string? GetDropSql()
        => GetSql(new StringBuilder("DROP SYNONYM "), HasSql.DefaultSqlFlags).ToString();

    /// <inheritdoc/>
    public override void CheckRename() => throw DbException.GetUnsupportedException("SYNONYM");

    /// <inheritdoc/>
    public override void RemoveChildrenAndResources(SessionLocal session)
    {
        synonymFor?.RemoveSynonym(this);
        GetSchema().GetDatabase().RemoveMeta(session, GetId());
    }

    /// <summary>Gets the name of the backing table.</summary>
    public string GetSynonymForName() => data.SynonymFor;

    /// <summary>Gets the schema of the backing table.</summary>
    public Schema GetSynonymForSchema() => data.SynonymForSchema;

    /// <summary>Whether the backing table is valid.</summary>
    public bool IsInvalid()
        => throw new TODOException("TableSynonym.isInvalid: table validity is not ported");

    /// <summary>Resolves the backing table and registers this synonym on it.</summary>
    public void UpdateSynonymFor()
    {
        synonymFor?.RemoveSynonym(this);
        synonymFor = data.SynonymForSchema.GetTableOrView(data.Session, data.SynonymFor);
        synonymFor.AddSynonym(this);
    }
}
