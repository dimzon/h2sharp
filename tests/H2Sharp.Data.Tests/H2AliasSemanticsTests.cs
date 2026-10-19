/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The AS keyword in a table alias, over ADO.NET, written without reading the
 * implementation. The reference of the same H2 version was asked what it does
 * with each form, and the answers are what these tests pin:
 *
 * - a table alias may be written with the AS or without it, and the two forms
 *   mean the same thing: FROM T t and FROM T AS t are the same query, and so
 *   are the quoted spellings, "T" "t" and "T" AS "t";
 * - the same holds for a table alias in a JOIN, on both sides;
 * - a derived table takes an alias with or without the AS, and so does a
 *   VALUES table, with or without a column list;
 * - an UPDATE and a DELETE may name the table with an alias, with or without
 *   the AS;
 * - a CTE requires the AS: WITH c (SELECT ...) is a syntax error, while
 *   WITH c AS (SELECT ...) is the query;
 * - an INSERT does not take an alias at all: INSERT INTO T AS t (...) is a
 *   syntax error in the reference, and the port has to refuse it the same way
 *   rather than accept a form the reference rejects;
 * - a column alias may be written with the AS or without it, and the two
 *   forms mean the same thing.
 *
 * The oracle is not this port: the reference of the same H2 version answers
 * through the parser, and a form the reference accepts is a form this port has
 * to accept. The forms the reference refuses are refused here too.
 *
 * Dapper is used as the caller convenience only; nothing against the provider's
 * own members.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using H2Sharp;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2AliasSemanticsTests
{
    private static IDbConnection Open() => OpenNamed("alias_" + Guid.NewGuid().ToString("N"));

    private static IDbConnection OpenNamed(string name)
    {
        IDbConnection connection = new H2Connection($"jdbc:h2:mem:{name}");
        connection.Open();
        return connection;
    }

    /// <summary>
    ///     Two tables with two rows each, and a schema, so that an alias can be tried in
    ///     every place one can appear.
    /// </summary>
    private static void Prepare(IDbConnection connection)
    {
        _ = connection.Execute("DROP TABLE IF EXISTS U");
        _ = connection.Execute("DROP TABLE IF EXISTS T");
        _ = connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR)");
        _ = connection.Execute("CREATE TABLE U(ID INT PRIMARY KEY, NAME VARCHAR)");
        _ = connection.Execute("INSERT INTO T VALUES (1, 'a'), (2, 'b')");
        _ = connection.Execute("INSERT INTO U VALUES (1, 'x'), (2, 'y')");
    }

    private static List<int> Ids(IDbConnection connection, string sql)
        => connection.Query<int>(sql).ToList();

    [Fact]
    public void Table_alias_without_AS_names_the_table()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        Assert.Equal(new[] { 1, 2 }, Ids(connection, "SELECT t.ID FROM T t ORDER BY t.ID"));
    }

    [Fact]
    public void Table_alias_with_AS_names_the_table()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        Assert.Equal(new[] { 1, 2 }, Ids(connection, "SELECT t.ID FROM T AS t ORDER BY t.ID"));
    }

    [Fact]
    public void Quoted_table_alias_without_AS_names_the_table()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        Assert.Equal(new[] { 1, 2 }, Ids(connection, "SELECT \"t\".\"ID\" FROM T \"t\" ORDER BY \"t\".\"ID\""));
    }

    [Fact]
    public void Quoted_table_alias_with_AS_names_the_table()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        Assert.Equal(new[] { 1, 2 }, Ids(connection, "SELECT \"t\".\"ID\" FROM T AS \"t\" ORDER BY \"t\".\"ID\""));
    }

    [Fact]
    public void The_two_table_alias_forms_return_the_same_rows()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        var without = Ids(connection, "SELECT t.ID, t.NAME FROM T t ORDER BY t.ID");
        var with = Ids(connection, "SELECT t.ID, t.NAME FROM T AS t ORDER BY t.ID");

        Assert.Equal(without, with);
    }

    [Fact]
    public void A_table_alias_works_in_where_group_by_and_order_by()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        Assert.Equal(new[] { 2 }, Ids(connection, "SELECT t.ID FROM T AS t WHERE t.ID = 2"));
        Assert.Equal(new[] { 1, 2 }, Ids(connection, "SELECT t.ID FROM T AS t GROUP BY t.ID ORDER BY t.ID"));
        Assert.Equal(new[] { 1, 2 }, Ids(connection, "SELECT t.ID FROM T AS t ORDER BY t.ID"));
    }

    [Fact]
    public void A_table_alias_works_with_select_star()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        Assert.Equal(new[] { 1, 2 }, Ids(connection, "SELECT t.* FROM T AS t"));
    }

    [Fact]
    public void Both_sides_of_a_join_may_be_aliased_without_AS()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        Assert.Equal(new[] { 1, 2 }, Ids(connection, "SELECT t.ID FROM T t JOIN U u ON t.ID = u.ID ORDER BY t.ID"));
    }

    [Fact]
    public void Both_sides_of_a_join_may_be_aliased_with_AS()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        Assert.Equal(new[] { 1, 2 }, Ids(connection, "SELECT t.ID FROM T AS t JOIN U AS u ON t.ID = u.ID ORDER BY t.ID"));
    }

    [Fact]
    public void A_left_join_may_alias_its_tables_with_AS()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        Assert.Equal(new[] { 1, 2 }, Ids(connection, "SELECT t.ID FROM T AS t LEFT JOIN U AS u ON t.ID = u.ID ORDER BY t.ID"));
    }

    [Fact]
    public void A_derived_table_takes_an_alias_without_AS()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        Assert.Equal(new[] { 1, 2 }, Ids(connection, "SELECT x.ID FROM (SELECT ID FROM T) x ORDER BY x.ID"));
    }

    [Fact]
    public void A_derived_table_takes_an_alias_with_AS()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        Assert.Equal(new[] { 1, 2 }, Ids(connection, "SELECT x.ID FROM (SELECT ID FROM T) AS x ORDER BY x.ID"));
    }

    [Fact]
    public void A_values_table_takes_an_alias_with_AS()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        Assert.Equal(new[] { 1, 2 }, Ids(connection, "SELECT v.X FROM (VALUES (1), (2)) AS v(X) ORDER BY v.X"));
    }

    [Fact]
    public void A_values_table_takes_an_alias_without_AS()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        Assert.Equal(new[] { 1, 2 }, Ids(connection, "SELECT v.X FROM (VALUES (1), (2)) v(X) ORDER BY v.X"));
    }

    [Fact]
    public void A_common_table_expression_requires_the_AS()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        Assert.Equal(new[] { 1, 2 }, Ids(connection, "WITH c AS (SELECT ID FROM T) SELECT ID FROM c ORDER BY ID"));
    }

    [Fact]
    public void A_common_table_expression_without_the_AS_is_a_syntax_error()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        // The reference refuses this, so the port has to refuse it too: accepting a form the
        // reference rejects would be the port being more permissive than the database.
        Assert.ThrowsAny<Exception>(() => Ids(connection, "WITH c (SELECT ID FROM T) SELECT ID FROM c"));
    }

    [Fact]
    public void An_update_may_alias_the_table_with_AS()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        _ = connection.Execute("UPDATE T AS t SET NAME = 'z' WHERE t.ID = 1");

        Assert.Equal("z", connection.Query<string>("SELECT NAME FROM T WHERE ID = 1").Single());
    }

    [Fact]
    public void An_update_may_alias_the_table_without_AS()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        _ = connection.Execute("UPDATE T t SET NAME = 'y' WHERE t.ID = 2");

        Assert.Equal("y", connection.Query<string>("SELECT NAME FROM T WHERE ID = 2").Single());
    }

    [Fact]
    public void A_delete_may_alias_the_table_with_AS()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        _ = connection.Execute("DELETE FROM T AS t WHERE t.ID = 1");

        Assert.Equal(new[] { 2 }, Ids(connection, "SELECT ID FROM T ORDER BY ID"));
    }

    [Fact]
    public void A_delete_may_alias_the_table_without_AS()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        _ = connection.Execute("DELETE FROM T t WHERE t.ID = 2");

        Assert.Equal(new[] { 1 }, Ids(connection, "SELECT ID FROM T ORDER BY ID"));
    }

    [Fact]
    public void An_insert_does_not_take_a_table_alias()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        // The reference refuses this, so the port has to refuse it as well.
        Assert.ThrowsAny<Exception>(
            () => _ = connection.Execute("INSERT INTO T AS t (ID, NAME) VALUES (5, 'q')"));
    }

    [Fact]
    public void A_column_alias_with_AS_names_the_column()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        Assert.Equal(new[] { 1, 2 }, Ids(connection, "SELECT ID AS X FROM T ORDER BY X"));
    }

    [Fact]
    public void A_column_alias_without_AS_names_the_column()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        Assert.Equal(new[] { 1, 2 }, Ids(connection, "SELECT ID X FROM T ORDER BY X"));
    }

    [Fact]
    public void A_merge_may_alias_both_tables_with_AS()
    {
        using IDbConnection connection = Open();
        Prepare(connection);

        _ = connection.Execute(
            "MERGE INTO T AS t USING U AS u ON t.ID = u.ID WHEN MATCHED THEN UPDATE SET NAME = 'm'");

        Assert.Equal("m", connection.Query<string>("SELECT NAME FROM T WHERE ID = 1").Single());
    }
}
