/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The event mask of a trigger registration — the .NET side of the reference's
 * org.h2.api.Trigger constants (INSERT = 1, UPDATE = 2, DELETE = 4, SELECT = 8).
 */
using System;

namespace H2Sharp.Data;

/// <summary>
/// The statement types a trigger listens to, as a bit mask.
/// </summary>
[Flags]
public enum H2TriggerEvent
{
    /// <summary>The trigger is called for INSERT statements.</summary>
    Insert = 1,

    /// <summary>The trigger is called for UPDATE statements.</summary>
    Update = 2,

    /// <summary>The trigger is called for DELETE statements.</summary>
    Delete = 4,

    /// <summary>
    /// The trigger is called for SELECT statements. Declared with the reference's value;
    /// the SELECT event itself is not ported yet.
    /// </summary>
    Select = 8,
}
