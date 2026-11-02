/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Внешние результаты сортировки: файловая БД с крошечным MAX_MEMORY_ROWS
 * выливает ORDER BY / DISTINCT / DISTINCT ON в MVSortedTempResult; тот же
 * запрос тем же файлом гоняет эталон, и строки обязаны совпасть.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class SortedTempResultOracleTests
{
    private const string Setup = """
        CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(50));
        INSERT INTO T VALUES (1, 'Bob'), (2, 'Ada'), (3, 'Bob'), (4, 'Alan'),
            (5, 'Ada'), (6, 'Grace'), (7, 'Bob'), (8, 'Alan'),
            (9, 'Hopper'), (10, 'Ada'), (11, 'Grace'), (12, 'Alan');
        SET MAX_MEMORY_ROWS 2;
        """;

    [JavaOracleFact]
    public void SpilledOrderByMatchesTheReference()
        => Assert.Equal(
            RunReference("SELECT ID, NAME FROM T ORDER BY NAME, ID"),
            RunPort("SELECT ID, NAME FROM T ORDER BY NAME, ID"));

    [JavaOracleFact]
    public void SpilledDistinctMatchesTheReference()
        => Assert.Equal(
            RunReference("SELECT DISTINCT NAME FROM T ORDER BY NAME"),
            RunPort("SELECT DISTINCT NAME FROM T ORDER BY NAME"));

    [JavaOracleFact]
    public void SpilledDistinctOnMatchesTheReference()
        => Assert.Equal(
            RunReference("SELECT DISTINCT ON (NAME) ID, NAME FROM T ORDER BY NAME, ID"),
            RunPort("SELECT DISTINCT ON (NAME) ID, NAME FROM T ORDER BY NAME, ID"));

    private static List<string> RunPort(string query)
    {
        string path = NewPath();
        try
        {
            using H2Connection connection = OpenPort(path);
            Execute(connection, Setup);
            return ReadAll(connection, query);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static List<string> RunReference(string query)
    {
        string path = NewPath();
        try
        {
            return OracleRunner.Run(
                [path, Setup.Replace('\n', ' '), query],
                """
                java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:file:" + inputs[0]);
                java.sql.Statement stat = conn.createStatement();
                for (String s : inputs[1].split(";")) {
                  if (!s.isBlank()) { stat.execute(s); }
                }
                java.sql.ResultSet rs = stat.executeQuery(inputs[2]);
                int columns = rs.getMetaData().getColumnCount();
                while (rs.next()) {
                  StringBuilder row = new StringBuilder("=");
                  for (int i = 1; i <= columns; i++) {
                    if (i > 1) { row.append(':'); }
                    row.append(rs.getString(i));
                  }
                  System.out.println(row);
                }
                conn.close();
                """,
                extraJavaArgs: ["-Duser.language=en"]);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static H2Connection OpenPort(string path)
    {
        H2Connection connection = new("Data Source=" + path + ";Storage=File");
        connection.Open();
        return connection;
    }

    private static void Execute(H2Connection connection, string sql)
    {
        foreach (string statement in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = statement;
            _ = command.ExecuteNonQuery();
        }
    }

    private static List<string> ReadAll(H2Connection connection, string query)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = query;
        using DbDataReader reader = command.ExecuteReader();
        List<string> rows = [];
        while (reader.Read())
        {
            string row = "=";
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (i > 0)
                {
                    row += ":";
                }

                row += Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static string NewPath()
    {
        string dir = Path.Combine(
            Path.GetTempPath(), "h2sharp-sorted-tmp", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(dir);
        return Path.Combine(dir, "test").Replace('\\', '/');
    }

    private static void DeleteDatabase(string path)
    {
        string directory = Path.GetDirectoryName(path)!;
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // best effort: a locked file must not hide the assertion
        }
    }
}
