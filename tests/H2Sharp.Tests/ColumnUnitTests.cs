/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct unit coverage of Tables.Column: the static column-list writer with its separator
 * and suffix, renames, the declared precision/scale accessors, the _ROWID_ flag, the
 * diagnostics ToString, and the conversion error that keeps the engine's message.
 */
using System;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ColumnUnitTests
{
    private static Column NumericColumn() =>
        new("N", TypeInfo.GetTypeInfo(Value.Numeric, 10, 2, null));

    [Fact]
    public void WriteColumnsAppendsSeparatorAndSuffix()
    {
        Column a = new("A", TypeInfo.TypeInteger);
        Column b = new("B", TypeInfo.TypeInteger);
        StringBuilder builder = Column.WriteColumns(
            new StringBuilder(), new[] { a, b }, ", ", " NOT NULL", HasSql.DefaultSqlFlags);
        Assert.Equal("\"A\" NOT NULL, \"B\" NOT NULL", builder.ToString());
    }

    [Fact]
    public void RenameChangesTheName()
    {
        Column column = new("OLD", TypeInfo.TypeInteger);
        column.Rename("NEW");
        Assert.Equal("NEW", column.GetName());

        column.Rename(new StringBuilder("NEWER"));
        Assert.Equal("NEWER", column.GetName());
    }

    [Fact]
    public void DeclaredPrecisionScaleAndTypeNameComeFromTheType()
    {
        Column column = NumericColumn();
        Assert.Equal(10, column.GetPrecision());
        Assert.Equal(2, column.GetScale());
        Assert.Contains("NUMERIC(10, 2)", column.GetTypeName());
        Assert.Equal(Value.Numeric, column.GetValueType());
    }

    [Fact]
    public void RowIdFlagAnswersTheColumn()
    {
        Column rowId = new("X", TypeInfo.TypeInteger);
        rowId.SetRowId(true);
        Assert.True(rowId.IsRowId());
        Assert.False(new Column("X", TypeInfo.TypeInteger).IsRowId());
    }

    [Fact]
    public void HashCodeOfTablelessColumnIsZero()
    {
        Column column = new("X", TypeInfo.TypeInteger);
        Assert.Equal(0, column.GetHashCode());
        Assert.False(column.Equals("not a column"));
        Assert.False(column.Equals(NumericColumn()));
        Assert.Equal("X:0:" + column.GetSql(HasSql.DefaultSqlFlags), column.ToString());
    }

    [Fact]
    public void ConvertWrapsTheEngineConversionError()
    {
        Column column = new("I", TypeInfo.TypeInteger);
        H2SharpException exception = Assert.Throws<H2SharpException>(
            () => column.Convert(null, ValueVarchar.Get("abc")));
        Assert.Equal(Api.ErrorCode.DataConversionError1, exception.H2ErrorCode);
    }
}
