/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The table base class as the statements use it but no statement shows: the index-column
 * surface, the column lookups and the implicit INSERT column list, the children of a table,
 * the dependency walk, the index-for-column choice, the NULL row and the foreign-key switch.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp;
using H2Sharp.Api;
using H2Sharp.Command;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class TableBaseSurfaceTests
{
    private static SessionLocal NewSession(string name)
        => new(new Database(ConnectionInfo.Memory(name + "_" + Guid.NewGuid().ToString("N"))), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };

    private static void Execute(SessionLocal session, string sql)
        => Assert.IsType<CommandContainer>(session.PrepareCommand(sql)).GetPreparedCommand().Update();

    private static Table GetTable(SessionLocal session, string name)
        => session.GetDatabase()!.GetSchema("PUBLIC")!.GetTableOrView(session, name)!;

    [Fact]
    public void IndexColumnAnswersItsOrderAndSql()
    {
        IndexColumn ascending = new("A", SortOrder.Ascending);
        Assert.True(ascending.IsAscending());
        Assert.Equal(SortOrder.Ascending, ascending.GetSortOrder());
        Assert.Equal("\"A\"", ascending.GetSql(new StringBuilder(), 0).ToString());
        Assert.Equal("A", ascending.GetName());

        // A name-only column prints its name; it has no column object yet, which is where a
        // CREATE INDEX's key holds it before the statement maps it onto the table.
        IndexColumn descending = new("B", SortOrder.Descending | SortOrder.NullsFirst);
        Assert.False(descending.IsAscending());
        Assert.Equal(SortOrder.Descending, descending.GetSortOrder());
        Assert.Equal("\"B\" DESC NULLS FIRST", descending.GetSql(new StringBuilder(), 0).ToString());
    }

    [Fact]
    public void ColumnLookupsAndTheImplicitInsertListAnswer()
    {
        SessionLocal session = NewSession("table_surface");
        Execute(session, "CREATE TABLE T(ID INT, V INT, H INT INVISIBLE)");
        Table table = GetTable(session, "T");

        Assert.NotNull(table.FindColumn("ID"));
        Assert.Null(table.FindColumn("MISSING"));
        Assert.Null(table.GetColumn("MISSING", ifExists: true));
        H2SharpException missing = Assert.Throws<H2SharpException>(
            () => table.GetColumn("MISSING", ifExists: false));
        Assert.Equal(ErrorCode.ColumnNotFound1, missing.H2ErrorCode);

        // INVISIBLE keeps the column out of the implicit INSERT list, but a name still finds it.
        Column[] visible = table.GetVisibleColumns();
        Assert.Equal(2, visible.Length);
        Assert.Equal("ID", visible[0].GetName());
        Assert.NotNull(table.FindColumn("H"));
        Assert.Equal(SearchRow.RowidIndex, table.GetMainIndexColumn());
        Assert.False(table.IsView());
        Assert.True(table.IsDeterministic());
        Assert.Contains("T", table.GetTraceSql(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheNullRowIsMadeOnceAndUnderstoodByTheTable()
    {
        SessionLocal session = NewSession("null_row");
        Execute(session, "CREATE TABLE T(ID INT, V INT)");
        Table table = GetTable(session, "T");

        Row first = table.GetNullRow();
        Row second = table.GetNullRow();
        Assert.Same(first, second);
        Assert.Equal(2, first.GetColumnCount());
        Assert.Same(ValueNull.Instance, first.GetValue(0));

        session.Close();
        session.GetDatabase()!.Close();
    }

    [Fact]
    public void ReadOnlyDatabaseRefusesTheTableWrite()
    {
        SessionLocal session = NewSession("table_readonly");
        Execute(session, "CREATE TABLE T(ID INT)");
        session.GetDatabase()!.SetReadOnly(true);
        H2SharpException refused = Assert.Throws<H2SharpException>(
            () => Execute(session, "INSERT INTO T VALUES (1)"));
        Assert.Equal(ErrorCode.DatabaseIsReadOnly, refused.H2ErrorCode);
        session.GetDatabase()!.SetReadOnly(false);
    }

    [Fact]
    public void ChildrenDependenciesAndTheIndexForAColumn()
    {
        SessionLocal session = NewSession("children");
        Execute(session, "CREATE TABLE T(ID INT GENERATED ALWAYS AS IDENTITY, V INT, W INT, "
            + "CONSTRAINT U UNIQUE(V))");
        Execute(session, "CREATE INDEX I2 ON T(V)");
        Table table = GetTable(session, "T");

        List<DbObject> children = table.GetChildren();
        Assert.Contains(children, child => child is Schemas.Sequence);
        Assert.Contains(children, child => child is Constraints.ConstraintUnique);
        Assert.Contains(children, child => child is Indexes.Index);

        HashSet<object> dependencies = [];
        table.AddDependencies(dependencies);
        Assert.Contains(table, dependencies);
        Assert.Contains(dependencies, dependency => dependency is Schemas.Sequence);

        Column v = table.GetColumn("V");
        Indexes.Index? byColumn = table.GetIndexForColumn(v, needGetFirstOrLast: false, needFindNext: false);
        Assert.NotNull(byColumn);
        Assert.Same(v, byColumn!.GetColumns()[0].GetColumn());
        Assert.NotNull(table.GetIndexForColumn(v, needGetFirstOrLast: true, needFindNext: false));
        Assert.NotNull(table.GetIndexForColumn(v, needGetFirstOrLast: false, needFindNext: true));
        // A column no index starts with has no index that could serve it.
        Assert.Null(table.GetIndexForColumn(table.GetColumn("W"), false, false));
    }

    [Fact]
    public void RenamingAColumnChecksForTheDuplicateFirst()
    {
        SessionLocal session = NewSession("rename_column");
        Execute(session, "CREATE TABLE T(A INT, B INT)");
        Table table = GetTable(session, "T");

        table.RenameColumn(table.GetColumn("B"), "C");
        Assert.NotNull(table.FindColumn("C"));
        Assert.Null(table.FindColumn("B"));

        H2SharpException duplicate = Assert.Throws<H2SharpException>(
            () => table.RenameColumn(table.GetColumn("A"), "C"));
        Assert.Equal(ErrorCode.DuplicateColumnName1, duplicate.H2ErrorCode);
        Assert.NotNull(table.FindColumn("A"));
        Assert.NotNull(table.GetCreateSqlForMeta());
        Assert.Equal("PUBLIC", table.GetSchemaName());
    }

    [Fact]
    public void TheBaseTableAnswersItsDefaults()
    {
        SessionLocal session = NewSession("dual_defaults");
        DualTable dual = new(session.GetDatabase()!);

        // The base table has no rows of its own to identify, no space to report and nothing
        // to lock; DUAL is the table that really answers these defaults.
        Assert.Null(dual.GetRowIdColumn());
        Assert.Equal(0L, dual.GetDiskSpaceUsed(false, false));
        Assert.Equal(0L, dual.GetDiskSpaceUsed(true, true));
        Assert.False(dual.IsRowLockable());
        Assert.False(dual.Lock(session, Table.ReadLock));
        dual.Unlock(session);
        Assert.False(dual.IsLockedExclusivelyBy(session));
        Assert.Null(dual.GetSchemaName());
        Assert.Null(dual.FindColumn("ANYTHING"));

        H2SharpException unsupported = Assert.Throws<H2SharpException>(
            () => dual.LockRow(session, null!, 0));
        Assert.Equal(ErrorCode.FeatureNotSupported1, unsupported.H2ErrorCode);
        H2SharpException noAlter = Assert.Throws<H2SharpException>(dual.CheckSupportAlter);
        Assert.Equal(ErrorCode.FeatureNotSupported1, noAlter.H2ErrorCode);
    }

    [Fact]
    public void TheStaticInCheckRefusesWhatTheIndexCannotServe()
    {
        SessionLocal session = NewSession("in_check");
        Execute(session, "CREATE TABLE T(A INT, B INT)");
        Execute(session, "CREATE INDEX IAB ON T(A, B)");
        Table table = GetTable(session, "T");
        Indexes.Index index = Array.Find(table.GetIndexes(), i => i.GetName() == "IAB")!;

        Column a = table.GetColumn("A");
        Column b = table.GetColumn("B");
        Assert.True(IndexCursor.CanUseIndexForIn(index, [a, b]));
        Assert.False(IndexCursor.CanUseIndexForIn(index, [b, a]));
        Assert.False(IndexCursor.CanUseIndexForIn(index, [a]));
        Assert.False(IndexCursor.CanUseIndexForIn(index, null));
        Assert.False(IndexCursor.CanUseIndexForIn(null, [a, b]));

        // A cursor that was never prepared has no bounds and never is always false.
        IndexCursor cursor = new();
        Assert.Null(cursor.GetStart());
        Assert.Null(cursor.GetEnd());
        Assert.False(cursor.IsAlwaysFalse());
    }

    [Fact]
    public void TheForeignKeySwitchRoundTrips()
    {
        SessionLocal session = NewSession("fk_switch");
        Execute(session, "CREATE TABLE P(ID INT PRIMARY KEY)");
        Execute(session, "CREATE TABLE C(ID INT, P_ID INT, CONSTRAINT FK FOREIGN KEY(P_ID) REFERENCES P(ID))");
        Table child = GetTable(session, "C");
        Assert.True(child.GetCheckForeignKeyConstraints());

        child.SetCheckForeignKeyConstraints(session, enabled: false, checkExisting: false);
        Assert.False(child.GetCheckForeignKeyConstraints());
        child.SetCheckForeignKeyConstraints(session, enabled: true, checkExisting: false);
        Assert.True(child.GetCheckForeignKeyConstraints());

        // An orphan written while the checks were off makes the CHECK turn fail.
        child.SetCheckForeignKeyConstraints(session, enabled: false, checkExisting: false);
        Execute(session, "INSERT INTO C VALUES (1, 99)");
        H2SharpException orphan = Assert.Throws<H2SharpException>(
            () => child.SetCheckForeignKeyConstraints(session, enabled: true, checkExisting: true));
        Assert.Equal(ErrorCode.ReferentialIntegrityViolatedParentMissing1, orphan.H2ErrorCode);
        Assert.False(child.GetCheckForeignKeyConstraints());
    }
}
