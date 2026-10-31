/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The context of a trigger: what the trigger is attached to and how it fires. The names
 * are the raw stored names — no case folding is applied by the engine, the trigger
 * decides how to compare identifiers itself.
 */
namespace H2Sharp.Data;

/// <summary>
/// The context of a trigger, passed to <see cref="IH2Trigger.Init"/> and carrying the
/// transaction a fire runs in.
/// </summary>
public interface IH2TriggerContext
{
    /// <summary>The name of the schema the table belongs to, as stored.</summary>
    string SchemaName { get; }

    /// <summary>The name of the trigger, as registered.</summary>
    string TriggerName { get; }

    /// <summary>The name of the table the trigger is attached to, as stored.</summary>
    string TableName { get; }

    /// <summary>Whether the fire method is called before or after the operation.</summary>
    bool Before { get; }

    /// <summary>The operation types the trigger listens to.</summary>
    H2TriggerEvent Type { get; }

    /// <summary>Whether the trigger is row-based rather than statement-based.</summary>
    bool RowBased { get; }

    /// <summary>
    /// Gets the transaction of the statement the trigger runs inside. DML is atomic, so
    /// during <see cref="IH2Trigger.Fire"/> this transaction is always present: queries
    /// through it see the uncommitted changes of the outer statement and roll back
    /// together with it. Committing or rolling it back from a trigger is refused.
    /// </summary>
    H2Transaction GetCurrentTransaction();
}
