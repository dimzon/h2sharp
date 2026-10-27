// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — transaction statements, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class TransactionCommandTests
{
    /// <summary>docs/commands.html:2311 — COMMIT [ WORK ]</summary>
    [Fact]
    public void Commit_KeepsThePendingChanges() // CMD-2311
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("SET AUTOCOMMIT FALSE");
        _ = connection.Execute("INSERT INTO TEST(ID) VALUES (1)");

        _ = connection.Execute("COMMIT WORK");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST"));
    }

    /// <summary>docs/commands.html:2330 — COMMIT TRANSACTION transactionName</summary>
    [Fact]
    public void CommitTransaction_CommitsThePreparedTransaction() // CMD-2330
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("SET AUTOCOMMIT FALSE");
        _ = connection.Execute("INSERT INTO TEST(ID) VALUES (1)");
        _ = connection.Execute("PREPARE COMMIT XID_TEST");

        _ = connection.Execute("COMMIT TRANSACTION XID_TEST");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST"));
    }

    /// <summary>docs/commands.html:2429 — PREPARE COMMIT newTransactionName</summary>
    [Fact]
    public void PrepareCommit_PutsTheTransactionInThePreparedState() // CMD-2429
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("SET AUTOCOMMIT FALSE");
        _ = connection.Execute("INSERT INTO TEST(ID) VALUES (1)");

        _ = connection.Execute("PREPARE COMMIT XID_TEST");

        _ = connection.Execute("ROLLBACK TRANSACTION XID_TEST");
    }

    /// <summary>docs/commands.html:2509 — ROLLBACK [ WORK ] [ TO SAVEPOINT savepointName ]</summary>
    [Fact]
    public void Rollback_DiscardsThePendingChanges() // CMD-2509
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("SET AUTOCOMMIT FALSE");
        _ = connection.Execute("INSERT INTO TEST(ID) VALUES (1)");

        _ = connection.Execute("ROLLBACK WORK");
        _ = connection.Execute("SET AUTOCOMMIT TRUE");

        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST"));
    }

    /// <summary>docs/commands.html:2528 — ROLLBACK TRANSACTION transactionName</summary>
    [Fact]
    public void RollbackTransaction_RollsBackThePreparedTransaction() // CMD-2528
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("SET AUTOCOMMIT FALSE");
        _ = connection.Execute("INSERT INTO TEST(ID) VALUES (1)");
        _ = connection.Execute("PREPARE COMMIT XID_TEST");

        _ = connection.Execute("ROLLBACK TRANSACTION XID_TEST");
        _ = connection.Execute("SET AUTOCOMMIT TRUE");

        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST"));
    }

    /// <summary>docs/commands.html:2547 — SAVEPOINT savepointName</summary>
    [Fact]
    public void Savepoint_RollsBackToTheMark() // CMD-2547
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("SET AUTOCOMMIT FALSE");
        _ = connection.Execute("INSERT INTO TEST(ID) VALUES (1)");
        _ = connection.Execute("SAVEPOINT HALF_DONE");
        _ = connection.Execute("INSERT INTO TEST(ID) VALUES (2)");

        _ = connection.Execute("ROLLBACK TO SAVEPOINT HALF_DONE");
        _ = connection.Execute("COMMIT");
        _ = connection.Execute("SET AUTOCOMMIT TRUE");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST"));
    }
}
