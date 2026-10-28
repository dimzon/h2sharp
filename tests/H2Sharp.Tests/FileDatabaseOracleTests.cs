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

    [JavaOracleFact]
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

    [JavaOracleFact]
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

    [Fact]
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
    /// A lock method the port does not implement is refused, not silently replaced with FS.
    /// </summary>
    [Fact]
    public void UnportedFileLockMethodsAreRefused()
    {
        string path = NewPath();
        try
        {
            using (H2Connection connection = new("Data Source=" + path + ";Storage=File;FILE_LOCK=SOCKET"))
            {
                _ = Assert.Throws<TODOException>(() => connection.Open());
            }

            using (H2Connection connection = new("Data Source=" + path + ";Storage=File;AUTO_SERVER=TRUE"))
            {
                H2SharpException e = Assert.Throws<H2SharpException>(() => connection.Open());
                Assert.Equal(Api.ErrorCode.FeatureNotSupported1, e.H2ErrorCode);
            }

            using (H2Connection connection = new("Data Source=" + path + ";Storage=File;FILE_LOCK=LDAP"))
            {
                H2SharpException e = Assert.Throws<H2SharpException>(() => connection.Open());
                Assert.Equal(Api.ErrorCode.UnsupportedLockMethod1, e.H2ErrorCode);
            }
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    /// <summary>
    /// FILE_LOCK=NO is accepted: for the MVStore the data-file lock is taken either way.
    /// </summary>
    [Fact]
    public void FileLockNoStillLocksTheDataFile()
    {
        string path = NewPath();
        try
        {
            using (H2Connection connection = new("Data Source=" + path + ";Storage=File;FILE_LOCK=NO"))
            {
                connection.Open();
                Execute(connection, CreateAndFill);
            }

            using H2Connection reopened = new("Data Source=" + path + ";Storage=File;FILE_LOCK=NO");
            reopened.Open();
            Assert.Equal(["=1:Ada", "=2:Alan"], ReadRows(reopened));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    /// <summary>
    /// A read-only open refuses to start while a .lock.db from another holder is there.
    /// </summary>
    [Fact]
    public void ReadOnlyRefusesAnExistingLockFile()
    {
        string path = NewPath();
        try
        {
            using (H2Connection connection = OpenPort(path))
            {
                Execute(connection, "CREATE TABLE T(ID INT)");
            }

            File.WriteAllText(path + ".lock.db", "id=someone-else");
            using H2Connection readOnly = new("Data Source=" + path + ";Storage=File;ACCESS_MODE_DATA=r");
            H2SharpException e = Assert.Throws<H2SharpException>(() => readOnly.Open());
            Assert.Equal(Api.ErrorCode.DatabaseAlreadyOpen1, e.H2ErrorCode);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    /// <summary>
    /// A file database held by the port refuses the reference with the reference's own error.
    /// </summary>
    /// <remarks>
    /// Both sides lock the data file with the platform's range lock
    /// (<c>FileChannel.tryLock</c> on the JVM, <see cref="FileStream.Lock"/> here), so the
    /// reference must answer <c>DATABASE_ALREADY_OPEN_1</c> (90020) rather than an I/O error.
    /// </remarks>
    [JavaOracleFact]
    public void FileLockedByThePortIsRefusedByTheReference()
    {
        string path = NewPath();
        try
        {
            using H2Connection connection = OpenPort(path);
            Execute(connection, "CREATE TABLE T(ID INT)");

            List<string> java = OracleRunner.Run(
                [path],
                """
                try {
                  java.sql.DriverManager.getConnection("jdbc:h2:file:" + inputs[0]).close();
                  System.out.println("=opened");
                } catch (java.sql.SQLException e) {
                  System.out.println("=" + e.getErrorCode());
                }
                """,
                extraJavaArgs: ["-Duser.language=en"]);

            Assert.Equal(["=90020"], java);
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

            string? here;
            using (H2Connection reopened = OpenPort(path))
            {
                here = QueryScalar(reopened, "SELECT DATABASE_PATH()");
            }
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

