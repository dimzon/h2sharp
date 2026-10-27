// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — BACKUP command, one test per checklist row.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class BackupCommandTests
{
    /// <summary>docs/commands.html:815 — BACKUP TO fileNameString</summary>
    [Fact]
    public void Backup_InMemory_IsRejectedAsNotPersistent() // CMD-0815
    {
        using IDbConnection connection = TestDatabase.Open();

        Exception error = Assert.ThrowsAny<Exception>(() => connection.Execute("BACKUP TO 'backup.zip'"));

        Assert.Contains("not persistent", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
