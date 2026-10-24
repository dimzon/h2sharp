/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.engine.MetaRecord.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * One row of the system table: the id, the object type, and the SQL that creates the object
 * again. Startup reads every row, groups them by type, and replays the SQL; the id travels with
 * the statement so the object comes back under the same id, and therefore finds its data maps.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Api;
using H2Sharp.Command;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Values;

namespace H2Sharp.Engine;

/// <summary>
/// A record in the system table of the database.
/// It contains the SQL statement to create the database object.
/// </summary>
public sealed class MetaRecord : IComparable<MetaRecord>
{
    /// <summary>
    /// Comparator for prepared constraints, sorts unique and primary key constraints first.
    /// </summary>
    internal static readonly IComparer<Prepared> ConstraintsComparator = Comparer<Prepared>.Create(
        static (o1, o2) =>
        {
            bool u1 = IsPrimaryOrUniqueConstraint(o1);
            bool u2 = IsPrimaryOrUniqueConstraint(o2);
            if (u1 == u2)
            {
                return o1.GetPersistedObjectId().CompareTo(o2.GetPersistedObjectId());
            }

            return u1 ? -1 : 1;
        });

    private readonly int id;

    private readonly int objectType;

    private readonly string sql;

    /// <summary>
    /// Copy metadata from the specified object into the specified search row.
    /// </summary>
    /// <param name="obj">database object</param>
    /// <param name="r">search row</param>
    public static void PopulateRowFromDbObject(DbObject obj, SearchRow r)
    {
        r.SetValue(0, ValueInteger.Get(obj.GetId()));
        r.SetValue(1, ValueInteger.Get(0));
        r.SetValue(2, ValueInteger.Get(obj.GetTypeId()));
        r.SetValue(3, ValueVarchar.Get(obj.GetCreateSqlForMeta()!));
    }

    /// <summary>
    /// Copy metadata from a table into the specified search row.
    /// </summary>
    /// <remarks>
    /// The reference's table is a <c>DbObject</c> and goes through
    /// <see cref="PopulateRowFromDbObject"/>. In the port it is not one, so it gets its own
    /// entry point with the same four columns and type <c>TABLE_OR_VIEW</c>.
    /// </remarks>
    /// <param name="table">the table</param>
    /// <param name="r">search row</param>
    public static void PopulateRowFromTable(Tables.Table table, SearchRow r)
    {
        r.SetValue(0, ValueInteger.Get(table.GetId()));
        r.SetValue(1, ValueInteger.Get(0));
        r.SetValue(2, ValueInteger.Get(DbObject.TableOrView));
        r.SetValue(3, ValueVarchar.Get(table.GetCreateSql()!));
    }

    /// <summary>
    /// Reads a record out of its row.
    /// </summary>
    /// <param name="r">the row</param>
    public MetaRecord(SearchRow r)
    {
        id = r.GetValue(0)!.GetInt();
        objectType = r.GetValue(2)!.GetInt();
        sql = r.GetValue(3)!.GetString()!;
    }

    /// <summary>
    /// Execute the meta data statement.
    /// </summary>
    /// <param name="db">the database</param>
    /// <param name="systemSession">the system session</param>
    /// <param name="listener">the database event listener, or null</param>
    /// <param name="throwIt">whether to throw instead of reporting</param>
    internal void PrepareAndExecute(
        Database db, SessionLocal systemSession, DatabaseEventListener? listener, bool throwIt)
    {
        Prepared? command = Prepare(db, systemSession, listener, throwIt);
        if (command is not null)
        {
            Execute(db, command, listener, sql, throwIt);
        }
    }

    /// <summary>
    /// Prepares the metadata statement.
    /// </summary>
    /// <param name="db">the database</param>
    /// <param name="systemSession">the system session</param>
    /// <param name="listener">the database event listener, or null</param>
    /// <param name="throwIt">whether to throw instead of reporting</param>
    /// <returns>the prepared command, or null when the failure was reported</returns>
    internal Prepared? Prepare(
        Database db, SessionLocal systemSession, DatabaseEventListener? listener, bool throwIt)
    {
        try
        {
            CommandInterface command = systemSession.PrepareCommand(sql);
            Prepared prepared = command switch
            {
                CommandContainer container => container.GetPreparedCommand(),
                _ => throw new TODOException("MetaRecord.prepare: a meta row must be one statement"),
            };
            prepared.SetPersistedObjectId(id);
            return prepared;
        }
        catch (H2SharpException e)
        {
            ProcessException(listener, e, sql, throwIt);
            return null;
        }
    }

    /// <summary>
    /// Execute the metadata statement.
    /// </summary>
    /// <param name="db">the database</param>
    /// <param name="command">the prepared command</param>
    /// <param name="listener">the database event listener, or null</param>
    /// <param name="sql">SQL</param>
    /// <param name="throwIt">whether to throw instead of reporting</param>
    internal static void Execute(
        Database db, Prepared command, DatabaseEventListener? listener, string sql, bool throwIt)
    {
        try
        {
            _ = command.Update();
        }
        catch (H2SharpException e)
        {
            ProcessException(listener, e, sql, throwIt);
        }
    }

    private static bool IsPrimaryOrUniqueConstraint(Prepared record)
    {
        int type = record.GetCommandType();
        return type == CommandInterface.AlterTableAddConstraintPrimaryKey
            || type == CommandInterface.AlterTableAddConstraintUnique;
    }

    /// <remarks>
    /// The reference adds the SQL to the exception and reports it to the listener, or to the
    /// trace when there is none; it throws only when <paramref name="throwIt"/> is set.
    /// <see cref="H2SharpException"/> has neither an <c>addSQL</c> nor a trace writer in the
    /// port, so the SQL is already in the message and a reported failure is rethrown when the
    /// caller asked for that and otherwise left alone, as the reference leaves it.
    /// </remarks>
    private static void ProcessException(
        DatabaseEventListener? listener, H2SharpException e, string sql, bool throwIt)
    {
        if (listener is not null)
        {
            listener.ExceptionThrown(e, sql);
        }
        else if (throwIt)
        {
            throw e;
        }
    }

    /// <summary>Gets the object id.</summary>
    /// <returns>the id</returns>
    public int GetId() => id;

    /// <summary>Gets the object type.</summary>
    /// <returns>the type</returns>
    public int GetObjectType() => objectType;

    /// <summary>Gets the SQL that creates the object.</summary>
    /// <returns>the SQL</returns>
    public string GetSql() => sql;

    /// <summary>
    /// Sort the list of meta records by 'create order'.
    /// </summary>
    /// <param name="other">the other record</param>
    /// <returns>-1, 0, or 1</returns>
    public int CompareTo(MetaRecord? other)
    {
        ArgumentNullException.ThrowIfNull(other);
        int c1 = GetCreateOrder();
        int c2 = other.GetCreateOrder();
        if (c1 != c2)
        {
            return c1 - c2;
        }

        return GetId() - other.GetId();
    }

    /// <summary>
    /// Get the sort order id for this object type. Objects are created in this
    /// order when opening a database.
    /// </summary>
    /// <returns>the sort index</returns>
    private int GetCreateOrder()
    {
        switch (objectType)
        {
            case DbObject.Setting:
                return 0;
            case DbObject.User:
                return 1;
            case DbObject.Schema:
                return 2;
            case DbObject.FunctionAlias:
                return 3;
            case DbObject.Domain:
                return 4;
            case DbObject.Sequence:
                return 5;
            case DbObject.Constant:
                return 6;
            case DbObject.TableOrView:
                return 7;
            case DbObject.Index:
                return 8;
            case DbObject.Constraint:
                return 9;
            case DbObject.Trigger:
                return 10;
            case DbObject.Synonym:
                return 11;
            case DbObject.Role:
                return 12;
            case DbObject.Right:
                return 13;
            case DbObject.Aggregate:
                return 14;
            case DbObject.Comment:
                return 15;
            default:
                throw DbException.GetInternalError("type=" + objectType);
        }
    }

    /// <inheritdoc/>
    public override string ToString()
        => "MetaRecord [id=" + id + ", objectType=" + objectType + ", sql=" + sql + ']';
}
