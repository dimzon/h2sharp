/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.CreateIndex.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * CREATE INDEX.
 *
 * One statement for four kinds of index, told apart by three flags rather than by four classes:
 * unique, hash and spatial. A caller that had to know which kind before reading the statement
 * would have to read it twice, and the three are not exclusive — a spatial hash index exists,
 * and it is neither "the unique one" nor "the other one".
 *
 * It is a DefineCommand and not an AlterTableAddConstraint, and that is not a detail:
 * CommandWithColumns casts to AlterTableAddConstraint after excluding CreateIndex, because a
 * CREATE INDEX inside CREATE TABLE is a different thing that happens to be read at the same
 * moment.
 *
 * The body resolves the table and the optional index name before touching anything, so a
 * missing table or a taken name fails before an index exists. Users, rights and table locks
 * are not ported.
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// A CREATE INDEX statement.
/// </summary>
public class CreateIndex : SchemaCommand
{
    private string tableName = string.Empty;

    private string? indexName;

    private IndexColumn[] columns = [];

    private bool ifTableExists;

    private bool ifNotExists;

    private bool primaryKey;

    private bool hash;

    private bool spatial;

    private string? comment;

    private NullsDistinct? nullsDistinct;

    private int uniqueColumnCount;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="schema">the schema</param>
    public CreateIndex(SessionLocal session, Schema schema)
        : base(session, schema)
    {
    }

    /// <summary>Sets whether the statement is IF EXISTS.</summary>
    /// <param name="b">true for IF EXISTS</param>
    public void SetIfTableExists(bool b) => ifTableExists = b;

    /// <summary>Sets whether the statement is IF NOT EXISTS.</summary>
    /// <param name="ifNotExists">true for IF NOT EXISTS</param>
    public void SetIfNotExists(bool ifNotExists) => this.ifNotExists = ifNotExists;

    /// <summary>Sets the name of the table.</summary>
    /// <param name="tableName">the name</param>
    public void SetTableName(string tableName) => this.tableName = tableName;

    /// <summary>Sets the name of the index.</summary>
    /// <param name="indexName">the name</param>
    public void SetIndexName(string indexName) => this.indexName = indexName;

    /// <summary>Sets the columns of the index.</summary>
    /// <param name="columns">the columns</param>
    public void SetIndexColumns(IndexColumn[] columns) => this.columns = columns;

    /// <inheritdoc/>
    public override long Update()
    {
        Database db = GetSession().GetDatabase();
        bool persistent = db.IsPersistent();
        Table? table = GetSchema().FindTableOrView(GetSession(), tableName);
        if (table is null)
        {
            if (ifTableExists)
            {
                return 0;
            }

            throw DbException.Get(Api.ErrorCode.TableOrViewNotFound1, tableName);
        }

        if (indexName is not null && GetSchema().FindIndex(GetSession(), indexName) is not null)
        {
            if (ifNotExists)
            {
                return 0;
            }

            throw DbException.Get(Api.ErrorCode.IndexAlreadyExists1, indexName);
        }

        // The reference checks the table right of the session's user and takes an exclusive
        // table lock here; users, rights and the lock manager are not ported.
        if (!table.IsPersistIndexes())
        {
            persistent = false;
        }

        int id = GetObjectId();
        if (indexName is null)
        {
            indexName = primaryKey
                ? table.GetSchema()!.GetUniqueIndexName(GetSession(), table, Constants.PrefixPrimaryKey)
                : table.GetSchema()!.GetUniqueIndexName(GetSession(), table, Constants.PrefixIndex);
        }

        IndexType indexType;
        if (primaryKey)
        {
            if (table.FindPrimaryKey() is not null)
            {
                throw DbException.Get(Api.ErrorCode.SecondPrimaryKey);
            }

            indexType = IndexType.CreatePrimaryKey(persistent, hash);
        }
        else if (uniqueColumnCount > 0)
        {
            // The reference passes null through and lets the index type decide the effective
            // rule; this port's factory takes a non-null value. The parser always supplies one
            // for a unique index, so the fallback is only for a caller that did not.
            indexType = IndexType.CreateUnique(persistent, hash, uniqueColumnCount,
                nullsDistinct ?? NullsDistinct.Distinct);
        }
        else
        {
            indexType = IndexType.CreateNonUnique(persistent, hash, spatial);
        }

        IndexColumn[] mapped = IndexColumn.MapColumns(columns, table);
        _ = table.AddIndex(GetSession(), indexName, id, mapped, uniqueColumnCount, indexType,
            create: true, comment);
        return 0;
    }

    /// <summary>Sets whether this is a primary key index.</summary>
    /// <param name="b">true for a primary key index</param>
    public void SetPrimaryKey(bool b) => primaryKey = b;

    /// <summary>Sets whether this is a unique index.</summary>
    /// <param name="nullsDistinct">how NULLs compare</param>
    /// <param name="uniqueColumnCount">how many columns are unique</param>
    public void SetUnique(NullsDistinct? nullsDistinct, int uniqueColumnCount)
    {
        this.nullsDistinct = nullsDistinct;
        this.uniqueColumnCount = uniqueColumnCount;
    }

    /// <summary>Sets whether this is a hash index.</summary>
    /// <param name="b">true for a hash index</param>
    public void SetHash(bool b) => hash = b;

    /// <summary>Sets whether this is a spatial index.</summary>
    /// <param name="b">true for a spatial index</param>
    public void SetSpatial(bool b) => spatial = b;

    /// <summary>Sets the comment on the index.</summary>
    /// <param name="comment">the comment</param>
    public void SetComment(string comment) => this.comment = comment;

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.CreateIndex;
}
