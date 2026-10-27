/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.CreateSynonym.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>The statement CREATE SYNONYM.</summary>
public sealed class CreateSynonym : SchemaCommand
{
    private readonly CreateSynonymData data = new();

    private bool ifNotExists;

    private bool orReplace;

    private string? comment;

    /// <summary>Creates the statement.</summary>
    public CreateSynonym(SessionLocal session, Schema schema) : base(session, schema)
    {
    }

    /// <summary>Sets the synonym name.</summary>
    public void SetName(string name) => data.SynonymName = name;

    /// <summary>Sets the backing table name.</summary>
    public void SetSynonymFor(string tableName) => data.SynonymFor = tableName;

    /// <summary>Sets the backing table schema.</summary>
    public void SetSynonymForSchema(Schema schema) => data.SynonymForSchema = schema;

    /// <summary>Sets whether the statement is IF NOT EXISTS.</summary>
    public void SetIfNotExists(bool value) => ifNotExists = value;

    /// <summary>Sets whether the statement is OR REPLACE.</summary>
    public void SetOrReplace(bool value) => orReplace = value;

    /// <summary>Sets the comment.</summary>
    public void SetComment(string? comment) => this.comment = comment;

    /// <inheritdoc/>
    public override long Update()
    {
        Database db = GetSession().GetDatabase();
        data.Session = GetSession();
        db.LockMeta(GetSession());

        Schema schema = GetSchema();
        if (schema.FindTableOrView(GetSession(), data.SynonymName) is not null)
        {
            throw DbException.Get(ErrorCode.TableOrViewAlreadyExists1, data.SynonymName);
        }

        if (data.SynonymForSchema.FindTableOrView(GetSession(), data.SynonymFor) is null)
        {
            throw DbException.Get(ErrorCode.TableOrViewNotFound1,
                data.SynonymForSchema.GetName() + "." + data.SynonymFor);
        }

        TableSynonym? old = schema.FindSynonym(data.SynonymName);
        if (old is not null)
        {
            if (orReplace)
            {
                // Replace the existing synonym.
            }
            else if (ifNotExists)
            {
                return 0;
            }
            else
            {
                throw DbException.Get(ErrorCode.TableOrViewAlreadyExists1, data.SynonymName);
            }
        }

        TableSynonym synonym;
        if (old is not null)
        {
            synonym = old;
            data.Schema = synonym.GetSchema();
            synonym.UpdateData(data);
            synonym.SetComment(comment);
            synonym.SetModified();
            db.UpdateMeta(GetSession(), synonym);
        }
        else
        {
            data.Id = GetObjectId();
            synonym = schema.CreateSynonym(data);
            synonym.SetComment(comment);
            db.AddSchemaObject(GetSession(), synonym);
        }

        synonym.UpdateSynonymFor();
        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.CreateSynonym;
}
