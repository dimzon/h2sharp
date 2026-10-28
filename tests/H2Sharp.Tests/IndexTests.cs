/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What CREATE INDEX, DROP INDEX and ALTER TABLE DROP PRIMARY KEY do, checked against the
 * reference.
 *
 * An index created on a table that already has rows is built by scanning them: a non-unique
 * index simply files every row, a unique index refuses the whole build at the first pair of
 * equal keys and leaves no half-built index or reserved name behind. After that the index is
 * maintained by every INSERT, UPDATE and DELETE, which is what the row counts below see.
 *
 * A unique index also enforces itself on later writes, with the reference's message: the
 * bare index name appears twice, "INDEX PUBLIC.UX ON PUBLIC.T(A)", with no constraint SQL
 * because no constraint owns the index. DROP INDEX frees the name; an index that belongs to
 * a PRIMARY KEY is refused with 90085 and only goes away through ALTER TABLE DROP PRIMARY
 * KEY, which is the same DROP INDEX found by the primary key's index name.
 *
 * ALTER TABLE ADD PRIMARY KEY on a table with rows goes through the same rebuild, so the
 * duplicate-key check runs while the key is being built. The failure must free the generated
 * name so the next ADD receives it again, and must leave the constraint catalogue empty --
 * both are observed here.
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
public sealed class IndexTests
{
    /// <summary>
    /// The CREATE INDEX / DROP INDEX lifecycle: unique and non-unique indexes on empty and
    /// filled tables, the duplicate message, generated names, IF NOT EXISTS, the unknown
    /// column, and the missing-index errors on DROP.
    /// </summary>
    [JavaOracleFact]
    public void CreatingAndDroppingIndexesMatchesTheReference()
    {
        List<string> java = OracleRunner.Run([], JavaBody);
        List<string> csharp = Describe();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// ALTER TABLE ADD/DROP PRIMARY KEY on tables with rows: the rebuild takes the existing
    /// rows, duplicates refuse it and free the name, and dropping the key turns the unique
    /// index back into an ordinary table that accepts duplicates.
    /// </summary>
    [JavaOracleFact]
    public void AddingAPrimaryKeyToAFilledTableMatchesTheReference()
    {
        List<string> java = OracleRunner.Run([], JavaRebuildBody);
        List<string> csharp = DescribeRebuild();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private const string JavaBody = """
        class H {
            java.sql.Connection open(String name) throws Exception {
                return java.sql.DriverManager.getConnection("jdbc:h2:mem:ix_" + name);
            }
            String exec(java.sql.Connection c, String sql) {
                try { c.createStatement().execute(sql); return "OK"; }
                catch (java.sql.SQLException e) { return "ERR " + e.getErrorCode(); }
            }
            String count(java.sql.Connection c, String sql) {
                try {
                    java.sql.ResultSet r = c.createStatement().executeQuery(sql);
                    r.next();
                    return "OK " + r.getInt(1);
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
            String indexes(java.sql.Connection c, String table) {
                org.h2.engine.SessionLocal s =
                    (org.h2.engine.SessionLocal) ((org.h2.jdbc.JdbcConnection) c).getSession();
                org.h2.table.Table t = s.getDatabase().getSchema("PUBLIC").findTableOrView(s, table);
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
            String constraints(java.sql.Connection c, String table) {
                org.h2.engine.SessionLocal s =
                    (org.h2.engine.SessionLocal) ((org.h2.jdbc.JdbcConnection) c).getSession();
                org.h2.table.Table t = s.getDatabase().getSchema("PUBLIC").findTableOrView(s, table);
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
        }
        H h = new H();

        java.sql.Connection a = h.open("a");
        System.out.println("A | CREATE | " + h.exec(a, "CREATE TABLE T(ID INT NOT NULL, A INT)"));
        System.out.println("A | CREATE-UNIQUE | " + h.exec(a, "CREATE UNIQUE INDEX UX ON T(A)"));
        System.out.println("A | INDEXES | " + h.indexes(a, "T"));
        System.out.println("A | INSERT1 | " + h.exec(a, "INSERT INTO T VALUES(1, 1)"));
        System.out.println("A | INSERT2 | " + h.exec(a, "INSERT INTO T VALUES(2, 2)"));
        System.out.println("A | INSERT3-DUP | " + h.dup(a, "INSERT INTO T VALUES(3, 1)"));
        System.out.println("A | INSERT-NULL | " + h.exec(a, "INSERT INTO T VALUES(4, NULL)"));
        System.out.println("A | INSERT-NULL2 | " + h.exec(a, "INSERT INTO T VALUES(5, NULL)"));
        System.out.println("A | UPDATE-DUP | " + h.dup(a, "UPDATE T SET A = 1 WHERE ID = 2"));
        System.out.println("A | COUNT | " + h.count(a, "SELECT COUNT(*) FROM T"));
        System.out.println("A | CREATE-IFNE | " + h.exec(a, "CREATE UNIQUE INDEX IF NOT EXISTS UX ON T(A)"));
        System.out.println("A | CREATE-DUP | " + h.exec(a, "CREATE UNIQUE INDEX UX ON T(A)"));
        System.out.println("A | CREATE-UNNAMED | " + h.exec(a, "CREATE INDEX ON T(A)"));
        System.out.println("A | INDEXES2 | " + h.indexes(a, "T"));
        System.out.println("A | CREATE-BAD-COLUMN | " + h.exec(a, "CREATE INDEX BX ON T(NOPE)"));
        System.out.println("A | CREATE-DESC | " + h.exec(a, "CREATE INDEX DX ON T(A DESC NULLS FIRST)"));
        System.out.println("A | INDEXES3 | " + h.indexes(a, "T"));
        System.out.println("A | DROP-UX | " + h.exec(a, "DROP INDEX UX"));
        System.out.println("A | DROP-UX-AGAIN | " + h.exec(a, "DROP INDEX UX"));
        System.out.println("A | DROP-UX-IF | " + h.exec(a, "DROP INDEX IF EXISTS UX"));
        System.out.println("A | INDEXES4 | " + h.indexes(a, "T"));
        System.out.println("A | DROP-MYSQL | " + h.exec(a, "DROP INDEX DX ON T"));
        System.out.println("A | DROP-MISSING-TABLE | " + h.exec(a, "DROP TABLE T"));
        System.out.println("A | INDEXES5 | " + h.indexes(a, "T"));
        a.close();

        java.sql.Connection b = h.open("b");
        System.out.println("B | CREATE | " + h.exec(b, "CREATE TABLE T(ID INT NOT NULL, A INT)"));
        System.out.println("B | INSERT1 | " + h.exec(b, "INSERT INTO T VALUES(1, 1)"));
        System.out.println("B | INSERT2 | " + h.exec(b, "INSERT INTO T VALUES(2, 2)"));
        System.out.println("B | INSERT3 | " + h.exec(b, "INSERT INTO T VALUES(3, 1)"));
        System.out.println("B | CREATE | " + h.exec(b, "CREATE INDEX IX ON T(A)"));
        System.out.println("B | INDEXES | " + h.indexes(b, "T"));
        System.out.println("B | COUNT-A1 | " + h.count(b, "SELECT COUNT(*) FROM T WHERE A = 1"));
        System.out.println("B | UPDATE | " + h.exec(b, "UPDATE T SET A = 2 WHERE ID = 3"));
        System.out.println("B | COUNT-A2 | " + h.count(b, "SELECT COUNT(*) FROM T WHERE A = 2"));
        System.out.println("B | DELETE | " + h.exec(b, "DELETE FROM T WHERE ID = 1"));
        System.out.println("B | COUNT-A1-B | " + h.count(b, "SELECT COUNT(*) FROM T WHERE A = 1"));
        System.out.println("B | DROP | " + h.exec(b, "DROP INDEX IX"));
        System.out.println("B | INDEXES2 | " + h.indexes(b, "T"));
        b.close();

        java.sql.Connection c = h.open("c");
        System.out.println("C | CREATE | " + h.exec(c, "CREATE TABLE T(ID INT NOT NULL, A INT)"));
        System.out.println("C | INSERT1 | " + h.exec(c, "INSERT INTO T VALUES(1, 5)"));
        System.out.println("C | INSERT2 | " + h.exec(c, "INSERT INTO T VALUES(2, 5)"));
        System.out.println("C | CREATE-DUP | " + h.dup(c, "CREATE UNIQUE INDEX UX ON T(A)"));
        System.out.println("C | INDEXES | " + h.indexes(c, "T"));
        System.out.println("C | DELETE | " + h.exec(c, "DELETE FROM T WHERE ID = 2"));
        System.out.println("C | CREATE | " + h.exec(c, "CREATE UNIQUE INDEX UX ON T(A)"));
        System.out.println("C | INDEXES2 | " + h.indexes(c, "T"));
        c.close();
        """;

    private const string JavaRebuildBody = """
        class H {
            java.sql.Connection open(String name) throws Exception {
                return java.sql.DriverManager.getConnection("jdbc:h2:mem:ix_" + name);
            }
            String exec(java.sql.Connection c, String sql) {
                try { c.createStatement().execute(sql); return "OK"; }
                catch (java.sql.SQLException e) { return "ERR " + e.getErrorCode(); }
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
            String indexes(java.sql.Connection c, String table) {
                org.h2.engine.SessionLocal s =
                    (org.h2.engine.SessionLocal) ((org.h2.jdbc.JdbcConnection) c).getSession();
                org.h2.table.Table t = s.getDatabase().getSchema("PUBLIC").findTableOrView(s, table);
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
            String constraints(java.sql.Connection c, String table) {
                org.h2.engine.SessionLocal s =
                    (org.h2.engine.SessionLocal) ((org.h2.jdbc.JdbcConnection) c).getSession();
                org.h2.table.Table t = s.getDatabase().getSchema("PUBLIC").findTableOrView(s, table);
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
        }
        H h = new H();

        java.sql.Connection d = h.open("d");
        System.out.println("D | CREATE | " + h.exec(d, "CREATE TABLE T(ID INT NOT NULL, A INT)"));
        System.out.println("D | INSERT1 | " + h.exec(d, "INSERT INTO T VALUES(1, 1)"));
        System.out.println("D | INSERT2 | " + h.exec(d, "INSERT INTO T VALUES(2, 2)"));
        System.out.println("D | INSERT3 | " + h.exec(d, "INSERT INTO T VALUES(3, 3)"));
        System.out.println("D | ADD-PK | " + h.exec(d, "ALTER TABLE T ADD PRIMARY KEY(ID)"));
        System.out.println("D | INDEXES | " + h.indexes(d, "T"));
        System.out.println("D | CONSTRAINTS | " + h.constraints(d, "T"));
        System.out.println("D | INSERT-DUP | " + h.dup(d, "INSERT INTO T VALUES(1, 9)"));
        System.out.println("D | INSERT-OK | " + h.exec(d, "INSERT INTO T VALUES(4, 4)"));
        System.out.println("D | DROP-PK | " + h.exec(d, "ALTER TABLE T DROP PRIMARY KEY"));
        System.out.println("D | INDEXES2 | " + h.indexes(d, "T"));
        System.out.println("D | CONSTRAINTS2 | " + h.constraints(d, "T"));
        System.out.println("D | INSERT-DUP-PK | " + h.exec(d, "INSERT INTO T VALUES(4, 5)"));
        System.out.println("D | DROP-PK-AGAIN | " + h.exec(d, "ALTER TABLE T DROP PRIMARY KEY"));
        System.out.println("D | ADD-PK-DUPS | " + h.dup(d, "ALTER TABLE T ADD PRIMARY KEY(ID)"));
        System.out.println("D | INDEXES3 | " + h.indexes(d, "T"));
        System.out.println("D | CONSTRAINTS3 | " + h.constraints(d, "T"));
        System.out.println("D | DELETE-DUP | " + h.exec(d, "DELETE FROM T WHERE ID = 4 AND A = 5"));
        System.out.println("D | ADD-PK-AGAIN | " + h.exec(d, "ALTER TABLE T ADD PRIMARY KEY(ID)"));
        System.out.println("D | INDEXES4 | " + h.indexes(d, "T"));
        System.out.println("D | CONSTRAINTS4 | " + h.constraints(d, "T"));
        System.out.println("D | DROP-INDEX-PK | " + h.dup(d, "DROP INDEX " + h.indexes(d, "T")));
        d.close();
        """;

    private static List<string> Describe()
    {
        List<string> lines = [];

        using (H2Connection a = Open("a"))
        {
            lines.Add("A | CREATE | " + Exec(a, "CREATE TABLE T(ID INT NOT NULL, A INT)"));
            lines.Add("A | CREATE-UNIQUE | " + Exec(a, "CREATE UNIQUE INDEX UX ON T(A)"));
            lines.Add("A | INDEXES | " + Indexes(a, "T"));
            lines.Add("A | INSERT1 | " + Exec(a, "INSERT INTO T VALUES(1, 1)"));
            lines.Add("A | INSERT2 | " + Exec(a, "INSERT INTO T VALUES(2, 2)"));
            lines.Add("A | INSERT3-DUP | " + Dup(a, "INSERT INTO T VALUES(3, 1)"));
            lines.Add("A | INSERT-NULL | " + Exec(a, "INSERT INTO T VALUES(4, NULL)"));
            lines.Add("A | INSERT-NULL2 | " + Exec(a, "INSERT INTO T VALUES(5, NULL)"));
            lines.Add("A | UPDATE-DUP | " + Dup(a, "UPDATE T SET A = 1 WHERE ID = 2"));
            lines.Add("A | COUNT | " + Count(a, "SELECT COUNT(*) FROM T"));
            lines.Add("A | CREATE-IFNE | " + Exec(a, "CREATE UNIQUE INDEX IF NOT EXISTS UX ON T(A)"));
            lines.Add("A | CREATE-DUP | " + Exec(a, "CREATE UNIQUE INDEX UX ON T(A)"));
            lines.Add("A | CREATE-UNNAMED | " + Exec(a, "CREATE INDEX ON T(A)"));
            lines.Add("A | INDEXES2 | " + Indexes(a, "T"));
            lines.Add("A | CREATE-BAD-COLUMN | " + Exec(a, "CREATE INDEX BX ON T(NOPE)"));
            lines.Add("A | CREATE-DESC | " + Exec(a, "CREATE INDEX DX ON T(A DESC NULLS FIRST)"));
            lines.Add("A | INDEXES3 | " + Indexes(a, "T"));
            lines.Add("A | DROP-UX | " + Exec(a, "DROP INDEX UX"));
            lines.Add("A | DROP-UX-AGAIN | " + Exec(a, "DROP INDEX UX"));
            lines.Add("A | DROP-UX-IF | " + Exec(a, "DROP INDEX IF EXISTS UX"));
            lines.Add("A | INDEXES4 | " + Indexes(a, "T"));
            lines.Add("A | DROP-MYSQL | " + Exec(a, "DROP INDEX DX ON T"));
            lines.Add("A | DROP-MISSING-TABLE | " + Exec(a, "DROP TABLE T"));
            lines.Add("A | INDEXES5 | " + Indexes(a, "T"));
        }

        using (H2Connection b = Open("b"))
        {
            lines.Add("B | CREATE | " + Exec(b, "CREATE TABLE T(ID INT NOT NULL, A INT)"));
            lines.Add("B | INSERT1 | " + Exec(b, "INSERT INTO T VALUES(1, 1)"));
            lines.Add("B | INSERT2 | " + Exec(b, "INSERT INTO T VALUES(2, 2)"));
            lines.Add("B | INSERT3 | " + Exec(b, "INSERT INTO T VALUES(3, 1)"));
            lines.Add("B | CREATE | " + Exec(b, "CREATE INDEX IX ON T(A)"));
            lines.Add("B | INDEXES | " + Indexes(b, "T"));
            lines.Add("B | COUNT-A1 | " + Count(b, "SELECT COUNT(*) FROM T WHERE A = 1"));
            lines.Add("B | UPDATE | " + Exec(b, "UPDATE T SET A = 2 WHERE ID = 3"));
            lines.Add("B | COUNT-A2 | " + Count(b, "SELECT COUNT(*) FROM T WHERE A = 2"));
            lines.Add("B | DELETE | " + Exec(b, "DELETE FROM T WHERE ID = 1"));
            lines.Add("B | COUNT-A1-B | " + Count(b, "SELECT COUNT(*) FROM T WHERE A = 1"));
            lines.Add("B | DROP | " + Exec(b, "DROP INDEX IX"));
            lines.Add("B | INDEXES2 | " + Indexes(b, "T"));
        }

        using (H2Connection c = Open("c"))
        {
            lines.Add("C | CREATE | " + Exec(c, "CREATE TABLE T(ID INT NOT NULL, A INT)"));
            lines.Add("C | INSERT1 | " + Exec(c, "INSERT INTO T VALUES(1, 5)"));
            lines.Add("C | INSERT2 | " + Exec(c, "INSERT INTO T VALUES(2, 5)"));
            lines.Add("C | CREATE-DUP | " + Dup(c, "CREATE UNIQUE INDEX UX ON T(A)"));
            lines.Add("C | INDEXES | " + Indexes(c, "T"));
            lines.Add("C | DELETE | " + Exec(c, "DELETE FROM T WHERE ID = 2"));
            lines.Add("C | CREATE | " + Exec(c, "CREATE UNIQUE INDEX UX ON T(A)"));
            lines.Add("C | INDEXES2 | " + Indexes(c, "T"));
        }

        return lines;
    }

    private static List<string> DescribeRebuild()
    {
        List<string> lines = [];

        using H2Connection d = Open("d");
        lines.Add("D | CREATE | " + Exec(d, "CREATE TABLE T(ID INT NOT NULL, A INT)"));
        lines.Add("D | INSERT1 | " + Exec(d, "INSERT INTO T VALUES(1, 1)"));
        lines.Add("D | INSERT2 | " + Exec(d, "INSERT INTO T VALUES(2, 2)"));
        lines.Add("D | INSERT3 | " + Exec(d, "INSERT INTO T VALUES(3, 3)"));
        lines.Add("D | ADD-PK | " + Exec(d, "ALTER TABLE T ADD PRIMARY KEY(ID)"));
        lines.Add("D | INDEXES | " + Indexes(d, "T"));
        lines.Add("D | CONSTRAINTS | " + Constraints(d, "T"));
        lines.Add("D | INSERT-DUP | " + Dup(d, "INSERT INTO T VALUES(1, 9)"));
        lines.Add("D | INSERT-OK | " + Exec(d, "INSERT INTO T VALUES(4, 4)"));
        lines.Add("D | DROP-PK | " + Exec(d, "ALTER TABLE T DROP PRIMARY KEY"));
        lines.Add("D | INDEXES2 | " + Indexes(d, "T"));
        lines.Add("D | CONSTRAINTS2 | " + Constraints(d, "T"));
        lines.Add("D | INSERT-DUP-PK | " + Exec(d, "INSERT INTO T VALUES(4, 5)"));
        lines.Add("D | DROP-PK-AGAIN | " + Exec(d, "ALTER TABLE T DROP PRIMARY KEY"));
        lines.Add("D | ADD-PK-DUPS | " + Dup(d, "ALTER TABLE T ADD PRIMARY KEY(ID)"));
        lines.Add("D | INDEXES3 | " + Indexes(d, "T"));
        lines.Add("D | CONSTRAINTS3 | " + Constraints(d, "T"));
        lines.Add("D | DELETE-DUP | " + Exec(d, "DELETE FROM T WHERE ID = 4 AND A = 5"));
        lines.Add("D | ADD-PK-AGAIN | " + Exec(d, "ALTER TABLE T ADD PRIMARY KEY(ID)"));
        lines.Add("D | INDEXES4 | " + Indexes(d, "T"));
        lines.Add("D | CONSTRAINTS4 | " + Constraints(d, "T"));
        lines.Add("D | DROP-INDEX-PK | " + Dup(d, "DROP INDEX " + Indexes(d, "T")));

        return lines;
    }

    private static H2Connection Open(string name)
    {
        H2Connection connection = new("Data Source=ix_" + name + ";Storage=Memory");
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

    private static string Count(H2Connection connection, string sql)
    {
        try
        {
            return "OK " + connection.ExecuteScalar(sql);
        }
        catch (H2SharpException ex)
        {
            return "ERR " + ex.H2ErrorCode;
        }
    }

    private static string Indexes(H2Connection connection, string tableName)
    {
        Tables.Table? table = FindTable(connection, tableName);
        if (table is null)
        {
            return "NO-TABLE";
        }

        return string.Join(", ", table.GetIndexes().Select(i => i.GetName()).Where(n => n is not null));
    }

    private static string Constraints(H2Connection connection, string tableName)
    {
        Tables.Table? table = FindTable(connection, tableName);
        if (table is null)
        {
            return "NO-TABLE";
        }

        return string.Join(", ", table.GetConstraints().Select(c => c.GetName()));
    }

    private static Tables.Table? FindTable(H2Connection connection, string tableName)
    {
        Engine.SessionLocal session = connection.Session
            ?? throw new InvalidOperationException("The connection is closed.");
        return session.GetDatabase().GetMainSchema()!.FindTableOrView(session, tableName);
    }

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

