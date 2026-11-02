using System;
using System.Data.Common;
using System.Threading.Tasks;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2LocalTempTableTests
{
    [Fact]
    public void ConcurrentConnectionsCanUseTheSameLocalTempTableName()
    {
        const string url = "Data Source=local_temp_parallel;Storage=Memory";
        using H2Connection keeper = new(url);
        keeper.Open();

        Task[] tasks = new Task[3];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < 20; j++)
                {
                    using H2Connection connection = new(url);
                    connection.Open();
                    using DbCommand command = connection.CreateCommand();
                    command.CommandText = "CREATE LOCAL TEMPORARY TABLE TEMP(ID IDENTITY)";
                    _ = command.ExecuteNonQuery();
                    command.CommandText = "INSERT INTO TEMP VALUES(1)";
                    _ = command.ExecuteNonQuery();
                    connection.Close();
                }
            });
        }

        Task.WaitAll(tasks);
    }

    [Fact]
    public void TwoReadersOverSystemRangeAreIndependent()
    {
        using H2Connection connection = new("Data Source=local_two_readers;Storage=Memory");
        connection.Open();
        using DbCommand first = connection.CreateCommand();
        using DbCommand second = connection.CreateCommand();
        first.CommandText = "SELECT * FROM SYSTEM_RANGE(1, 20)";
        second.CommandText = first.CommandText;
        using DbDataReader r1 = first.ExecuteReader();
        using DbDataReader r2 = second.ExecuteReader();
        for (int i = 0; i < 10; i++)
        {
            Assert.True(r1.Read());
            Assert.True(r2.Read());
            Assert.Equal(i + 1, r1.GetInt32(0));
            Assert.Equal(i + 1, r2.GetInt32(0));
        }
    }

    [Fact]
    public void TwoReadersOverFileSystemRangeWithMemoryLimitAreIndependent()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "h2sharp_readers_" + Guid.NewGuid().ToString("N"));
        try
        {
            using H2Connection connection = new(
                "Data Source=" + path + ";Storage=File;MAX_MEMORY_ROWS=10;IGNORE_UNKNOWN_SETTINGS=true");
            connection.Open();
            using DbCommand first = connection.CreateCommand();
            using DbCommand second = connection.CreateCommand();
            first.CommandText = "SELECT * FROM SYSTEM_RANGE(1, 20)";
            second.CommandText = first.CommandText;
            using DbDataReader r1 = first.ExecuteReader();
            using DbDataReader r2 = second.ExecuteReader();
            for (int i = 0; i < 10; i++)
            {
                Assert.True(r1.Read());
                Assert.True(r2.Read());
                Assert.Equal(i + 1, r1.GetInt32(0));
                Assert.Equal(i + 1, r2.GetInt32(0));
            }
        }
        finally
        {
            foreach (string file in System.IO.Directory.GetFiles(
                System.IO.Path.GetTempPath(),
                System.IO.Path.GetFileName(path) + "*"))
            {
                System.IO.File.Delete(file);
            }
        }
    }

    [Fact]
    public void SortedDistinctResultSpillsToDiskAndReadsBack()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "h2sharp_sorted_" + Guid.NewGuid().ToString("N"));
        try
        {
            using H2Connection connection = new(
                "Data Source=" + path + ";Storage=File;IGNORE_UNKNOWN_SETTINGS=true");
            connection.Open();
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "SET MAX_MEMORY_ROWS 1";
            _ = command.ExecuteNonQuery();
            command.CommandText = "SELECT DISTINCT X FROM SYSTEM_RANGE(1, 20) ORDER BY X";
            using DbDataReader reader = command.ExecuteReader();
            for (int i = 1; i <= 20; i++)
            {
                Assert.True(reader.Read());
                Assert.Equal(i, reader.GetInt32(0));
            }

            Assert.False(reader.Read());
        }
        finally
        {
            DeleteDatabaseFiles(path);
        }
    }

    [Fact]
    public void DistinctOnResultSpillsToDiskAndReadsBack()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "h2sharp_distinct_on_" + Guid.NewGuid().ToString("N"));
        try
        {
            using H2Connection connection = new(
                "Data Source=" + path + ";Storage=File;IGNORE_UNKNOWN_SETTINGS=true");
            connection.Open();
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "SET MAX_MEMORY_ROWS 1";
            _ = command.ExecuteNonQuery();
            command.CommandText = "SELECT DISTINCT ON (X % 5) X FROM SYSTEM_RANGE(1, 20) ORDER BY X";
            using DbDataReader reader = command.ExecuteReader();
            for (int i = 1; i <= 5; i++)
            {
                Assert.True(reader.Read());
                Assert.Equal(i, reader.GetInt32(0));
            }

            Assert.False(reader.Read());
        }
        finally
        {
            DeleteDatabaseFiles(path);
        }
    }

    [Fact]
    public void PlainResultSpillsToDiskAndReadsBack()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "h2sharp_plain_" + Guid.NewGuid().ToString("N"));
        try
        {
            using H2Connection connection = new(
                "Data Source=" + path + ";Storage=File;IGNORE_UNKNOWN_SETTINGS=true");
            connection.Open();
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "SET MAX_MEMORY_ROWS 1";
            _ = command.ExecuteNonQuery();
            command.CommandText = "SELECT X FROM SYSTEM_RANGE(1, 20)";
            using DbDataReader reader = command.ExecuteReader();
            for (int i = 1; i <= 20; i++)
            {
                Assert.True(reader.Read());
                Assert.Equal(i, reader.GetInt32(0));
            }

            Assert.False(reader.Read());
        }
        finally
        {
            DeleteDatabaseFiles(path);
        }
    }

    [Fact]
    public void ReorderedSortedResultSpillsToDiskAndReadsBack()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "h2sharp_reordered_" + Guid.NewGuid().ToString("N"));
        try
        {
            using H2Connection connection = new(
                "Data Source=" + path + ";Storage=File;IGNORE_UNKNOWN_SETTINGS=true");
            connection.Open();
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "SET MAX_MEMORY_ROWS 1";
            _ = command.ExecuteNonQuery();
            command.CommandText = "SELECT X, X * 2 FROM SYSTEM_RANGE(1, 20) ORDER BY 2 DESC";
            using DbDataReader reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(20, reader.GetInt32(0));
            Assert.Equal(40, reader.GetInt32(1));
            int count = 1;
            while (reader.Read())
            {
                count++;
            }

            Assert.Equal(20, count);
        }
        finally
        {
            DeleteDatabaseFiles(path);
        }
    }

    [Fact]
    public void TwoSortedReadersOverTheSameExternalResultAreIndependent()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "h2sharp_sorted_" + Guid.NewGuid().ToString("N"));
        try
        {
            using H2Connection connection = new(
                "Data Source=" + path + ";Storage=File;IGNORE_UNKNOWN_SETTINGS=true");
            connection.Open();
            using DbCommand first = connection.CreateCommand();
            first.CommandText = "SET MAX_MEMORY_ROWS 1";
            _ = first.ExecuteNonQuery();
            using DbCommand second = connection.CreateCommand();
            first.CommandText = "SELECT DISTINCT X FROM SYSTEM_RANGE(1, 20) ORDER BY X";
            second.CommandText = first.CommandText;
            using DbDataReader r1 = first.ExecuteReader();
            using DbDataReader r2 = second.ExecuteReader();
            for (int i = 1; i <= 10; i++)
            {
                Assert.True(r1.Read());
                Assert.True(r2.Read());
                Assert.Equal(i, r1.GetInt32(0));
                Assert.Equal(i, r2.GetInt32(0));
            }
        }
        finally
        {
            DeleteDatabaseFiles(path);
        }
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (string file in System.IO.Directory.GetFiles(
            System.IO.Path.GetTempPath(),
            System.IO.Path.GetFileName(path) + "*"))
        {
            System.IO.File.Delete(file);
        }
    }

    [Fact]
    public void ReopeningAConnectionDoesNotReuseLocalTempRows()
    {
        const string url = "Data Source=local_temp_reopen;Storage=Memory";
        using H2Connection keeper = new(url);
        keeper.Open();
        for (int i = 0; i < 3; i++)
        {
            using H2Connection connection = new(url);
            connection.Open();
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "CREATE LOCAL TEMPORARY TABLE TEMP(ID IDENTITY)";
            _ = command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO TEMP VALUES(1)";
            _ = command.ExecuteNonQuery();
        }
    }
}

