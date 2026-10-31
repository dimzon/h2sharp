/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The user-supplied trigger implementation — the .NET counterpart of org.h2.api.Trigger,
 * without the JDBC Connection parameter: the engine hands the trigger its view of the
 * world through IH2TriggerContext and IH2TriggerRow instead.
 */
namespace H2Sharp.Data;

/// <summary>
/// A trigger implementation supplied by the embedder.
/// </summary>
/// <remarks>
/// The reference reaches the trigger through org.h2.api.Trigger and passes a connection of
/// the session on every call; here the context and the rows carry that information. The
/// lifecycle is the reference's: Init on attach, Fire per row or per statement, Close when
/// the database closes, Remove when the trigger is unregistered.
/// </remarks>
public interface IH2Trigger
{
    /// <summary>
    /// Called once by the engine when the trigger is attached to a table, as the reference
    /// calls init when a trigger is created or the database is opened.
    /// </summary>
    /// <param name="context">the trigger context</param>
    void Init(IH2TriggerContext context) { }

    /// <summary>
    /// Called for each row the trigger fires on, or once per statement for a statement
    /// trigger (both rows null). An exception aborts the operation and rolls back the
    /// statement.
    /// </summary>
    /// <param name="oldRow">the row as it was, or null for INSERT / statement triggers</param>
    /// <param name="newRow">the row as it becomes, or null for DELETE / statement triggers</param>
    void Fire(IH2TriggerRow? oldRow, IH2TriggerRow? newRow);

    /// <summary>
    /// Called when the database closes. Exceptions are ignored, as the reference logs
    /// and continues closing.
    /// </summary>
    void Close() { }

    /// <summary>
    /// Called when the trigger is unregistered (or, later, dropped).
    /// </summary>
    void Remove() { }
}
