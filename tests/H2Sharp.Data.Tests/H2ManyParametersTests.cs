/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A statement that names many parameters, seen through ADO.NET.
 *
 * The reference marks the parameters a statement uses in a java.util.BitSet, which grows as
 * the parameters are marked. The port marked them in a list of eight bits, so a statement
 * with more than eight parameters failed — the case of a provider that saves three rows of
 * five columns each.
 */
using System;
using System.Data.Common;
using System.Linq;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2ManyParametersTests
{
    [Fact]
    public void TheStatementMayNameManyParameters()
    {
        using H2Connection connection = new("Data Source=ado_many_parameters;Storage=Memory");
        connection.Open();

        Execute(connection, "CREATE TABLE T(A INT, B INT, C INT, D INT, E INT)");

        using DbCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO T VALUES ($a, $b, $c, $d, $e), ($a, $b, $c, $d, $e), ($a, $b, $c, $d, $e)";
        foreach (string name in new[] { "a", "b", "c", "d", "e" })
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "$" + name;
            parameter.Value = 1;
            command.Parameters.Add(parameter);
        }

        Assert.Equal(3, command.ExecuteNonQuery());
    }

    /// <summary>
    /// Opens a connection to a database of its own.
    /// </summary>
    /// <param name="name">the name of the database</param>
    /// <returns>the connection</returns>
    private static H2Connection Open(string name)
    {
        H2Connection connection = new($"Data Source=ado_{name};Storage=Memory");
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Runs a statement that answers nothing.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the statement</param>
    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        _ = command.ExecuteNonQuery();
    }
}
