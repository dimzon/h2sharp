// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestSetCollation.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; setInt/setString are parameter
// values, and the list equality of assertEquals is the element-wise array comparison of
// TestBase. The reference's main() entry is the runner; xUnit runs Test() directly.
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests SET COLLATION and the collation a reopened database keeps.
/// </summary>
public sealed class TestSetCollation : TestDb
{
    private static readonly string?[] TestStrings = ["A", "Ä", "AA", "B", "$", "1A", null];

    private const string DbName = "collator";

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    [Trait("Category", "BlockedByPort")]
    public override void Test()
    {
        // BlockedByPort: the named collations it exercises (DE, CHARSET_CP500) are
        // not ported (D-002); the platform's collation weights disagree with the
        // reference's.
        TestDefaultCollator();
        TestCp500Collator();
        TestDeCollator();
        TestUrlParameter();
        TestReopenDatabase();
        TestReopenDatabaseWithUrlParameter();
        TestReopenDatabaseWithDifferentCollationInUrl();
        TestReopenDatabaseWithSameCollationInUrl();
    }

    private void TestDefaultCollator()
    {
        AssertEquals(new string?[] { null, "$", "1A", "A", "AA", "B", "Ä" },
            OrderedWithCollator(null).ToArray());
    }

    private void TestDeCollator()
    {
        AssertEquals(new string?[] { null, "$", "1A", "A", "Ä", "AA", "B" },
            OrderedWithCollator("DE").ToArray());
        AssertEquals(new string?[] { null, "$", "1A", "A", "Ä", "AA", "B" },
            OrderedWithCollator("DEFAULT_DE").ToArray());
    }

    private void TestCp500Collator()
    {
        // IBM z/OS codepage
        AssertEquals(new string?[] { null, "A", "AA", "B", "1A", "$", "Ä" },
            OrderedWithCollator("CHARSET_CP500").ToArray());
    }

    private void TestUrlParameter()
    {
        // Specifying the collator in the JDBC Url should have the same effect
        // as setting it with a set statement
        Config.Collation = "CHARSET_CP500";
        try
        {
            AssertEquals(new string?[] { null, "A", "AA", "B", "1A", "$", "Ä" },
                OrderedWithCollator(null).ToArray());
        }
        finally
        {
            Config.Collation = null;
        }
    }

    private void TestReopenDatabase()
    {
        if (Config.Memory)
        {
            return;
        }

        _ = OrderedWithCollator("DE");

        using (DbConnection con = GetConnection(DbName))
        {
            InsertValues(con, ["A", "Ä"], 100);

            AssertEquals(new string?[] { null, "$", "1A", "A", "A", "Ä", "Ä", "AA", "B" },
                LoadTableValues(con).ToArray());
        }
    }

    private void TestReopenDatabaseWithUrlParameter()
    {
        if (Config.Memory)
        {
            return;
        }

        Config.Collation = "DE";
        try
        {
            _ = OrderedWithCollator(null);
        }
        finally
        {
            Config.Collation = null;
        }

        // reopen the database without specifying a collation in the url.
        // This should keep the initial collation.
        using (DbConnection con = GetConnection(DbName))
        {
            InsertValues(con, ["A", "Ä"], 100);

            AssertEquals(new string?[] { null, "$", "1A", "A", "A", "Ä", "Ä", "AA", "B" },
                LoadTableValues(con).ToArray());
        }
    }

    private void TestReopenDatabaseWithDifferentCollationInUrl()
    {
        if (Config.Memory)
        {
            return;
        }

        Config.Collation = "DE";
        try
        {
            _ = OrderedWithCollator(null);
        }
        finally
        {
            Config.Collation = null;
        }

        Config.Collation = "CHARSET_CP500";
        try
        {
            _ = GetConnection(DbName);
            Fail();
        }
        catch (DbException)
        {
            // expected
        }
        finally
        {
            Config.Collation = null;
        }
    }

    private void TestReopenDatabaseWithSameCollationInUrl()
    {
        if (Config.Memory)
        {
            return;
        }

        Config.Collation = "DE";
        try
        {
            _ = OrderedWithCollator(null);
        }
        finally
        {
            Config.Collation = null;
        }

        Config.Collation = "DE";
        try
        {
            using (DbConnection con = GetConnection(DbName))
            {
                InsertValues(con, ["A", "Ä"], 100);

                AssertEquals(new string?[] { null, "$", "1A", "A", "A", "Ä", "Ä", "AA", "B" },
                    LoadTableValues(con).ToArray());
            }
        }
        finally
        {
            Config.Collation = null;
        }
    }

    private List<string?> OrderedWithCollator(string? collator)
    {
        DeleteDb(DbName);
        using DbConnection con = GetConnection(DbName);
        DbCommand statement = con.CreateCommand();
        if (collator is not null)
        {
            statement.CommandText = "SET COLLATION " + collator;
            _ = statement.ExecuteNonQuery();
        }

        statement.CommandText = "CREATE TABLE charsettable(id INT PRIMARY KEY, testvalue VARCHAR(50))";
        _ = statement.ExecuteNonQuery();

        InsertValues(con, TestStrings, 1);

        return LoadTableValues(con);
    }

    private static void InsertValues(DbConnection con, string?[] values, int startId)
    {
        using DbCommand ps = con.CreateCommand();
        ps.CommandText = "INSERT INTO charsettable VALUES (?, ?)";
        ps.Parameters.Add(new H2Parameter());
        ps.Parameters.Add(new H2Parameter());
        int id = startId;
        foreach (string? value in values)
        {
            ps.Parameters[0].Value = id++;
            ps.Parameters[1].Value = value is null ? DBNull.Value : value;
            _ = ps.ExecuteNonQuery();
        }
    }

    private static List<string?> LoadTableValues(DbConnection con)
    {
        List<string?> results = [];
        DbCommand statement = con.CreateCommand();
        statement.CommandText = "select testvalue from charsettable order by testvalue";
        using DbDataReader resultSet = statement.ExecuteReader();
        while (resultSet.Read())
        {
            results.Add(resultSet.IsDBNull(0) ? null : resultSet.GetString(0));
        }

        return results;
    }
}
