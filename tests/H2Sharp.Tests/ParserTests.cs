/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The parser, against the statements the provider test runs.
 *
 * The corpus is those statements themselves rather than a prettier set of them: the point is that
 * the SQL an application writes is the SQL the database accepts. Where the reference's spelling
 * shows through, the test pins it — the statement words are identifiers the tokenizer never saw
 * and a keyword carries its text. An at sign plus a name is a session variable in the reference
 * (`Variable`, not a parameter), and that class is not ported, so the parser refuses it by name
 * rather than reading it into something it is not.
 */
using System;
using System.Collections;
using System.Linq;
using System.Text;
using H2Sharp.Command;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Conditions;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ParserTests
{
    [Fact]
    public void CreateTableReadsItsColumns()
    {
        Statement statement = Parse("CREATE TABLE PERSON(ID INT PRIMARY KEY, NAME VARCHAR(100) NOT NULL, AGE INT)");

        CreateTableStatement create = Assert.IsType<CreateTableStatement>(statement);
        Assert.Equal(CommandInterface.CreateTable, create.GetCommandType());
        Assert.Equal("PERSON", create.Name);
        Assert.Equal(["ID", "NAME", "AGE"], create.Columns.Select(c => c.Name));
        Assert.Equal(["INT", "VARCHAR(100)", "INT"], create.Columns.Select(c => c.Type));
        Assert.True(create.Columns[0].PrimaryKey);
        Assert.True(create.Columns[1].NotNull);
        Assert.False(create.Columns[2].PrimaryKey);
        Assert.False(create.IfNotExists);
    }

    [Fact]
    public void CreateTableReadsIfNotExists()
    {
        CreateTableStatement create = Assert.IsType<CreateTableStatement>(
            Parse("CREATE TABLE IF NOT EXISTS PERSON(ID INT)"));

        Assert.True(create.IfNotExists);
        Assert.Equal("PERSON", create.Name);
    }

    [Fact]
    public void CreateTableReadsMultiWordTypes()
    {
        CreateTableStatement create = Assert.IsType<CreateTableStatement>(
            Parse("CREATE TABLE T(A DOUBLE PRECISION, B TIMESTAMP WITH TIME ZONE, C NUMERIC(10, 2))"));

        Assert.Equal(
            ["DOUBLE PRECISION", "TIMESTAMP WITH TIME ZONE", "NUMERIC(10, 2)"],
            create.Columns.Select(c => c.Type));
    }

    [Fact]
    public void InsertWithVariablesSaysSo()
    {
        // @Id is a session Variable in the reference, and a sessionless parser has no
        // session to bind it to; it refuses by naming what is missing.
        TODOException e = Assert.Throws<TODOException>(
            () => Parse("INSERT INTO PERSON(ID, NAME, AGE) VALUES (@Id, @Name, @Age)"));
        Assert.Contains("no session", e.Message);
    }

    [Fact]
    public void InsertReadsSeveralRows()
    {
        InsertStatement insert = Assert.IsType<InsertStatement>(
            Parse("INSERT INTO PERSON(ID) VALUES (1), (2), (3)"));

        Assert.Equal(3, insert.Rows.Count);
        Assert.Equal("1", Assert.IsType<ValueExpression>(insert.Rows[0][0]).GetValueFromLiteral().GetString());
    }

    [Fact]
    public void InsertReadsNegativeLiterals()
    {
        InsertStatement insert = Assert.IsType<InsertStatement>(
            Parse("INSERT INTO PERSON(AGE) VALUES (-1)"));

        Assert.Equal("-1", Assert.IsType<ValueExpression>(insert.Rows[0][0]).GetValueFromLiteral().GetString());
    }

    [Fact]
    public void SelectReadsColumnsWhereAndOrder()
    {
        Statement statement = Parse("SELECT ID, NAME, AGE FROM PERSON WHERE ID = 1 ORDER BY ID");

        SelectStatement select = Assert.IsType<SelectStatement>(statement);
        Assert.Equal(CommandInterface.Select, select.GetCommandType());
        Assert.Equal(["ID", "NAME", "AGE"], select.Items.Select(i => i.Column));
        Assert.Equal("PERSON", select.Name);
        Comparison where = Assert.IsType<Comparison>(select.Where);
        Assert.Equal(Comparison.Equal, CompareTypeOf(where));
        Assert.Equal(("ID", true), Assert.Single(select.OrderBy));
    }

    [Fact]
    public void SelectReadsDescendingOrder()
    {
        SelectStatement select = Assert.IsType<SelectStatement>(
            Parse("SELECT ID FROM PERSON ORDER BY ID DESC"));

        Assert.Equal(("ID", false), Assert.Single(select.OrderBy));
    }

    [Fact]
    public void SelectReadsAStar()
    {
        SelectStatement select = Assert.IsType<SelectStatement>(Parse("SELECT * FROM PERSON"));

        SelectItem item = Assert.Single(select.Items);
        Assert.False(item.CountAll);
        Assert.Null(item.Column);
        Assert.Null(select.Where);
    }

    [Fact]
    public void SelectReadsACount()
    {
        SelectStatement select = Assert.IsType<SelectStatement>(Parse("SELECT COUNT(*) FROM PERSON"));

        SelectItem item = Assert.Single(select.Items);
        Assert.True(item.CountAll);
        Assert.Null(item.Column);
    }

    [Fact]
    public void SelectReadsSeveralConditions()
    {
        SelectStatement select = Assert.IsType<SelectStatement>(
            Parse("SELECT ID FROM PERSON WHERE ID = 1 OR ID = 2"));

        ConditionAndOr or = Assert.IsType<ConditionAndOr>(select.Where);
        Assert.Contains(" OR ", Render(or), StringComparison.Ordinal);
    }

    [Fact]
    public void SelectReadsIsNull()
    {
        SelectStatement select = Assert.IsType<SelectStatement>(Parse("SELECT ID FROM PERSON WHERE ID IS NULL"));

        Assert.IsType<ConditionIsNull>(select.Where);
    }

    [Fact]
    public void SelectReadsParenthesesAroundACondition()
    {
        SelectStatement select = Assert.IsType<SelectStatement>(
            Parse("SELECT ID FROM PERSON WHERE (ID = 1 OR ID = 2) AND NAME = 'x'"));

        ConditionAndOr and = Assert.IsType<ConditionAndOr>(select.Where);
        Assert.Contains(" AND ", Render(and), StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateReadsAssignmentsAndCondition()
    {
        Statement statement = Parse("UPDATE PERSON SET AGE = 1 WHERE ID = 2");

        UpdateStatement update = Assert.IsType<UpdateStatement>(statement);
        Assert.Equal(CommandInterface.Update, update.GetCommandType());
        (string column, Expression[]? indexes, Expression value) = Assert.Single(update.Assignments);
        Assert.Null(indexes);
        Assert.Equal("AGE", column);
        Assert.IsType<ValueExpression>(value);
        Assert.IsType<Comparison>(update.Where);
    }

    [Fact]
    public void DeleteReadsItsTableAndCondition()
    {
        Statement statement = Parse("DELETE FROM PERSON WHERE ID = 100");

        DeleteStatement delete = Assert.IsType<DeleteStatement>(statement);
        Assert.Equal(CommandInterface.Delete, delete.GetCommandType());
        Assert.Equal("PERSON", delete.Name);
        Assert.IsType<Comparison>(delete.Where);
    }

    [Fact]
    public void DeleteWithoutAWhereHasNoCondition()
    {
        DeleteStatement delete = Assert.IsType<DeleteStatement>(Parse("DELETE FROM PERSON"));

        Assert.Null(delete.Where);
    }

    [Fact]
    public void DropTableReadsIfExists()
    {
        DropTableStatement statement = Assert.IsType<DropTableStatement>(Parse("DROP TABLE IF EXISTS PERSON"));

        Assert.Equal(CommandInterface.DropTable, statement.GetCommandType());
        Assert.Equal("PERSON", statement.Name);
        Assert.True(statement.IfExists);
    }

    [Fact]
    public void AStatementItCannotReadSaysSo()
    {
        // Refusing by name is the point: a parser that guessed would read a statement it does not
        // understand into one it thinks it does.
        Assert.Throws<H2SharpException>(() => Parse("GRANT SELECT ON PERSON TO SOMEONE"));
    }

    private static Statement Parse(string sql)
        => Command.Parser.Parse(sql, new TestCastDataProvider(), new BitList());

    private static string Render(Expression expression)
        => expression.GetUnenclosedSql(new StringBuilder(), 0).ToString();

    /// <summary>
    /// The comparison type, read off the SQL the expression renders itself as. That is the only
    /// place it is visible without adding a member that exists for a test.
    /// </summary>
    private static int CompareTypeOf(Comparison comparison)
    {
        string rendered = Render(comparison);
        foreach ((string op, int type) in new[]
        {
            (" <> ", Comparison.NotEqual),
            (" <= ", Comparison.SmallerEqual),
            (" >= ", Comparison.BiggerEqual),
            (" = ", Comparison.Equal),
            (" < ", Comparison.Smaller),
            (" > ", Comparison.Bigger),
        })
        {
            if (rendered.Contains(op, StringComparison.Ordinal))
            {
                return type;
            }
        }

        throw new InvalidOperationException("no operator in " + rendered);
    }
}
