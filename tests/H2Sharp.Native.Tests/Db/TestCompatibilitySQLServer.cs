// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestCompatibilitySQLServer.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands. The reference's main() entry is
// the runner; xUnit runs Test() directly.
namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test MSSQLServer compatibility mode.
/// </summary>
public sealed class TestCompatibilitySQLServer : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("sqlserver");

        using DbConnection conn = GetConnection("sqlserver;MODE=MSSQLServer");
        try
        {
            TestDiscardTableHints(conn);
            TestPrimaryKeyIdentity(conn);
        }
        finally
        {
            conn.Close();
            DeleteDb("sqlserver");
        }
    }

    private void TestDiscardTableHints(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();

        Execute(stat, "create table parent(id int primary key, name varchar(255))");
        Execute(stat, "create table child("
            + "id int primary key, "
            + "parent_id int, "
            + "name varchar(255), "
            + "foreign key (parent_id) references public.parent(id))");

        Execute(stat, "select * from parent");
        Execute(stat, "select * from parent with(nolock)");
        Execute(stat, "select * from parent with(nolock, index = id)");
        Execute(stat, "select * from parent with(nolock, index(id, name))");

        Execute(stat, "select * from parent p "
            + "join child ch on ch.parent_id = p.id");
        Execute(stat, "select * from parent p with(nolock) "
            + "join child ch with(nolock) on ch.parent_id = p.id");
        Execute(stat, "select * from parent p with(nolock) "
            + "join child ch with(nolock, index = id) on ch.parent_id = p.id");
        Execute(stat, "select * from parent p with(nolock) "
            + "join child ch with(nolock, index(id, name)) on ch.parent_id = p.id");
    }

    private void TestPrimaryKeyIdentity(DbConnection conn)
    {
        DbCommand stat = conn.CreateCommand();

        // IDENTITY after PRIMARY KEY is an undocumented syntax of MS SQL
        Execute(stat, "create table test(id int primary key identity, expected_id int)");
        Execute(stat, "insert into test (expected_id) VALUES (1), (2), (3)");

        stat.CommandText = "select * from test";
        using (DbDataReader results = stat.ExecuteReader())
        {
            while (results.Read())
            {
                AssertEquals(results.GetInt32(results.GetOrdinal("expected_id")),
                    results.GetInt32(results.GetOrdinal("id")));
            }
        }

        Execute(stat, "create table test2 (id int primary key not null identity)");
    }
}
