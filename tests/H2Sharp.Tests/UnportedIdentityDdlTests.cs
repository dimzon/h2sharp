/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * D-004: users and roles are not ported. Every identity statement must say what is
 * missing instead of looking like a syntax error or silently answering. */
using System;
using System.Collections.Generic;
using H2Sharp;
using H2Sharp.Api;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Message;
using Xunit;

namespace H2Sharp.Tests;

public sealed class UnportedIdentityDdlTests
{
    public static IEnumerable<object[]> IdentityStatements()
    {
        yield return ["CREATE USER A PASSWORD 'x'", "CreateUser", "users"];
        yield return ["CREATE USER IF NOT EXISTS A PASSWORD 'x'", "CreateUser", "users"];
        yield return ["CREATE USER A PASSWORD 'x' ADMIN", "CreateUser", "users"];
        yield return ["CREATE USER A SALT X'00' HASH X'11'", "CreateUser", "users"];
        yield return ["CREATE USER A IDENTIFIED BY x", "CreateUser", "users"];

        yield return ["CREATE ROLE R", "CreateRole", "roles"];
        yield return ["CREATE ROLE IF NOT EXISTS R", "CreateRole", "roles"];

        yield return ["ALTER USER A ADMIN TRUE", "AlterUser", "users"];
        yield return ["ALTER USER A ADMIN FALSE", "AlterUser", "users"];
        yield return ["ALTER USER A RENAME TO B", "AlterUser", "users"];
        yield return ["ALTER USER A SET PASSWORD 'x'", "AlterUser", "users"];
        yield return ["ALTER USER A SET SALT X'00' HASH X'11'", "AlterUser", "users"];
        yield return ["SET PASSWORD 'x'", "AlterUser", "users"];
        yield return ["SET SALT X'00' HASH X'11'", "AlterUser", "users"];

        yield return ["DROP USER A", "DropUser", "users"];
        yield return ["DROP USER IF EXISTS A", "DropUser", "users"];
        yield return ["DROP ROLE R", "DropRole", "roles"];
        yield return ["DROP ROLE IF EXISTS R", "DropRole", "roles"];

        yield return ["GRANT SELECT ON T TO A", "GrantRevoke", "rights"];
        yield return ["GRANT R TO A", "GrantRevoke", "roles"];
        yield return ["GRANT ALTER ANY SCHEMA TO A", "GrantRevoke", "rights"];
        yield return ["REVOKE SELECT ON T FROM A", "GrantRevoke", "rights"];
        yield return ["REVOKE R FROM A", "GrantRevoke", "roles"];
        yield return ["REVOKE ALTER ANY SCHEMA FROM A", "GrantRevoke", "rights"];
    }

    [Theory]
    [MemberData(nameof(IdentityStatements))]
    public void IdentityDdlNamesTheMissingReferenceClass(string sql, string commandClass, string subsystem)
    {
        using SessionLocal session = new(
            new Database(ConnectionInfo.Memory("identity_ddl_" + Guid.NewGuid().ToString("N"))), "sa");
        TODOException error = Assert.Throws<TODOException>(() => session.PrepareCommand(sql));
        Assert.StartsWith("Not ported yet:", error.Message, StringComparison.Ordinal);
        Assert.Contains(commandClass, error.Message, StringComparison.Ordinal);
        Assert.Contains(subsystem, error.Message, StringComparison.Ordinal);
        Assert.Contains("D-004", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Syntax error", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("COMMENT ON USER A IS 'x'", ErrorCode.UserNotFound1)]
    [InlineData("COMMENT ON ROLE R IS 'x'", ErrorCode.RoleNotFound1)]
    public void CommentOnIdentityReportsTheReferenceNotFoundCode(string sql, int errorCode)
    {
        using SessionLocal session = new(
            new Database(ConnectionInfo.Memory("identity_comment_" + Guid.NewGuid().ToString("N"))), "sa");
        H2SharpException error = Assert.Throws<H2SharpException>(() =>
            Assert.IsType<CommandContainer>(session.PrepareCommand(sql)).GetPreparedCommand().Update());
        Assert.Equal(errorCode, error.H2ErrorCode);
        Assert.Contains(errorCode == ErrorCode.UserNotFound1 ? "USER" : "ROLE", error.Message,
            StringComparison.OrdinalIgnoreCase);
    }
}
