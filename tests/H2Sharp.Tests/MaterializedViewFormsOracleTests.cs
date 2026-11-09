/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 99: the materialized view as a table — the delegating members a query reads
 * (columns, scan index, row counts, plan items), COMMENT (after AS), OR REPLACE,
 * REFRESH, the drop SQL, the writes the statement resolves to the underlying table,
 * and the refusals of the view's own writing members.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Api;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Tests.Oracle;
using Xunit;
using H2Index = H2Sharp.Indexes.Index;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class MaterializedViewFormsOracleTests
{
    [JavaOracleFact]
    public void QueriesAndRefreshReadTheMaterializedRows()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE MS(ID INT PRIMARY KEY, VAL INT)",
            "INSERT INTO MS VALUES (1, 10), (2, 20)",
            "CREATE MATERIALIZED VIEW MV AS SELECT ID, VAL FROM MS",
            "SELECT * FROM MV ORDER BY ID",
            "SELECT COUNT(*), SUM(VAL) FROM MV",
            "SELECT MAX(VAL) FROM MV",
            "EXPLAIN SELECT * FROM MV WHERE ID = 1",
            "INSERT INTO MS VALUES (3, 30)",
            "SELECT COUNT(*) FROM MV",
            "REFRESH MATERIALIZED VIEW MV",
            "SELECT COUNT(*), SUM(VAL) FROM MV",
            "DROP MATERIALIZED VIEW MV",
            "DROP TABLE MS",
        ]);
    }

    [JavaOracleFact]
    public void CommentReplaceAndWritesMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE MS(ID INT PRIMARY KEY, VAL INT)",
            "INSERT INTO MS VALUES (1, 10), (2, 20)",
            // The comment follows AS and precedes the query.
            "CREATE MATERIALIZED VIEW M1 AS COMMENT 'one' SELECT ID, VAL FROM MS",
            "SELECT REMARKS FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'M1'",
            "SELECT * FROM M1 ORDER BY ID",
            "CREATE OR REPLACE MATERIALIZED VIEW M1 AS SELECT ID + 1, VAL * 2 FROM MS",
            "SELECT * FROM M1 ORDER BY 1",
            "CREATE MATERIALIZED VIEW IF NOT EXISTS M1 AS SELECT 1",
            "CREATE MATERIALIZED VIEW M1 AS SELECT 1",
            "DROP MATERIALIZED VIEW IF EXISTS M1",
            // The statement resolves the view to its underlying table, so the rows can be
            // written and then truncated.
            "CREATE MATERIALIZED VIEW M2 AS SELECT ID, VAL FROM MS",
            "INSERT INTO M2 VALUES (3, 30)",
            "UPDATE M2 SET VAL = 0",
            "DELETE FROM M2 WHERE ID = 1",
            "TRUNCATE TABLE M2",
            "SELECT COUNT(*) FROM M2",
            "DROP MATERIALIZED VIEW M2",
            "DROP TABLE MS",
        ]);
    }

    /// <summary>
    /// The view object itself, which SQL never hands out: every member delegates to the
    /// underlying table, while the view's own writing members refuse.
    /// </summary>
    [Fact]
    public void TheViewDelegatesToItsUnderlyingTable()
    {
        Database database = new(ConnectionInfo.Memory("mv_direct_" + Guid.NewGuid().ToString("N")));
        SessionLocal session = new(database, "sa");
        Execute(session, "CREATE TABLE MS(ID INT PRIMARY KEY, VAL INT)");
        Execute(session, "INSERT INTO MS VALUES (1, 10), (2, 20)");
        Execute(session, "CREATE MATERIALIZED VIEW MV AS SELECT ID, VAL FROM MS");

        MaterializedView view = Assert.IsType<MaterializedView>(
            database.GetSchema("PUBLIC").FindTableOrView(session, "MV"));

        Assert.Equal(2, view.GetColumnCount());
        Assert.Equal("ID", view.GetColumn(0).GetName());
        Assert.Equal(2, view.GetColumns().Length);
        Assert.NotNull(view.GetScanIndex(session));
        Assert.NotNull(view.GetScanIndex(session, null, null, 0, null, null));
        Assert.True(view.CanGetRowCount(session));
        Assert.Equal(2, view.GetRowCount(session));
        Assert.True(view.GetRowCountApproximation(session) >= 2);
        Assert.True(view.GetMaxDataModificationId() >= 0);
        Assert.True(view.GetIndexes().Length >= 1);
        // The underlying table is created from the query, so it carries no primary key.
        Assert.Null(view.FindPrimaryKey());
        Assert.NotNull(view.GetDropSql());
        _ = view.GetTableType();
        Assert.False(view.IsClosed());

        Row row = view.GetTemplateRow();
        Assert.NotNull(view.GetIndexFor(session, row));
        TableFilter filter = new(session, view, "MV", rightsChecked: true, select: null, 0, null);
        Assert.NotNull(view.GetBestPlanItem(session, null, [filter], 0, null, null, isSelectCommand: true));

        // A secondary index is created on the underlying table and dropped from it.
        H2Index added = view.AddIndex(session, "MV_IDX", database.AllocateObjectId(),
            [new IndexColumn(view.GetColumn(1), ascending: true)], 0,
            IndexType.CreateNonUnique(persistent: false), create: true, indexComment: null);
        Assert.Contains(added, view.GetIndexes());
        view.RemoveIndex(added);

        Assert.Equal(
            ErrorCode.FeatureNotSupported1,
            Assert.Throws<H2SharpException>(() => view.CheckSupportAlter()).H2ErrorCode);
        Assert.Equal(
            ErrorCode.FeatureNotSupported1,
            Assert.Throws<H2SharpException>(() => view.AddRow(session, row)).H2ErrorCode);
        Assert.Equal(
            ErrorCode.FeatureNotSupported1,
            Assert.Throws<H2SharpException>(() => view.UpdateRow(session, row, row)).H2ErrorCode);
        Assert.Equal(
            ErrorCode.FeatureNotSupported1,
            Assert.Throws<H2SharpException>(() => view.RemoveRow(session, 0L)).H2ErrorCode);
        Assert.Equal(
            ErrorCode.FeatureNotSupported1,
            Assert.Throws<H2SharpException>(() => view.Truncate(session)).H2ErrorCode);

        view.Close();
        Assert.True(view.IsClosed());
        database.Close();
    }

    private static void Execute(SessionLocal session, string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        _ = container.ExecuteUpdate(false);
    }
}
