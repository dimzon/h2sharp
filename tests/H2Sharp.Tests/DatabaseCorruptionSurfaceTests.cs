/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 268: a database file whose bytes were damaged must be refused or recovered with a
 * database exception. This exercises the SQL-layer opening path (meta records, schema and
 * session setup), not just the low-level MVStore reader.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Tests;

public sealed class DatabaseCorruptionSurfaceTests : IDisposable
{
    private readonly string file = Path.Combine(
        Path.GetTempPath(), "db_corrupt_" + Guid.NewGuid().ToString("N") + ".mv.db");

    public void Dispose()
    {
        foreach (string f in Directory.GetFiles(
            Path.GetTempPath(), Path.GetFileNameWithoutExtension(file) + "*"))
        {
            try { File.Delete(f); } catch { }
        }
    }

    [Fact]
    public void DamagedDatabaseFilesAreRefusedOrRecovered()
    {
        byte[] original = BuildDatabase();
        List<int> offsets = Enumerable.Range(0, 128).ToList();
        offsets.AddRange(Enumerable.Range(4096, 64).Select(i => i * 16));
        int opened = 0;
        int refused = 0;
        foreach (int offset in offsets.Where(o => o < original.Length))
        {
            byte[] damaged = (byte[])original.Clone();
            damaged[offset] ^= 0xFF;
            File.WriteAllBytes(file, damaged);
            try
            {
                using H2Connection connection = new("Data Source=" + file + ";Storage=File");
                connection.Open();
                using DbCommand command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES";
                _ = command.ExecuteScalar();
                opened++;
            }
            catch (H2SharpException)
            {
                refused++;
            }
            catch (MvStore.MvStoreException)
            {
                refused++;
            }
        }

        Assert.Equal(offsets.Count(o => o < original.Length), opened + refused);
    }

    private byte[] BuildDatabase()
    {
        string path = file[..^".mv.db".Length];
        using (H2Connection connection = new("Data Source=" + path + ";Storage=File"))
        {
            connection.Open();
            using DbCommand create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE T(ID INT PRIMARY KEY, V VARCHAR(50))";
            create.ExecuteNonQuery();
            using DbCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO T VALUES (1, 'one'), (2, 'two')";
            insert.ExecuteNonQuery();
        }

        return File.ReadAllBytes(file);
    }
}
