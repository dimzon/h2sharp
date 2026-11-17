/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 283: a materialized view's delegating surface, reached by creating one through SQL and
 * then asking the descriptor itself about its columns, indexes and underlying table.
 */
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Tables;
using Xunit;

namespace H2Sharp.Tests;

public sealed class MaterializedViewSurfaceTests
{
    [Fact]
    public void AViewDelegatesShapeAndIndexesToItsTable()
    {
        Database database = new(ConnectionInfo.Memory("materialized_view_surface"));
        SessionLocal session = new(database, "sa");
        try
        {
            session.PrepareCommand("CREATE TABLE T(ID INT)").ExecuteUpdate(false);
            session.PrepareCommand("INSERT INTO T VALUES 1, 2").ExecuteUpdate(false);
            session.PrepareCommand("CREATE MATERIALIZED VIEW MV AS SELECT * FROM T").ExecuteUpdate(false);

            MaterializedView view = Assert.IsType<MaterializedView>(
                database.GetSchema("PUBLIC")!.FindTableOrView(session, "MV"));

            Assert.Equal("MV", view.GetTableName());
            Assert.Equal(Table.MATERIALIZED_VIEW, view.GetTableType());
            Assert.True(view.IsView());
            Assert.NotNull(view.GetUnderlyingTable());
            Assert.NotNull(view.GetSelect());
            Assert.Equal(1, view.GetColumnCount());
            Assert.Single(view.GetColumns());
            Assert.Equal("ID", view.GetColumn(0).GetName());
            Assert.Throws<TODOException>(() => view.HasPrimaryKey());
            Assert.NotNull(view.GetScanIndex(session));
            Assert.NotNull(view.GetScanIndex(session, null, null, 0, null, null));
            Assert.NotNull(view.GetBestPlanItem(session, null, null, 0, null, null, false));
            Assert.NotNull(view.GetIndexFor(session, null!));
            Assert.True(view.GetRowCountApproximation(session) >= 0);
            Assert.NotNull(view.GetSql(0));
        }
        finally
        {
            database.Close();
        }
    }
}
