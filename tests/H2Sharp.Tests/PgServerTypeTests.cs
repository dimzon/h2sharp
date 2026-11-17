/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 241: the type-code mapping of the PostgreSQL protocol server.
 */
using H2Sharp.Server.Pg;
using Xunit;

namespace H2Sharp.Tests;

public sealed class PgServerTypeTests
{
    [Theory]
    [InlineData(0, "-")]
    [InlineData(PgServer.PgTypeBool, "BOOLEAN")]
    [InlineData(PgServer.PgTypeBytea, "BINARY VARYING")]
    [InlineData(18, "char")]
    [InlineData(19, "name")]
    [InlineData(PgServer.PgTypeInt8, "BIGINT")]
    [InlineData(PgServer.PgTypeInt2, "SMALLINT")]
    [InlineData(22, "int2vector")]
    [InlineData(PgServer.PgTypeInt4, "INTEGER")]
    [InlineData(24, "regproc")]
    [InlineData(PgServer.PgTypeText, "CHARACTER LARGE OBJECT")]
    [InlineData(PgServer.PgTypeFloat4, "REAL")]
    [InlineData(PgServer.PgTypeFloat8, "DOUBLE PRECISION")]
    [InlineData(PgServer.PgTypeInt2Array, "smallint[]")]
    [InlineData(PgServer.PgTypeInt4Array, "integer[]")]
    [InlineData(PgServer.PgTypeVarcharArray, "character varying[]")]
    [InlineData(PgServer.PgTypeBpchar, "CHARACTER")]
    [InlineData(PgServer.PgTypeVarchar, "CHARACTER VARYING")]
    [InlineData(PgServer.PgTypeDate, "DATE")]
    [InlineData(PgServer.PgTypeTime, "TIME")]
    [InlineData(PgServer.PgTypeTimetz, "TIME WITH TIME ZONE")]
    [InlineData(PgServer.PgTypeTimestamp, "TIMESTAMP")]
    [InlineData(PgServer.PgTypeTimestamptz, "TIMESTAMP WITH TIME ZONE")]
    [InlineData(PgServer.PgTypeNumeric, "NUMERIC")]
    [InlineData(2205, "regclass")]
    [InlineData(-1, "???")]
    public void FormatTypeIsTheReferenceTable(int code, string expected)
        => Assert.Equal(expected, PgServer.FormatType(code));
}
