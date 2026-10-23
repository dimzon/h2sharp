// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.jdbc.TestTransactionIsolation.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): the reference drives JdbcConnection.getTransactionIsolation /
// setTransactionIsolation; the ADO.NET way to carry an isolation level is the level the
// transaction begins with, and the way to read it back is the transaction's own
// IsolationLevel property. The java.sql.Connection constants and the ADO.NET
// IsolationLevel share their numeric values (1, 2, 4, 8), so the levels here are the
// System.Data.IsolationLevel members the reference's constants name. setAutoCommit(false)
// is an explicit transaction that the connection's statements carry from then on, and
// commit() commits it and begins the next one, which is what manual-commit mode means;
// the Command1/Command2 helpers are conn.createStatement with that transaction on it.
// The reference's main() entry is the runner; xUnit runs Test() directly.
using System.Data;
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Jdbc;

/// <summary>
/// Transaction isolation level tests.
/// </summary>
public sealed class TestTransactionIsolation : TestDb
{
    private DbConnection? conn1;

    private DbConnection? conn2;

    /// <summary>
    /// The transaction conn1 runs in since its setAutoCommit(false); the reference's
    /// conn1 never commits, so it stays open for the whole test.
    /// </summary>
    private DbTransaction? transaction1;

    /// <summary>
    /// The transaction conn2 runs in since its setAutoCommit(false); the reference's
    /// conn2.commit() commits it and the next statement begins the next one.
    /// </summary>
    private DbTransaction? transaction2;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestTableLevelLocking();
    }

    private void TestTableLevelLocking()
    {
        DeleteDb("transactionIsolation");

        conn1 = GetConnection("transactionIsolation");
        // conn1.setAutoCommit(false)
        transaction1 = conn1.BeginTransaction();

        conn2 = GetConnection("transactionIsolation");
        // conn2.setAutoCommit(false)
        transaction2 = conn2.BeginTransaction();

        // getDefaultTransactionIsolation: the level a transaction begun without one runs
        // at. The reference's metadata answers TRANSACTION_READ_COMMITTED.
        using (DbTransaction transaction = conn1.BeginTransaction())
        {
            AssertEquals(IsolationLevel.ReadCommitted, transaction.IsolationLevel);
        }

        // getTransactionIsolation: the level the open transaction runs at.
        AssertEquals(IsolationLevel.ReadCommitted, transaction1!.IsolationLevel);

        using (DbConnection conn = GetConnection("transactionIsolation"))
        {
            DbCommand stmt = conn.CreateCommand();
            Execute(stmt, "CREATE TABLE TEST(ID INT)");
        }

        TestIt(IsolationLevel.ReadUncommitted);
        TestIt(IsolationLevel.ReadCommitted);
        TestIt(IsolationLevel.RepeatableRead);
        TestIt(IsolationLevel.Serializable);

        using (DbConnection conn = GetConnection("transactionIsolation"))
        {
            DbCommand stmt = conn.CreateCommand();
            Execute(stmt, "DROP TABLE TEST");
            Execute(stmt, "CREATE TABLE TEST(ID INT UNIQUE)");
        }

        TestIt(IsolationLevel.ReadUncommitted);
        TestIt(IsolationLevel.ReadCommitted);
        TestIt(IsolationLevel.RepeatableRead);
        TestIt(IsolationLevel.Serializable);

        conn2.Close();
        conn1.Close();
        DeleteDb("transactionIsolation");
    }

    private void TestIt(IsolationLevel isolationLevel2)
    {
        using (DbConnection conn = GetConnection("transactionIsolation"))
        {
            DbCommand stmt = conn.CreateCommand();
            Execute(stmt, "DELETE FROM TEST");
            Execute(stmt, "INSERT INTO TEST VALUES(1)");
        }

        // conn2.setTransactionIsolation(level): the level the next transaction begins
        // with, read back from the transaction itself.
        transaction2 = conn2!.BeginTransaction(isolationLevel2);
        AssertEquals(isolationLevel2, transaction2.IsolationLevel);

        TestRowLocks(IsolationLevel.ReadUncommitted);
        TestRowLocks(IsolationLevel.ReadCommitted);
        TestRowLocks(IsolationLevel.RepeatableRead);
        TestRowLocks(IsolationLevel.Serializable);

        TestDirtyRead(IsolationLevel.ReadUncommitted, 1, true, true);
        TestDirtyRead(IsolationLevel.ReadCommitted, 2, false, true);
        TestDirtyRead(IsolationLevel.RepeatableRead, 3, false, false);
        TestDirtyRead(IsolationLevel.Serializable, 4, false, false);
    }

    private void TestDirtyRead(IsolationLevel isolationLevel, int value, bool dirtyVisible,
        bool committedVisible)
    {
        transaction1 = conn1!.BeginTransaction(isolationLevel);
        AssertSingleValue(Command1(), "SELECT * FROM TEST", value);
        int newValue = value + 1;
        DbCommand update = conn2!.CreateCommand();
        update.Transaction = transaction2;
        update.CommandText = "UPDATE TEST SET ID=" + newValue;
        _ = update.ExecuteNonQuery();
        AssertSingleValue(Command1(), "SELECT * FROM TEST", dirtyVisible ? newValue : value);

        // conn2.commit()
        IsolationLevel level2 = transaction2!.IsolationLevel;
        transaction2.Commit();
        transaction2 = conn2.BeginTransaction(level2);
        AssertSingleValue(Command1(), "SELECT * FROM TEST", committedVisible ? newValue : value);
    }

    private void TestRowLocks(IsolationLevel isolationLevel)
    {
        transaction1 = conn1!.BeginTransaction(isolationLevel);
        AssertSingleValue(Command1(), "SELECT * FROM TEST", 1);
        AssertSingleValue(Command2(), "SELECT * FROM TEST FOR UPDATE", 1);
        DbCommand delete = conn1.CreateCommand();
        delete.Transaction = transaction1;
        AssertThrows(ErrorCode.LockTimeout1,
            () =>
            {
                delete.CommandText = "DELETE FROM TEST";
                _ = delete.ExecuteNonQuery();
            });

        // conn2.commit()
        IsolationLevel level2 = transaction2!.IsolationLevel;
        transaction2.Commit();
        transaction2 = conn2!.BeginTransaction(level2);
    }

    /// <summary>
    /// The reference's conn1.createStatement() with the SQL set: a statement of conn1's
    /// open manual-commit transaction.
    /// </summary>
    /// <param name="sql">the SQL statement</param>
    /// <returns>the statement</returns>
    private DbCommand Command1()
    {
        DbCommand command = conn1!.CreateCommand();
        command.Transaction = transaction1;
        return command;
    }

    /// <summary>
    /// The reference's conn2.createStatement() with the SQL set: a statement of conn2's
    /// open manual-commit transaction.
    /// </summary>
    /// <param name="sql">the SQL statement</param>
    /// <returns>the statement</returns>
    private DbCommand Command2()
    {
        DbCommand command = conn2!.CreateCommand();
        command.Transaction = transaction2;
        return command;
    }
}
