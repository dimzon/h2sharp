/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.engine.Comment.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A comment stored as an object of its own. Tables, columns, indexes and constraints keep
 * their comment in their own meta row; a schema, sequence, constant or domain has no such
 * row, so its comment is this object: a COMMENT ON statement in SYS.
 */
using System.Text;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Engine;

/// <summary>
/// Represents a database object comment.
/// </summary>
public sealed class Comment : DbObject
{
    private readonly int objectType;

    private readonly string quotedObjectName;

    private string? commentText;

    /// <summary>
    /// Creates the comment of an object.
    /// </summary>
    /// <param name="database">the database</param>
    /// <param name="id">the object id</param>
    /// <param name="obj">the object the comment belongs to</param>
    public Comment(Database database, int id, DbObject obj)
        : base(database, id, GetKey(obj), Trace.Database)
    {
        objectType = obj.GetTypeId();
        quotedObjectName = obj.GetSql(HasSql.DefaultSqlFlags);
    }

    /// <inheritdoc/>
    public override string GetCreateSql()
    {
        StringBuilder builder = new StringBuilder("COMMENT ON ");
        _ = builder.Append(GetTypeName(objectType)).Append(' ')
            .Append(quotedObjectName).Append(" IS ")
            .Append(StringUtils.QuoteStringSql(commentText));
        return builder.ToString();
    }

    /// <inheritdoc/>
    public override int GetTypeId() => DbObject.Comment;

    /// <inheritdoc/>
    public override void RemoveChildrenAndResources(SessionLocal session)
        => database!.RemoveMeta(session, GetId());

    /// <inheritdoc/>
    public override void CheckRename() => throw DbException.GetInternalError();

    /// <summary>
    /// The key that associates a comment with its object.
    /// </summary>
    /// <param name="obj">the object</param>
    /// <returns>the key</returns>
    internal static string GetKey(DbObject obj)
    {
        StringBuilder builder = new StringBuilder(GetTypeName(obj.GetTypeId())).Append(' ');
        _ = obj.GetSql(builder, HasSql.DefaultSqlFlags);
        return builder.ToString();
    }

    /// <summary>
    /// Sets the comment text.
    /// </summary>
    /// <param name="comment">the text, or null</param>
    public void SetCommentText(string? comment) => commentText = comment;

    /// <summary>The comment text.</summary>
    public string? GetCommentText() => commentText;

    // Internal so the type-name mapping itself can be covered directly: USER, ROLE and ALIAS
    // have no DbObject instances in the port yet, so no SQL path can reach their arms.
    internal static string GetTypeName(int type) => type switch
    {
        DbObject.Constant => "CONSTANT",
        DbObject.Constraint => "CONSTRAINT",
        DbObject.FunctionAlias => "ALIAS",
        DbObject.Index => "INDEX",
        DbObject.Role => "ROLE",
        DbObject.Schema => "SCHEMA",
        DbObject.Sequence => "SEQUENCE",
        DbObject.TableOrView => "TABLE",
        DbObject.Trigger => "TRIGGER",
        DbObject.User => "USER",
        DbObject.Domain => "DOMAIN",
        // Not supported by the parser, but required when trying to find a comment.
        _ => "type" + type,
    };
}
