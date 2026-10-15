/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The rules a column enforces on a value.
 *
 * The corpus is about *order*. A missing value in a NOT NULL column has to be refused as a
 * missing value and not as a conversion failure of NULL, and the only way to see which happened is
 * the error code: 23502 (no value) and 22018 (conversion) are different problems, and a caller
 * that branches on them would branch wrongly. So the test reads the code, not just "it threw".
 *
 * The second rule is identity: a column is the same column after it is widened, because that is
 * what makes an ALTER TABLE an alteration of the column a lookup would find. Hashing the type
 * would make the second one a different object, and a schema that dropped a view and a table would
 * quietly lose one of them.
 */
using System;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ColumnTests
{
    [Fact]
    public void AMissingValueInANotNullColumnIsRefusedAsAMissingValue()
    {
        Column column = new("NAME", TypeInfo.TypeVarchar, null, 0);
        column.SetNullable(false);

        H2SharpException ex = Assert.Throws<H2SharpException>(
            () => column.ValidateConvertUpdateSequence(Session(), null, null));

        // NULL_NOT_ALLOWED, and not DATA_CONVERSION_ERROR: the value never got as far as being
        // converted, and saying otherwise sends the caller looking at the wrong column.
        Assert.Equal(ErrorCode.NullNotAllowed, ex.H2ErrorCode);
    }

    [Fact]
    public void AValueIsConvertedToTheColumnsType()
    {
        Column column = new("AGE", TypeInfo.TypeBigint, null, 0);

        Value converted = column.ValidateConvertUpdateSequence(Session(), ValueInteger.Get(42), null);

        Assert.Equal(Value.Bigint, converted.GetValueType());
        Assert.Equal(42L, converted.GetLong());
    }

    [Fact]
    public void AColumnIsTheSameColumnAfterItIsWidened()
    {
        Column narrow = new("NAME", TypeInfo.TypeChar, null, 0);
        Column wide = new("NAME", TypeInfo.TypeVarchar, null, 0);

        // Same name, no table: two columns without a table are never the same column, which is
        // the reference's rule and the reason a schema does not merge them by name alone.
        Assert.False(narrow.Equals(wide));

        TableFake table = new();
        Column a = new("NAME", TypeInfo.TypeChar, table, 0);
        Column b = new("NAME", TypeInfo.TypeVarchar, table, 0);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void AColumnPrintsItsNameAndType()
    {
        Column column = new("NAME", TypeInfo.TypeInteger, null, 0);

        Assert.Equal("\"NAME\"", column.GetSql(0));
        Assert.Equal(Value.Integer, column.GetValueType());
        // A column name is an identifier, and an identifier is printed quoted — which is the
        // one thing the expectation gets wrong most often, because the name without quotes looks
        // right until a reserved word or a case difference appears.
        Assert.Equal("NAME", column.GetName());
        Assert.Equal(Column.Nullable, column.IsNullable());
        Assert.False(column.IsPrimaryKey());
    }

    [Fact]
    public void TheRowIdColumnIsNeverQuoted()
    {
        Column column = new(Column.RowId, TypeInfo.TypeBigint, null, 0);
        column.SetRowId(true);

        Assert.Equal(Column.RowId, column.GetSql(0));
    }

    /// <summary>
    /// A real session on an in-memory database. The rules under test are about values; a session is
    /// only passed in because the reference's signature takes one, and a value with no default
    /// never asks it anything.
    /// </summary>
    private static SessionLocal Session()
        => new(new Database(new ConnectionInfo("jdbc:h2:mem:column_tests")), "sa");

    /// <summary>A table that exists only so two columns can share one.</summary>
    private sealed class TableFake : Table
    {
        public override int GetId() => 7;

        public override string GetTableName() => "T";

        /// <summary>
        /// A fake has no storage, so it has no scan index either: null is the honest answer,
        /// and a caller that scans a fake was already mistaken about what it holds.
        /// </summary>
        public override H2Sharp.Indexes.Index? GetScanIndex(SessionLocal session) => null;

        public override int GetTableType() => TABLE;

        public override int GetColumnCount() => 1;

        public override Column GetColumn(int index)
            => new("NAME", TypeInfo.TypeChar, this, index);

        public override Column[] GetColumns() => [GetColumn(0)];

        public override H2Sharp.Indexes.Index? GetPrimaryKey() => null;

        public override H2Sharp.Indexes.Index? GetIndexFor(SessionLocal session, H2Sharp.Result.SearchRow row) => null;

        public override long GetRowCountApproximation() => 0;

        public override bool CanGetRowCount(SessionLocal session) => false;

        public override long GetRowCount(SessionLocal session) => throw new TODOException("TableFake");

        public override string GetCreateSql() => "CREATE TABLE T(NAME CHAR)";

        public override string GetSql(int sqlFlags) => "T";

        public override System.Text.StringBuilder GetSql(System.Text.StringBuilder builder, int sqlFlags)
            => builder.Append("T");

        public override void Truncate(SessionLocal session) => throw new TODOException("TableFake");

        public override void RemoveChildrenAndResources(SessionLocal session) => throw new TODOException("TableFake");

        public override void Close()
        {
        }

        public override bool IsClosed() => false;
    }
}
