/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The table surface of an INFORMATION_SCHEMA view, answered without a query.
 */
using System.Text;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Util;
using H2Sharp.Tables;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The overrides an information schema table answers on its own: counts, columns, the
/// single scan index, the modification id, and the refusals. A query walks some of them
/// through the planner; these checks call them directly, so a change to any of the answers
/// is visible without depending on which planner road a statement happens to take.
/// </summary>
public sealed class InformationSchemaTableSurfaceTests
{
    private static InformationSchemaTable GetTable(SessionLocal session, string name)
        => (InformationSchemaTable)session.GetDatabase()!
            .GetSchema("INFORMATION_SCHEMA")!.GetTableOrView(session, name);

    [Fact]
    public void MetaTableAnswersItsOwnSurface()
    {
        SessionLocal session = new(new Database(ConnectionInfo.Memory("infoschema_surface")), "sa");
        InformationSchemaTable table = GetTable(session, "TABLES");

        Assert.Equal(0, table.GetId());
        Assert.Equal(Table.SYSTEM_TABLE, table.GetTableType());
        Assert.Equal(table.GetColumns().Length, table.GetColumnCount());
        Assert.Same(table.GetColumns()[0], table.GetColumn(0));
        Assert.Same(table.GetScanIndex(session), table.GetIndexFor(session, null!));
        Assert.Single(table.GetIndexes());
        Assert.Same(table.GetScanIndex(session), table.GetIndexes()[0]);
        Assert.Equal(InformationSchemaTable.RowCountApproximation, table.GetRowCountApproximation(session));
        Assert.False(table.CanGetRowCount(session));

        // The row count is refused: the metadata is generated on read, and a count would
        // have to materialise the whole view. The reference throws an internal error for
        // the same reason, and the port's GetInternalError answers a plain Exception.
        Assert.Throws<Exception>(() => table.GetRowCount(session));

        Assert.Null(table.GetCreateSql());
        Assert.Equal("TABLES", table.GetSql(HasSql.DefaultSqlFlags));
        Assert.Equal("TABLES", table.GetSql(new StringBuilder(), HasSql.DefaultSqlFlags).ToString());
        Assert.Same(table.GetSchema(), session.GetDatabase()!.GetSchema("INFORMATION_SCHEMA"));
        Assert.False(table.IsClosed());

        // The no-ops the table contract still demands. TRUNCATE is a refusal in the reference:
        // MetaTable.truncate throws FEATURE_NOT_SUPPORTED for "META".
        H2SharpException truncate = Assert.Throws<H2SharpException>(() => table.Truncate(session));
        Assert.Equal(H2Sharp.Api.ErrorCode.FeatureNotSupported1, truncate.H2ErrorCode);
        table.RemoveChildrenAndResources(session);
        table.Close();
        Assert.False(table.IsClosed());
    }

    [Fact]
    public void VolatileViewsReportTheMaximumModificationId()
    {
        SessionLocal session = new(new Database(ConnectionInfo.Memory("infoschema_modid")), "sa");

        // Settings, Sequences, InDoubt, Sessions, Locks and SessionState change without a
        // data modification; the rest follow the database's own id.
        Assert.Equal(long.MaxValue, GetTable(session, "SETTINGS").GetMaxDataModificationId());
        Assert.Equal(long.MaxValue, GetTable(session, "SEQUENCES").GetMaxDataModificationId());
        Assert.Equal(long.MaxValue, GetTable(session, "SESSIONS").GetMaxDataModificationId());
        Assert.Equal(long.MaxValue, GetTable(session, "LOCKS").GetMaxDataModificationId());
        Assert.Equal(
            session.GetDatabase()!.GetModificationDataId(),
            GetTable(session, "TABLES").GetMaxDataModificationId());
    }
}
