/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 250: the parser's recorded refusals. Users, roles and rights are outside the port
 * (D-004); UNIQUE and CHECK constraints are table changes the port has not taken yet. Each
 * refusal names the missing feature, which is the contract the project keeps until the
 * feature is ported.
 */
using System;
using System.Data.Common;
using H2Sharp.Api;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ParserRefusalSurfaceTests
{
    private static Exception Refusal(string sql)
    {
        using H2Connection connection = new(
            "Data Source=parser_refusal_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Assert.ThrowsAny<Exception>(() => command.ExecuteNonQuery());
    }

    [Fact]
    public void UsersRolesAndRightsAreRefusedByThePort()
    {
        Assert.Contains("D-004",
            Assert.IsType<TODOException>(Refusal("CREATE USER U1 PASSWORD 'p'")).Message,
            StringComparison.Ordinal);
        Assert.Contains("D-004",
            Assert.IsType<TODOException>(Refusal("CREATE ROLE R1")).Message,
            StringComparison.Ordinal);
        Assert.Contains("D-004",
            Assert.IsType<TODOException>(Refusal("ALTER USER U1 SET PASSWORD 'q'")).Message,
            StringComparison.Ordinal);
        Assert.Contains("D-004",
            Assert.IsType<TODOException>(Refusal("DROP USER U1")).Message,
            StringComparison.Ordinal);
        Assert.Contains("D-004",
            Assert.IsType<TODOException>(Refusal("DROP ROLE R1")).Message,
            StringComparison.Ordinal);
        Assert.Contains("D-004",
            Assert.IsType<TODOException>(Refusal("GRANT SELECT ON INFORMATION_SCHEMA.TABLES TO PUBLIC")).Message,
            StringComparison.Ordinal);
        Assert.Contains("D-004",
            Assert.IsType<TODOException>(Refusal("REVOKE SELECT ON INFORMATION_SCHEMA.TABLES FROM PUBLIC")).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SetNextToAColumnListIsASyntaxError()
    {
        using H2Connection connection = new(
            "Data Source=parser_insert_set_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        using DbCommand create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE T(A INT)";
        _ = create.ExecuteNonQuery();

        using DbCommand insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO T(A) SET A = 1";
        H2SharpException e = Assert.IsType<H2SharpException>(
            Assert.ThrowsAny<Exception>(() => insert.ExecuteNonQuery()));
        Assert.Equal(ErrorCode.SyntaxError1, e.H2ErrorCode);

        // Without a column list the SET form is the reference's other INSERT syntax.
        using DbCommand plain = connection.CreateCommand();
        plain.CommandText = "INSERT INTO T SET A = 2";
        Assert.Equal(1, plain.ExecuteNonQuery());
    }
}
