/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Values that only touch disk on a file database: JSON documents and non-finite
 * DECFLOAT. Each side stores into its own temporary file database, checkpoints, closes
 * and reopens, and only the selected text is compared — the database paths differ.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class FileStorageOracleTests
{
    [JavaOracleFact]
    public void JsonAndDecfloatRoundTripThroughTheFileStorage()
    {
        List<string> java = OracleRunner.Run([], """
            java.nio.file.Path dir = java.nio.file.Files.createTempDirectory("h2filestorage");
            try {
              try (java.sql.Connection c = java.sql.DriverManager.getConnection(
                      "jdbc:h2:" + dir.resolve("db"))) {
                java.sql.Statement s = c.createStatement();
                s.execute("CREATE TABLE J(ID INT, V JSON)");
                s.execute("INSERT INTO J VALUES (1, '[1]')");
                s.execute("INSERT INTO J VALUES (2, '[1]' FORMAT JSON)");
                s.execute("INSERT INTO J VALUES (3, NULL)");
                s.execute("CREATE TABLE D(ID INT, V DECFLOAT)");
                s.execute("INSERT INTO D VALUES (1, 1.5), (2, '-Infinity'), (3, 'Infinity'), (4, 'NaN'), (5, NULL)");
                s.execute("CHECKPOINT");
              }
              try (java.sql.Connection c = java.sql.DriverManager.getConnection(
                      "jdbc:h2:" + dir.resolve("db"))) {
                java.sql.ResultSet r = c.createStatement().executeQuery(
                    "SELECT ID, CAST(V AS VARCHAR) FROM J ORDER BY ID");
                while (r.next()) System.out.println(r.getInt(1) + "|" + r.getString(2));
                r = c.createStatement().executeQuery(
                    "SELECT ID, CAST(V AS VARCHAR) FROM D ORDER BY ID");
                while (r.next()) System.out.println(r.getInt(1) + "|" + r.getString(2));
              }
            } finally {
              java.nio.file.Files.walk(dir).sorted(java.util.Comparator.reverseOrder())
                  .forEach(p -> p.toFile().delete());
            }
            """);

        List<string> port = RunPort();
        Assert.Equal(java.Count, port.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == port[i],
                "line " + i + ":\n  jvm:  " + java[i] + "\n  port: " + port[i]);
        }
    }

    private static List<string> RunPort()
    {
        string dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "h2filestorage_" + Guid.NewGuid().ToString("N"));
        _ = System.IO.Directory.CreateDirectory(dir);
        try
        {
            string url = "Data Source=" + dir + "/db;Storage=File";
            using (H2Connection connection = new(url))
            {
                connection.Open();
                Execute(connection, "CREATE TABLE J(ID INT, V JSON)");
                Execute(connection, "INSERT INTO J VALUES (1, '[1]')");
                Execute(connection, "INSERT INTO J VALUES (2, '[1]' FORMAT JSON)");
                Execute(connection, "INSERT INTO J VALUES (3, NULL)");
                Execute(connection, "CREATE TABLE D(ID INT, V DECFLOAT)");
                Execute(connection, "INSERT INTO D VALUES (1, 1.5), (2, '-Infinity'), "
                    + "(3, 'Infinity'), (4, 'NaN'), (5, NULL)");
                Execute(connection, "CHECKPOINT");
            }

            // A reopen reads what the checkpoint wrote: the round trip is the point.
            using H2Connection reopened = new(url);
            reopened.Open();
            List<string> output = [];
            Read(reopened, "SELECT ID, CAST(V AS VARCHAR) FROM J ORDER BY ID", output);
            Read(reopened, "SELECT ID, CAST(V AS VARCHAR) FROM D ORDER BY ID", output);
            return output;
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }

    private static void Read(H2Connection connection, string sql, List<string> output)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using DbDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            // Java string concatenation spells a null column "null"; match it here.
            output.Add(reader.GetInt32(0).ToString(CultureInfo.InvariantCulture)
                + "|" + (reader.IsDBNull(1) ? "null" : reader.GetString(1)));
        }
    }

    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
