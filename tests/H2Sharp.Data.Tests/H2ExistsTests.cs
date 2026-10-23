/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
using System;
using System.Data.Common;
using Dapper;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2ExistsTests
{
    [Fact]
    public void Issue27TopicsWithAnswersAndWithoutAnswersAreSelected()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE \"Topics\"(\"Id\" INT PRIMARY KEY)");
        connection.Execute("CREATE TABLE \"Posts\"(\"Id\" INT PRIMARY KEY, \"TopicId\" INT, \"Approved\" BOOLEAN)");
        connection.Execute("INSERT INTO \"Topics\" VALUES (1), (2), (3), (4)");
        connection.Execute("INSERT INTO \"Posts\" VALUES (1, 1, TRUE), (2, 1, FALSE), (3, 2, TRUE), (4, NULL, TRUE), (5, 4, NULL)");
        Assert.Equal(new[] { 1, 2, 4 }, connection.Query<int>(
            "SELECT \"t\".\"Id\" FROM \"Topics\" \"t\" WHERE EXISTS "
            + "(SELECT 1 FROM \"Posts\" \"p\" WHERE \"p\".\"TopicId\" = \"t\".\"Id\") ORDER BY \"t\".\"Id\""));
        Assert.Equal(new[] { 3 }, connection.Query<int>(
            "SELECT \"t\".\"Id\" FROM \"Topics\" AS \"t\" WHERE NOT EXISTS "
            + "(SELECT 1 FROM \"Posts\" AS \"p\" WHERE \"p\".\"TopicId\" = \"t\".\"Id\") ORDER BY \"t\".\"Id\""));
        // All uses NOT EXISTS over rows violating the predicate; the empty set is true.
        Assert.Equal(new[] { 2, 3, 4 }, connection.Query<int>(
            "SELECT \"t\".\"Id\" FROM \"Topics\" \"t\" WHERE NOT EXISTS "
            + "(SELECT 1 FROM \"Posts\" \"p\" WHERE \"p\".\"TopicId\" = \"t\".\"Id\" AND NOT \"p\".\"Approved\") ORDER BY \"t\".\"Id\""));
    }

    [Fact]
    public void BoundExistsReflectsNewValuesAndDataChanges()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY)");
        connection.Execute("INSERT INTO T VALUES (1), (2)");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM T WHERE ID = @id)";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = 1;
        command.Parameters.Add(parameter);
        command.Prepare();
        Assert.True(Assert.IsType<bool>(command.ExecuteScalar()));
        parameter.Value = 3;
        Assert.False(Assert.IsType<bool>(command.ExecuteScalar()));
        connection.Execute("INSERT INTO T VALUES (3)");
        Assert.True(Assert.IsType<bool>(command.ExecuteScalar()));
        connection.Execute("DELETE FROM T WHERE ID = 3");
        Assert.False(Assert.IsType<bool>(command.ExecuteScalar()));
        parameter.Value = DBNull.Value;
        Assert.False(Assert.IsType<bool>(command.ExecuteScalar()));
    }

    [Fact]
    public void ExistenceDependsOnRowsRatherThanColumnValues()
    {
        using H2Connection connection = Open();
        Assert.True(connection.QuerySingle<bool>("SELECT EXISTS (SELECT NULL)"));
        Assert.False(connection.QuerySingle<bool>("SELECT EXISTS (SELECT 1 WHERE FALSE)"));
        Assert.True(connection.QuerySingle<bool>("SELECT NOT EXISTS (SELECT 1 WHERE FALSE)"));
        Assert.False(connection.QuerySingle<bool>("SELECT EXISTS (SELECT 1 LIMIT 0)"));
        Assert.True(connection.QuerySingle<bool>("SELECT EXISTS (VALUES (NULL))"));
        Assert.True(connection.QuerySingle<bool>("SELECT EXISTS (SELECT 1 FROM UNNEST(ARRAY[1, 2]) a WHERE a.C1 = 2)"));
    }

    [Fact]
    public void PatternSetsAreAskedThroughExists()
    {
        using H2Connection connection = Open();
        DynamicParameters parameters = new();
        parameters.Add("patterns", new[] { "a%", "b%" }, System.Data.DbType.Object);
        Assert.True(connection.QuerySingle<bool>(
            "SELECT EXISTS (SELECT 1 FROM UNNEST(@patterns) p WHERE 'abc' LIKE p.C1)", parameters));
        parameters = new DynamicParameters();
        parameters.Add("patterns", new[] { "a%", "%c" }, System.Data.DbType.Object);
        Assert.True(connection.QuerySingle<bool>(
            "SELECT NOT EXISTS (SELECT 1 FROM UNNEST(@patterns) p WHERE NOT ('abc' LIKE p.C1))", parameters));
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("jdbc:h2:mem:exists_ado_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        return connection;
    }
}
