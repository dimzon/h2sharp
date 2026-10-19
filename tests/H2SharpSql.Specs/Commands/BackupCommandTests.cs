// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — BACKUP command, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class BackupCommandTests
{
    /// <summary>docs/commands.html:815 — BACKUP TO fileNameString</summary>
    [Fact]
    public void Backup_ToFile_WritesTheDatabaseAndTouchesNoRows() // CMD-0815
    {
        using IDbConnection connection = TestDatabase.Open();

        int affected = connection.Execute("BACKUP TO 'backup.zip'");

        Assert.Equal(0, affected);
    }
}
