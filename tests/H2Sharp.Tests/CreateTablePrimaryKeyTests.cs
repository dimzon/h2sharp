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
 * write then comes from the primary's own map, and its message starts at
 * "PRIMARY KEY ON <table>(<key>)".
 *
 * The reference prefixes that message with the SQL of the owning PRIMARY KEY constraint
 * ("PUBLIC.CONSTRAINT_5 "). The constraint layer is not ported, so the compared fact
 * starts at "PRIMARY KEY ON" on the Java side too; everything from there on must be
 * identical, including the row rendering with its key comment. Table-level constraints
 * -- "PRIMARY KEY(ID)" written after the columns -- are not parsed yet, so no case here
 * uses one; a column-level PRIMARY KEY is the parsed one, and it is one constraint over
 * one column, so two of them are two primary keys and error 90017, not one composite key.
 */
using System;
using System.Collections.Generic;
using System.Linq;
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
    /// and created again receives the same name, because the drop freed it.
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
                    int i = m.indexOf("PRIMARY KEY ON");
                    int j = i < 0 ? -1 : m.indexOf('"', i);
                    return "ERR " + e.getErrorCode() + " | " + (i >= 0 && j > i ? m.substring(i, j) : "?");
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

        return lines;
    }

    private static H2Connection Open(string name)
    {
        H2Connection connection = new("jdbc:h2:mem:pk_" + name);
        connection.Open();
        return connection;
    }

    private static string Exec(H2Connection connection, string sql)
    {
        try
        {
            using System.Data.Common.DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            _ = command.ExecuteNonQuery();
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
        Engine.SessionLocal session = connection.Session
            ?? throw new InvalidOperationException("The connection is closed.");
        Tables.Table? table = session.GetDatabase().GetMainSchema()!
            .FindTableOrView(session, "T");
        if (table is null)
        {
            return "NO-TABLE";
        }

        return string.Join(", ", table.GetIndexes().Select(i => i.GetName()).Where(n => n is not null));
    }

    private static string Count(H2Connection connection)
    {
        try
        {
            using System.Data.Common.DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM T";
            return "PRESENT " + command.ExecuteScalar();
        }
        catch (H2SharpException ex)
        {
            return "ERR " + ex.H2ErrorCode;
        }
    }

    /// <summary>
    /// The duplicate-key fact: the error code, and the message from "PRIMARY KEY ON" to the
    /// quote that closes it -- the part the constraint layer would have prefixed, and the
    /// part that must be identical once it is ported.
    /// </summary>
    private static string Dup(H2Connection connection, string sql)
    {
        try
        {
            using System.Data.Common.DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            _ = command.ExecuteNonQuery();
            return "OK";
        }
        catch (H2SharpException ex)
        {
            string m = ex.Message;
            int i = m.IndexOf("PRIMARY KEY ON", StringComparison.Ordinal);
            int j = i < 0 ? -1 : m.IndexOf('"', i);
            return "ERR " + ex.H2ErrorCode + " | " + (i >= 0 && j > i ? m[i..j] : "?");
        }
    }
}
