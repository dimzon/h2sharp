/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.engine.DbObject.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A database object such as a table, an index, or a user.
 *
 * Two things here are the whole contract of "a thing in a database", and both are easy to get
 * wrong.
 *
 * The object id is the identity, not the name. A table can be renamed and still be the same
 * table, and two schemas can hold objects with the same name; equals and hashCode are on the id
 * for that reason. The id is also what invalidate() sets to -1, which is why isValid() asks
 * whether the id is still there rather than keeping a flag: there is one field to write instead
 * of two that could disagree.
 *
 * The modification id is the other half. Every object carries the id of the database state it was
 * last changed in, and a schema compare asks "is this object's modification id newer than the one
 * I loaded" — that is how two connections see that a table changed under them. setModified()
 * takes the *next* id from the database, and an object with no database (a detached copy, or one
 * being built before it is added) gets -1 rather than a number that would compare as real.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;

namespace H2Sharp.Engine;

/// <summary>
/// A database object such as a table, an index, or a user.
/// </summary>
public abstract class DbObject
{
    /// <summary>The object is of the type table or view.</summary>
    public const int TableOrView = 0;

    /// <summary>This object is an index.</summary>
    public const int Index = 1;

    /// <summary>This object is a user.</summary>
    public const int User = 2;

    /// <summary>This object is a sequence.</summary>
    public const int Sequence = 3;

    /// <summary>This object is a trigger.</summary>
    public const int Trigger = 4;

    /// <summary>This object is a constraint.</summary>
    public const int Constraint = 5;

    /// <summary>This object is a setting.</summary>
    public const int Setting = 6;

    /// <summary>This object is a role.</summary>
    public const int Role = 7;

    /// <summary>This object is a right.</summary>
    public const int Right = 8;

    /// <summary>This object is an alias for a Java function.</summary>
    public const int FunctionAlias = 9;

    /// <summary>This object is a schema.</summary>
    public const int Schema = 10;

    /// <summary>This object is a constant.</summary>
    public const int Constant = 11;

    /// <summary>This object is a domain.</summary>
    public const int Domain = 12;

    /// <summary>This object is a comment.</summary>
    public const int Comment = 13;

    /// <summary>This object is a user-defined aggregate function.</summary>
    public const int Aggregate = 14;

    /// <summary>This object is a synonym.</summary>
    public const int Synonym = 15;

    /// <summary>
    /// The database.
    /// </summary>
    protected Database? database;

    /// <summary>
    /// The trace module.
    /// </summary>
    protected Trace? trace;

    /// <summary>
    /// The comment (if set).
    /// </summary>
    protected string? comment;

    private int id;

    private string? objectName;

    private long modificationId;

    private bool temporary;

    /// <summary>
    /// Initialize some attributes of this object.
    /// </summary>
    /// <param name="db">the database</param>
    /// <param name="objectId">the object id</param>
    /// <param name="name">the name</param>
    /// <param name="traceModule">the trace module</param>
    protected DbObject(Database? db, int objectId, string name, int traceModuleId)
    {
        database = db;
        TraceModuleId = traceModuleId;
        id = objectId;
        objectName = name;
        modificationId = db is null ? -1 : db.GetModificationMetaId();
    }

    /// <summary>
    /// The trace module this object belongs to, by module id. The trace itself stays null: it
    /// needs a TraceWriter, and none is ported (TraceSystem is not ported either).
    /// Database.GetTrace is where one is asked for and it refuses, so nothing here pretends to
    /// have a trace that writes nowhere.
    /// </summary>
    protected int TraceModuleId { get; }

    /// <summary>
    /// Tell the object that it was modified.
    /// </summary>
    public void SetModified()
    {
        modificationId = database is null ? -1 : database.GetNextModificationMetaId();
    }

    /// <summary>
    /// Gets the id of the database state this object was last changed in.
    /// </summary>
    public long GetModificationId()
    {
        return modificationId;
    }

    /// <summary>
    /// Sets the name of this object.
    /// </summary>
    /// <param name="name">the name</param>
    protected void SetObjectName(string name)
    {
        objectName = name;
    }

    /// <summary>
    /// Gets the SQL snippet for this object.
    /// </summary>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the SQL</returns>
    public virtual string GetSql(int sqlFlags)
    {
        return GetSql(new StringBuilder(), sqlFlags).ToString();
    }

    /// <summary>
    /// Appends the SQL snippet for this object.
    /// </summary>
    /// <param name="builder">the builder</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the builder</returns>
    public virtual StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        return ParserUtil.QuoteIdentifier(builder, objectName ?? string.Empty, sqlFlags);
    }

    /// <summary>
    /// Get the list of dependent children (for tables, this includes indexes and so on).
    /// </summary>
    /// <returns>the list of children, or null</returns>
    public virtual List<DbObject>? GetChildren()
    {
        return null;
    }

    /// <summary>
    /// Get the database.
    /// </summary>
    /// <returns>the database</returns>
    public Database? GetDatabase()
    {
        return database;
    }

    /// <summary>
    /// Get the unique object id.
    /// </summary>
    /// <returns>the object id</returns>
    public int GetId()
    {
        return id;
    }

    /// <summary>
    /// Get the name.
    /// </summary>
    /// <returns>the name</returns>
    public string? GetName()
    {
        return objectName;
    }

    /// <summary>
    /// Set the main attributes to null to make sure the object is no longer
    /// used.
    /// </summary>
    protected virtual void Invalidate()
    {
        if (id == -1)
        {
            throw DbException.GetInternalError();
        }

        SetModified();
        id = -1;
        database = null;
        objectName = null;
    }

    /// <summary>
    /// Whether this object is still in use.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsValid()
    {
        return id != -1;
    }

    /// <summary>
    /// Construct the CREATE ... SQL statement for this object for meta table.
    /// </summary>
    /// <returns>the SQL statement</returns>
    public virtual string? GetCreateSqlForMeta()
    {
        return GetCreateSql();
    }

    /// <summary>
    /// Construct the CREATE ... SQL statement for this object.
    /// </summary>
    /// <remarks>
    /// Null when no statement creates it: the table's scan index answers null, and the
    /// planner skips what has no statement.
    /// </remarks>
    /// <returns>the SQL statement, or null</returns>
    public abstract string? GetCreateSql();

    /// <summary>Gets the SQL to copy this child to another table.</summary>
    public virtual string? GetCreateSqlForCopy(Tables.Table table, string quotedName)
        => throw new TODOException("DbObject.getCreateSQLForCopy: this object kind is not ported");

    /// <summary>
    /// Construct a DROP ... SQL statement for this object.
    /// </summary>
    /// <returns>the SQL statement</returns>
    public virtual string? GetDropSql()
    {
        return null;
    }

    /// <summary>
    /// Get the object type. Named GetTypeId for the reason Expression.GetTypeInfo was: object.GetType()
    /// already exists and hiding it is a trap.
    /// </summary>
    /// <returns>the object type</returns>
    public abstract int GetTypeId();

    /// <summary>
    /// Delete all dependent children objects and resources of this object.
    /// </summary>
    /// <param name="session">the session</param>
    public abstract void RemoveChildrenAndResources(SessionLocal session);

    /// <summary>
    /// Check if renaming is allowed. Does nothing when allowed.
    /// </summary>
    public virtual void CheckRename()
    {
        // Allowed by default
    }

    /// <summary>
    /// Rename the object.
    /// </summary>
    /// <param name="newName">the new name</param>
    public void Rename(string newName)
    {
        CheckRename();
        objectName = newName;
        SetModified();
    }

    /// <summary>
    /// Check if this object is temporary (for example, a temporary table).
    /// </summary>
    /// <returns>true if is temporary</returns>
    public bool IsTemporary()
    {
        return temporary;
    }

    /// <summary>
    /// Tell this object that it is temporary or not.
    /// </summary>
    /// <param name="temporary">the new value</param>
    public void SetTemporary(bool temporary)
    {
        this.temporary = temporary;
    }

    /// <summary>
    /// Change the comment of this object.
    /// </summary>
    /// <param name="comment">the new comment, or null for no comment</param>
    public void SetComment(string? comment)
    {
        this.comment = !string.IsNullOrEmpty(comment) ? comment : null;
    }

    /// <summary>
    /// Get the current comment of this object.
    /// </summary>
    /// <returns>the comment, or null if not set</returns>
    public string? GetComment()
    {
        return comment;
    }

    /// <summary>
    /// Whether this object belongs to the given schema.
    /// </summary>
    /// <param name="other">the other object</param>
    /// <returns>true if it does</returns>
    public virtual bool DependsOn(DbObject other)
        => throw new TODOException("DbObject.dependsOn");

    /// <inheritdoc/>
    public override bool Equals(object? o)
    {
        return o is DbObject dbObject && id == dbObject.id;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return id;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return (objectName ?? "?") + ":" + id + ":" + base.ToString();
    }
}
