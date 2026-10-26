/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.test.db.TestIndex.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The reference's own index tests, ported. This is the port's guiding star: every test here is
 * a statement about what H2 does, written by H2, and each one fails with TODOException until
 * the class it exercises is ported — so the red list *is* the work list, and it cannot drift
 * from the reference the way a hand-written expectation can.
 *
 * The JDBC surface is mapped to the provider surface, member for member: getConnection becomes
 * an H2Connection over an in-memory database, createStatement is a command with no parameters,
 * executeQuery is a reader, and assertEquals/assertTrue are xunit. Nothing else is adapted:
 * the SQL is the reference's SQL, character for character, because a test whose SQL was
 * "cleaned up" is a test of the cleaned-up SQL.
 *
 * Ported so far, in the reference's own order: testOrderIndex, testIndexTypes, and the error
 * message test that reads the index name back out of a failure.
 */
using System;
using System.Data.Common;
using H2Sharp;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Engine.Tests;

public sealed class TestIndex
{
    /// <summary>
    /// The reference's testOrderIndex: a table without an index, an index on a second column,
    /// and a query that has to read the table and sort, because the index is not the one the
    /// query orders by.
    /// </summary>
    [Fact]
    public void OrderIndex()
    {
        using H2Connection conn = new("jdbc:h2:mem:index");
        conn.Open();
        using DbCommand stat = conn.CreateCommand();
        stat.CommandText = "create table test(id int, name varchar)";
        _ = stat.ExecuteNonQuery();
        stat.CommandText = "insert into test values (2, 'a'), (1, 'a')";
        _ = stat.ExecuteNonQuery();
        stat.CommandText = "create index on test(name)";
        _ = stat.ExecuteNonQuery();
        stat.CommandText = "select id from test where name = 'a' order by id";
        using DbDataReader rs = stat.ExecuteReader();
        Assert.True(rs.Read());
        Assert.Equal(1, rs.GetInt32(0));
        Assert.True(rs.Read());
        Assert.Equal(2, rs.GetInt32(0));
        Assert.False(rs.Read());
    }

    /// <summary>
    /// The reference's testIndexTypes: the same table under unique, hash and unique hash, with a
    /// thousand rows, a point lookup that must find exactly one row, a delete and the same
    /// lookup again, and a min/max over the whole table.
    /// </summary>
    [Theory]
    [InlineData("unique")]
    [InlineData("hash")]
    [InlineData("unique hash")]
    public void IndexTypes(string type)
    {
        using H2Connection conn = new("jdbc:h2:mem:index");
        conn.Open();
        using DbCommand stat = conn.CreateCommand();

        stat.CommandText = "create table test(id int)";
        _ = stat.ExecuteNonQuery();
        stat.CommandText = "create " + type + " index idx_name on test(id)";
        _ = stat.ExecuteNonQuery();
        stat.CommandText = "insert into test select x from system_range(1, 1000)";
        _ = stat.ExecuteNonQuery();

        stat.CommandText = "select * from test where id=100";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            Assert.True(rs.Read());
            Assert.False(rs.Read());
        }

        stat.CommandText = "delete from test where id=100";
        _ = stat.ExecuteNonQuery();

        stat.CommandText = "select * from test where id=100";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            Assert.False(rs.Read());
        }

        stat.CommandText = "select min(id), max(id) from test";
        using DbDataReader minMax = stat.ExecuteReader();
        Assert.True(minMax.Read());
        Assert.Equal(1, minMax.GetInt32(0));
        Assert.Equal(1000, minMax.GetInt32(1));
    }

    /// <summary>
    /// The reference's testErrorMessage: the index name that appears in an error is the index's
    /// own name, read back out of the message, and it is spelled with the table and column.
    /// </summary>
    [Fact]
    public void ErrorMessage()
    {
        using H2Connection conn = new("jdbc:h2:mem:index");
        conn.Open();
        using DbCommand stat = conn.CreateCommand();
        stat.CommandText = "create table test(id int primary key, name varchar)";
        _ = stat.ExecuteNonQuery();

        TestErrorMessage(conn, "PRIMARY", "KEY", " ON PUBLIC.TEST(ID)");

        stat.CommandText = "create table test(id int, name varchar primary key)";
        _ = stat.ExecuteNonQuery();

        TestErrorMessage(conn, "PRIMARY", "KEY", " ON PUBLIC.TEST(NAME)");
    }

    private static void TestErrorMessage(H2Connection conn, string head, string tail, string suffix)
    {
        using DbCommand stat = conn.CreateCommand();
        stat.CommandText = "insert into test values(1, 'a'), (2, 'a')";
        H2SharpException ex = Assert.Throws<H2SharpException>(() => _ = stat.ExecuteNonQuery());
        Assert.Equal(
            "Unique index or primary key violation: " + head + tail + suffix,
            ex.Message.Split('"')[0]);
    }
}
