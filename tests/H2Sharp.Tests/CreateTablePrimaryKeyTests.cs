/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What CREATE TABLE ... PRIMARY KEY leaves behind, checked against the reference.
 *
 * The key of a single-column table is not a map of its own: the statement narrows the
 * primary index to the key column and hands the constraint an MVDelegateIndex over it,
 * named the way the reference names it -- PRIMARY_KEY_<hash>, where the hash is the
 * Java hash of the *table* name in lowercase hex, uppercased for a-f. The duplicate
 * write then comes from the primary's own map, and its message is prefixed with the SQL
 * of the owning PRIMARY KEY constraint -- "PUBLIC.CONSTRAINT_5" when the key was
 * written without a name, "PUBLIC.PKX" when one was -- followed by
 * "PRIMARY KEY ON <table>(<key>)" and the row with its key comment. The whole body is
 * compared, quote to quote, so a missing or misspelled constraint name fails the test.
 *
 * The constraint is written either after the column ("ID INT CONSTRAINT PKX PRIMARY
 * KEY") or as a table-level item ("PRIMARY KEY(ID)", named with the optional
 * "CONSTRAINT <name>" prefix); both forms land in the same ALTER TABLE ADD CONSTRAINT
 * the constraint layer executes. Two primary keys are compared by columns first -- same
 * columns with a named first key ignore the second, anything else is error 90017 --
 * which is why a second same-named key never reaches the constraint-name check here.
 *
 * Dapper is used as the caller convenience only: Execute/ExecuteScalar over IDbConnection
 * with an H2SharpException as the statement's failure.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class CreateTablePrimaryKeyTests
{
    /// <summary>
    /// The key lifecycle: a single-column primary key answers with its delegate index and
    /// its name, a second write of the same key is a duplicate, a second primary key over
    /// another column is refused and takes the whole table with it, and a table dropped
    /// and created again receives the same names, because the drop freed them.
    /// </summary>
    [JavaOracleFact]
    public void ThePrimaryKeyOfAColumnLivesAndDiesTheWayTheReferenceSays()
    {
        List<string> java = OracleRunner.Run([], JavaBody);
        List<string> csharp = Describe();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private const string JavaBody = """
        class H {
            java.sql.Connection open(String name) throws Exception {
                return java.sql.DriverManager.getConnection("jdbc:h2:mem:pk_" + name);
            }
            String exec(java.sql.Connection c, String sql) {
                try { c.createStatement().execute(sql); return "OK"; }
                catch (java.sql.SQLException e) { return "ERR " + e.getErrorCode(); }
            }
            String indexes(java.sql.Connection c) {
                org.h2.engine.SessionLocal s =
                    (org.h2.engine.SessionLocal) ((org.h2.jdbc.JdbcConnection) c).getSession();
                org.h2.table.Table t = s.getDatabase().getSchema("PUBLIC").getTableOrView(s, "T");
                if (t == null) {
                    return "NO-TABLE";
                }
                StringBuilder b = new StringBuilder();
                for (org.h2.index.Index idx : t.getIndexes()) {
                    if (idx.getName() != null) {
                        if (b.length() > 0) {
                            b.append(", ");
                        }
                        b.append(idx.getName());
                    }
                }
                return b.toString();
            }
            String constraints(java.sql.Connection c) {
                org.h2.engine.SessionLocal s =
                    (org.h2.engine.SessionLocal) ((org.h2.jdbc.JdbcConnection) c).getSession();
                org.h2.table.Table t = s.getDatabase().getSchema("PUBLIC").getTableOrView(s, "T");
                if (t == null) {
                    return "NO-TABLE";
                }
                StringBuilder b = new StringBuilder();
                for (org.h2.constraint.Constraint x : t.getConstraints()) {
                    if (b.length() > 0) {
                        b.append(", ");
                    }
                    b.append(x.getName());
                }
                return b.toString();
            }
            String count(java.sql.Connection c) {
                try {
                    java.sql.ResultSet r = c.createStatement().executeQuery("SELECT COUNT(*) FROM T");
                    r.next();
                    return "PRESENT " + r.getInt(1);
                } catch (java.sql.SQLException e) { return "ERR " + e.getErrorCode(); }
            }
            String dup(java.sql.Connection c, String sql) {
                try { c.createStatement().execute(sql); return "OK"; }
                catch (java.sql.SQLException e) {
                    String m = e.getMessage();
                    int k = m.indexOf('"');
                    int j = k < 0 ? -1 : m.indexOf('"', k + 1);
                    return "ERR " + e.getErrorCode() + " | " + (k >= 0 && j > k ? m.substring(k + 1, j) : "?");
                }
            }
        }
        H h = new H();

        java.sql.Connection f = h.open("f");
        System.out.println("F | CREATE | " + h.exec(f, "CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR)"));
        System.out.println("F | INDEXES | " + h.indexes(f));
        System.out.println("F | INSERT | " + h.exec(f, "INSERT INTO T VALUES(1, 'a')"));
        System.out.println("F | DUP | " + h.dup(f, "INSERT INTO T VALUES(1, 'a')"));
        System.out.println("F | COUNT | " + h.count(f));
        f.close();

        java.sql.Connection a = h.open("a");
        System.out.println("A | CREATE | " + h.exec(a, "CREATE TABLE T(A INT PRIMARY KEY, B INT PRIMARY KEY)"));
        System.out.println("A | COUNT | " + h.count(a));
        a.close();

        java.sql.Connection g = h.open("g");
        System.out.println("G | CREATE | " + h.exec(g, "CREATE TABLE T(ID INT PRIMARY KEY)"));
        System.out.println("G | INDEXES | " + h.indexes(g));
        System.out.println("G | DROP | " + h.exec(g, "DROP TABLE T"));
        System.out.println("G | COUNT | " + h.count(g));
        System.out.println("G | CREATE2 | " + h.exec(g, "CREATE TABLE T(ID INT PRIMARY KEY)"));
        System.out.println("G | INDEXES2 | " + h.indexes(g));
        g.close();

        java.sql.Connection hh = h.open("h");
        System.out.println("H | CREATE | " + h.exec(hh, "CREATE TABLE T(ID INT CONSTRAINT PKX PRIMARY KEY)"));
        System.out.println("H | INDEXES | " + h.indexes(hh));
        System.out.println("H | CONSTRAINTS | " + h.constraints(hh));
        System.out.println("H | INSERT | " + h.exec(hh, "INSERT INTO T VALUES(1)"));
        System.out.println("H | DUP | " + h.dup(hh, "INSERT INTO T VALUES(1)"));
        hh.close();

        java.sql.Connection i = h.open("i");
        System.out.println("I | CREATE | " + h.exec(i, "CREATE TABLE T(ID INT, PRIMARY KEY(ID))"));
        System.out.println("I | INDEXES | " + h.indexes(i));
        System.out.println("I | CONSTRAINTS | " + h.constraints(i));
        System.out.println("I | INSERT | " + h.exec(i, "INSERT INTO T VALUES(1)"));
        System.out.println("I | DUP | " + h.dup(i, "INSERT INTO T VALUES(1)"));
        i.close();

        java.sql.Connection j = h.open("j");
        System.out.println("J | CREATE | " + h.exec(j, "CREATE TABLE T(ID INT, CONSTRAINT PKY PRIMARY KEY(ID))"));
        System.out.println("J | CONSTRAINTS | " + h.constraints(j));
        System.out.println("J | INSERT | " + h.exec(j, "INSERT INTO T VALUES(1)"));
        System.out.println("J | DUP | " + h.dup(j, "INSERT INTO T VALUES(1)"));
        System.out.println("J | DROP | " + h.exec(j, "DROP TABLE T"));
        System.out.println("J | CREATE2 | " + h.exec(j, "CREATE TABLE T(ID INT, CONSTRAINT CONSTRAINT_5 PRIMARY KEY(ID))"));
        System.out.println("J | CONSTRAINTS2 | " + h.constraints(j));
        j.close();

        java.sql.Connection k = h.open("k");
        System.out.println("K | SAME-NAME | " + h.exec(k, "CREATE TABLE T(A INT, CONSTRAINT PKX PRIMARY KEY(A), CONSTRAINT PKX PRIMARY KEY(A))"));
        System.out.println("K | CONSTRAINTS | " + h.constraints(k));
        System.out.println("K | OTHER-COL | " + h.exec(k, "CREATE TABLE T(A INT, CONSTRAINT PKZ PRIMARY KEY(A), CONSTRAINT IF NOT EXISTS PKZ PRIMARY KEY(B))"));
        k.close();
        """;

    private static List<string> Describe()
    {
        List<string> lines = [];

        using (H2Connection f = Open("f"))
        {
            lines.Add("F | CREATE | " + Exec(f, "CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR)"));
            lines.Add("F | INDEXES | " + Indexes(f));
            lines.Add("F | INSERT | " + Exec(f, "INSERT INTO T VALUES(1, 'a')"));
            lines.Add("F | DUP | " + Dup(f, "INSERT INTO T VALUES(1, 'a')"));
            lines.Add("F | COUNT | " + Count(f));
        }

        using (H2Connection a = Open("a"))
        {
            lines.Add("A | CREATE | " + Exec(a, "CREATE TABLE T(A INT PRIMARY KEY, B INT PRIMARY KEY)"));
            lines.Add("A | COUNT | " + Count(a));
        }

        using (H2Connection g = Open("g"))
        {
            lines.Add("G | CREATE | " + Exec(g, "CREATE TABLE T(ID INT PRIMARY KEY)"));
            lines.Add("G | INDEXES | " + Indexes(g));
            lines.Add("G | DROP | " + Exec(g, "DROP TABLE T"));
            lines.Add("G | COUNT | " + Count(g));
            lines.Add("G | CREATE2 | " + Exec(g, "CREATE TABLE T(ID INT PRIMARY KEY)"));
            lines.Add("G | INDEXES2 | " + Indexes(g));
        }

        using (H2Connection h = Open("h"))
        {
            lines.Add("H | CREATE | " + Exec(h, "CREATE TABLE T(ID INT CONSTRAINT PKX PRIMARY KEY)"));
            lines.Add("H | INDEXES | " + Indexes(h));
            lines.Add("H | CONSTRAINTS | " + Constraints(h));
            lines.Add("H | INSERT | " + Exec(h, "INSERT INTO T VALUES(1)"));
            lines.Add("H | DUP | " + Dup(h, "INSERT INTO T VALUES(1)"));
        }

        using (H2Connection i = Open("i"))
        {
            lines.Add("I | CREATE | " + Exec(i, "CREATE TABLE T(ID INT, PRIMARY KEY(ID))"));
            lines.Add("I | INDEXES | " + Indexes(i));
            lines.Add("I | CONSTRAINTS | " + Constraints(i));
            lines.Add("I | INSERT | " + Exec(i, "INSERT INTO T VALUES(1)"));
            lines.Add("I | DUP | " + Dup(i, "INSERT INTO T VALUES(1)"));
        }

        using (H2Connection j = Open("j"))
        {
            lines.Add("J | CREATE | " + Exec(j, "CREATE TABLE T(ID INT, CONSTRAINT PKY PRIMARY KEY(ID))"));
            lines.Add("J | CONSTRAINTS | " + Constraints(j));
            lines.Add("J | INSERT | " + Exec(j, "INSERT INTO T VALUES(1)"));
            lines.Add("J | DUP | " + Dup(j, "INSERT INTO T VALUES(1)"));
            lines.Add("J | DROP | " + Exec(j, "DROP TABLE T"));
            lines.Add("J | CREATE2 | " + Exec(j, "CREATE TABLE T(ID INT, CONSTRAINT CONSTRAINT_5 PRIMARY KEY(ID))"));
            lines.Add("J | CONSTRAINTS2 | " + Constraints(j));
        }

        using (H2Connection k = Open("k"))
        {
            lines.Add("K | SAME-NAME | " + Exec(k, "CREATE TABLE T(A INT, CONSTRAINT PKX PRIMARY KEY(A), CONSTRAINT PKX PRIMARY KEY(A))"));
            lines.Add("K | CONSTRAINTS | " + Constraints(k));
            lines.Add("K | OTHER-COL | " + Exec(k, "CREATE TABLE T(A INT, CONSTRAINT PKZ PRIMARY KEY(A), CONSTRAINT IF NOT EXISTS PKZ PRIMARY KEY(B))"));
        }

        return lines;
    }

    private static H2Connection Open(string name)
    {
        H2Connection connection = new("Data Source=pk_" + name + ";Storage=Memory");
        connection.Open();
        return connection;
    }

    private static string Exec(H2Connection connection, string sql)
    {
        try
        {
            _ = connection.Execute(sql);
            return "OK";
        }
        catch (H2SharpException ex)
        {
            return "ERR " + ex.H2ErrorCode;
        }
    }

    /// <summary>
    /// The named indexes of T, in the order the table lists them, joined like the Java side
    /// joins them: the scan index carries the table's data-map name, the delegate the key's.
    /// </summary>
    private static string Indexes(H2Connection connection)
    {
        Tables.Table? table = FindTable(connection);
        if (table is null)
        {
            return "NO-TABLE";
        }

        return string.Join(", ", table.GetIndexes().Select(i => i.GetName()).Where(n => n is not null));
    }

    /// <summary>The names of T's constraints, joined like the Java side joins them.</summary>
    private static string Constraints(H2Connection connection)
    {
        Tables.Table? table = FindTable(connection);
        if (table is null)
        {
            return "NO-TABLE";
        }

        return string.Join(", ", table.GetConstraints().Select(c => c.GetName()));
    }

    private static Tables.Table? FindTable(H2Connection connection)
    {
        Engine.SessionLocal session = connection.Session
            ?? throw new InvalidOperationException("The connection is closed.");
        return session.GetDatabase().GetMainSchema()!.FindTableOrView(session, "T");
    }

    private static string Count(H2Connection connection)
    {
        try
        {
            return "PRESENT " + connection.ExecuteScalar("SELECT COUNT(*) FROM T");
        }
        catch (H2SharpException ex)
        {
            return "ERR " + ex.H2ErrorCode;
        }
    }

    /// <summary>
    /// The duplicate-key fact: the error code, and the whole quoted body of the message --
    /// the constraint's SQL, the table's key and the offending row -- which the reference
    /// and this port must render identically.
    /// </summary>
    private static string Dup(H2Connection connection, string sql)
    {
        try
        {
            _ = connection.Execute(sql);
            return "OK";
        }
        catch (H2SharpException ex)
        {
            string m = ex.Message;
            int k = m.IndexOf('"');
            int j = k < 0 ? -1 : m.IndexOf('"', k + 1);
            return "ERR " + ex.H2ErrorCode + " | " + (k >= 0 && j > k ? m[(k + 1)..j] : "?");
        }
    }
}

