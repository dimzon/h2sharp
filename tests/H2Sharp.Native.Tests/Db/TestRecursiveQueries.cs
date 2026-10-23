// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestRecursiveQueries.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; setInt/setString are parameter
// values. The java.sql.Types codes of assertResultSetMeta are the CLR types the reader
// answers (INTEGER→int, VARCHAR→string, TIMESTAMP→DateTime). The reference's main()
// entry is the runner; xUnit runs Test() directly.
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test recursive queries using WITH.
/// </summary>
public sealed class TestRecursiveQueries : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestWrongLinkLargeResult();
        TestSimpleUnionAll();
        TestSimpleUnion();
        TestParameters();
    }

    private void TestWrongLinkLargeResult()
    {
        DeleteDb("recursiveQueries");
        using DbConnection conn = GetConnection("recursiveQueries");
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(parent varchar(255), child varchar(255))");
        Execute(stat, "insert into test values('/', 'a'), ('a', 'b1'), "
            + "('a', 'b2'), ('a', 'c'), ('c', 'd1'), ('c', 'd2')");

        stat.CommandText = "with recursive rec_test(depth, parent, child) as ("
            + "select 0, parent, child from test where parent = '/' "
            + "union all "
            + "select depth+1, r.parent, r.child from test i join rec_test r "
            + "on (i.parent = r.child) where depth<9 "
            + ") select count(*) from rec_test";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(29524, rs.GetInt32(0));
        }

        Execute(stat, "with recursive rec_test(depth, parent, child) as ( "
            + "select 0, parent, child from test where parent = '/' "
            + "union all "
            + "select depth+1, i.parent, i.child from test i join rec_test r "
            + "on (r.child = i.parent) where depth<10 "
            + ") select * from rec_test");
    }

    private void TestSimpleUnionAll()
    {
        DeleteDb("recursiveQueries");
        using DbConnection conn = GetConnection("recursiveQueries");
        DbCommand stat = conn.CreateCommand();

        stat.CommandText = "with recursive t(n) as "
            + "(select 1 union all select n+1 from t where n<3) "
            + "select * from t";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(3, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        DbCommand prep = conn.CreateCommand();
        prep.CommandText = "with recursive t(n) as "
            + "(select 1 union all select n+1 from t where n<3) "
            + "select * from t where n>?";
        prep.Parameters.Add(new H2Parameter());
        prep.Parameters[0].Value = 2;
        using (DbDataReader rs = prep.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(3, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        prep.Parameters[0].Value = 1;
        using (DbDataReader rs = prep.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(3, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        prep.Dispose();
        prep = conn.CreateCommand();
        prep.CommandText = "with recursive t(n) as "
            + "(select @start union all select n+@inc from t where n<@end_index) "
            + "select * from t";
        prep.Parameters.Add(new H2Parameter());
        prep.Parameters.Add(new H2Parameter());
        prep.Parameters.Add(new H2Parameter());
        using DbCommand prep2 = conn.CreateCommand();
        prep2.CommandText = "select @start:=?, @inc:=?, @end_index:=?";
        prep2.Parameters.Add(new H2Parameter());
        prep2.Parameters.Add(new H2Parameter());
        prep2.Parameters.Add(new H2Parameter());
        prep2.Parameters[0].Value = 10;
        prep2.Parameters[1].Value = 2;
        prep2.Parameters[2].Value = 14;
        using (DbDataReader rs = prep2.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }

        using (DbDataReader rs = prep.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(10, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(12, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(14, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        prep2.Parameters[0].Value = 100;
        prep2.Parameters[1].Value = 3;
        prep2.Parameters[2].Value = 103;
        using (DbDataReader rs = prep2.ExecuteReader())
        {
            AssertTrue(rs.Read());
        }

        using (DbDataReader rs = prep.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(100, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(103, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }

        prep.Dispose();
        prep = conn.CreateCommand();
        prep.CommandText = "with recursive t(n) as "
            + "(select ? union all select n+? from t where n<?) "
            + "select * from t";
        prep.Parameters.Add(new H2Parameter());
        prep.Parameters.Add(new H2Parameter());
        prep.Parameters.Add(new H2Parameter());
        prep.Parameters[0].Value = 10;
        prep.Parameters[1].Value = 2;
        prep.Parameters[2].Value = 14;
        using (DbDataReader rs = prep.ExecuteReader())
        {
            AssertResultSetOrdered(rs, new string?[][] { ["10"], ["12"], ["14"] });
        }

        prep.Parameters[0].Value = 100;
        prep.Parameters[1].Value = 3;
        prep.Parameters[2].Value = 103;
        using (DbDataReader rs = prep.ExecuteReader())
        {
            AssertResultSetOrdered(rs, new string?[][] { ["100"], ["103"] });
        }

        prep.Dispose();

        stat.CommandText = "with recursive t(i, s, d) as "
            + "(select 1, '.', localtimestamp union all"
            + " select i+1, s||'.', d from t where i<3)"
            + " select * from t";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertResultSetMeta(rs, 3, ["I", "S", "D"],
                [typeof(int), typeof(string), typeof(DateTime)],
                null, null);
        }

        stat.CommandText = "select x from system_range(1,5) "
            + "where x not in (with recursive w(x) as (select 1 union all select x+1 from w where x<3) "
            + "select x from w)";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertResultSetOrdered(rs, new string?[][] { ["4"], ["5"] });
        }
    }

    private void TestSimpleUnion()
    {
        DeleteDb("recursiveQueries");
        using DbConnection conn = GetConnection("recursiveQueries");
        DbCommand stat = conn.CreateCommand();

        stat.CommandText = "with recursive t(n) as "
            + "(select 1 union select n+1 from t where n<3) "
            + "select * from t";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(2, rs.GetInt32(0));
            AssertTrue(rs.Read());
            AssertEquals(3, rs.GetInt32(0));
            AssertFalse(rs.Read());
        }
    }

    private void TestParameters()
    {
        DeleteDb("recursiveQueries");
        using DbConnection conn = GetConnection("recursiveQueries");
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "WITH RECURSIVE T1(F1, F2) AS (\n"
            + "    SELECT CAST(? AS INT), CAST(? AS VARCHAR(15))\n"
            + "    UNION ALL\n"
            + "    SELECT (T1.F1 + CAST(? AS INT)), CAST((CAST(? AS VARCHAR) || T1.F2) AS VARCHAR(15))\n"
            + "    FROM T1 WHERE T1.F1 < 10\n"
            + "  ),\n"
            + "T2(G1, G2) AS (\n"
            + "    SELECT CAST(? AS INT), CAST(? AS VARCHAR(15))\n"
            + "    UNION ALL\n"
            + "    SELECT (T2.G1 + 1), CAST(('b' || T2.G2) AS VARCHAR(15))\n"
            + "    FROM T2 WHERE T2.G1 < 10\n"
            + "  )\n"
            + "SELECT T1.F1, T1.F2, T2.G1, T2.G2 FROM T1 JOIN T2 ON T1.F1 = T2.G1";
        prep.Parameters.Add(new H2Parameter { Value = 1 });
        prep.Parameters.Add(new H2Parameter { Value = "a" });
        prep.Parameters.Add(new H2Parameter { Value = 1 });
        prep.Parameters.Add(new H2Parameter { Value = "a" });
        prep.Parameters.Add(new H2Parameter { Value = 1 });
        prep.Parameters.Add(new H2Parameter { Value = "b" });
        using (DbDataReader rs = prep.ExecuteReader())
        {
            StringBuilder a = new(10), b = new(10);
            for (int i = 1; i <= 10; i++)
            {
                _ = a.Append('a');
                _ = b.Append('b');
                AssertTrue(rs.Read());
                AssertEquals(i, rs.GetInt32(0));
                AssertEquals(a.ToString(), rs.GetString(1));
                AssertEquals(i, rs.GetInt32(2));
                AssertEquals(b.ToString(), rs.GetString(3));
            }

            AssertFalse(rs.Read());
        }
    }
}
