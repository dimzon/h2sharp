/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The data adapter's row events and fill-error path.
 */
using System;
using System.Data;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2DataAdapterTests
{
    [Fact]
    public void RowEventsAreRaisedWithTheProvidersArguments()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        Execute(connection, "CREATE TABLE T(ID INT, NAME VARCHAR)");

        using H2DataAdapter adapter = new();
        adapter.SelectCommand = new H2Command("SELECT ID, NAME FROM T", connection);
        DataTable table = new();
        adapter.Fill(table);
        table.Rows.Add(1, "a");

        adapter.InsertCommand = new H2Command("INSERT INTO T(ID, NAME) VALUES(?, ?)", connection)
        {
            UpdatedRowSource = UpdateRowSource.None,
        };
        adapter.InsertCommand.Parameters.Add(new H2Parameter { SourceColumn = "ID" });
        adapter.InsertCommand.Parameters.Add(new H2Parameter { SourceColumn = "NAME" });

        int updating = 0;
        int updated = 0;
        adapter.RowUpdating += (_, e) =>
        {
            H2RowUpdatingEventArgs args = Assert.IsType<H2RowUpdatingEventArgs>(e);
            Assert.IsType<H2Command>(args.Command);
            Assert.Equal(StatementType.Insert, args.StatementType);
            updating++;
        };
        adapter.RowUpdated += (_, e) =>
        {
            H2RowUpdatedEventArgs args = Assert.IsType<H2RowUpdatedEventArgs>(e);
            Assert.IsType<H2Command>(args.Command);
            Assert.Equal(1, args.RecordsAffected);
            updated++;
        };

        Assert.Equal(1, adapter.Update(table));
        Assert.Equal(1, updating);
        Assert.Equal(1, updated);
        Assert.Equal("1", Scalar(connection, "SELECT COUNT(*) FROM T"));
    }

    [Fact]
    public void SkippingARowInRowUpdatingSkipsItsCommand()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        Execute(connection, "CREATE TABLE T(ID INT)");

        using H2DataAdapter adapter = new();
        adapter.SelectCommand = new H2Command("SELECT ID FROM T", connection);
        DataTable table = new();
        adapter.Fill(table);
        table.Rows.Add(1);
        table.Rows.Add(2);

        adapter.InsertCommand = new H2Command("INSERT INTO T(ID) VALUES(?)", connection)
        {
            UpdatedRowSource = UpdateRowSource.None,
        };
        adapter.InsertCommand.Parameters.Add(new H2Parameter { SourceColumn = "ID" });

        adapter.RowUpdating += (_, e) =>
        {
            if ((int)e.Row["ID"] == 1)
            {
                e.Status = UpdateStatus.SkipCurrentRow;
            }
        };

        Assert.Equal(1, adapter.Update(table));
        Assert.Equal("1", Scalar(connection, "SELECT COUNT(*) FROM T"));
        Assert.Equal("2", Scalar(connection, "SELECT ID FROM T"));
    }

    [Fact]
    public void FillErrorsReachTheHandlerAndContinueFill()
    {
        using H2Connection connection = NewConnection();
        connection.Open();

        using H2DataAdapter adapter = new();
        adapter.SelectCommand = new H2Command("SELECT CAST('abc' AS VARCHAR) AS ID", connection);
        DataTable table = new();
        table.Columns.Add("ID", typeof(int));

        int errors = 0;
        adapter.FillError += (_, e) =>
        {
            errors++;
            e.Continue = true;
        };

        adapter.Fill(table);

        Assert.Equal(1, errors);
        Assert.Empty(table.Rows);
    }

    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Scalar(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? "-"
            : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "-";
    }

    private static H2Connection NewConnection()
        => new($"Data Source=adapter_{Guid.NewGuid():N};Storage=Memory");
}
