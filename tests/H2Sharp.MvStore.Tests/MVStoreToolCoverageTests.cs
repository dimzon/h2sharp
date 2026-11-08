/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of MVStoreTool: Info on a missing and an existing file, and a Dump.
 */
using System;
using System.Data.Common;
using System.IO;
using H2Sharp.Data;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class MVStoreToolCoverageTests : IDisposable
{
    private readonly string fileName;
    private readonly string dbFile;

    public MVStoreToolCoverageTests()
    {
        fileName = Path.Combine(Path.GetTempPath(), "mvstore_tool_" + Guid.NewGuid().ToString("N"));
        dbFile = fileName + ".mv.db";
    }

    public void Dispose()
    {
        foreach (string f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(fileName) + "*"))
        {
            try { File.Delete(f); } catch { }
        }
    }

    [Fact]
    public void InfoAndDumpAnswerThePort()
    {
        // Missing file: the not-found branch.
        Assert.Equal("File not found: " + dbFile, MVStoreTool.Info(dbFile));

        // Create a file-backed database and close it.
        using (H2Connection connection = new("Data Source=" + fileName + ";Storage=File"))
        {
            connection.Open();
            using DbCommand create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE T(A INT, B INT)";
            create.ExecuteNonQuery();
            using DbCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO T VALUES (1, 2), (3, 4)";
            insert.ExecuteNonQuery();
        }

        // Existing file: the summary path.
        string? error = MVStoreTool.Info(dbFile);
        Assert.Null(error);

        // Dump the file (details off and on).
        MVStoreTool.Dump(dbFile, false);
        MVStoreTool.Dump(dbFile, true);
    }
}