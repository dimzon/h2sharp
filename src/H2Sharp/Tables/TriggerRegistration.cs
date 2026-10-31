/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A registered trigger configuration: the .NET extension boundary's side of a CREATE
 * TRIGGER statement. The SQL surface is stage two; stage one attaches the trigger to
 * every table the predicate accepts, at open, at registration, and lazily on first fire.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Data;

namespace H2Sharp.Tables;

/// <summary>
/// The configuration of one trigger registration.
/// </summary>
/// <remarks>
/// The predicate receives the raw stored schema and table names and decides applicability
/// itself — the engine folds nothing. One registration may attach to several tables.
/// </remarks>
internal sealed class TriggerRegistration
{
    internal TriggerRegistration(
        string name,
        Func<string, string, bool> appliesTo,
        H2TriggerEvent events,
        bool before,
        bool rowBased,
        Func<IH2Trigger> factory)
    {
        Name = name;
        AppliesTo = appliesTo;
        Events = events;
        Before = before;
        RowBased = rowBased;
        Factory = factory;
    }

    /// <summary>The name of the trigger, the exact registration key.</summary>
    internal string Name { get; }

    /// <summary>
    /// Whether this trigger applies to the table with the given raw stored names.
    /// </summary>
    internal Func<string, string, bool> AppliesTo { get; }

    /// <summary>The operation mask.</summary>
    internal H2TriggerEvent Events { get; }

    /// <summary>Whether the trigger fires before rather than after the operation.</summary>
    internal bool Before { get; }

    /// <summary>Whether the trigger is row-based rather than statement-based.</summary>
    internal bool RowBased { get; }

    /// <summary>Creates a trigger instance.</summary>
    internal Func<IH2Trigger> Factory { get; }

    /// <summary>The trigger objects this registration attached to tables.</summary>
    internal List<TriggerObject> Objects { get; } = [];
}
