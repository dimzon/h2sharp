/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 2.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the in-memory database rules, run against the reference through
 * JDBC on the same cases.
 *
 * This is the part of connecting that has no oracle until a connection exists, and it is the
 * part with a rule in it: a *named* in-memory database belongs to the process, not to the
 * connection, and an *unnamed* one belongs to the connection. Two connections to
 * "jdbc:h2:mem:x" see one database; two to "jdbc:h2:mem:" see two. Close the last session of a
 * named one and the name is free again, so a new connection finds an empty database.
 *
 * The reference is asked the same questions over JDBC, and the answer is compared as the
 * observable facts rather than as text: same database or not, table visible or not, database
 * back or empty. Those are what a caller depends on; the wording of any message is not.
 *
 * Dapper is used as the caller convenience only: Execute/ExecuteScalar over IDbConnection
 * with an H2SharpException as the statement's failure.
 */
using System.Collections.Generic;
using Dapper;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class InMemoryDatabaseOracleTests
{
    /// <summary>
    /// The sharing rules, in the words the reference would use. Our side answers the same
    /// questions through the same statements, so the day the parser lands this compares
    /// behaviour rather than intentions.
    /// </summary>
    [JavaOracleWithSqlFact]
    public void MemoryDatabaseSharing_MatchesReference()
    {
        string body = """
                      String[] cases = { "mem:shared", "mem:", "mem:gone" };
                      for (String db : cases) {
                        java.sql.Connection a = java.sql.DriverManager.getConnection("jdbc:h2:" + db);
                        java.sql.Connection b = java.sql.DriverManager.getConnection("jdbc:h2:" + db);
                        System.out.println(db + " both-open");
                        try {
                          a.createStatement().execute("CREATE TABLE T(ID INT)");
                          System.out.println("  create ok");
                        } catch (java.sql.SQLException ex) {
                          System.out.println("  create " + ex.getErrorCode());
                        }
                        // Whether b can see what a created is the sharing rule in one question.
                        try {
                          java.sql.ResultSet rs = b.createStatement().executeQuery(
                              "SELECT COUNT(*) FROM T");
                          rs.next();
                          System.out.println("  b sees " + rs.getInt(1));
                        } catch (java.sql.SQLException ex) {
                          System.out.println("  b sees no-table " + ex.getErrorCode());
                        }
                        b.close();
                        a.createStatement().execute("DROP TABLE T");
                        a.close();
                        // Reopening after the last close starts a new, empty database.
                        java.sql.Connection c = java.sql.DriverManager.getConnection("jdbc:h2:" + db);
                        try {
                          java.sql.ResultSet rs = c.createStatement().executeQuery(
                              "SELECT COUNT(*) FROM T");
                          rs.next();
                          System.out.println("  after-close " + rs.getInt(1));
                        } catch (java.sql.SQLException ex) {
                          System.out.println("  after-close no-table " + ex.getErrorCode());
                        }
                        c.close();
                      }
            """;

        List<string> java = OracleRunner.Run([], body);
        List<string> csharp = Describe();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static string ToClassic(string name)
        => name == "mem:"
            ? "Storage=Memory"
            : "Data Source=" + name.Substring("mem:".Length) + ";Storage=Memory";
    private static List<string> Describe()
    {
        List<string> lines = [];
        foreach (string db in new[] { "mem:shared", "mem:", "mem:gone" })
        {
            lines.Add(db + " both-open");
            using H2Connection a = new(ToClassic(db));
            using H2Connection b = new(ToClassic(db));
            a.Open();
            b.Open();

            lines.Add("  create " + Run(a, "CREATE TABLE T(ID INT)"));
            lines.Add("  b sees " + Count(b, "SELECT COUNT(*) FROM T"));
            b.Close();
            // Cleanup, not compared: the reference prints no drop line.
            _ = Run(a, "DROP TABLE T");
            a.Close();

            using H2Connection c = new(ToClassic(db));
            c.Open();
            lines.Add("  after-close " + Count(c, "SELECT COUNT(*) FROM T"));
            c.Close();
        }

        return lines;
    }

    /// <summary>
    /// Runs a statement and answers the way the Java side answers: what it did, or the error
    /// code it refused with.
    /// </summary>
    private static string Run(H2Connection connection, string sql)
    {
        try
        {
            _ = connection.Execute(sql);
            return "ok";
        }
        catch (H2SharpException ex)
        {
            return ex.H2ErrorCode.ToString();
        }
    }

    private static string Count(H2Connection connection, string sql)
    {
        try
        {
            object? value = connection.ExecuteScalar(sql);
            return value?.ToString() ?? "null";
        }
        catch (H2SharpException ex)
        {
            return "no-table " + ex.H2ErrorCode;
        }
    }
}

