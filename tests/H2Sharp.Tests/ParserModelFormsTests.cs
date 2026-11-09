/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 107: the statement-model readers the port keeps for Parser.Parse — the rich
 * CREATE TABLE forms, INSERT value lists, UPDATE/DELETE and the expression shapes they
 * carry. The statements are parsed, never executed.
 */
using System.Collections;
using System.Collections.Generic;
using H2Sharp.Command;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ParserModelFormsTests
{
    private static Statement Parse(string sql)
        => Command.Parser.Parse(sql, new TestCastDataProvider(), new BitSet());

    public static TheoryData<string> Statements => new()
    {
        "CREATE TABLE P(ID INT PRIMARY KEY, NAME VARCHAR(100) NOT NULL, AGE INT)",
        "CREATE TABLE P(ID INT, NAME VARCHAR(10), CONSTRAINT PK PRIMARY KEY(ID, NAME))",
        "CREATE TABLE P(A INT DEFAULT 5)",
        "CREATE TABLE P(A INT GENERATED ALWAYS AS IDENTITY(START WITH 1 INCREMENT BY 2))",
        "CREATE TABLE P(A INT, B INT GENERATED ALWAYS AS (A + 1))",
        "CREATE TABLE P(A INT REFERENCES OTHER(ID))",
        "CREATE TABLE P(A INT REFERENCES OTHER(ID) ON DELETE CASCADE)",
        "CREATE TABLE P(A INT REFERENCES OTHER(ID) ON UPDATE SET NULL)",
        "CREATE TABLE P(A INT, FOREIGN KEY(A) REFERENCES OTHER(ID))",
        "CREATE TABLE P(A INT ARRAY[10])",
        "CREATE TABLE P(A INTERVAL DAY TO SECOND, B INTERVAL YEAR TO MONTH)",
        "CREATE TABLE P(A ENUM('x', 'y'))",
        "CREATE TABLE P(A JSON, B UUID, C GEOMETRY)",
        "CREATE TABLE P(A INT DEFAULT 1, B VARCHAR DEFAULT 'x')",
        "INSERT INTO P VALUES (1, 'a')",
        "INSERT INTO P(A) VALUES (1), (2), (3)",
        "INSERT INTO P(A) VALUES (DATE '2020-01-02')",
        "INSERT INTO P(A) VALUES (TIME '10:00:00')",
        "INSERT INTO P(A) VALUES (TIMESTAMP '2020-01-02 03:04:05')",
        "INSERT INTO P(A) VALUES (TIMESTAMP WITH TIME ZONE '2020-01-02 03:04:05+02')",
        "INSERT INTO P(A) VALUES (INTERVAL '1' DAY)",
        "INSERT INTO P(A) VALUES (X'0102')",
        "INSERT INTO P(A) VALUES (ARRAY[1, 2])",
        "INSERT INTO P(A) VALUES (ROW(1, 'a'))",
        "INSERT INTO P(A) VALUES (JSON '{\"a\":1}')",
        "INSERT INTO P(A) VALUES (UUID '00112233-4455-6677-8899-aabbccddeeff')",
        "INSERT INTO P(A) VALUES (N'abc')",
        "INSERT INTO P(A) VALUES (E'a\\tb')",
        "INSERT INTO P(A) VALUES (-1), (+2), (NULL)",
        "SELECT * FROM P",
        "SELECT ID, NAME FROM P WHERE ID = 1 AND NAME <> 'x' ORDER BY ID DESC",
        "SELECT ID FROM P LIMIT 10 OFFSET 2",
        "UPDATE P SET AGE = 1, NAME = 'x' WHERE ID = 2",
        "DELETE FROM P WHERE ID = 100",
        "DROP TABLE IF EXISTS P",
    };

    [Theory]
    [MemberData(nameof(Statements))]
    public void ModelReadersAcceptTheStatement(string sql)
    {
        Statement statement = Parse(sql);
        Assert.NotNull(statement);
        _ = statement.GetCommandType();
    }
}
