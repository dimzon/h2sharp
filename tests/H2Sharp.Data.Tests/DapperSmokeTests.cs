/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The provider as a caller actually meets it: through Dapper, on IDbConnection.
 *
 * Nothing here is written against H2Connection's own members beyond handing one to Dapper —
 * which is the point. Dapper talks to IDbConnection, and that is the whole surface it needs:
 * Open, Execute, Query and a parameter object. If this file compiles, the provider is shaped
 * the way a real micro-ORM expects it to be shaped, whatever is behind it.
 *
 * The flow is the smallest end-to-end one: an in-memory database, a table, an insert, an
 * update, a select and the read of what the update wrote. It is the gate for the provider:
 * H2Connection.Open refusing with TODOException is the expected result today, and the first
 * thing that changes when the engine lands.
 *
 * The connection string is H2's own mem: syntax rather than a Data Source one, because the
 * provider has to accept both and this is the form that needs parsing; a Data Source test
 * would pass on a provider that took the text and looked at none of it.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class DapperSmokeTests
{
    [Fact]
    public void Connects_CreatesATable_Inserts_Updates_AndSelects()
    {
        using IDbConnection connection = new H2Connection("jdbc:h2:mem:dapper_smoke");
        connection.Open();

        _ = connection.Execute("DROP TABLE IF EXISTS PERSON");
        _ = connection.Execute("CREATE TABLE PERSON(ID INT PRIMARY KEY, NAME VARCHAR(100) NOT NULL, AGE INT)");

        int inserted = connection.Execute(
            "INSERT INTO PERSON(ID, NAME, AGE) VALUES (@Id, @Name, @Age)",
            new Person { Id = 1, Name = "Ada", Age = 36 });
        Assert.Equal(1, inserted);

        List<Person> afterInsert = connection.Query<Person>(
            "SELECT ID, NAME, AGE FROM PERSON ORDER BY ID").AsList();
        Assert.Equal([new Person { Id = 1, Name = "Ada", Age = 36 }], afterInsert);

        int updated = connection.Execute(
            "UPDATE PERSON SET AGE = @Age WHERE ID = @Id",
            new { Id = 1, Age = 37 });
        Assert.Equal(1, updated);

        // The select is repeated after the update on purpose: an update that reports one row
        // and leaves the old value in place is the shape of bug a smoke test exists to catch,
        // and a single select would miss it.
        Person afterUpdate = connection.QuerySingle<Person>(
            "SELECT ID, NAME, AGE FROM PERSON WHERE ID = @Id", new { Id = 1 });
        Assert.Equal(1, afterUpdate.Id);
        Assert.Equal("Ada", afterUpdate.Name);
        Assert.Equal(37, afterUpdate.Age);

        // A scalar query, because Dapper's single-value path is a different code path from the
        // materialisation above and a provider that answers the first and not the second is
        // common.
        int count = connection.QuerySingle<int>("SELECT COUNT(*) FROM PERSON");
        Assert.Equal(1, count);

        _ = connection.Execute("DELETE FROM PERSON WHERE ID = @Id", new { Id = 1 });
        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM PERSON"));
    }

    [Fact]
    public void ParametersAreNotConcatenatedIntoTheText()
    {
        // The statement above carries '@Id' and the value travels beside it, which is how
        // Dapper calls a provider. A provider that inlined the value would answer these calls
        // perfectly and be a SQL injection, so the shape is asserted separately from the flow:
        // the text keeps its placeholders whatever the value is.
        using IDbConnection connection = new H2Connection("jdbc:h2:mem:dapper_parameters");
        connection.Open();

        _ = connection.Execute("CREATE TABLE T(ID INT, TEXT VARCHAR(50))");
        _ = connection.Execute("INSERT INTO T(ID, TEXT) VALUES (@Id, @Text)",
            new { Id = 1, Text = "O'Brien'; DROP TABLE T; --" });

        Assert.Equal("O'Brien'; DROP TABLE T; --", connection.QuerySingle<string>("SELECT TEXT FROM T WHERE ID = 1"));
    }

    /// <summary>The row shape Dapper materialises. Property names match the column names.</summary>
    private sealed class Person
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public int Age { get; set; }
    }
}
