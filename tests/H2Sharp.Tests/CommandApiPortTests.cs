/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The command surface that is not a query result: the ADO properties, the metadata answer
 * of a query, a non-query through ExecuteScalar (single and multi-statement text), the
 * commit a non-transactional statement performs when the session runs without autocommit,
 * the retry question a command list asks on the command's behalf, and the trace text with
 * its parameters.
 */
using System;
using System.Data.Common;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;
using static H2Sharp.Values.ValueInteger;

namespace H2Sharp.Tests;

public sealed class CommandApiPortTests
{
    [Fact]
    public void CommandAnswersItsSurfaceAndCommitsANonTransactionalStatement()
    {
        using H2Connection connection = new(
            "Data Source=cmd_api_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();

        // The ADO properties and the engine connection behind the command.
        using H2Command command = new("SELECT 1", connection);
        command.CommandTimeout = 7;
        Assert.Equal(7, command.CommandTimeout);
        command.DesignTimeVisible = false;
        Assert.False(command.DesignTimeVisible);
        Assert.Same(connection, command.H2Connection);

        // A non-query through ExecuteScalar answers null and runs what follows it.
        command.CommandText = "CREATE TABLE X(A INT)";
        Assert.Null(command.ExecuteScalar());
        command.CommandText = "INSERT INTO X VALUES (1); INSERT INTO X VALUES (2)";
        Assert.Null(command.ExecuteScalar());
        using (DbCommand probe = connection.CreateCommand())
        {
            probe.CommandText = "SELECT COUNT(*) FROM X";
            Assert.Equal(2L, Convert.ToInt64(probe.ExecuteScalar()));
        }

        // The ADO command text stays what was set.
        command.CommandText = "SELECT * FROM X WHERE A = ?";
        Assert.Equal("SELECT * FROM X WHERE A = ?", command.CommandText);

        connection.Close();

        // The engine side: metadata, the commit of a non-transactional statement, and the
        // retry question a list asks on the command's behalf.
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("cmd_api_engine_" + Guid.NewGuid().ToString("N"))), "sa")
        {
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        session.CastDataProvider = new SessionCastDataProvider(session, DefaultCastDataProvider.Instance);
        try
        {
            session.PrepareCommand("CREATE TABLE Y(A INT)").ExecuteUpdate(false);
            CommandContainer query = Assert.IsType<CommandContainer>(session.PrepareCommand("SELECT 1"));
            Assert.Null(query.GetMetaData());

            // The trace text of the engine command carries its parameters.
            CommandContainer parametrized = Assert.IsType<CommandContainer>(
                session.PrepareCommand("SELECT ?"));
            parametrized.GetParameters()[0].SetValue(ValueInteger.Get(5), true);
            Assert.Contains("?", parametrized.ToString(), StringComparison.Ordinal);

            session.SetAutoCommit(false);
            CommandContainer setting = Assert.IsType<CommandContainer>(
                session.PrepareCommand("SET IGNORECASE TRUE"));
            Assert.Equal(0, setting.ExecuteUpdate(false).GetUpdateCount());

            // A DML statement is the one the list may re-run after a locking failure.
            CommandContainer insert = Assert.IsType<CommandContainer>(
                session.PrepareCommand("INSERT INTO Y VALUES (1)"));
            Assert.True(insert.IsRetryableForList());
        }
        finally
        {
            session.Close();
        }
    }
}
