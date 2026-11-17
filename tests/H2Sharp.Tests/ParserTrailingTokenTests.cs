/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 264: a statement followed by an unexpected token must be refused by the statement's
 * own parser, not silently ignored. The batch covers the AtEnd/expectation branches of many
 * different statement kinds with one oracle-shaped test.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ParserTrailingTokenTests
{
    public static TheoryData<string> Statements => new()
    {
        "SELECT 1",
        "SELECT 1 FROM (VALUES 1) T(A)",
        "SELECT * FROM (VALUES 1) T(A) WHERE A > 0",
        "SELECT DISTINCT A FROM (VALUES 1) T(A)",
        "SELECT A, COUNT(*) FROM (VALUES 1) T(A) GROUP BY A",
        "WITH C AS (SELECT 1) SELECT * FROM C",
        "INSERT INTO T VALUES 1",
        "CREATE TABLE T(A INT)",
        "DROP TABLE T",
        "CREATE INDEX I ON T(A)",
        "CREATE SEQUENCE S",
        "CREATE VIEW V AS SELECT 1",
        "CREATE DOMAIN D AS INT",
        "ALTER TABLE T ALTER COLUMN A DROP IDENTITY",
        "ALTER SEQUENCE S RESTART WITH 1",
        "COMMIT",
        "ROLLBACK",
        "SAVEPOINT S1",
        "EXPLAIN SELECT 1",
        "CALL ABS(-1)",
        "SET AUTOCOMMIT TRUE",
        "TRUNCATE TABLE T",
        "MERGE INTO T KEY(A) VALUES (1)",
        "SCRIPT NODATA",
        "SHOW TABLES",
    };

    [Theory]
    [MemberData(nameof(Statements))]
    public void TrailingTokenIsRefused(string sql)
    {
        using H2Connection connection = new(
            "Data Source=parser_trailing_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        using (DbCommand create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE T(A INT)";
            create.ExecuteNonQuery();
        }

        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql + " )";
        Assert.ThrowsAny<Exception>(() => command.ExecuteNonQuery());
    }
}
