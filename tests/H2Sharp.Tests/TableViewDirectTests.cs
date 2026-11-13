/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of TableView: replace with recompile failure, column templates.
 */
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using Xunit;

namespace H2Sharp.Tests;

public sealed class TableViewDirectTests
{
    private readonly SessionLocal session;
    private readonly Database database;

    public TableViewDirectTests()
    {
        database = new Database(
            ConnectionInfo.Memory("tableview_" + Guid.NewGuid().ToString("N")));
        session = new SessionLocal(database, "sa");
    }

    private CommandContainer Prepare(string sql)
        => Assert.IsType<CommandContainer>(session.PrepareCommand(sql));

    private void Execute(string sql)
    {
        CommandContainer container = Prepare(sql);
        _ = container.ExecuteUpdate(false);
    }

    private string Scalar(string sql)
    {
        CommandContainer container = Prepare(sql);
        using ResultInterface result = container.ExecuteQuery(10, 0, false);
        Assert.True(result.Next());
        return result.CurrentRow()![0]!.GetString()!;
    }

    [Fact]
    public void ReplaceViewWithInvalidQueryRestoresOldQuery()
    {
        // Replace a view with invalid SQL → old query is restored (строки 69-71)
        Execute("CREATE TABLE t(a INT)");
        Execute("INSERT INTO t VALUES (1)");
        Execute("CREATE VIEW v AS SELECT a FROM t");

        // Replace with invalid SQL → the old view should still work
        Assert.Throws<H2SharpException>(() =>
            Execute("CREATE OR REPLACE VIEW v AS SELECT b FROM t"));

        // The old view still works with old query
        Assert.Equal("1", Scalar("SELECT a FROM v"));
    }

    [Fact]
    public void ViewWithColumnTemplates()
    {
        // Column templates are written in CREATE SQL (строки 166-169)
        Execute("CREATE TABLE t(a INT, b INT)");
        Execute("INSERT INTO t VALUES (1, 2)");

        // CREATE VIEW with column aliases uses columnTemplates path
        Execute("CREATE VIEW v2(x, y) AS SELECT a, b FROM t");

        string def = Scalar("SELECT VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME='V2'");
        Assert.Contains("A", def);
        Assert.Contains("B", def);
    }
}
