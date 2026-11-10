/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 118: the command builder edges SQL does not reach — the parameter name answer,
 * the row-updating handler attach/detach/swap and its refusal of a foreign adapter, the
 * null schema table of an update result, and the key-column resolution for a metadata
 * view the current schema does not hold.
 */
using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2CommandBuilderEdgeTests
{
    /// <summary>An adapter of another provider: the builder only handles its own.</summary>
    private sealed class FakeAdapter : DbDataAdapter
    {
    }

    private static MethodInfo Protected(string name, params Type[] parameters)
        => typeof(H2CommandBuilder).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(m => m.Name == name
                && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters));

    private static string ParameterName(H2CommandBuilder builder, string name)
        => (string)Protected("GetParameterName", typeof(string)).Invoke(builder, [name])!;

    private static DataTable? SchemaTable(H2CommandBuilder builder, DbCommand command)
        => (DataTable?)Protected("GetSchemaTable", typeof(DbCommand)).Invoke(builder, [command]);

    private static void RowUpdatingHandler(H2CommandBuilder builder, DbDataAdapter adapter)
        => Protected("SetRowUpdatingHandler", typeof(DbDataAdapter)).Invoke(builder, [adapter]);

    [Fact]
    public void ParameterNameAndHandlerEdgesAnswerTheirShapes()
    {
        H2CommandBuilder builder = new();

        // A named parameter keeps its name: the statement binds by position.
        Assert.Equal("anyName", ParameterName(builder, "anyName"));

        // Attach, detach the same one, attach again, swap, and a foreign adapter.
        H2DataAdapter first = new();
        H2DataAdapter second = new();
        RowUpdatingHandler(builder, first);
        RowUpdatingHandler(builder, first);
        RowUpdatingHandler(builder, first);
        RowUpdatingHandler(builder, second);
        RowUpdatingHandler(builder, new FakeAdapter());
    }

    [Fact]
    public void SchemaTableEdges()
    {
        using H2DataSource dataSource = new H2DataSourceBuilder(
            $"Data Source=cb_{Guid.NewGuid():N};Storage=Memory").Build();
        using H2Connection connection = (H2Connection)dataSource.CreateConnection();
        connection.Open();
        using H2Command command = (H2Command)connection.CreateCommand();
        command.CommandText = "CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR)";
        command.ExecuteNonQuery();

        H2CommandBuilder builder = new();

        // An update result has no columns: the reader answers no schema table, and the
        // builder passes the null through rather than asking the engine for keys.
        command.CommandText = "CREATE TABLE U(ID INT PRIMARY KEY)";
        Assert.Null(SchemaTable(builder, command));

        // A metadata view of another schema reports no base schema: the current schema
        // does not hold the table, so no key column is resolved for its columns.
        command.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES";
        DataTable? table = SchemaTable(builder, command);
        Assert.NotNull(table);
        Assert.True(table!.Columns.Contains(SchemaTableColumn.IsKey));
        Assert.True(table.Columns.Contains(SchemaTableOptionalColumn.IsReadOnly));
        Assert.True(table.Columns.Contains(SchemaTableOptionalColumn.IsAutoIncrement));
        // No column resolved: the boolean columns the builder added stay unset (DBNull)
        // for every row of the view, while the engine's own IsAutoIncrement column keeps
        // its value.
        Assert.NotEmpty(table.Rows);
        foreach (DataRow row in table.Rows)
        {
            Assert.True(row[SchemaTableColumn.IsKey] is DBNull);
            Assert.True(row[SchemaTableOptionalColumn.IsReadOnly] is DBNull);
            Assert.False((bool)row[SchemaTableOptionalColumn.IsAutoIncrement]);
        }
    }
}
