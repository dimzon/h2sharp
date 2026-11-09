/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Command generation over a select command: the builder attaches to the adapter, reads the
 * select's schema metadata, and the adapter updates rows without commands supplied by hand.
 */
using System;
using System.Data;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2CommandBuilderTests
{
    [Fact]
    public void GeneratedCommandsUpdateThroughTheAdapter()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        Execute(connection, "CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(20), NOTE VARCHAR(20))");

        using H2DataAdapter adapter = new("SELECT ID, NAME, NOTE FROM T", connection);
        using H2CommandBuilder builder = new(adapter);

        DataTable table = new();
        adapter.Fill(table);

        // Insert, update and delete all run through the commands the builder generates
        // during the adapter's row-updating event.
        table.Rows.Add(1, "a", DBNull.Value);
        Assert.Equal(1, adapter.Update(table));

        table.Rows[0]["NAME"] = "b";
        table.Rows[0]["NOTE"] = "n";
        Assert.Equal(1, adapter.Update(table));

        table.Rows[0].Delete();
        Assert.Equal(1, adapter.Update(table));

        Assert.Equal("0", Scalar(connection, "SELECT COUNT(*) FROM T"));
    }

    [Fact]
    public void GeneratedCommandsNameTheBaseColumnsOfAnAliasedSelect()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        Execute(connection, "CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(20))");
        Execute(connection, "INSERT INTO T(ID, NAME) VALUES(1, 'a')");

        using H2DataAdapter adapter = new("SELECT ID AS K, NAME AS N FROM T", connection);
        using H2CommandBuilder builder = new(adapter);

        DataTable table = new();
        adapter.Fill(table);

        // The statement text names the base columns; the parameters read the aliases the
        // result set spells, because that is what the DataTable columns are called.
        DbCommand update = builder.GetUpdateCommand();
        Assert.Contains("\"ID\"", update.CommandText, StringComparison.Ordinal);
        Assert.Contains("\"NAME\"", update.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"K\"", update.CommandText, StringComparison.Ordinal);
        Assert.Equal("K", update.Parameters[0].SourceColumn);

        table.Rows[0]["N"] = "b";
        Assert.Equal(1, adapter.Update(table));
        Assert.Equal("b", Scalar(connection, "SELECT NAME FROM T WHERE ID = 1"));
    }

    [Fact]
    public void IdentityAndGeneratedColumnsStayOutOfTheInsert()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        Execute(
            connection,
            "CREATE TABLE T("
            + "ID INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,"
            + "NAME VARCHAR(20),"
            + "UPPER_NAME VARCHAR(20) GENERATED ALWAYS AS (UPPER(NAME)))");

        using H2DataAdapter adapter = new("SELECT ID, NAME, UPPER_NAME FROM T", connection);
        using H2CommandBuilder builder = new(adapter);

        DbCommand insert = builder.GetInsertCommand();
        Assert.Contains("\"NAME\"", insert.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ID\"", insert.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"UPPER_NAME\"", insert.CommandText, StringComparison.Ordinal);
        Assert.Single(insert.Parameters);
    }

    [Fact]
    public void NullableSearchableColumnsGetTheNullTestWhereClause()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        Execute(connection, "CREATE TABLE T(ID INT PRIMARY KEY, NOTE VARCHAR(20))");
        Execute(connection, "INSERT INTO T(ID, NOTE) VALUES(1, NULL)");

        using H2DataAdapter adapter = new("SELECT ID, NOTE FROM T", connection);
        using H2CommandBuilder builder = new(adapter);

        DbCommand delete = builder.GetDeleteCommand();
        Assert.Contains("\"ID\" = ?", delete.CommandText, StringComparison.Ordinal);
        // A nullable column in the WHERE clause is matched with the null-test form, because
        // "= NULL" would never be true.
        Assert.Contains("IS NULL", delete.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void ATableWithoutPrimaryKeyCannotGenerateUpdateOrDelete()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        Execute(connection, "CREATE TABLE T(ID INT, NAME VARCHAR(20))");

        using H2DataAdapter adapter = new("SELECT ID, NAME FROM T", connection);
        using H2CommandBuilder builder = new(adapter);

        // The insert needs no key and is generated; the update and delete refuse rather than
        // writing a WHERE clause that would match every row.
        Assert.NotNull(builder.GetInsertCommand());
        Assert.Throws<InvalidOperationException>(() => builder.GetUpdateCommand());
        Assert.Throws<InvalidOperationException>(() => builder.GetDeleteCommand());
    }

    [Fact]
    public void ASelectWithoutABaseTableCannotGenerateCommands()
    {
        using H2Connection connection = NewConnection();
        connection.Open();

        using H2DataAdapter adapter = new("SELECT 1 AS ID", connection);
        using H2CommandBuilder builder = new(adapter);

        Assert.Throws<InvalidOperationException>(() => builder.GetInsertCommand());
    }

    [Fact]
    public void TheReaderReportsBaseTableAndColumnOfResolvedColumns()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        Execute(connection, "CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(20))");

        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ID AS K, NAME FROM T";
        using DbDataReader reader = command.ExecuteReader(CommandBehavior.SchemaOnly);
        DataTable schema = reader.GetSchemaTable()!;

        Assert.Equal("T", schema.Rows[0]["BaseTableName"]);
        Assert.Equal("PUBLIC", schema.Rows[0]["BaseSchemaName"]);
        Assert.Equal("ID", schema.Rows[0]["BaseColumnName"]);
        Assert.Equal("K", schema.Rows[0]["ColumnName"]);
        Assert.Equal(false, schema.Rows[0]["IsAutoIncrement"]);

        // An expression has no base table, so no base column either.
        using DbCommand expression = connection.CreateCommand();
        expression.CommandText = "SELECT ID + 1 AS NEXT FROM T";
        using DbDataReader expressionReader = expression.ExecuteReader(CommandBehavior.SchemaOnly);
        DataTable expressionSchema = expressionReader.GetSchemaTable()!;
        Assert.Equal(DBNull.Value, expressionSchema.Rows[0]["BaseTableName"]);
        Assert.Equal(DBNull.Value, expressionSchema.Rows[0]["BaseColumnName"]);
    }

    [Fact]
    public void ATimeColumnRoundTripsThroughTheGeneratedCommands()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        Execute(connection, "CREATE TABLE T(ID INT PRIMARY KEY, AT TIME)");

        using H2DataAdapter adapter = new("SELECT ID, AT FROM T", connection);
        using H2CommandBuilder builder = new(adapter);

        DataTable table = new();
        adapter.Fill(table);
        table.Rows.Add(1, new TimeSpan(12, 30, 0));
        Assert.Equal(1, adapter.Update(table));

        Assert.Equal("12:30:00", Scalar(connection, "SELECT CAST(AT AS VARCHAR) FROM T"));
    }

    [Fact]
    public void GeneratedCommandsKeepQuotedIdentifierCase()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        Execute(connection, "CREATE TABLE \"lower\" (\"Id\" INT PRIMARY KEY, \"Name\" VARCHAR(20))");
        Execute(connection, "INSERT INTO \"lower\"(\"Id\", \"Name\") VALUES(1, 'a')");

        using H2DataAdapter adapter = new("SELECT * FROM \"lower\"", connection);
        using H2CommandBuilder builder = new(adapter);

        DataTable table = new();
        adapter.Fill(table);
        table.Rows[0]["Name"] = "b";
        Assert.Equal(1, adapter.Update(table));

        // The table and column names are quoted, so the lower case stays the name the
        // table was created with rather than the upper case an unquoted name would become.
        DbCommand update = builder.GetUpdateCommand();
        Assert.Contains("\"lower\"", update.CommandText, StringComparison.Ordinal);
        Assert.Contains("\"Id\"", update.CommandText, StringComparison.Ordinal);
        Assert.Equal("b", Scalar(connection, "SELECT \"Name\" FROM \"lower\" WHERE \"Id\" = 1"));
    }

    [Fact]
    public void ACompositePrimaryKeyUsesEveryKeyColumn()
    {
        using H2Connection connection = NewConnection();
        connection.Open();
        Execute(connection, "CREATE TABLE T(A INT, B INT, V VARCHAR(20), PRIMARY KEY(A, B))");
        Execute(connection, "INSERT INTO T(A, B, V) VALUES(1, 2, 'a')");

        using H2DataAdapter adapter = new("SELECT A, B, V FROM T", connection);
        using H2CommandBuilder builder = new(adapter);

        DataTable table = new();
        adapter.Fill(table);
        table.Rows[0]["V"] = "b";
        Assert.Equal(1, adapter.Update(table));

        DbCommand delete = builder.GetDeleteCommand();
        Assert.Contains("\"A\" = ?", delete.CommandText, StringComparison.Ordinal);
        Assert.Contains("\"B\" = ?", delete.CommandText, StringComparison.Ordinal);
        Assert.Equal("b", Scalar(connection, "SELECT V FROM T WHERE A = 1 AND B = 2"));
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
        => new($"Data Source=command_builder_{Guid.NewGuid():N};Storage=Memory");
}
