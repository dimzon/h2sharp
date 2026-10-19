// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — ALTER TABLE ... SET REFERENTIAL_INTEGRITY, one test per checklist row.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class AlterTableSetCommandTests
{
    /// <summary>docs/commands.html:1429 — ALTER TABLE [ IF EXISTS ] [schemaName.]tableName</summary>
    [Fact]
    public void AlterTableSet_ReferentialIntegrityFalse_AllowsAnOrphanRow() // CMD-1429
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenParentAndChild(connection);

        _ = connection.Execute("ALTER TABLE IF EXISTS CHILD SET REFERENTIAL_INTEGRITY FALSE");
        _ = connection.Execute("INSERT INTO CHILD(PARENT_ID) VALUES (99)");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM CHILD WHERE PARENT_ID = 99"));
    }

    /// <summary>docs/commands.html:1430 — SET REFERENTIAL_INTEGRITY</summary>
    [Fact]
    public void AlterTableSet_ReferentialIntegrityTrue_EnforcesTheForeignKeyAgain() // CMD-1430
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenParentAndChild(connection);

        _ = connection.Execute("ALTER TABLE CHILD SET REFERENTIAL_INTEGRITY FALSE");
        _ = connection.Execute("ALTER TABLE CHILD SET REFERENTIAL_INTEGRITY TRUE");

        _ = Assert.ThrowsAny<Exception>(() => connection.Execute("INSERT INTO CHILD(PARENT_ID) VALUES (99)"));
    }

    /// <summary>docs/commands.html:1431 — { FALSE | TRUE } [ CHECK | NOCHECK ]</summary>
    [Fact]
    public void AlterTableSet_ReferentialIntegrityFalseNoCheck_AllowsAnOrphanRow() // CMD-1431
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenParentAndChild(connection);

        _ = connection.Execute("ALTER TABLE CHILD SET REFERENTIAL_INTEGRITY FALSE NOCHECK");
        _ = connection.Execute("INSERT INTO CHILD(PARENT_ID) VALUES (99)");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM CHILD WHERE PARENT_ID = 99"));
    }

    private static void GivenParentAndChild(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE PARENT(ID INT PRIMARY KEY)");
        _ = connection.Execute("CREATE TABLE CHILD(PARENT_ID INT REFERENCES PARENT(ID))");
        _ = connection.Execute("INSERT INTO PARENT(ID) VALUES (1)");
    }
}
