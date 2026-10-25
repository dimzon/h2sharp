// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestCheckpoint.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; setAutoCommit(false) is an
// explicit transaction that the statements after it carry, and the reference's COMMIT
// statement stays a statement. The reference's main() entry is the runner; xUnit runs
// Test() directly.
//
// Blocked by the port: the parser does not dispatch CHECKPOINT yet (issue #64), so the
// test refuses at its first statement. The trait keeps it in the suite as the defect's
// checklist entry.
namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests the CHECKPOINT SQL statement.
/// </summary>
public sealed class TestCheckpoint : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        // TODO test checkpoint with rollback, not only just run the command
        DeleteDb("checkpoint");
        using DbConnection c0 = GetConnection("checkpoint");
        DbCommand s0 = c0.CreateCommand();
        using DbConnection c1 = GetConnection("checkpoint");
        DbCommand s1 = c1.CreateCommand();
        Execute(s1, "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(255))");
        Execute(s1, "INSERT INTO TEST VALUES(1, 'Hello')");
        Execute(s0, "CHECKPOINT");

        Execute(s1, "INSERT INTO TEST VALUES(2, 'World')");
        using DbTransaction transaction = c1.BeginTransaction();
        s1.Transaction = transaction;
        Execute(s1, "INSERT INTO TEST VALUES(3, 'Maybe')");
        Execute(s0, "CHECKPOINT");

        Execute(s1, "INSERT INTO TEST VALUES(4, 'Or not')");
        Execute(s0, "CHECKPOINT");

        Execute(s1, "INSERT INTO TEST VALUES(5, 'ok yes')");
        Execute(s1, "COMMIT");
        Execute(s0, "CHECKPOINT");
    }
}
