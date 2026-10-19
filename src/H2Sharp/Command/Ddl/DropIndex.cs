/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.DropIndex.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * DROP INDEX.
 *
 * The index may be owned by a constraint that is not a primary key, and then it is the
 * constraint that is an error: dropping the index would leave the constraint naming one that
 * is gone. A primary key index is the exception the reference keeps for compatibility — it
 * drops the constraint with it, unless a foreign key points at that constraint, which is the
 * same rule ALTER TABLE DROP CONSTRAINT applies.
 */
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// A DROP INDEX statement.
/// </summary>
public sealed class DropIndex : SchemaCommand
{
    private string indexName = string.Empty;

    private bool ifExists;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="schema">the schema</param>
    public DropIndex(SessionLocal session, Schema schema)
        : base(session, schema)
    {
    }

    /// <summary>Sets whether the statement is IF EXISTS.</summary>
    /// <param name="b">true for IF EXISTS</param>
    public void SetIfExists(bool b) => ifExists = b;

    /// <summary>Sets the name of the index.</summary>
    /// <param name="indexName">the name</param>
    public void SetIndexName(string indexName) => this.indexName = indexName;

    /// <inheritdoc/>
    public override long Update()
    {
        Database db = GetSession().GetDatabase();
        Index? index = GetSchema().FindIndex(GetSession(), indexName);
        if (index is null)
        {
            if (!ifExists)
            {
                throw Message.DbException.Get(Api.ErrorCode.IndexNotFound1, indexName);
            }
        }
        else
        {
            // The reference checks the table right of the session's user here; users and
            // rights are not ported.
            Table table = index.GetTable()!;
            Constraint? pkConstraint = null;
            foreach (Constraint cons in table.GetConstraints())
            {
                if (!cons.UsesIndex(index))
                {
                    continue;
                }

                // can drop primary key index (for compatibility)
                if (cons.GetConstraintType() == Constraint.Type.PRIMARY_KEY)
                {
                    foreach (Constraint c in table.GetConstraints())
                    {
                        if (ReferenceEquals(c.GetReferencedConstraint(), cons))
                        {
                            throw Message.DbException.Get(Api.ErrorCode.IndexBelongsToConstraint2,
                                indexName, cons.GetName());
                        }
                    }

                    pkConstraint = cons;
                }
                else
                {
                    throw Message.DbException.Get(Api.ErrorCode.IndexBelongsToConstraint2,
                        indexName, cons.GetName());
                }
            }

            table.SetModified();
            if (pkConstraint is not null)
            {
                db.RemoveSchemaObject(GetSession(), pkConstraint);
            }
            else
            {
                db.RemoveSchemaObject(GetSession(), index);
            }
        }

        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.DropIndex;
}
