/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Two features without a common part with the reference to compare: TO_CHAR, whose format
 * model is .NET by the approved decision (D-019 area, see AGENTS.md), and the BACKUP TO
 * persistent branch, which waits for the file store and answers TODOException.
 */
using System;
using System.Data.Common;
using System.IO;
using H2Sharp;
using H2Sharp.Data;
using H2Sharp.Message;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ToCharAndBackupPortTests
{
    /// <summary>SELECT over an in-memory connection, one scalar per statement.</summary>
    private static object? SelectScalar(string sql)
    {
        using H2Connection connection = new("Data Source=to_char_port_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    [Fact]
    public void ToCharFormatsTimestampAndDate()
    {
        Assert.Equal("2020-01-02 12:34:56",
            SelectScalar("SELECT TO_CHAR(TIMESTAMP '2020-01-02 12:34:56', 'yyyy-MM-dd HH:mm:ss')"));
        Assert.Equal("2020-01-02",
            SelectScalar("SELECT TO_CHAR(DATE '2020-01-02', 'yyyy-MM-dd')"));
    }

    [Fact]
    public void ToCharAnswersNullForNullValueOrFormat()
    {
        // ExecuteScalar maps SQL NULL to DBNull, as ADO.NET does.
        Assert.Equal(DBNull.Value, SelectScalar("SELECT TO_CHAR(CAST(NULL AS TIMESTAMP), 'yyyy')"));
        Assert.Equal(DBNull.Value, SelectScalar("SELECT TO_CHAR(TIMESTAMP '2020-01-02 12:34:56', NULL)"));
    }

    [Fact]
    public void ToCharRefusesANonDateTimeValue()
    {
        H2SharpException e = Assert.Throws<H2SharpException>(
            () => SelectScalar("SELECT TO_CHAR(1, 'yyyy')"));
        Assert.Equal(H2Sharp.Api.ErrorCode.InvalidValue2, e.H2ErrorCode);
    }

    [Fact]
    public void BackupOnPersistentDatabaseNamesTheMissingStore()
    {
        string path = Path.Combine(Path.GetTempPath(), "h2sharp_backup_stub_" + Guid.NewGuid().ToString("N"));
        try
        {
            using H2Connection connection = new("Data Source=" + path + ";Storage=File");
            connection.Open();
            using (DbCommand create = connection.CreateCommand())
            {
                create.CommandText = "CREATE TABLE T(ID INT)";
                _ = create.ExecuteNonQuery();
            }

            using DbCommand command = connection.CreateCommand();
            command.CommandText = "BACKUP TO '" + path + ".zip'";
            // The persistent branch of the port waits for the file store to open (see
            // BackupCommand): the stub must refuse by name, not pretend to have backed up.
            TODOException e = Assert.Throws<TODOException>(() => _ = command.ExecuteNonQuery());
            Assert.Contains("BackupCommand", e.Message);
        }
        finally
        {
            foreach (string file in Directory.GetFiles(Path.GetTempPath(), "h2sharp_backup_stub_*"))
            {
                File.Delete(file);
            }
        }
    }
}
