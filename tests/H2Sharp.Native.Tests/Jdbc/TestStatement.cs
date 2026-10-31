// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.jdbc.TestStatement.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands. Statement.executeUpdate answers
// ExecuteNonQuery, Statement.execute is the reader's RecordsAffected (a result set
// answers -1), and the savepoints are the transaction's Save/Rollback by name.
//
// Modified (deferred, see MIGRATION_STATUS.md): the JDBC-only surfaces of the reference
// test are not portable and are skipped in place — unwrap and the type map, the
// holdability/poolable/fetch-size/max-field-size knobs, cancel and query timeout
// (their deferral is its own entry), the executeQuery-refusal checks (the provider
// answers an update-count reader instead of refusing), getMoreResults and
// getUpdateCount, the warnings, closeOnCompletion, the generated keys of
// testIdentityMerge, and the identifier quoting helpers
// (enquoteIdentifier/isSimpleIdentifier). The reference's main() entry is the runner;
// xUnit runs Test() directly.
using Xunit;

namespace H2Sharp.Native.Tests.Jdbc;

/// <summary>
/// Tests for the Statement implementation.
/// </summary>
public sealed class TestStatement : TestDb
{
    private DbConnection? conn;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("statement");
        conn = GetConnection("statement");
        TestTraceError();
        TestConnectionRollback();
        TestSavepoint();
        TestStatement_();
        TestPreparedStatement();
        TestMultipleCommands();
        conn.Close();
        DeleteDb("statement");
        // testIdentifiers: deferred, see the header and MIGRATION_STATUS.md.
        DeleteDb("statement");
    }

    /// <summary>
    /// The reference's TestTraceError compares the growth of the trace file: a failed
    /// statement writes a line, a successful one writes none. The port replaces the file
    /// channel with the embedder's trace listener (H2DataSource.SetTraceListener), and the
    /// same assertions run against the lines the listener receives.
    /// </summary>
    private void TestTraceError()
    {
        // A dedicated data source so the listener is the only one attached; TestDb's shared
        // data sources are shared by URL and must not receive a test listener.
        string classic = ToClassicConnectionString(GetURL("statementTrace", admin: true));
        H2DataSource dataSource = new H2DataSourceBuilder(classic).Build();
        List<string> lines = [];
        dataSource.SetTraceListener(new RecordingTraceListener(lines));

        using DbConnection c = dataSource.CreateConnection(classic);
        c.Open();
        DbCommand stat = c.CreateCommand();
        Execute(stat, "DROP TABLE TEST IF EXISTS");
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY)");
        Execute(stat, "INSERT INTO TEST VALUES(1)");
        try
        {
            Execute(stat, "ERROR");
        }
        catch (DbException)
        {
            // ignore
        }

        int linesBefore = lines.Count;
        try
        {
            Execute(stat, "ERROR");
        }
        catch (DbException)
        {
            // ignore
        }

        AssertTrue(lines.Count > linesBefore);
        linesBefore = lines.Count;
        // A successful statement writes no trace line: the failure case is what the trace
        // records. A SELECT is the safe success here — a second INSERT with the same key
        // would itself fail and add a line.
        Execute(stat, "SELECT * FROM TEST");
        AssertEquals(linesBefore, lines.Count);
        Execute(stat, "DROP TABLE TEST IF EXISTS");
        dataSource.Dispose();
    }

    /// <summary>
    /// Collects the lines the database's trace listener receives.
    /// </summary>
    private sealed class RecordingTraceListener(List<string> lines) : H2Sharp.Data.IH2DatabaseTraceListener
    {
        /// <inheritdoc/>
        public void Write(H2Sharp.Data.H2TraceLevel level, string module, string message, Exception? t)
            => lines.Add(module + ": " + message);

        /// <inheritdoc/>
        public bool IsEnabled(H2Sharp.Data.H2TraceLevel level) => true;
    }

    private void TestConnectionRollback()
    {
        DbCommand stat = conn!.CreateCommand();
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(255))");
            Execute(stat, "INSERT INTO TEST VALUES(1, 'Hello')");
            transaction.Rollback();
        }

        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            stat.CommandText = "SELECT * FROM TEST";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertFalse(rs.Read());
            }

            Execute(stat, "DROP TABLE TEST");
            transaction.Commit();
        }
    }

    private void TestSavepoint()
    {
        DbCommand stat = conn!.CreateCommand();
        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(255))");
        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            Execute(stat, "INSERT INTO TEST VALUES(0, 'Hi')");
            // setSavepoint(): the ADO.NET savepoint is named; the JDBC Savepoint object's
            // id and name assertions are the JDBC surface.
            transaction.Save("SP1");
            Execute(stat, "DELETE FROM TEST");
            transaction.Rollback("SP1");
            Execute(stat, "UPDATE TEST SET NAME='Hello'");
            transaction.Save("SP2");
            Execute(stat, "UPDATE TEST SET NAME='Hallo' WHERE NAME='Hello'");
            transaction.Save("Joe's");
            Execute(stat, "DELETE FROM TEST");
            transaction.Rollback("Joe's");
            transaction.Commit();
        }

        using (DbTransaction transaction = conn.BeginTransaction())
        {
            stat.Transaction = transaction;
            stat.CommandText = "SELECT NAME FROM TEST";
            using (DbDataReader rs = stat.ExecuteReader())
            {
                AssertTrue(rs.Read());
                string name = rs.GetString(0);
                AssertEquals(name, "Hallo");
                AssertFalse(rs.Read());
            }

            Execute(stat, "DROP TABLE TEST");
            transaction.Commit();
        }
    }

    private void TestStatement_()
    {
        DbCommand stat = conn!.CreateCommand();

        // The holdability, poolable, cursor-name, fetch-direction, max-field-size and
        // fetch-size knobs are the JDBC surface; the provider has none of them.

        stat.CommandText = "CREATE TABLE TEST(ID INT)";
        _ = stat.ExecuteNonQuery();
        stat.CommandText = "SELECT * FROM TEST";
        using (_ = stat.ExecuteReader())
        {
        }

        stat.CommandText = "DROP TABLE TEST";
        _ = stat.ExecuteNonQuery();

        // trace("executeUpdate");
        stat.CommandText = "CREATE TABLE TEST(ID INT PRIMARY KEY,V VARCHAR(255))";
        int count = stat.ExecuteNonQuery();
        AssertEquals(0, count);
        stat.CommandText = "INSERT INTO TEST VALUES(1,'Hello')";
        count = stat.ExecuteNonQuery();
        AssertEquals(1, count);
        stat.CommandText = "INSERT INTO TEST(V,ID) VALUES('JDBC',2)";
        count = stat.ExecuteNonQuery();
        AssertEquals(1, count);
        stat.CommandText = "UPDATE TEST SET V='LDBC' WHERE ID=2 OR ID=1";
        count = stat.ExecuteNonQuery();
        AssertEquals(2, count);
        stat.CommandText = "UPDATE TEST SET V='\\LDBC\\' WHERE V LIKE 'LDBC' ";
        count = stat.ExecuteNonQuery();
        AssertEquals(2, count);
        stat.CommandText = "UPDATE TEST SET V='LDBC' WHERE V LIKE '\\\\LDBC\\\\'";
        count = stat.ExecuteNonQuery();
        AssertEquals(2, count);
        stat.CommandText = "DELETE FROM TEST WHERE ID=-1";
        count = stat.ExecuteNonQuery();
        AssertEquals(0, count);
        stat.CommandText = "DELETE FROM TEST WHERE ID=2";
        count = stat.ExecuteNonQuery();
        AssertEquals(1, count);

        // The reference refuses executeUpdate on a query with
        // METHOD_NOT_ALLOWED_FOR_QUERY; the provider's ExecuteNonQuery runs the query
        // and drops the rows instead (see the class header).
        stat.CommandText = "SELECT * FROM TEST";
        _ = stat.ExecuteNonQuery();

        stat.CommandText = "DROP TABLE TEST";
        count = stat.ExecuteNonQuery();
        AssertTrue(count == 0);

        // Statement.execute: a result set answers through the reader's RecordsAffected.
        stat.CommandText = "CREATE TABLE TEST(ID INT PRIMARY KEY,V VARCHAR(255))";
        bool result;
        using (DbDataReader reader = stat.ExecuteReader())
        {
            result = reader.RecordsAffected < 0;
        }

        AssertFalse(result);
        stat.CommandText = "INSERT INTO TEST VALUES(1,'Hello')";
        using (DbDataReader reader = stat.ExecuteReader())
        {
            result = reader.RecordsAffected < 0;
        }

        AssertFalse(result);
        stat.CommandText = "INSERT INTO TEST(V,ID) VALUES('JDBC',2)";
        using (DbDataReader reader = stat.ExecuteReader())
        {
            result = reader.RecordsAffected < 0;
        }

        AssertFalse(result);
        stat.CommandText = "UPDATE TEST SET V='LDBC' WHERE ID=2";
        using (DbDataReader reader = stat.ExecuteReader())
        {
            result = reader.RecordsAffected < 0;
        }

        AssertFalse(result);
        stat.CommandText = "DELETE FROM TEST WHERE ID=3";
        using (DbDataReader reader = stat.ExecuteReader())
        {
            result = reader.RecordsAffected < 0;
        }

        AssertFalse(result);
        stat.CommandText = "SELECT * FROM TEST";
        using (DbDataReader reader = stat.ExecuteReader())
        {
            result = reader.RecordsAffected < 0;
        }

        AssertTrue(result);
        stat.CommandText = "DROP TABLE TEST";
        using (DbDataReader reader = stat.ExecuteReader())
        {
            result = reader.RecordsAffected < 0;
        }

        AssertFalse(result);

        Execute(stat, "CREATE TABLE TEST(ID INT PRIMARY KEY,V VARCHAR(255))");

        // The executeQuery refusals of the reference are the same provider shape as
        // above; see the class header.

        // getMoreResults: the rows of the first result set have to be gone when the
        // command moves to the next one.
        stat.CommandText = "SELECT * FROM TEST";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertFalse(rs.NextResult());
        }

        stat.CommandText = "DELETE FROM TEST";
        _ = stat.ExecuteNonQuery();
        Execute(stat, "DROP TABLE TEST");
        Execute(stat, "DROP TABLE IF EXISTS TEST");
        stat.Dispose();
    }

    private void TestPreparedStatement()
    {
        DbCommand stat = conn!.CreateCommand();
        Execute(stat, "create table test(id int primary key, name varchar(255))");
        Execute(stat, "insert into test values(1, 'Hello')");
        Execute(stat, "insert into test values(2, 'World')");
        using DbCommand ps = conn.CreateCommand();
        ps.CommandText = "select name from test where id in (select id from test where name REGEXP ?)";
        ps.Parameters.Add(new H2Parameter());
        ps.Parameters[0].Value = "Hello";
        using (DbDataReader rs = ps.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("Hello", rs.GetString(rs.GetOrdinal("name")));
            AssertFalse(rs.Read());
        }

        ps.Parameters[0].Value = "World";
        using (DbDataReader rs = ps.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("World", rs.GetString(rs.GetOrdinal("name")));
            AssertFalse(rs.Read());
        }

        // Changes the table structure
        Execute(stat, "create index t_id on test(name)");
        // Test the prepared statement again to check if the internal cache attributes were reset
        ps.Parameters[0].Value = "Hello";
        using (DbDataReader rs = ps.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("Hello", rs.GetString(rs.GetOrdinal("name")));
            AssertFalse(rs.Read());
        }

        ps.Parameters[0].Value = "World";
        using (DbDataReader rs = ps.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals("World", rs.GetString(rs.GetOrdinal("name")));
            AssertFalse(rs.Read());
        }

        // addBatch/executeBatch: one execution per parameter set, in order.
        using (DbCommand batch = conn.CreateCommand())
        {
            batch.CommandText = "insert into test values(?, ?)";
            batch.Parameters.Add(new H2Parameter());
            batch.Parameters.Add(new H2Parameter());
            batch.Parameters[0].Value = 3;
            batch.Parameters[1].Value = "v3";
            AssertEquals(1, batch.ExecuteNonQuery());
            batch.Parameters[0].Value = 4;
            batch.Parameters[1].Value = "v4";
            AssertEquals(1, batch.ExecuteNonQuery());
        }

        using (DbCommand batch = conn.CreateCommand())
        {
            batch.CommandText = "insert into test values(?, ?)";
            batch.Parameters.Add(new H2Parameter());
            batch.Parameters.Add(new H2Parameter());
            batch.Parameters[0].Value = 5;
            batch.Parameters[1].Value = "v5";
            AssertEquals(1, batch.ExecuteNonQuery());
            batch.Parameters[0].Value = 6;
            batch.Parameters[1].Value = "v6";
            AssertEquals(1, batch.ExecuteNonQuery());
        }

        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "insert into test values(?, ?)";
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters[0].Value = 7;
            prep.Parameters[1].Value = "v7";
            AssertEquals(1, prep.ExecuteNonQuery());
            prep.Parameters[0].Value = 8;
            prep.Parameters[1].Value = "v8";
            AssertEquals(1, prep.ExecuteNonQuery());
        }

        Execute(stat, "drop table test");
    }

    private void TestMultipleCommands()
    {
        DbCommand stat = conn!.CreateCommand();
        stat.CommandText = "VALUES 1; VALUES 2";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertTrue(rs.NextResult());
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertFalse(rs.NextResult());
        }
    }
}
