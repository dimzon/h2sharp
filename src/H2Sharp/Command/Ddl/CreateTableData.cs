/*
 * Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.command.ddl.CreateTableData, H2 2.5.
 */
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>The data required to create a table.</summary>
public sealed class CreateTableData
{
    public Schema Schema = null!;
    public string TableName = string.Empty;
    public int Id;
    public List<Column> Columns = [];
    public bool Temporary;
    public bool GlobalTemporary;
    public bool PersistIndexes;
    public bool PersistData;
    public SessionLocal Session = null!;
    public string? TableEngine;
    public List<string>? TableEngineParams;
}
