/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Файловая БД: один и тот же каталог читают порт и эталон.
 *
 * Тесты написаны до портирования, как требует правило: сначала оракул «туда»,
 * «обратно» и «туда-обратно», потом код. Файл, записанный портом, открывает
 * H2 2.5.259 и читает те же строки; файл, записанный эталоном, читает порт;
 * и порт переоткрывает собственный файл после закрытия.
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
public sealed class FileDatabaseOracleTests
{
    private const string CreateAndFill = """
        CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(50));
        INSERT INTO T VALUES (1, 'Ada');
        INSERT INTO T VALUES (2, 'Alan');
        """;

    /// <summary>
    /// The rows a database of this test holds, as the test prints them.
    /// </summary>
    private const string SelectRows = "SELECT ID, NAME FROM T ORDER BY ID";

    // Blocked: file open/close works and the reference can read the file's header, but the
    // database schema is kept in the SYS table, which this port does not create yet.
    [JavaOracleFact]
    [Trait("Category", "BlockedByPort")]
    public void FileWrittenHereIsReadByTheReference()
    {
        string path = NewPath();
        try
        {
            using (H2Connection connection = OpenPort(path))
            {
                Execute(connection, CreateAndFill);
            }

            List<string> java = OracleRunner.Run(
                [path],
                """
                java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:file:" + inputs[0]);
                java.sql.Statement stat = conn.createStatement();
                java.sql.ResultSet rs = stat.executeQuery("SELECT ID, NAME FROM T ORDER BY ID");
                while (rs.next()) {
                  System.out.println("=" + rs.getInt(1) + ":" + rs.getString(2));
                }
                conn.close();
                """,
                extraJavaArgs: ["-Duser.language=en"]);

            Assert.Equal(["=1:Ada", "=2:Alan"], java);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    // Blocked: same SYS-table gap as above, in the other direction.
    [JavaOracleFact]
    [Trait("Category", "BlockedByPort")]
    public void FileWrittenByTheReferenceIsReadHere()
    {
        string path = NewPath();
        try
        {
            OracleRunner.Run(
                [path],
                """
                java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:file:" + inputs[0]);
                java.sql.Statement stat = conn.createStatement();
                stat.execute("CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(50))");
                stat.execute("INSERT INTO T VALUES (1, 'Ada'), (2, 'Alan')");
                conn.close();
                """,
                extraJavaArgs: ["-Duser.language=en"]);

            using H2Connection connection = OpenPort(path);
            Assert.Equal(["=1:Ada", "=2:Alan"], ReadRows(connection));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    // Blocked: same SYS-table gap; the data maps do round-trip, the table definition does not.
    [Fact]
    [Trait("Category", "BlockedByPort")]
    public void FileSurvivesCloseAndReopen()
    {
        string path = NewPath();
        try
        {
            using (H2Connection connection = OpenPort(path))
            {
                Execute(connection, CreateAndFill);
            }

            using H2Connection reopened = OpenPort(path);
            Assert.Equal(["=1:Ada", "=2:Alan"], ReadRows(reopened));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    /// <summary>
    /// The database path the engine reports for a file database, checked against the
    /// reference running on the same file.
    /// </summary>
    [JavaOracleFact]
    public void DatabasePathMatchesTheReference()
    {
        string path = NewPath();
        try
        {
            using (H2Connection connection = OpenPort(path))
            {
                Execute(connection, "CREATE TABLE T(ID INT)");
            }

            string? here = QueryScalar(OpenPort(path), "SELECT DATABASE_PATH()");
            List<string> java = OracleRunner.Run(
                [path],
                """
                java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:file:" + inputs[0]);
                java.sql.ResultSet rs = conn.createStatement().executeQuery("SELECT DATABASE_PATH()");
                rs.next();
                System.out.println("=" + rs.getString(1));
                conn.close();
                """,
                extraJavaArgs: ["-Duser.language=en"]);

            Assert.Single(java);
            Assert.Equal(java[0][1..], here);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static H2Connection OpenPort(string path)
    {
        H2Connection connection = new("jdbc:h2:file:" + path);
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

    private static List<string> ReadRows(H2Connection connection)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = SelectRows;
        using DbDataReader reader = command.ExecuteReader();
        List<string> rows = [];
        while (reader.Read())
        {
            rows.Add("="
                + Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)
                + ":" + Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture));
        }

        return rows;
    }

    private static string? QueryScalar(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? null
            : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static string NewPath()
    {
        string dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "h2sharp-file-oracle", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, "test").Replace('\\', '/');
    }

    private static void DeleteDatabase(string path)
    {
        string directory = System.IO.Path.GetDirectoryName(path)!;
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
