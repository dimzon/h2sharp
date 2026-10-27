/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.CreateSynonymData.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Schemas;

namespace H2Sharp.Command.Ddl;

/// <summary>The data required to create a synonym.</summary>
public sealed class CreateSynonymData
{
    /// <summary>The schema.</summary>
    public Schema Schema = null!;

    /// <summary>The synonym name.</summary>
    public string SynonymName = string.Empty;

    /// <summary>The name of the table the synonym is created for.</summary>
    public string SynonymFor = string.Empty;

    /// <summary>The schema <see cref="SynonymFor"/> is located in.</summary>
    public Schema SynonymForSchema = null!;

    /// <summary>The object id.</summary>
    public int Id;

    /// <summary>The session.</summary>
    public SessionLocal Session = null!;
}
