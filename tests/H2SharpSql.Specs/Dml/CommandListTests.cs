// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// One text, several statements: the first answers, the rest run for effect; a statement that
// cannot be prepared before the earlier ones have run is deferred and parsed later.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Dml;

public sealed class CommandListTests
{
    [Fact]
    public void SeveralStatementsInOneTextAllRun()
    {
        using IDbConnection connection = TestDatabase.Open();

        // The first statement answers; the rest run for effect.
        int count = connection.Execute(
            "CREATE TABLE T(ID INT PRIMARY KEY); INSERT INTO T VALUES (1); INSERT INTO T VALUES (2)");

        Assert.Equal(0, count);
        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM T"));
    }

    [Fact]
    public void TheFirstUpdateCountIsTheAnswer()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY)");

        Assert.Equal(1, connection.Execute(
            "INSERT INTO T VALUES (1); INSERT INTO T VALUES (2); INSERT INTO T VALUES (3)"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT COUNT(*) FROM T"));
    }

    [Fact]
    public void ADeferredStatementSeesWhatTheEarlierOnesDid()
    {
        using IDbConnection connection = TestDatabase.Open();

        // The SELECT cannot be prepared before the CREATE has run, so it is parsed and run
        // after it, in the same text.
        int count = connection.Execute(
            "CREATE TABLE T(ID INT PRIMARY KEY); INSERT INTO T VALUES (7); "
            + "SELECT COUNT(*) FROM T");

        Assert.Equal(0, count);
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM T"));
    }

    [Fact]
    public void ParametersReachEveryStatement()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY)");

        Assert.Equal(1, connection.Execute(
            "INSERT INTO T VALUES ($a); INSERT INTO T VALUES ($b)", new { a = 1, b = 2 }));

        Assert.Equal([1, 2], connection.Query<int>("SELECT ID FROM T ORDER BY ID").AsList());
    }

    [Fact]
    public void AReaderWalksTheTrailingStatementsOnNextResult()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY)");
        _ = connection.Execute("INSERT INTO T VALUES (1), (2)");

        // The first statement is the result; the rest run when the reader moves past it.
        using IDataReader reader = connection.ExecuteReader("SELECT ID FROM T ORDER BY ID; DELETE FROM T");
        int rows = 0;
        while (reader.Read())
        {
            rows++;
        }

        Assert.Equal(2, rows);
        Assert.False(reader.NextResult());
        // The DELETE inside the text has run; the table is empty now.
        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM T"));
    }

    [Fact]
    public void APreparedListIsReusedUntilSomethingItNamesChanges()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE T(ID INT)");

        const string text = "INSERT INTO T VALUES (1); INSERT INTO T VALUES (2)";
        Assert.Equal(1, connection.Execute(text));
        // The second run of the same text is a cache hit.
        Assert.Equal(1, connection.Execute(text));
        Assert.Equal(4, connection.QuerySingle<int>("SELECT COUNT(*) FROM T"));

        // A new column invalidates the cached list; the next run still works.
        _ = connection.Execute("ALTER TABLE T ADD V INT");
        Assert.Equal(2, connection.Execute("INSERT INTO T SELECT X, X FROM SYSTEM_RANGE(10, 11)"));
        Assert.Equal(6, connection.QuerySingle<int>("SELECT COUNT(*) FROM T"));
    }
}
