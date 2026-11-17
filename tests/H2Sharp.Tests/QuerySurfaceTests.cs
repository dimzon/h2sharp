/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 285: the query base class's own getters and plan rendering, reached through a prepared
 * SELECT with ORDER BY and LIMIT rather than through ExecuteReader.
 */
using System;
using H2Sharp.Command;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using Xunit;

namespace H2Sharp.Tests;

public sealed class QuerySurfaceTests
{
    [Fact]
    public void APreparedQueryAnswersItsOwnPlanFacts()
    {
        Database database = new(ConnectionInfo.Memory("query_surface"));
        SessionLocal session = new(database, "sa");
        try
        {
            Query query = Assert.IsAssignableFrom<Query>(Assert.IsType<CommandContainer>(
                session.PrepareCommand("SELECT 1 AS A ORDER BY 1 LIMIT 2")).GetPreparedCommand());

            Assert.Null(query.GetForUpdate());
            Assert.Equal(-1, query.GetOrderByIndex());
            Assert.Equal(-1, query.GetLimitOffset());
            Assert.False(query.IsFetchPercent());
            Assert.Contains("ORDER BY", query.GetPlanSql(0), StringComparison.Ordinal);
        }
        finally
        {
            database.Close();
        }
    }
}
