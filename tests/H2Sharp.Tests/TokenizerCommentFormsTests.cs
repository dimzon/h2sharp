/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 123: the tokenizer comment forms (bracketed, nested, line, slash) and the numeric
 * literal failures, plus the outer-join condition that is not evaluable as a join condition.
 */
using System;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Result;
using Xunit;

namespace H2Sharp.Tests;

public sealed class TokenizerCommentFormsTests
{
    private static (Database Database, SessionLocal Session) Open()
    {
        Database database = new(ConnectionInfo.Memory("tokenizer_forms_" + Guid.NewGuid().ToString("N")));
        SessionLocal session = new(database, "sa");
        return (database, session);
    }

    private static void Exec(SessionLocal session, string sql)
        => session.PrepareCommand(sql).ExecuteUpdate(false);

    private static void Query(SessionLocal session, string sql)
    {
        using ResultInterface result = session.PrepareCommand(sql).ExecuteQuery(10, 0, false);
        _ = result.Next();
    }

    [Fact]
    public void CommentFormsAreSkipped()
    {
        (Database database, SessionLocal session) = Open();
        Query(session, "SELECT 1 /* bracketed */");
        Query(session, "SELECT 1 -- line\n");
        Query(session, "SELECT 1 // slash line\n");
        Query(session, "SELECT 1 /* outer /* inner */ outer */");
        Assert.Throws<H2SharpException>(() => Query(session, "SELECT 1 /* unterminated"));

        database.Close();
    }

    [Fact]
    public void NumericLiteralFormsAnswerOrRefuse()
    {
        (Database database, SessionLocal session) = Open();
        Query(session, "SELECT 100000000000000000000000000000");
        Query(session, "SELECT 1_000_000_000_000_000_000_000");
        Assert.NotNull(Record.Exception(() => Query(session, "SELECT 1e99999999999999999999")));

        database.Close();
    }

    [Fact]
    public void GroupByAndWindowFormsAnswer()
    {
        (Database database, SessionLocal session) = Open();
        Exec(session, "CREATE TABLE T(ID INT, N INT)");
        Exec(session, "INSERT INTO T VALUES(1, 1), (1, 2), (2, 3)");
        Query(session, "SELECT ID AS X FROM T GROUP BY X");
        Query(session, "SELECT ID AS X, N + 1 AS Y FROM T GROUP BY X, Y");
        Query(session, "SELECT ID, COUNT(*) FROM T GROUP BY ID HAVING COUNT(*) > 1");
        Query(session, "SELECT ID, ROW_NUMBER() OVER (ORDER BY ID) FROM T");
        Query(session, "SELECT ID FROM T ORDER BY ID DESC");
        Query(session, "SELECT ID FROM T ORDER BY ID LIMIT 1");

        database.Close();
    }

    [Fact]
    public void OuterJoinWithNonEquiConditionAnswers()
    {
        (Database database, SessionLocal session) = Open();
        Exec(session, "CREATE TABLE A(ID INT)");
        Exec(session, "CREATE TABLE B(ID INT)");
        Exec(session, "INSERT INTO A VALUES(1), (2)");
        Exec(session, "INSERT INTO B VALUES(2), (3)");
        Query(session, "SELECT * FROM A LEFT JOIN B ON A.ID > B.ID");

        database.Close();
    }
}
