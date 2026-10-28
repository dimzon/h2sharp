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

