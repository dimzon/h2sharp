/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.schema.TriggerObject, minus the JDBC and TriggerAdapter paths: one
 * attached trigger of a .NET registration. The SQL CREATE/DROP TRIGGER surface is stage
 * two; stage one attaches these objects directly from the data source registration.
 *
 * The reference's fire hands the trigger a connection of the session; here the context
 * carries the session's transaction instead, and the per-fire session slot below is what
 * makes GetCurrentTransaction answer. The commit/rollback disable and the last-identity
 * save the reference wraps around a fire are not ported yet (see MIGRATION_STATUS).
 */
using System;
using System.Collections.Generic;
using H2Sharp.Api;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Values;

namespace H2Sharp.Tables;

/// <summary>
/// The engine half of a registered trigger: the mask, the before/row flags, and the calls
/// into the embedder's <see cref="IH2Trigger"/> in the reference's order.
/// </summary>
internal sealed class TriggerObject
{
    private readonly Table table;

    private readonly string triggerName;

    private readonly IH2Trigger callback;

    private readonly bool before;

    private readonly int typeMask;

    private readonly bool rowBased;

    /// <summary>The session the current fire runs in, or null outside a fire.</summary>
    private SessionLocal? currentSession;

    /// <summary>A connection borrowing that session, created lazily per session.</summary>
    private H2Connection? borrowedConnection;

    private SessionLocal? borrowedSession;

    /// <summary>
    /// The auto-commit of the session before the fire: inside a fire it is switched off so
    /// a nested statement cannot commit the transaction of the firing statement, the way
    /// the reference disables commit/rollback within a fire. Restored on the way out.
    /// </summary>
    private bool savedAutoCommit;

    private TriggerObject(Table table, IH2Trigger callback, bool before, H2TriggerEvent events, bool rowBased, string triggerName)
    {
        this.table = table;
        this.callback = callback;
        this.before = before;
        typeMask = (int)events;
        this.rowBased = rowBased;
        this.triggerName = triggerName;
    }

    /// <summary>
    /// Creates the trigger instance from the registration and initializes it, in the
    /// reference's order: a failure to create or initialize is ERROR_CREATING_TRIGGER_
    /// OBJECT_3.
    /// </summary>
    internal static TriggerObject Create(Table table, TriggerRegistration registration)
    {
        IH2Trigger callback;
        try
        {
            callback = registration.Factory();
        }
        catch (Exception e)
        {
            throw CreateError(registration.Name, e);
        }

        TriggerObject trigger = new(
            table, callback, registration.Before, registration.Events, registration.RowBased, registration.Name);
        trigger.Init();
        return trigger;
    }

    private void Init()
    {
        try
        {
            callback.Init(new TriggerContext(this));
        }
        catch (Exception e)
        {
            throw CreateError(triggerName, e);
        }
    }

    internal string GetName() => triggerName;

    internal bool IsBefore() => before;

    internal bool IsRowBased() => rowBased;

    internal int GetTypeMask() => typeMask;

    internal Table GetTable() => table;

    internal bool IsSelectTrigger() => (typeMask & (int)H2TriggerEvent.Select) != 0;

    /// <summary>
    /// Fires the statement trigger, mirroring the reference's TriggerObject.fire: only a
    /// statement trigger of the matching side and mask fires, once per statement, with
    /// both rows null.
    /// </summary>
    internal void Fire(SessionLocal session, int type, bool beforeAction)
    {
        if (rowBased || before != beforeAction || (typeMask & type) == 0)
        {
            return;
        }

        Enter(session);
        try
        {
            callback.Fire(null, null);
        }
        catch (Exception e)
        {
            throw FireError(e);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>
    /// Fires the row trigger, mirroring the reference's TriggerObject.fireRow: the mask
    /// selects by the shape of the old and new rows, a rollback never fires (onRollback is
    /// stage two), and a BEFORE trigger's changes to the new row are written back.
    /// </summary>
    /// <returns>true if no further action is required (for 'instead of' triggers)</returns>
    internal bool FireRow(SessionLocal session, Table table, Row? oldRow, Row? newRow, bool beforeAction, bool rollback)
    {
        if (!rowBased || before != beforeAction)
        {
            return false;
        }

        if (rollback)
        {
            return false;
        }

        bool fire = false;
        if ((typeMask & (int)H2TriggerEvent.Insert) != 0 && oldRow is null && newRow is not null)
        {
            fire = true;
        }
        else if ((typeMask & (int)H2TriggerEvent.Update) != 0 && oldRow is not null && newRow is not null)
        {
            fire = true;
        }
        else if ((typeMask & (int)H2TriggerEvent.Delete) != 0 && oldRow is not null && newRow is null)
        {
            fire = true;
        }

        if (!fire)
        {
            return false;
        }

        object?[]? oldList = ConvertToObjectList(oldRow);
        object?[]? newList = ConvertToObjectList(newRow);
        object?[]? newListBackup = before && newList is not null ? (object?[])newList.Clone() : null;

        Enter(session);
        try
        {
            callback.Fire(
                oldList is null ? null : new TriggerRow(table, oldList),
                newList is null ? null : new TriggerRow(table, newList));
        }
        catch (Exception e)
        {
            throw FireError(e);
        }
        finally
        {
            Exit();
        }

        if (newListBackup is not null)
        {
            bool modified = false;
            for (int i = 0; i < newList!.Length; i++)
            {
                object? o = newList[i];
                if (!ReferenceEquals(o, newListBackup[i]))
                {
                    modified = true;
                    newRow!.SetValue(i, H2ValueMapper.ToValue(o, null, session.CastDataProvider));
                }
            }

            if (modified)
            {
                table.ConvertUpdateRow(session, newRow!, fromTrigger: true);
            }
        }

        // INSTEAD OF triggers are stage two; they are the only ones that answer true.
        return false;
    }

    /// <summary>Called when the database closes; exceptions are ignored, as in the reference.</summary>
    internal void Close()
    {
        try
        {
            callback.Close();
        }
        catch
        {
            // The reference logs a close failure and continues closing.
        }
    }

    /// <summary>Called when the trigger is unregistered.</summary>
    internal void Remove() => callback.Remove();

    private static object?[]? ConvertToObjectList(Row? row)
    {
        if (row is null)
        {
            return null;
        }

        Value?[] values = row.GetValueList();
        object?[] list = new object?[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            list[i] = H2ValueMapper.ToObject(values[i]);
        }

        return list;
    }

    private void Enter(SessionLocal session)
    {
        currentSession = session;
        savedAutoCommit = session.GetAutoCommit();
        if (savedAutoCommit)
        {
            session.SetAutoCommit(false);
        }

        if (!ReferenceEquals(borrowedSession, session))
        {
            borrowedSession = session;
            borrowedConnection = null;
        }
    }

    private void Exit()
    {
        if (savedAutoCommit)
        {
            currentSession?.SetAutoCommit(true);
        }

        savedAutoCommit = false;
        currentSession = null;
    }

    private H2Connection BorrowConnection(SessionLocal session)
    {
        borrowedConnection ??= H2Connection.BorrowSession(session);
        return borrowedConnection;
    }

    private static H2SharpException CreateError(string name, Exception e)
        => e is H2SharpException h2 ? h2
            : DbException.Get(ErrorCode.ErrorCreatingTriggerObject3, e, name, "<factory>", e.ToString());

    private H2SharpException FireError(Exception e)
        => e is H2SharpException h2 ? h2
            : DbException.Get(ErrorCode.ErrorExecutingTrigger3, e, triggerName, "<factory>", e.ToString());

    /// <summary>
    /// The context handed to the trigger: raw schema/table names and the transaction of
    /// the firing statement.
    /// </summary>
    private sealed class TriggerContext : IH2TriggerContext
    {
        private readonly TriggerObject owner;

        internal TriggerContext(TriggerObject owner)
        {
            this.owner = owner;
        }

        public string SchemaName => owner.table.GetSchema()!.GetName();

        public string TriggerName => owner.triggerName;

        public string TableName => owner.table.GetTableName();

        public bool Before => owner.before;

        public H2TriggerEvent Type => (H2TriggerEvent)owner.typeMask;

        public bool RowBased => owner.rowBased;

        public H2Transaction GetCurrentTransaction()
        {
            SessionLocal? session = owner.currentSession
                ?? throw new InvalidOperationException("The trigger context is not inside a fire call");
            H2Connection connection = owner.BorrowConnection(session);
            return new H2Transaction(
                connection, H2Connection.ToAdoIsolationLevel(session.GetIsolationLevel()), blockCompletion: true);
        }
    }

    /// <summary>
    /// A row as the trigger sees it: CLR values in column order and the raw stored column
    /// names.
    /// </summary>
    private sealed class TriggerRow : IH2TriggerRow
    {
        private readonly string[] columnNames;

        private readonly object?[] values;

        internal TriggerRow(Table table, object?[] values)
        {
            Column[] columns = table.GetColumns();
            columnNames = new string[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                columnNames[i] = columns[i].GetName();
            }

            this.values = values;
        }

        public IReadOnlyList<object?> Values => values;

        public IReadOnlyList<string> ColumnNames => columnNames;

        public int Count => values.Length;

        public object? GetValue(int index) => values[index];

        public void SetValue(int index, object? value) => values[index] = value;
    }
}
