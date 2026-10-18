/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What ALTER TABLE ... ADD/DROP CONSTRAINT does, checked against the reference.
 *
 * The ADD path is the same command CREATE TABLE's constraints run: the statement finds the
 * table by name (or answers TABLE_NOT_FOUND, which IF EXISTS turns into a no-op), resolves
 * the key columns, and hands the key to the table. On an empty table the key becomes a
 * delegate index over the primary data map; on a table with rows the reference builds a
 * secondary index -- that build is not ported (MVSecondaryIndex is a skeleton), so the
 * oracle below only adds keys to empty tables and the duplicate rebuild stays a TODO.
 *
 * The DROP path finds the constraint in the *schema* and then checks it belongs to the
 * named table: a name another table owns is CONSTRAINT_NOT_FOUND, exactly like a name that
 * nothing has, and IF EXISTS (written before or after the name) suppresses it. Dropping
 * frees the constraint's name and its index, so the same names can be taken again, and the
 * rows become unconstrained, duplicates included.
 *
 * Dapper is used as the caller convenience only: Execute over IDbConnection with an
 * H2SharpException as the statement's failure.
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
public sealed class AlterTableConstraintTests
{
    /// <summary>
    /// The ALTER TABLE constraint lifecycle, statement by statement: named and nameless key
    /// names, the nullable column refusal, a second primary key, a missing table, a
    /// constraint of another table, dropping and taking the names again, IF EXISTS in both
    /// positions and the CASCADE/RESTRICT words.
    /// </summary>
    [JavaOracleFact]
    public void AddingAndDroppingAPrimaryKeyMatchesTheReference()
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
                return java.sql.DriverManager.getConnection("jdbc:h2:mem:al_" + name);
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

        java.sql.Connection a = h.open("a");
        System.out.println("A | CREATE | " + h.exec(a, "CREATE TABLE T(ID INT NOT NULL)"));
        System.out.println("A | ADD-NAMED | " + h.exec(a, "ALTER TABLE T ADD CONSTRAINT PKX PRIMARY KEY(ID)"));
        System.out.println("A | INDEXES | " + h.indexes(a));
        System.out.println("A | CONSTRAINTS | " + h.constraints(a));
        System.out.println("A | INSERT | " + h.exec(a, "INSERT INTO T VALUES(1)"));
        System.out.println("A | DUP | " + h.dup(a, "INSERT INTO T VALUES(1)"));
        System.out.println("A | IFNE-EXISTS | " + h.exec(a, "ALTER TABLE T ADD CONSTRAINT IF NOT EXISTS PKX PRIMARY KEY(ID)"));
        System.out.println("A | DUP-NAME | " + h.exec(a, "ALTER TABLE T ADD CONSTRAINT PKX PRIMARY KEY(ID)"));
        System.out.println("A | ADD-NAMELESS | " + h.exec(a, "ALTER TABLE T ADD PRIMARY KEY(ID)"));
        System.out.println("A | NULL-INSERT | " + h.dup(a, "INSERT INTO T VALUES(NULL)"));
        a.close();

        java.sql.Connection n = h.open("n");
        System.out.println("N | CREATE | " + h.exec(n, "CREATE TABLE T(ID INT)"));
        System.out.println("N | ADD-NULLABLE | " + h.exec(n, "ALTER TABLE T ADD CONSTRAINT PKX PRIMARY KEY(ID)"));
        System.out.println("N | CONSTRAINTS | " + h.constraints(n));
        n.close();

        java.sql.Connection b = h.open("b");
        System.out.println("B | CREATE | " + h.exec(b, "CREATE TABLE T(ID INT NOT NULL, B INT)"));
        System.out.println("B | ADD | " + h.exec(b, "ALTER TABLE T ADD PRIMARY KEY(ID)"));
        System.out.println("B | CONSTRAINTS | " + h.constraints(b));
        System.out.println("B | ADD2-OTHER-COL | " + h.exec(b, "ALTER TABLE T ADD CONSTRAINT PK2 PRIMARY KEY(B)"));
        System.out.println("B | MISSING-TABLE | " + h.exec(b, "ALTER TABLE U ADD CONSTRAINT PK9 PRIMARY KEY(ID)"));
        System.out.println("B | MISSING-IF-EXISTS | " + h.exec(b, "ALTER TABLE IF EXISTS U ADD CONSTRAINT PK9 PRIMARY KEY(ID)"));
        b.close();

        java.sql.Connection d = h.open("d");
        System.out.println("D | CREATE | " + h.exec(d, "CREATE TABLE T(ID INT NOT NULL)"));
        System.out.println("D | ADD | " + h.exec(d, "ALTER TABLE T ADD CONSTRAINT PKX PRIMARY KEY(ID)"));
        System.out.println("D | CREATE-U | " + h.exec(d, "CREATE TABLE U(A INT, CONSTRAINT PKU PRIMARY KEY(A))"));
        System.out.println("D | DROP-OTHER-TABLE | " + h.exec(d, "ALTER TABLE T DROP CONSTRAINT PKU"));
        System.out.println("D | DROP | " + h.exec(d, "ALTER TABLE T DROP CONSTRAINT PKX"));
        System.out.println("D | CONSTRAINTS | " + h.constraints(d));
        System.out.println("D | INDEXES | " + h.indexes(d));
        System.out.println("D | READD | " + h.exec(d, "ALTER TABLE T ADD CONSTRAINT PKX PRIMARY KEY(ID)"));
        System.out.println("D | CONSTRAINTS2 | " + h.constraints(d));
        System.out.println("D | INDEXES2 | " + h.indexes(d));
        System.out.println("D | DROP2 | " + h.exec(d, "ALTER TABLE T DROP CONSTRAINT PKX"));
        System.out.println("D | INSERT2 | " + h.exec(d, "INSERT INTO T VALUES(1)"));
        System.out.println("D | DUP2 | " + h.dup(d, "INSERT INTO T VALUES(1)"));
        System.out.println("D | DROP-MISSING | " + h.exec(d, "ALTER TABLE T DROP CONSTRAINT NOPE"));
        System.out.println("D | DROP-IF-EXISTS | " + h.exec(d, "ALTER TABLE T DROP CONSTRAINT IF EXISTS NOPE"));
        System.out.println("D | DROP-IF-EXISTS-AFTER | " + h.exec(d, "ALTER TABLE T DROP CONSTRAINT NOPE IF EXISTS"));
        System.out.println("D | DROP-RESTRICT | " + h.exec(d, "ALTER TABLE T DROP CONSTRAINT PKX RESTRICT"));
        System.out.println("D | DROP-CASCADE | " + h.exec(d, "ALTER TABLE T DROP CONSTRAINT IF EXISTS PKX CASCADE"));
        System.out.println("D | MISSING-DROP-IF-EXISTS | " + h.exec(d, "ALTER TABLE IF EXISTS Q DROP CONSTRAINT NOPE"));
        d.close();
        """;

    private static List<string> Describe()
    {
        List<string> lines = [];

        using (H2Connection a = Open("a"))
        {
            lines.Add("A | CREATE | " + Exec(a, "CREATE TABLE T(ID INT NOT NULL)"));
            lines.Add("A | ADD-NAMED | " + Exec(a, "ALTER TABLE T ADD CONSTRAINT PKX PRIMARY KEY(ID)"));
            lines.Add("A | INDEXES | " + Indexes(a));
            lines.Add("A | CONSTRAINTS | " + Constraints(a));
            lines.Add("A | INSERT | " + Exec(a, "INSERT INTO T VALUES(1)"));
            lines.Add("A | DUP | " + Dup(a, "INSERT INTO T VALUES(1)"));
            lines.Add("A | IFNE-EXISTS | " + Exec(a, "ALTER TABLE T ADD CONSTRAINT IF NOT EXISTS PKX PRIMARY KEY(ID)"));
            lines.Add("A | DUP-NAME | " + Exec(a, "ALTER TABLE T ADD CONSTRAINT PKX PRIMARY KEY(ID)"));
            lines.Add("A | ADD-NAMELESS | " + Exec(a, "ALTER TABLE T ADD PRIMARY KEY(ID)"));
            lines.Add("A | NULL-INSERT | " + Dup(a, "INSERT INTO T VALUES(NULL)"));
        }

        using (H2Connection n = Open("n"))
        {
            lines.Add("N | CREATE | " + Exec(n, "CREATE TABLE T(ID INT)"));
            lines.Add("N | ADD-NULLABLE | " + Exec(n, "ALTER TABLE T ADD CONSTRAINT PKX PRIMARY KEY(ID)"));
            lines.Add("N | CONSTRAINTS | " + Constraints(n));
        }

        using (H2Connection b = Open("b"))
        {
            lines.Add("B | CREATE | " + Exec(b, "CREATE TABLE T(ID INT NOT NULL, B INT)"));
            lines.Add("B | ADD | " + Exec(b, "ALTER TABLE T ADD PRIMARY KEY(ID)"));
            lines.Add("B | CONSTRAINTS | " + Constraints(b));
            lines.Add("B | ADD2-OTHER-COL | " + Exec(b, "ALTER TABLE T ADD CONSTRAINT PK2 PRIMARY KEY(B)"));
            lines.Add("B | MISSING-TABLE | " + Exec(b, "ALTER TABLE U ADD CONSTRAINT PK9 PRIMARY KEY(ID)"));
            lines.Add("B | MISSING-IF-EXISTS | " + Exec(b, "ALTER TABLE IF EXISTS U ADD CONSTRAINT PK9 PRIMARY KEY(ID)"));
        }

        using (H2Connection d = Open("d"))
        {
            lines.Add("D | CREATE | " + Exec(d, "CREATE TABLE T(ID INT NOT NULL)"));
            lines.Add("D | ADD | " + Exec(d, "ALTER TABLE T ADD CONSTRAINT PKX PRIMARY KEY(ID)"));
            lines.Add("D | CREATE-U | " + Exec(d, "CREATE TABLE U(A INT, CONSTRAINT PKU PRIMARY KEY(A))"));
            lines.Add("D | DROP-OTHER-TABLE | " + Exec(d, "ALTER TABLE T DROP CONSTRAINT PKU"));
            lines.Add("D | DROP | " + Exec(d, "ALTER TABLE T DROP CONSTRAINT PKX"));
            lines.Add("D | CONSTRAINTS | " + Constraints(d));
            lines.Add("D | INDEXES | " + Indexes(d));
            lines.Add("D | READD | " + Exec(d, "ALTER TABLE T ADD CONSTRAINT PKX PRIMARY KEY(ID)"));
            lines.Add("D | CONSTRAINTS2 | " + Constraints(d));
            lines.Add("D | INDEXES2 | " + Indexes(d));
            lines.Add("D | DROP2 | " + Exec(d, "ALTER TABLE T DROP CONSTRAINT PKX"));
            lines.Add("D | INSERT2 | " + Exec(d, "INSERT INTO T VALUES(1)"));
            lines.Add("D | DUP2 | " + Dup(d, "INSERT INTO T VALUES(1)"));
            lines.Add("D | DROP-MISSING | " + Exec(d, "ALTER TABLE T DROP CONSTRAINT NOPE"));
            lines.Add("D | DROP-IF-EXISTS | " + Exec(d, "ALTER TABLE T DROP CONSTRAINT IF EXISTS NOPE"));
            lines.Add("D | DROP-IF-EXISTS-AFTER | " + Exec(d, "ALTER TABLE T DROP CONSTRAINT NOPE IF EXISTS"));
            lines.Add("D | DROP-RESTRICT | " + Exec(d, "ALTER TABLE T DROP CONSTRAINT PKX RESTRICT"));
            lines.Add("D | DROP-CASCADE | " + Exec(d, "ALTER TABLE T DROP CONSTRAINT IF EXISTS PKX CASCADE"));
            lines.Add("D | MISSING-DROP-IF-EXISTS | " + Exec(d, "ALTER TABLE IF EXISTS Q DROP CONSTRAINT NOPE"));
        }

        return lines;
    }

    private static H2Connection Open(string name)
    {
        H2Connection connection = new("jdbc:h2:mem:al_" + name);
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

    private static string Indexes(H2Connection connection)
    {
        Tables.Table? table = FindTable(connection);
        if (table is null)
        {
            return "NO-TABLE";
        }

        return string.Join(", ", table.GetIndexes().Select(i => i.GetName()).Where(n => n is not null));
    }

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
