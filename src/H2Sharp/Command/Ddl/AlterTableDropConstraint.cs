/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.AlterTableDropConstraint.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * ALTER TABLE ... DROP CONSTRAINT.
 *
 * The statement removes a constraint by name, but the name alone is not enough to find it:
 * the constraint must also belong to the table the statement names. A name that a different
 * table owns is not found here — the reference answers CONSTRAINT_NOT_FOUND for it, exactly
 * as it does for a name that no constraint has — so the lookup is the schema's, and the table
 * is checked on the result.
 *
 * Dropping a unique constraint that a referential one points at is either refused or cascades,
 * depending on the drop action: RESTRICT is the default (the reference reads DROP_RESTRICT,
 * which defaults to true), and CASCADE is written after the name. The port has no referential
 * constraints yet, so the loop that enforces this finds nothing; it is here because the check
 * belongs to the statement, not to the constraints that happen to exist.
 *
 * The constraint is removed through the database, not the table: that is what frees its name
 * in the schema, and it takes the index the constraint owns with it, so a table whose primary
 * key was dropped can take the same names again.
 */
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// An ALTER TABLE DROP CONSTRAINT statement.
/// </summary>
public class AlterTableDropConstraint : AlterTable
{
    private string? constraintName;

    private readonly bool ifExists;

    private ConstraintActionType dropAction;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="schema">the schema</param>
    /// <param name="ifExists">whether the statement is IF EXISTS</param>
    public AlterTableDropConstraint(SessionLocal session, Schema schema, bool ifExists)
        : base(session, schema)
    {
        this.ifExists = ifExists;

        // The reference reads the database setting DROP_RESTRICT here and takes RESTRICT when
        // it is on, which is the default; the settings layer is not ported, so the default is
        // the constant.
        dropAction = ConstraintActionType.RESTRICT;
    }

    /// <summary>
    /// Sets the name of the constraint.
    /// </summary>
    /// <param name="constraintName">the name</param>
    public void SetConstraintName(string constraintName)
    {
        this.constraintName = constraintName;
    }

    /// <summary>
    /// Sets what happens to constraints that use the dropped one.
    /// </summary>
    /// <param name="dropAction">the action</param>
    public void SetDropAction(ConstraintActionType dropAction)
    {
        this.dropAction = dropAction;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A domain constraint is never found here, even when the name matches: domains are not
    /// tables, and this statement changes a table. The user's rights on the referenced table
    /// and on tables that use the constraint wait for the rights layer; refusing here would
    /// refuse every ALTER TABLE on a database that has no users yet.
    /// </remarks>
    public override long Update(Table table)
    {
        Constraint? constraint = GetSchema().FindConstraint(GetSession(), constraintName!);
        if (constraint is null || constraint.GetConstraintType() == Constraint.Type.DOMAIN
            || !ReferenceEquals(constraint.GetTable(), table))
        {
            if (!ifExists)
            {
                throw DbException.Get(Api.ErrorCode.ConstraintNotFound1, constraintName);
            }
        }
        else
        {
            if (ConstraintTypeRules.IsUnique(constraint.GetConstraintType()))
            {
                foreach (Constraint c in constraint.GetTable().GetConstraints())
                {
                    if (ReferenceEquals(c.GetReferencedConstraint(), constraint))
                    {
                        if (dropAction == ConstraintActionType.RESTRICT)
                        {
                            throw DbException.Get(Api.ErrorCode.ConstraintIsUsedByConstraint2,
                                GetTraceSql(constraint), GetTraceSql(c));
                        }
                    }
                }
            }

            GetSession().GetDatabase().RemoveSchemaObject(GetSession(), constraint);
        }

        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.AlterTableDropConstraint;

    private static string GetTraceSql(DbObject obj)
        => obj.GetSql(new System.Text.StringBuilder(), HasSql.TraceSqlFlags).ToString();
}
