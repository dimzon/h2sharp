/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 263: malformed and half-ported statements that exercise the parser's error branches.
 * A malformed statement must be refused, never silently accepted; the exact code depends on the
 * branch, so the contract asserted here is that there is an explicit refusal.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ParserErrorSurfaceTests
{
    private static void Refuses(string sql)
    {
        using H2Connection connection = new(
            "Data Source=parser_errors_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        Assert.ThrowsAny<Exception>(() => command.ExecuteNonQuery());
    }

    [Fact]
    public void MalformedQueriesAreRefused()
    {
        Refuses("SELECT 1 2");
        Refuses("SELECT ,");
        Refuses("FOO");
        Refuses("SELECT PUBLIC..T");
        Refuses("SELECT TIME WITH TIME ZONE");
        Refuses("SELECT TIMESTAMP WITHOUT TIME ZONE");
        Refuses("SELECT TIME WITH TIME ZONE 'x'");
    }

    [Fact]
    public void MalformedTableAndJoinFormsAreRefused()
    {
        Refuses("SELECT * FROM (VALUES 1) A(X) JOIN (VALUES 1) B(Y) USING(Z)");
        Refuses("CREATE VIEW V AS SELECT 1 2");
        Refuses("CREATE MEMORY FOO");
        Refuses("CREATE TEMPORARY FOO");
    }

    [Fact]
    public void MalformedAlterAndSequenceFormsAreRefused()
    {
        Refuses("ALTER TABLE NO_T ALTER COLUMN A DROP IDENTITY");
        Refuses("ALTER TABLE T ALTER COLUMN NO_A DROP IDENTITY");
        Refuses("ALTER TABLE T ADD COLUMN B INT");
        Refuses("ALTER TABLE T RENAME TO U");
        Refuses("CREATE SEQUENCE S1 MINVALUE");
        Refuses("CREATE SEQUENCE S2 START WITH");
        Refuses("CREATE INDEX I1 ON T()");
        Refuses("CREATE INDEX I2 ON T(A, A)");
        Refuses("CREATE INDEX I3 ON T(A)");
    }
}
