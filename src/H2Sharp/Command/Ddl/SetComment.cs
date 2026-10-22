/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.SetComment.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * COMMENT ON: a comment on a table, a column, an index, a schema, a sequence,
 * or a constraint.
 *
 * A comment lives on the object itself: DbObject for everything but the column, which lives
 * on the column. The catalog views read it from there, which is also what the reference does
 * once its separate comment row is written — information_schema asks the object, not the
 * database. The port has no separate comment row and no meta table to write it into (the
 * catalog reads the live objects), so a comment is set on the object and nothing else happens:
 * there is nowhere else for it to go.
 */
using System;
using System.Text;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
using Index = H2Sharp.Indexes.Index;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// A COMMENT ON statement.
/// </summary>
public sealed class SetComment : DefineCommand
{
    private string? schemaName;

    private string objectName = string.Empty;

    private bool column;

    private string? columnName;

    private int objectType;

    private Expressions.Expression? comment;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    public SetComment(SessionLocal session)
        : base(session)
    {
    }

    /// <inheritdoc/>
    public override long Update()
    {
        SessionLocal session = GetSession();
        Engine.Database database = session.GetDatabase();
        Schemas.Schema schema = schemaName is null
            ? session.GetCurrentSchema()
            : database.GetSchema(schemaName);
        switch (objectType)
        {
            case DbObject.TableOrView:
                {
                    Table table = schema.GetTableOrView(session, objectName);
                    if (column)
                    {
                        // A comment on a column of a view has nowhere to go: the reference refuses
                        // it in checkSupportAlter, and views are not named objects in the port, so
                        // any table this resolves to is a real one.
                        Column c = table.GetColumn(columnName!);
                        c.SetComment(GetCommentText());
                        break;
                    }

                    table.SetComment(GetCommentText());
                    break;
                }

            case DbObject.Index:
                {
                    Index? index = schema.FindIndex(session, objectName);
                    if (index is null)
                    {
                        throw DbException.Get(Api.ErrorCode.IndexNotFound1, objectName);
                    }

                    index.SetComment(GetCommentText());
                    break;
                }

            case DbObject.Schema:
                database.GetSchema(objectName).SetComment(GetCommentText());
                break;
            case DbObject.Sequence:
                schema.GetSequence(objectName).SetComment(GetCommentText());
                break;
            case DbObject.Constraint:
                {
                    Constraints.Constraint? constraint = schema.FindConstraint(session, objectName);
                    if (constraint is null)
                    {
                        throw DbException.Get(Api.ErrorCode.ConstraintNotFound1, objectName);
                    }

                    constraint.SetComment(GetCommentText());
                    break;
                }
            case DbObject.Constant:
                // The port has no constants: every name is missing, and the reference
                // answers a missing constant this way.
                throw DbException.Get(Api.ErrorCode.ConstantNotFound1, objectName);
            case DbObject.FunctionAlias:
                throw DbException.Get(Api.ErrorCode.FunctionAliasNotFound1, objectName);
            case DbObject.User:
                throw DbException.Get(Api.ErrorCode.UserNotFound1, objectName);
            case DbObject.Role:
                throw DbException.Get(Api.ErrorCode.RoleNotFound1, objectName);
            case DbObject.Trigger:
                throw DbException.Get(Api.ErrorCode.TriggerNotFound1, objectName);
            case DbObject.Domain:
                throw DbException.Get(Api.ErrorCode.DomainNotFound1, objectName);
            default:
                throw DbException.Get(Api.ErrorCode.GeneralError1, objectName);
        }

        return 0;
    }

    /// <summary>
    /// Runs the comment expression and answers its text, or null when it says nothing.
    /// </summary>
    /// <remarks>
    /// An empty string means no comment, as in the reference: SetComment stores null rather
    /// than an empty string, and both read back as nothing.
    /// </remarks>
    /// <returns>the comment text, or null</returns>
    private string? GetCommentText()
    {
        string? text = comment?.Optimize(GetSession()).GetValue(GetSession()).GetString();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>
    /// Sets the comment expression.
    /// </summary>
    /// <param name="expr">the expression</param>
    public void SetCommentExpression(Expressions.Expression expr) => comment = expr;

    /// <summary>
    /// Sets the name of the object.
    /// </summary>
    /// <param name="name">the object name</param>
    public void SetObjectName(string name) => objectName = name;

    /// <summary>
    /// Sets the type of the object, one of the DbObject constants.
    /// </summary>
    /// <param name="type">the type</param>
    public void SetObjectType(int type) => objectType = type;

    /// <summary>
    /// Sets the name of the column.
    /// </summary>
    /// <param name="name">the column name</param>
    public void SetColumnName(string name) => columnName = name;

    /// <summary>
    /// Sets the name of the schema.
    /// </summary>
    /// <param name="name">the schema name, or null for the session's schema</param>
    public void SetSchemaName(string? name) => schemaName = name;

    /// <summary>
    /// Whether the comment belongs to a column of the object.
    /// </summary>
    /// <param name="isColumn">true for a column comment</param>
    public void SetColumn(bool isColumn) => column = isColumn;

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.Comment;
}
