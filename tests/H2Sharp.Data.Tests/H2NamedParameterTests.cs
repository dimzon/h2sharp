/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Named parameters at the ADO.NET layer: "$name" becomes "$N" before the engine parses the
 * statement. "@" and ":" are not parameter prefixes, so H2 variables and JSON_OBJECT's colon
 * keep their meaning.
 */
using System;
using System.Data.Common;
using Dapper;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2NamedParameterTests
{
    [Fact]
    public void NamedParametersAreBoundByName()
    {
        using H2Connection connection = Open();
        Execute(connection, "CREATE TABLE T(ID INT, NAME VARCHAR(20))");

        using DbCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO T VALUES ($id, $name)";
        Add(command, "id", 1);
        Add(command, "name", "a");

        Assert.Equal(1, command.ExecuteNonQuery());
        Assert.Equal(1, connection.QuerySingle<int>("SELECT ID FROM T WHERE NAME = 'a'"));
    }

    [Fact]
    public void ARepeatedNameUsesOneIndex()
    {
        using H2Connection connection = Open();

        Assert.Equal(14, connection.QuerySingle<int>(
            "SELECT $value + $value", new { value = 7 }));
    }

    [Fact]
    public void TheSameNamesMayRepeatManyTimesInOneStatement()
    {
        using H2Connection connection = Open();

        Assert.Equal(15, connection.QuerySingle<int>(
            "SELECT $a + $b + $a + $b + $a + $b + $a + $b + $a + $b",
            new { a = 1, b = 2 }));

        Execute(connection, "CREATE TABLE T(A INT, B INT, C INT)");
        using DbCommand insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO T VALUES ($a, $b, $a), ($b, $a, $b), ($a, $a, $a)";
        Add(insert, "a", 1);
        Add(insert, "b", 2);
        Assert.Equal(3, insert.ExecuteNonQuery());

        Assert.Equal(
            [111, 121, 212],
            [.. connection.Query<int>("SELECT A * 100 + B * 10 + C FROM T ORDER BY A * 100 + B * 10 + C")]);
    }

    [Fact]
    public void ValuesFollowTheNamesNotTheCollectionOrder()
    {
        using H2Connection connection = Open();

        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT $a + $b";
        Add(command, "b", 2);
        Add(command, "a", 1);

        Assert.Equal(3, Convert.ToInt32(command.ExecuteScalar()));
    }

    [Fact]
    public void NamesAreCaseSensitive()
    {
        using H2Connection connection = Open();

        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT $Id * 10 + $id";
        Add(command, "$Id", 1);
        Add(command, "$id", 2);

        Assert.Equal(12, Convert.ToInt32(command.ExecuteScalar()));

        command.CommandText = "SELECT $Id";
        command.Parameters.RemoveAt(0);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => command.ExecuteScalar());
        Assert.Contains("'$Id'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheParameterCollectionIsCaseSensitive()
    {
        using H2Connection connection = Open();
        using DbCommand command = connection.CreateCommand();
        DbParameter upper = command.CreateParameter();
        upper.ParameterName = "$Id";
        DbParameter lower = command.CreateParameter();
        lower.ParameterName = "$id";
        command.Parameters.Add(upper);
        command.Parameters.Add(lower);

        Assert.Equal(0, command.Parameters.IndexOf("Id"));
        Assert.Equal(1, command.Parameters.IndexOf("id"));
        Assert.Equal(-1, command.Parameters.IndexOf("ID"));
        Assert.Same(upper, command.Parameters["Id"]);
        Assert.Same(lower, command.Parameters["id"]);
        Assert.Throws<IndexOutOfRangeException>(() => command.Parameters["ID"]);
    }

    [Fact]
    public void AMissingNameFails()
    {
        using H2Connection connection = Open();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Execute(connection, "SELECT $missing"));
        Assert.Contains("$missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABareDollarFails()
    {
        using H2Connection connection = Open();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Execute(connection, "SELECT $"));
        Assert.Contains("name or an index", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NamedAndPositionalParametersCannotBeMixed()
    {
        using H2Connection connection = Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT $id + ?";
        Add(command, "id", 1);
        Add(command, string.Empty, 2);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => command.ExecuteScalar());
        Assert.Contains("cannot be mixed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExplicitIndexIsStillPositional()
    {
        using H2Connection connection = Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT $1";
        Add(command, string.Empty, 7);

        Assert.Equal(7, Convert.ToInt32(command.ExecuteScalar()));
    }

    [Fact]
    public void DapperSeesDollarNames()
    {
        using H2Connection connection = Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT $Id", new { Id = 1 }));
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => connection.QuerySingle<int>("SELECT $id", new { Id = 1 }));
        Assert.Contains("'$id'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DapperListExpansionUsesDollarNames()
    {
        using H2Connection connection = Open();
        Execute(connection, "CREATE TABLE T(ID INT)");
        Execute(connection, "INSERT INTO T VALUES (1), (2), (3)");

        Assert.Equal(
            [1, 2],
            [.. connection.Query<int>("SELECT ID FROM T WHERE ID IN $Ids ORDER BY ID",
                new { Ids = new[] { 1, 2 } })]);
    }

    [Fact]
    public void DynamicParametersWithDollarPrefixIsFound()
    {
        using H2Connection connection = Open();
        DynamicParameters parameters = new();
        parameters.Add("$Id", 5);

        Assert.Equal(5, connection.QuerySingle<int>("SELECT $Id", parameters));
    }

    [Fact]
    public void AtIsAnH2VariableNotAParameter()
    {
        using H2Connection connection = Open();
        Execute(connection, "SET @v = 10");

        Assert.Equal(15, connection.QuerySingle<int>("SELECT @v + $x", new { x = 5 }));
    }

    [Fact]
    public void JsonObjectColonIsNotAParameter()
    {
        using H2Connection connection = Open();

        string json = connection.QuerySingle<string>(
            "SELECT CAST(JSON_OBJECT('value': $id) AS VARCHAR)", new { id = 1 });
        Assert.Equal("{\"value\":1}", json);
    }

    [Fact]
    public void DollarInsideLiteralsIdentifiersAndCommentsIsNotAParameter()
    {
        using H2Connection connection = Open();
        Execute(connection, "CREATE TABLE T(a$b INT)");
        Execute(connection, "INSERT INTO T VALUES (1)");

        Assert.Equal(
            "$missing,1",
            connection.QuerySingle<string>(
                "SELECT '$missing' || ',' || CAST($id AS VARCHAR)", new { id = 1 }));
        Assert.Equal(
            " $missing ",
            connection.QuerySingle<string>("SELECT $$ $missing $$"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT a$b FROM T"));
        Assert.Equal(
            2,
            connection.QuerySingle<int>(
                "SELECT 1 -- $missing\n + $id", new { id = 1 }));
        Assert.Equal(
            2,
            connection.QuerySingle<int>(
                "SELECT 1 /* $missing /* nested */ $missing */ + $id", new { id = 1 }));
        Assert.Equal(
            2,
            connection.QuerySingle<int>(
                "SELECT 1 // $missing\n + $id", new { id = 1 }));
    }

    [Fact]
    public void ABatchBindsEachStatement()
    {
        using H2Connection connection = Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT $a; SELECT $b";
        Add(command, "a", 1);
        Add(command, "b", 2);

        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.True(reader.NextResult());
        Assert.True(reader.Read());
        Assert.Equal(2, reader.GetInt32(0));
    }

    [Fact]
    public void ADbNullIsAValueAndANullIsNot()
    {
        using H2Connection connection = Open();

        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT $v IS NULL";
        Add(command, "v", DBNull.Value);
        Assert.True(Convert.ToBoolean(command.ExecuteScalar()));

        command.Parameters.Clear();
        DbParameter missing = command.CreateParameter();
        missing.ParameterName = "v";
        missing.Value = null;
        command.Parameters.Add(missing);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => command.ExecuteScalar());
        Assert.Contains("use DBNull.Value", error.Message, StringComparison.Ordinal);
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        DbParameter parameter = command.CreateParameter();
        if (name.Length > 0)
        {
            parameter.ParameterName = name;
        }

        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        _ = command.ExecuteNonQuery();
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("jdbc:h2:mem:named_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        return connection;
    }
}
