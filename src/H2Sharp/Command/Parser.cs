/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.Parser.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The statement parser.
 *
 * Partially ported: this reads the statements the table engine needs — CREATE TABLE, DROP TABLE,
 * INSERT, UPDATE, DELETE and a SELECT over one table with WHERE and ORDER BY — and refuses
 * everything else by name. A parser that guesses is the one place where a wrong answer looks
 * exactly like a right one.
 *
 * How a token is matched is not a detail, and the shapes these are:
 *
 * * **A keyword token carries its text.** `SELECT` arrives as a token of kind
 *   `ParserUtil.Select` whose identifier is "SELECT", and `FROM` as `ParserUtil.From` with
 *   "FROM". So a word can be matched by its text, whatever it is.
 * * **The statement words are identifiers.** CREATE, DROP, INSERT, UPDATE, DELETE and SELECT are
 *   kinds of statement in `CommandInterface`; the tokenizer has never heard of them and they come
 *   through as `IdentifierToken`. Adding them to `ParserUtil` would renumber every token after
 *   them and disagree with every file the reference wrote.
 * * **A variable is two tokens.** `@Age` is a `Token.At` followed by the name as an
 *   identifier — the reference's `readTerm`: `case AT: read();
 *   r = new Variable(session, readIdentifier())`. It is a session variable, not a named
 *   parameter (those are the `?` this layer does not read yet), and `Variable` is not ported,
 *   so this layer refuses it by name. The lexer keeps no name, so a parser that expected one
 *   token would read the variable as a column.
 *
 * One trap that cost two rounds of guessing: <see cref="IsWord(string, bool)"/> *consumes* the
 * word unless asked not to, so a dispatch written as `if (IsWord("DELETE")) { Read(); … }` eats
 * the token after the word too, and the failure appears at the following word with the position
 * already past it.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Command;

/// <summary>What the parser understood.</summary>
public abstract class Statement
{
    /// <summary>The text this statement was written as.</summary>
    public string Sql { get; set; } = string.Empty;

    /// <summary>The kind of this statement, one of <see cref="CommandInterface"/>.</summary>
    public abstract int GetCommandType();
}

/// <summary>One column of a CREATE TABLE.</summary>
/// <param name="Name">the column name</param>
/// <param name="Type">the type as it was written</param>
/// <param name="NotNull">whether NOT NULL was written</param>
/// <param name="PrimaryKey">whether PRIMARY KEY was written</param>
/// <param name="ConstraintName">the name written after CONSTRAINT, or null</param>
/// <param name="PrimaryKeyHash">whether HASH followed PRIMARY KEY</param>
/// <param name="IdentityOptions">the options of the identity column, or null</param>
/// <param name="IdentityGeneratedAlways">whether the identity value is always generated</param>
/// <param name="Default">the default expression, or null</param>
/// <param name="Generated">the generated expression, or null</param>
/// <param name="OnUpdate">the expression of the value on update, or null</param>
/// <param name="DefaultOnNull">whether the default is used for a null value</param>
/// <param name="RefTableSchema">the schema of the table REFERENCES names, or null</param>
/// <param name="RefTable">the table REFERENCES names, or null when it names none</param>
/// <param name="RefColumns">the columns REFERENCES names, or null for the primary key</param>
/// <param name="RefUpdateAction">what an update of the referenced row does</param>
/// <param name="RefDeleteAction">what a delete of the referenced row does</param>
public sealed record ColumnDefinition(
    string Name,
    string Type,
    bool NotNull,
    bool PrimaryKey,
    string? ConstraintName,
    bool PrimaryKeyHash,
    Ddl.SequenceOptions? IdentityOptions = null,
    bool IdentityGeneratedAlways = false,
    Expression? Default = null,
    Expression? Generated = null,
    Expression? OnUpdate = null,
    bool DefaultOnNull = false,
    string? RefTableSchema = null,
    string? RefTable = null,
    List<string>? RefColumns = null,
    Constraints.ConstraintActionType RefUpdateAction = Constraints.ConstraintActionType.NO_ACTION,
    Constraints.ConstraintActionType RefDeleteAction = Constraints.ConstraintActionType.NO_ACTION);

/// <summary>One table-level constraint of a CREATE TABLE.</summary>
/// <param name="Name">the name written after CONSTRAINT, or null</param>
/// <param name="IfNotExists">whether IF NOT EXISTS was written</param>
/// <param name="Columns">the covered columns, by name</param>
/// <param name="Hash">whether HASH was written</param>
/// <param name="Type">the kind of constraint, one of the ALTER TABLE ADD CONSTRAINT types</param>
/// <param name="Comment">the comment written after the name, or null</param>
/// <param name="RefTableSchema">the schema of the referenced table, or null for the current one</param>
/// <param name="RefTable">the referenced table, or null for the statement's own table</param>
/// <param name="RefColumns">the referenced columns, by name, or null for the primary key</param>
/// <param name="UpdateAction">what an update of the referenced row does</param>
/// <param name="DeleteAction">what a delete of the referenced row does</param>
/// <param name="CheckExisting">whether the existing rows are checked</param>
public sealed record TableConstraintDefinition(
    string? Name,
    bool IfNotExists,
    List<string> Columns,
    bool Hash,
    int Type = CommandInterface.AlterTableAddConstraintPrimaryKey,
    string? Comment = null,
    string? RefTableSchema = null,
    string? RefTable = null,
    List<string>? RefColumns = null,
    Constraints.ConstraintActionType UpdateAction = Constraints.ConstraintActionType.NO_ACTION,
    Constraints.ConstraintActionType DeleteAction = Constraints.ConstraintActionType.NO_ACTION,
    bool CheckExisting = true);

/// <summary>ALTER TABLE ... ADD/DROP CONSTRAINT.</summary>
public sealed class AlterTableStatement : Statement
{
    /// <inheritdoc/>
    public override int GetCommandType()
        => AddConstraint is not null
            ? CommandInterface.AlterTableAddConstraintPrimaryKey
            : CommandInterface.AlterTableDropConstraint;

    /// <summary>The table name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether the table name was written with IF EXISTS.</summary>
    public bool IfTableExists { get; set; }

    /// <summary>The constraint to add, or null when the statement drops one.</summary>
    public TableConstraintDefinition? AddConstraint { get; set; }

    /// <summary>The name of the constraint to drop, or null when the statement adds one.</summary>
    public string? DropConstraintName { get; set; }

    /// <summary>Whether the constraint name was written with IF EXISTS.</summary>
    public bool DropConstraintIfExists { get; set; }

    /// <summary>The CASCADE/RESTRICT written after the name, or null.</summary>
    public ConstraintActionType? DropAction { get; set; }

    /// <summary>Whether PRIMARY KEY was written after DROP.</summary>
    public bool DropPrimaryKey { get; set; }

    /// <summary>The column that is changed, or null when the statement does not change one.</summary>
    public AlterTableAlterColumnDefinition? AlterColumn { get; set; }
}

/// <summary>ALTER TABLE ... ALTER COLUMN.</summary>
public sealed class AlterTableAlterColumnDefinition
{
    /// <summary>The name of the column.</summary>
    public string ColumnName { get; init; } = string.Empty;

    /// <summary>Whether the column loses its identity.</summary>
    public bool DropIdentity { get; init; }

    /// <summary>Whether the value is always generated, or null when it was not written.</summary>
    public bool? Always { get; init; }

    /// <summary>The options of the sequence of the column.</summary>
    public Ddl.SequenceOptions Options { get; init; } = new();
}

/// <summary>CREATE TABLE.</summary>
public sealed class CreateTableStatement : Statement
{
    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.CreateTable;

    /// <summary>
    /// The table name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The name of the schema the table belongs to, or null when the name says nothing.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>The columns, in order.</summary>
    public List<ColumnDefinition> Columns { get; } = [];

    /// <summary>The table-level constraints, in the order they were written.</summary>
    public List<TableConstraintDefinition> Constraints { get; } = [];

    /// <summary>Whether IF NOT EXISTS was written.</summary>
    public bool IfNotExists { get; set; }
}

/// <summary>DROP TABLE.</summary>
public sealed class DropTableStatement : Statement
{
    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.DropTable;

    /// <summary>The table name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether IF EXISTS was written.</summary>
    public bool IfExists { get; set; }
}

/// <summary>CREATE [UNIQUE] INDEX.</summary>
public sealed class CreateIndexStatement : Statement
{
    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.CreateIndex;

    /// <summary>The index name, or null when the name is to be generated.</summary>
    public string? Name { get; set; }

    /// <summary>The table the index is on.</summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>The columns, in order, with their sort types.</summary>
    public List<IndexColumn> Columns { get; } = [];

    /// <summary>Whether UNIQUE was written.</summary>
    public bool Unique { get; set; }

    /// <summary>How NULLs compare in a unique index, or null for a non-unique one.</summary>
    public NullsDistinct? NullsDistinct { get; set; }

    /// <summary>Whether IF NOT EXISTS was written.</summary>
    public bool IfNotExists { get; set; }

    /// <summary>Whether IF EXISTS was written before the table name.</summary>
    public bool IfTableExists { get; set; }
}

/// <summary>DROP INDEX.</summary>
public sealed class DropIndexStatement : Statement
{
    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.DropIndex;

    /// <summary>The index name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether IF EXISTS was written.</summary>
    public bool IfExists { get; set; }
}

/// <summary>INSERT INTO ... VALUES.</summary>
public sealed class InsertStatement : Statement
{
    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.Insert;

    /// <summary>The table name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The name of the schema the table belongs to, or null when the name says nothing.</summary>
    public string? Schema { get; set; }

    /// <summary>The columns the values are for, or empty for all of them in order.</summary>
    public List<string> Columns { get; } = [];

    /// <summary>The rows, each the expressions of its values.</summary>
    public List<List<Expression>> Rows { get; } = [];
}

/// <summary>UPDATE ... SET ... WHERE.</summary>
public sealed class UpdateStatement : Statement
{
    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.Update;

    /// <summary>The table name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The name of the schema the table belongs to, or null when the name says nothing.</summary>
    public string? Schema { get; set; }

    /// <summary>The alias of the table, or null when it has none.</summary>
    public string? Alias { get; set; }

    /// <summary>What to set each column to.</summary>
    public List<(string Column, Expression Value)> Assignments { get; } = [];

    /// <summary>The condition, or null.</summary>
    public Expression? Where { get; set; }
}

/// <summary>DELETE FROM ... WHERE.</summary>
public sealed class DeleteStatement : Statement
{
    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.Delete;

    /// <summary>The table name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The name of the schema the table belongs to, or null when the name says nothing.</summary>
    public string? Schema { get; set; }

    /// <summary>The alias of the table, or null when it has none.</summary>
    public string? Alias { get; set; }

    /// <summary>The condition, or null.</summary>
    public Expression? Where { get; set; }
}

/// <summary>One item of a select list.</summary>
/// <param name="Column">the column name, or null for a star or a count</param>
/// <param name="CountAll">whether this item is COUNT(*)</param>
public sealed record SelectItem(string? Column, bool CountAll);

/// <summary>SELECT ... FROM one table.</summary>
public sealed class SelectStatement : Statement
{
    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.Select;

    /// <summary>The table name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The items to select.</summary>
    public List<SelectItem> Items { get; } = [];

    /// <summary>The condition, or null.</summary>
    public Expression? Where { get; set; }

    /// <summary>The columns to order by, and in which direction.</summary>
    public List<(string Column, bool Ascending)> OrderBy { get; } = [];
}

/// <summary>
/// Reads a statement out of its tokens.
/// </summary>
public sealed partial class Parser
{
    private readonly CastDataProvider provider;

    private readonly string sql;

    private readonly List<Token> tokens;

    private readonly SessionLocal? session;

    private int position;

    /// <summary>
    /// The parameters read so far, in the order their placeholders appear.
    /// </summary>
    private readonly List<Expressions.Parameter> parameterList = [];

    /// <summary>
    /// The common table expressions this statement names, by name.
    /// </summary>
    private readonly Dictionary<string, Tables.CTE> ctes = [];

    private Parser(string sql, CastDataProvider provider, List<Token> tokens, SessionLocal? session)
    {
        this.sql = sql;
        this.provider = provider;
        this.tokens = tokens;
        this.session = session;
    }

    /// <summary>
    /// Creates a parser for a session, as the reference does before every statement.
    /// </summary>
    /// <remarks>
    /// The reference's parser is not reusable across statements and neither is this one: the
    /// token stream, the position in it and the list of parameters read so far all belong to the
    /// statement being read. A parser kept around would be a parser pointing at the end of the
    /// last statement, which reads as a statement and is not one.
    /// </remarks>
    /// <param name="session">the session</param>
    public Parser(SessionLocal session)
    {
        ArgumentNullException.ThrowIfNull(session);
        sql = string.Empty;
        provider = session.CastDataProvider;
        tokens = [];
        this.session = session;
    }

    /// <summary>
    /// Parses a statement and prepares it for execution.
    /// </summary>
    /// <remarks>
    /// The reference reads the statement, prepares it, and wraps it in a
    /// <see cref="CommandContainer"/>, so that what a caller receives is something it can run and
    /// ask about. The same three steps are here, once per statement: several statements in one
    /// text become a <see cref="CommandList"/>, which answers only its first statement while the
    /// rest run for effect or on NextResult.
    /// </remarks>
    /// <param name="sql">the statement text</param>
    /// <returns>the command</returns>
    public Command PrepareCommand(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        SessionLocal current = session ?? throw new TODOException("Parser.prepareCommand: this parser has no session");

        BitList parameters = new();
        List<Token> tokens = Tokenizer.TokenizeForTest(sql, false, parameters, provider);
        CheckParameterIndex(parameters);
        Parser parser = new(sql, provider, tokens, current);
        List<Prepared> prepared = [];
        while (!parser.AtEnd())
        {
            Prepared next = parser.ParseOneStatement();

            // The reference's prepareCommand: a statement ends at a semicolon or at the end of
            // the input, and anything else is a plain syntax error. The expected-token list is
            // empty on this path, so the reference's addExpected(SEMICOLON) adds nothing and the
            // failure is the one-argument form.
            if (parser.CurrentType != Token.Semicolon && parser.CurrentType != Token.EndOfInput)
            {
                throw DbException.GetSyntaxError(sql, parser.Current.Start);
            }

            next.Prepare();
            prepared.Add(next);
        }

        if (prepared.Count == 0)
        {
            return new CommandContainer(current, sql, new Dml.NoOperation(current));
        }

        if (prepared.Count == 1)
        {
            return new CommandContainer(current, sql, prepared[0]);
        }

        return new CommandList(current, sql, prepared);
    }

    /// <summary>
    /// Parses a SQL code snippet that represents an expression, as the reference's
    /// parseExpression does.
    /// </summary>
    /// <remarks>
    /// The reference reads the snippet with the parser of the session it belongs to, and this
    /// does the same: the snippet is tokenized on its own, and the expression is read out of
    /// those tokens.
    /// </remarks>
    /// <param name="sql">the code snippet</param>
    /// <returns>the expression</returns>
    public Expression ParseExpression(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        BitList parameters = new();
        List<Token> tokens = Tokenizer.TokenizeForTest(sql, false, parameters, provider);
        CheckParameterIndex(parameters);
        Parser parser = new(sql, provider, tokens, session);
        return parser.ReadExpression();
    }

    /// <summary>
    /// Whether only semicolons and the end stand between the position and the input end.
    /// </summary>
    /// <returns>true when nothing more is written</returns>
    private bool AtEnd()
    {
        int index = position;
        while (index < tokens.Count && tokens[index].TokenType == Token.Semicolon)
        {
            index++;
        }

        return index >= tokens.Count || tokens[index].TokenType == Token.EndOfInput;
    }

    /// <summary>
    /// Reads the next statement and builds the prepared statement for it.
    /// </summary>
    /// <returns>the prepared statement</returns>
    private Prepared ParseOneStatement()
    {
        SessionLocal current = session ?? throw new TODOException("Parser.parsePrepared: this parser has no session");
        while (position < tokens.Count && tokens[position].TokenType == Token.Semicolon)
        {
            position++;
        }

        Prepared prepared;
        if (position < tokens.Count && IsSelectFirst(tokens[position]))
        {
            if (IsWord("WITH", consume: false))
            {
                // The common table expressions come first, and the query that follows them
                // may read them.
                ReadWith();
            }

            prepared = ParseQuerySelect();
        }
        else if (IsWord("CREATE", consume: false) && NextIs("SEQUENCE"))
        {
            prepared = PrepareCreateSequence(current);
        }

        else if (IsWord("DROP", consume: false) && NextIs("ALL"))
        {
            prepared = PrepareDropAllObjects(current);
        }
        else if (IsWord("CREATE", consume: false) && NextIs("SCHEMA"))
        {
            prepared = PrepareCreateSchema(current);
        }
        else if (IsWord("ALTER", consume: false) && NextIs("SEQUENCE"))
        {
            prepared = PrepareAlterSequence(current);
        }
        else if (IsWord("DROP", consume: false) && NextIs("SEQUENCE"))
        {
            prepared = PrepareDropSequence(current);
        }
        else if (IsWord("TRUNCATE", consume: false))
        {
            prepared = PrepareTruncateTable(current);
        }
        else if (IsWord("MERGE", consume: false))
        {
            prepared = PrepareMergeUsing(current);
        }
        else if (IsWord("COMMENT", consume: false))
        {
            prepared = PrepareComment(current);
        }
        else
        {
            Statement statement = ReadStatement();
            statement.Sql = sql;
            prepared = statement switch
            {
                CreateTableStatement create => PrepareCreateTable(current, create),
                CreateIndexStatement createIndex => PrepareCreateIndex(current, createIndex),
                AlterTableStatement alter => PrepareAlterTable(current, alter),
                DropTableStatement drop => PrepareDropTable(current, drop),
                DropIndexStatement dropIndex => PrepareDropIndex(current, dropIndex),
                InsertStatement insert => PrepareInsert(current, insert),
                UpdateStatement update => PrepareUpdate(current, update),
                DeleteStatement delete => PrepareDelete(current, delete),
                SelectStatement select => new Query.Select(current),
                _ => new Dml.NoOperation(current),
            };
        }

        prepared.SetParameterList(parameterList.Count == 0 ? null : parameterList);
        return prepared;
    }

    /// <summary>
    /// Whether the word after the current one is the given word.
    /// </summary>
    /// <param name="word">the word</param>
    /// <returns>true if it is</returns>
    private bool NextIs(string word)
    {
        string? name = position + 1 < tokens.Count ? tokens[position + 1].AsIdentifier : null;
        return name is not null && name.Equals(word, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a table and the joins to it, as the reference's readTableReference does.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="select">the select the table belongs to</param>
    /// <returns>the filter of the table the join chain starts at</returns>
    private Tables.TableFilter ReadTableReference(SessionLocal session, Query.Select? select)
    {
        Tables.TableFilter top = ReadTablePrimary(session, select);
        Tables.TableFilter last = top;
        Tables.TableFilter join;
        while (true)
        {
            switch (CurrentType)
            {
                case ParserUtil.Right:
                    {
                        Read();
                        _ = IsWord("OUTER");
                        Expect(ParserUtil.Join, "JOIN");
                        // The right hand side is the 'inner' table usually.
                        join = ReadTableReference(session, select);
                        Expressions.Expression? on = ReadJoinSpecification(top, join, rightJoin: true);
                        AddJoin(join, top, outer: true, on);
                        top = join;
                        break;
                    }

                case ParserUtil.Left:
                    {
                        Read();
                        _ = IsWord("OUTER");
                        Expect(ParserUtil.Join, "JOIN");
                        join = ReadTableReference(session, select);
                        Expressions.Expression? on = ReadJoinSpecification(top, join, rightJoin: false);
                        AddJoin(top, join, outer: true, on);
                        break;
                    }

                case ParserUtil.Inner:
                    {
                        Read();
                        Expect(ParserUtil.Join, "JOIN");
                        join = ReadTableReference(session, select);
                        Expressions.Expression? on = ReadJoinSpecification(top, join, rightJoin: false);
                        AddJoin(top, join, outer: false, on);
                        break;
                    }

                case ParserUtil.Join:
                    {
                        Read();
                        join = ReadTableReference(session, select);
                        Expressions.Expression? on = ReadJoinSpecification(top, join, rightJoin: false);
                        AddJoin(top, join, outer: false, on);
                        break;
                    }

                case ParserUtil.Cross:
                    {
                        Read();
                        Expect(ParserUtil.Join, "JOIN");
                        join = ReadTablePrimary(session, select);
                        AddJoin(top, join, outer: false, null);
                        break;
                    }

                case ParserUtil.Natural:
                    {
                        Read();
                        Expect(ParserUtil.Join, "JOIN");
                        join = ReadTablePrimary(session, select);
                        Expressions.Expression? on = null;
                        foreach (Tables.Column column1 in last.GetColumns())
                        {
                            Tables.Column? column2 = FindColumn(join, last.GetColumnName(column1));
                            if (column2 is not null)
                            {
                                on = AddJoinColumn(on, last, join, column1, column2, rightJoin: false);
                            }
                        }

                        AddJoin(top, join, outer: false, on);
                        break;
                    }

                default:
                    return top;
            }

            last = join;
        }
    }

    /// <summary>
    /// Reads the rows of a VALUES clause, as the reference's parseValues does.
    /// </summary>
    /// <remarks>
    /// Every row must have the same number of values: the columns of the table are the
    /// columns of the first row, and a row with more or fewer values does not fit them.
    /// </remarks>
    /// <returns>the query the rows are read into</returns>
    private Query.Query ReadValues()
    {
        ExpectWord("VALUES");
        List<List<Expressions.Expression>> rows = [];
        List<Expressions.Expression> row = ReadValuesRow();
        rows.Add(row);
        int columnCount = row.Count;
        while (ReadIf(Token.Comma))
        {
            row = ReadValuesRow();
            if (row.Count != columnCount)
            {
                throw DbException.Get(Api.ErrorCode.ColumnCountDoesNotMatch);
            }

            rows.Add(row);
        }

        return new Query.TableValueConstructor(
            session ?? throw new TODOException("Parser.parseValues: this parser has no session"), rows);
    }

    /// <summary>
    /// Reads one row of a VALUES clause, as the reference's parseValuesRow does.
    /// </summary>
    /// <returns>the values of the row</returns>
    private List<Expressions.Expression> ReadValuesRow()
    {
        List<Expressions.Expression> row = [];
        if (!ReadIf(Token.OpenParen))
        {
            row.Add(ReadExpression());
            return row;
        }

        do
        {
            row.Add(ReadExpression());
        }
        while (ReadIf(Token.Comma));

        Expect(Token.CloseParen, ")");
        return row;
    }

    /// <summary>
    /// Reads a query in parentheses, as the reference's parseQueryExpression does.
    /// </summary>
    /// <remarks>
    /// A query in parentheses is a select, or the rows a VALUES clause names.
    /// </remarks>
    /// <returns>the query</returns>
    private Query.Query ParseQueryExpression(bool prepare = true)
    {
        if (IsWord("VALUES", consume: false))
        {
            Query.Query query = ReadValues();
            // The reference initializes the table value constructor in parseValues; the
            // predicates then prepare it after the outer columns are mapped.
            query.Init();
            return query;
        }

        if (IsWord("WITH", consume: false))
        {
            ReadWith();
            return ParseQuerySelect(endOfStatement: false, prepare: prepare);
        }

        return ParseQuerySelect(endOfStatement: false, prepare: prepare);
    }

    /// <summary>
    /// Reads the common table expressions of a WITH clause, as the reference's
    /// parseSingleCommonTableExpression does.
    /// </summary>
    /// <remarks>
    /// The tables the clause names are the tables of this statement: a query that follows the
    /// clause may read them, and a query inside a table of this statement may read them too.
    /// The reference keeps them in a chain of query scopes, one per WITH clause; this port
    /// keeps them in the parser, which reads one statement.
    /// </remarks>
    private void ReadWith()
    {
        SessionLocal current = session
            ?? throw new TODOException("Parser.parseWith: this parser has no session");
        ExpectWord("WITH");
        if (IsWord("RECURSIVE", consume: false))
        {
            // The reference reads a recursive expression with a table of its own that stands
            // in for the expression while it reads it, and an index that feeds the rows back
            // into the query. Neither is ported yet.
            throw new TODOException("Parser.parseWith: a recursive common table expression is not ported");
        }

        do
        {
            string name = ReadIdentifier();
            List<string>? columnNames = null;
            if (ReadIf(Token.OpenParen))
            {
                columnNames = [];
                do
                {
                    columnNames.Add(ReadIdentifier());
                }
                while (ReadIf(Token.Comma));

                Expect(Token.CloseParen, ")");
            }

            ExpectWord("AS");
            Expect(Token.OpenParen, "(");
            Query.Query query = ParseQueryExpression();
            Expect(Token.CloseParen, ")");

            Tables.Column[]? columnTemplates = columnNames is null
                ? null
                : CreateQueryColumnTemplateList(columnNames, query, cte: true);
            if (ctes.ContainsKey(name))
            {
                throw DbException.Get(Api.ErrorCode.TableOrViewAlreadyExists1, name);
            }

            ctes[name] = new Tables.CTE(
                current.GetDatabase().GetMainSchema(), name, columnTemplates, query, current);
        }
        while (ReadIf(Token.Comma));
    }

    /// <summary>
    /// Reads a current date or time function, as the reference's
    /// readCurrentDateTimeValueFunction does.
    /// </summary>
    /// <remarks>
    /// The function may name its scale in parentheses: CURRENT_TIMESTAMP(3) is a timestamp with
    /// three digits after the point. Without a scale the type brings its own.
    /// <para>
    /// The reference looks for a user-defined function of the same name first, when the
    /// database allows a built-in to be overridden, and hands that to the statement instead.
    /// The port has no user-defined functions yet, so the built-in is always the answer.
    /// </para>
    /// </remarks>
    /// <param name="function">the function, one of the constants of the function class</param>
    /// <param name="hasParen">whether the opening parenthesis was already read</param>
    /// <returns>the function</returns>
    private Expressions.Expression ReadCurrentDateTimeValueFunction(
        int function, bool hasParen)
    {
        int scale = -1;
        if (hasParen)
        {
            if (function != Expressions.Functions.CurrentDateTimeValueFunction.CurrentDate
                && CurrentType != Token.CloseParen)
            {
                scale = ReadInt();
                if (scale < 0 || scale > Values.ValueTime.MaximumScale)
                {
                    throw DbException.Get(
                        Api.ErrorCode.InvalidValueScale,
                        scale.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "0",
                        Values.ValueTime.MaximumScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            Expect(Token.CloseParen, ")");
        }

        return new Expressions.Functions.CurrentDateTimeValueFunction(function, scale);
    }

    /// <summary>
    /// Reads a derived table: a query in parentheses that stands for a table, as the
    /// reference's readDerivedTableWithCorrelation does.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="select">the select the table belongs to</param>
    /// <returns>the filter of the table</returns>
    private Tables.TableFilter ReadDerivedTable(SessionLocal session, Query.Select? select)
    {
        Query.Query query = ParseQueryExpression();
        Expect(Token.CloseParen, ")");
        string? alias = ReadFromAlias();
        List<string>? derivedColumnNames = null;
        Tables.Column[]? columnTemplates = null;
        string tableAlias;
        if (alias is not null)
        {
            derivedColumnNames = ReadDerivedColumnNames();
            if (derivedColumnNames is not null)
            {
                columnTemplates = CreateQueryColumnTemplateList(derivedColumnNames, query, cte: false);
            }

            tableAlias = alias;
        }
        else
        {
            // The reference gives a table without an alias a name of its own, so that the
            // columns of the table can be told apart from the columns of the query above.
            tableAlias = "_DERIVED_TABLE_" + Current.Start;
        }

        Tables.Table table = query.ToTable(tableAlias, columnTemplates, forCreateView: false, topQuery: null);
        Tables.TableFilter filter = new(
            session, table, tableAlias, rightsChecked: true, select, 0, null);
        if (derivedColumnNames is not null)
        {
            filter.SetDerivedColumns(derivedColumnNames);
        }

        return filter;
    }

    /// <summary>
    /// Reads the names a table gives its own columns, as the reference's readDerivedColumnNames
    /// does.
    /// </summary>
    /// <returns>the names, or null when the table has none</returns>
    private List<string>? ReadDerivedColumnNames()
    {
        if (!ReadIf(Token.OpenParen))
        {
            return null;
        }

        List<string> derivedColumnNames = [];
        do
        {
            derivedColumnNames.Add(ReadIdentifier());
        }
        while (ReadIfMore());
        return derivedColumnNames;
    }

    /// <summary>
    /// Reads the alias of a table that is in parentheses, as the reference's readCorrelation
    /// does.
    /// </summary>
    /// <param name="tableFilter">the filter of the table</param>
    /// <returns>the filter</returns>
    private Tables.TableFilter ReadCorrelation(Tables.TableFilter tableFilter)
    {
        string? alias = ReadFromAlias();
        if (alias is not null)
        {
            tableFilter.SetAlias(alias);
            List<string>? derivedColumnNames = ReadDerivedColumnNames();
            if (derivedColumnNames is not null)
            {
                tableFilter.SetDerivedColumns(derivedColumnNames);
            }
        }

        return tableFilter;
    }

    /// <summary>
    /// Whether what is in front of the parser is a query in parentheses, as the reference's
    /// isDerivedTable does.
    /// </summary>
    /// <remarks>
    /// The tokens are looked at ahead of the parser: the question is what the parentheses
    /// hold, and the parser cannot know that before it has read them.
    /// </remarks>
    /// <returns>true when the parentheses hold a query</returns>
    private bool IsDerivedQuery()
    {
        int offset = position;
        int level = 0;
        while (tokens[offset].TokenType == Token.OpenParen)
        {
            level++;
            offset++;
        }

        bool query = IsDirectQuery(offset);
        if (query && level > 0)
        {
            offset = ScanToCloseParen(offset + 1);
            if (offset < 0)
            {
                return false;
            }

            while (true)
            {
                switch (tokens[offset].TokenType)
                {
                    case Token.Semicolon:
                    case Token.EndOfInput:
                        return false;
                    case Token.OpenParen:
                        offset = ScanToCloseParen(offset + 1);
                        if (offset < 0)
                        {
                            return false;
                        }

                        break;
                    case Token.CloseParen:
                        if (--level == 0)
                        {
                            return true;
                        }

                        offset++;
                        break;
                    case ParserUtil.Join:
                        return false;
                    default:
                        offset++;
                        break;
                }
            }
        }

        return query;
    }

    /// <summary>
    /// Whether the token at the given place starts a query, as the reference's isDirectQuery
    /// does.
    /// </summary>
    /// <param name="offset">the place of the token</param>
    /// <returns>true when the token starts a query</returns>
    private bool IsDirectQuery(int offset)
    {
        // The statement words are identifiers in this port, so the words are compared by
        // their text and not by a token kind.
        string? name = tokens[offset].AsIdentifier;
        if (name is null)
        {
            return false;
        }

        if (name.Equals("SELECT", StringComparison.OrdinalIgnoreCase)
            || name.Equals("VALUES", StringComparison.OrdinalIgnoreCase)
            || name.Equals("WITH", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Equals("TABLE", StringComparison.OrdinalIgnoreCase))
        {
            return tokens[offset + 1].TokenType != Token.OpenParen;
        }

        return false;
    }

    /// <summary>
    /// Finds the closing parenthesis that matches the one at the given place, as the
    /// reference's scanToCloseParen does.
    /// </summary>
    /// <param name="offset">the place of the opening parenthesis</param>
    /// <returns>the place of the closing parenthesis, or -1 when there is none</returns>
    private int ScanToCloseParen(int offset)
    {
        for (int level = 0; ;)
        {
            switch (tokens[offset].TokenType)
            {
                case Token.Semicolon:
                case Token.EndOfInput:
                    return -1;
                case Token.OpenParen:
                    level++;
                    break;
                case Token.CloseParen:
                    if (--level < 0)
                    {
                        return offset + 1;
                    }

                    break;
            }

            offset++;
        }
    }

    /// <summary>
    /// Creates the columns of a table out of the columns of a query, as the reference's
    /// createQueryColumnTemplateList does.
    /// </summary>
    /// <param name="cols">the names the statement gave, or null</param>
    /// <param name="query">the query</param>
    /// <param name="cte">whether the table is a common table expression</param>
    /// <returns>the columns</returns>
    private static Tables.Column[] CreateQueryColumnTemplateList(
        IReadOnlyList<string> cols,
        Query.Query query,
        bool cte)
    {
        SessionLocal current = query.GetSession();
        query.Prepare();
        Expression[] expressions = query.GetExpressions();
        List<Tables.Column> columnTemplateList = [];
        for (int i = 0; i < expressions.Length; ++i)
        {
            Expression columnExp = expressions[i];

            // The name the statement gave wins; without it the name of the expression is the
            // name, and for a common table expression the name of the column.
            string columnName = cols.Count > i
                ? cols[i]
                : cte ? columnExp.GetColumnName(current, i) : columnExp.GetAlias(current, i);
            columnTemplateList.Add(new Tables.Column(columnName, columnExp.GetTypeInfo()));
        }

        return [.. columnTemplateList];
    }

    /// <summary>
    /// Reads a table and its alias, as the reference's readTablePrimary does.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="select">the select the table belongs to</param>
    /// <returns>the filter of the table</returns>
    private Tables.TableFilter ReadTablePrimary(SessionLocal session, Query.Select? select)
    {
        if (ReadIf(Token.OpenParen))
        {
            if (IsDerivedQuery())
            {
                // A derived table: a query in parentheses that stands for a table.
                return ReadDerivedTable(session, select);
            }

            // A parenthesized joined table: (T1 JOIN T2).
            Tables.TableFilter tableFilter = ReadTableReference(session, select);
            Expect(Token.CloseParen, ")");
            return ReadCorrelation(tableFilter);
        }

        Tables.Table fromTable;
        bool hasAlias = true;
        string tableName;
        if (IsWord("VALUES", consume: false))
        {
            // The table of a table value constructor: the rows a VALUES clause names.
            Query.Query values = ReadValues();
            tableName = "_VALUES_" + Current.Start.ToString(System.Globalization.CultureInfo.InvariantCulture);
            fromTable = values.ToTable(tableName, null, forCreateView: false, topQuery: null);
            hasAlias = false;
        }
        else
        {
            tableName = ReadIdentifier();
            Schemas.Schema schema = session.GetCurrentSchema();
            if (ReadIf(Token.Dot))
            {
                // The reference reads the schema of the name and looks the table up in it.
                string schemaName = tableName;
                tableName = ReadIdentifier();
                schema = session.GetDatabase().FindSchema(schemaName)
                    ?? throw DbException.Get(Api.ErrorCode.SchemaNotFound1, schemaName);
            }

            if (CurrentType == Token.OpenParen
                && (tableName.Equals(Tables.RangeTable.Name, StringComparison.OrdinalIgnoreCase)
                    || tableName.Equals(Tables.RangeTable.Alias, StringComparison.OrdinalIgnoreCase)))
            {
                // The range table is a table function: SYSTEM_RANGE(min, max[, step]).
                Read();
                Expressions.Expression min = ReadQueryOr();
                Expect(Token.Comma, ",");
                Expressions.Expression max = ReadQueryOr();
                Expressions.Expression? step = null;
                if (ReadIf(Token.Comma))
                {
                    step = ReadQueryOr();
                }

                Expect(Token.CloseParen, ")");
                fromTable = step is null
                    ? new Tables.RangeTable(session.GetDatabase().GetMainSchema()!, min, max)
                    : new Tables.RangeTable(session.GetDatabase().GetMainSchema()!, min, max, step);
            }
            else if (IsWord("TABLE"))
            {
                // The data change delta table: FINAL TABLE (INSERT INTO ...). The rows of the
                // table are the rows the statement changed, and the name in front of it says
                // which version of them the caller gets. It takes no alias: the reference
                // leaves the table before it reads one.
                fromTable = ReadDataChangeDeltaTable(session, schema, tableName);
                hasAlias = false;
            }
            else
            {
                fromTable = ReadTableOrView(session, schema, tableName);
            }
        }

        string? filterAlias = hasAlias ? ReadFromAlias() : null;
        return new(
            session, fromTable, filterAlias ?? tableName, rightsChecked: true, select, 0, null);
    }

    /// <summary>
    /// Reads the condition of a join, as the reference's readJoinSpecification does.
    /// </summary>
    /// <param name="filter1">the filter on the left of the join</param>
    /// <param name="filter2">the filter on the right of the join</param>
    /// <param name="rightJoin">whether the join is a right one</param>
    /// <returns>the condition, or null when the join has none</returns>
    private Expressions.Expression? ReadJoinSpecification(
        Tables.TableFilter filter1, Tables.TableFilter filter2, bool rightJoin)
    {
        Expressions.Expression? on = null;
        if (IsWord("ON"))
        {
            on = ReadQueryOr();
        }
        else if (IsWord("USING") && ReadIf(Token.OpenParen))
        {
            do
            {
                string columnName = ReadIdentifier();
                on = AddJoinColumn(
                    on,
                    filter1,
                    filter2,
                    FindColumn(filter1, columnName),
                    FindColumn(filter2, columnName),
                    rightJoin);
            }
            while (ReadIf(Token.Comma));

            Expect(Token.CloseParen, ")");
        }

        return on;
    }

    /// <summary>
    /// Adds one join to another, as the reference's addJoin does.
    /// </summary>
    /// <param name="top">the parent join</param>
    /// <param name="join">the child join</param>
    /// <param name="outer">whether the child join is an outer join</param>
    /// <param name="on">the condition of the join</param>
    private void AddJoin(
        Tables.TableFilter top, Tables.TableFilter join, bool outer, Expressions.Expression? on)
    {
        if (join.GetJoin() is not null)
        {
            // The join has a join of its own, so the two of them are nested: the inner one
            // is read as a table of its own, and the outer one joins that.
            string joinTable = "_JOIN_" + Current.Start;
            Tables.TableFilter nested = new(
                session ?? throw new TODOException("Parser.readTableReference: this parser has no session"),
                new Tables.DualTable((session ?? throw new TODOException(
                    "Parser.readTableReference: this parser has no session")).GetDatabase()),
                joinTable, rightsChecked: true, select: null, 0, null);
            nested.SetNestedJoin(join);
            join = nested;
        }

        top.AddJoin(join, outer, on);
    }

    /// <summary>
    /// Adds a column pair to the condition of a join, as the reference's addJoinColumn does.
    /// </summary>
    /// <param name="on">the condition so far, or null</param>
    /// <param name="filter1">the filter on the left of the join</param>
    /// <param name="filter2">the filter on the right of the join</param>
    /// <param name="column1">the column on the left</param>
    /// <param name="column2">the column on the right</param>
    /// <param name="rightJoin">whether the join is a right one</param>
    /// <returns>the condition</returns>
    private Expressions.Expression AddJoinColumn(
        Expressions.Expression? on,
        Tables.TableFilter filter1,
        Tables.TableFilter filter2,
        Tables.Column? column1,
        Tables.Column? column2,
        bool rightJoin)
    {
        if (column1 is null || column2 is null)
        {
            throw DbException.GetSyntaxError(sql, Current.Start, "column");
        }

        if (rightJoin)
        {
            filter1.AddCommonJoinColumns(column1, column2, filter2);
            filter2.AddCommonJoinColumnToExclude(column2);
        }
        else
        {
            filter1.AddCommonJoinColumns(column1, column1, filter1);
            filter2.AddCommonJoinColumnToExclude(column2);
        }

        Expressions.Expression tableExpr = new Expressions.ExpressionColumn(
            filter1.GetColumnName(column1), filter1.GetTableAlias(), 0);
        Expressions.Expression joinExpr = new Expressions.ExpressionColumn(
            filter2.GetColumnName(column2), filter2.GetTableAlias(), 0);
        Expressions.Expression equal = new Expressions.Conditions.Comparison(
            Expressions.Conditions.Comparison.Equal, tableExpr, joinExpr, whenOperand: false);
        if (on is null)
        {
            return equal;
        }

        return new Expressions.Conditions.ConditionAndOr(
            Expressions.Conditions.ConditionAndOr.And, on, equal);
    }

    /// <summary>
    /// Finds the column of a table by name, as the reference's TableFilter.getColumn does.
    /// </summary>
    /// <param name="filter">the filter of the table</param>
    /// <param name="name">the name of the column</param>
    /// <returns>the column, or null when the table has no such column</returns>
    private static Tables.Column? FindColumn(Tables.TableFilter filter, string name)
    {
        foreach (Tables.Column column in filter.GetColumns())
        {
            if (column.GetName().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return column;
            }
        }

        return null;
    }

    /// <summary>
    /// Collects the filters of a join, as the reference's visit of a nested join does.
    /// </summary>
    /// <param name="filter">the filter to visit</param>
    /// <returns>the filters</returns>
    private static List<Tables.TableFilter> CollectFilters(Tables.TableFilter filter)
    {
        List<Tables.TableFilter> filters = [];
        filter.Visit(new FilterCollector(filters));
        return filters;
    }

    /// <summary>
    /// Collects the filters a visit walks over.
    /// </summary>
    private sealed class FilterCollector : Tables.TableFilter.TableFilterVisitor
    {
        private readonly List<Tables.TableFilter> filters;

        /// <summary>
        /// Creates the collector.
        /// </summary>
        /// <param name="filters">the list to add to</param>
        internal FilterCollector(List<Tables.TableFilter> filters)
        {
            this.filters = filters;
        }

        /// <inheritdoc/>
        public void Accept(Tables.TableFilter f) => filters.Add(f);
    }

    /// <summary>
    /// Reads the alias of a table, as the reference's readFromAlias does.
    /// </summary>
    /// <remarks>
    /// The alias may be written with the AS or without it, and the two forms mean the same
    /// thing: "FROM T t" and "FROM T AS t" are the same query.
    /// </remarks>
    /// <returns>the alias, or null when there is none</returns>
    private string? ReadFromAlias(string? alias = null)
    {
        if (IsWord("AS"))
        {
            return ReadIdentifier();
        }

        // A name that is already there is not replaced by an implicit one: the reference
        // reads the alias of a table that has one only when the name was written out.
        if (alias is null && IsNameToken() && !IsClauseWord(Current.AsIdentifier!))
        {
            return ReadIdentifier();
        }

        return alias;
    }

    /// <summary>
    /// Reads a name that may name its schema, as the reference's readIdentifierWithSchema does,
    /// and gives back the name of the schema.
    /// </summary>
    /// <remarks>
    /// The name of the schema is not looked up here: the statement is read before the session
    /// is known, and the schema is looked up when the statement is built.
    /// </remarks>
    /// <param name="schemaName">the name of the schema, or null</param>
    /// <returns>the name</returns>
    private string ReadIdentifierWithSchemaName(out string? schemaName)
    {
        string name = ReadIdentifier();
        schemaName = null;
        if (ReadIf(Token.Dot))
        {
            schemaName = name;
            name = ReadIdentifier();
        }

        return name;
    }

    /// <summary>
    /// Reads a name that may name its schema, as the reference's readIdentifierWithSchema does.
    /// </summary>
    /// <remarks>
    /// The reference reads the schema name into the parser and the statement is built against
    /// it; a name without a schema belongs to the schema of the session. The name of the
    /// database may stand in front of the schema name, and then it is skipped.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="schema">the schema the name belongs to</param>
    /// <returns>the name</returns>
    private string ReadIdentifierWithSchema(SessionLocal session, out Schemas.Schema schema)
    {
        string name = ReadIdentifier();
        schema = CurrentSchema(session);
        if (ReadIf(Token.Dot))
        {
            string first = name;
            name = ReadIdentifier();
            if (CurrentType == Token.Dot
                && first.Equals(session.GetDatabase().GetShortName(), StringComparison.OrdinalIgnoreCase))
            {
                Read();
                schema = session.GetDatabase().GetSchema(name);
                name = ReadIdentifier();
            }
            else
            {
                schema = session.GetDatabase().GetSchema(first);
            }
        }

        return name;
    }

    /// <summary>
    /// Reads the data change delta table, as the reference's readDataChangeDeltaTable does.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="schema">the schema the table belongs to</param>
    /// <param name="resultOptionName">the name in front of the table</param>
    /// <returns>the table</returns>
    private Tables.Table ReadDataChangeDeltaTable(
        SessionLocal session, Schemas.Schema schema, string resultOptionName)
    {
        Tables.DataChangeDeltaTable.ResultOption resultOption;
        switch (resultOptionName.ToUpperInvariant())
        {
            case "OLD":
                resultOption = Tables.DataChangeDeltaTable.ResultOption.OLD;
                break;
            case "NEW":
                resultOption = Tables.DataChangeDeltaTable.ResultOption.NEW;
                break;
            case "FINAL":
                resultOption = Tables.DataChangeDeltaTable.ResultOption.FINAL;
                break;
            default:
                throw DbException.GetSyntaxError(sql, Current.Start, "OLD TABLE");
        }

        Expect(Token.OpenParen, "(");

        Statement statement = ReadStatement();
        Prepared prepared = statement switch
        {
            InsertStatement insert => PrepareInsert(session, insert),
            UpdateStatement update => PrepareUpdate(session, update),
            DeleteStatement delete => PrepareDelete(session, delete),
            _ => throw DbException.GetSyntaxError(sql, Current.Start, "INSERT"),
        };

        Expect(Token.CloseParen, ")");
        return new Tables.DataChangeDeltaTable(
            schema, (Dml.DataChangeStatement)prepared, resultOption);
    }

    /// <summary>
    /// Reads a literal with a type in front of it, as the reference's readTermWithIdentifier does.
    /// </summary>
    /// <remarks>
    /// The name says the type and the string that follows says the value: TIMESTAMP '2035-09-25
    /// 00:00:00' is a timestamp, DATE '2035-09-25' is a date. A name that is not one of these
    /// is not a typed literal, and the caller reads it as a column.
    /// </remarks>
    /// <param name="name">the name in front of the literal</param>
    /// <returns>the expression, or null when the name is not a typed literal</returns>
    private Expressions.Expression? ReadTypedLiteral(string name)
    {
        int index = position;
        Read();
        switch (name.ToUpperInvariant())
        {
            case "DATE":
            case "D":
                {
                    string? text = ReadStringLiteralValue();
                    if (text is null)
                    {
                        position = index;
                        return null;
                    }

                    return Expressions.ValueExpression.Get(Values.ValueDate.Parse(text));
                }

            case "TIME":
                {
                    bool withZone = IsWord("WITH") && IsWord("TIME") && IsWord("ZONE");
                    if (!withZone)
                    {
                        position = index + 1;
                    }

                    if (withZone)
                    {
                        string? text = ReadStringLiteralValue()
                            ?? throw DbException.GetSyntaxError(sql, Current.Start);
                        return Expressions.ValueExpression.Get(
                            Values.ValueTimeTimeZone.Parse(text, provider));
                    }

                    int withoutIndex = position;
                    bool without = IsWord("WITHOUT") && IsWord("TIME") && IsWord("ZONE");
                    if (!without)
                    {
                        position = withoutIndex;
                    }

                    string? time = ReadStringLiteralValue();
                    if (time is not null)
                    {
                        return Expressions.ValueExpression.Get(Values.ValueTime.Parse(time, provider));
                    }

                    if (without)
                    {
                        throw DbException.GetSyntaxError(sql, Current.Start);
                    }

                    position = index;
                    return null;
                }

            case "TIMESTAMP":
                {
                    bool withZone = IsWord("WITH") && IsWord("TIME") && IsWord("ZONE");
                    if (!withZone)
                    {
                        position = index + 1;
                    }

                    if (withZone)
                    {
                        string? text = ReadStringLiteralValue()
                            ?? throw DbException.GetSyntaxError(sql, Current.Start);
                        return Expressions.ValueExpression.Get(
                            Values.ValueTimestampTimeZone.Parse(text, provider));
                    }

                    int withoutIndex = position;
                    bool without = IsWord("WITHOUT") && IsWord("TIME") && IsWord("ZONE");
                    if (!without)
                    {
                        position = withoutIndex;
                    }

                    string? timestamp = ReadStringLiteralValue();
                    if (timestamp is not null)
                    {
                        return Expressions.ValueExpression.Get(
                            Values.ValueTimestamp.Parse(timestamp, provider));
                    }

                    if (without)
                    {
                        throw DbException.GetSyntaxError(sql, Current.Start);
                    }

                    position = index;
                    return null;
                }

            case "T":
            case "TS":
                {
                    string? text = ReadStringLiteralValue();
                    if (text is null)
                    {
                        position = index;
                        return null;
                    }

                    return name.Equals("T", StringComparison.OrdinalIgnoreCase)
                        ? Expressions.ValueExpression.Get(Values.ValueTime.Parse(text, provider))
                        : Expressions.ValueExpression.Get(Values.ValueTimestamp.Parse(text, provider));
                }

            case "GEOMETRY":
                {
                    string? text = ReadStringLiteralValue();
                    if (text is null)
                    {
                        position = index;
                        return null;
                    }

                    return Expressions.ValueExpression.Get(Values.ValueGeometry.Get(text));
                }

            default:
                position = index;
                return null;
        }
    }

    /// <summary>
    /// Reads a string literal, if the current token is one.
    /// </summary>
    /// <returns>the text of the literal, or null when the current token is not one</returns>
    private string? ReadStringLiteralValue()
    {
        Values.Value? literal = Current.GetValue(provider);
        if (literal is null || literal.GetValueType() != Values.Value.Varchar)
        {
            return null;
        }

        Read();
        return literal.GetString();
    }


    /// <summary>
    /// Builds the command of a COMMENT ON statement, as the reference's parseComment does.
    /// </summary>
    /// <remarks>
    /// The comment is an expression, most often a string literal. A qualified column name is
    /// read part by part — [catalog.]schema.table.column — because the generic qualified-name
    /// reader would not read a dotted column name; a catalog that does not name this database
    /// is refused, as the reference refuses it. The empty-schema forms that some modes allow
    /// are not ported: the port only has the regular mode, where they are syntax errors.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <returns>the command</returns>
    private Ddl.SetComment PrepareComment(SessionLocal session)
    {
        ExpectWord("COMMENT");
        ExpectWord("ON");
        int type;
        bool column = false;
        if (IsWord("TABLE") || IsWord("VIEW"))
        {
            type = Engine.DbObject.TableOrView;
        }
        else if (IsWord("COLUMN"))
        {
            column = true;
            type = Engine.DbObject.TableOrView;
        }
        else if (IsWord("CONSTANT"))
        {
            type = Engine.DbObject.Constant;
        }
        else if (IsWord("CONSTRAINT"))
        {
            type = Engine.DbObject.Constraint;
        }
        else if (IsWord("ALIAS"))
        {
            type = Engine.DbObject.FunctionAlias;
        }
        else if (IsWord("INDEX"))
        {
            type = Engine.DbObject.Index;
        }
        else if (IsWord("ROLE"))
        {
            type = Engine.DbObject.Role;
        }
        else if (IsWord("SCHEMA"))
        {
            type = Engine.DbObject.Schema;
        }
        else if (IsWord("SEQUENCE"))
        {
            type = Engine.DbObject.Sequence;
        }
        else if (IsWord("TRIGGER"))
        {
            type = Engine.DbObject.Trigger;
        }
        else if (IsWord("USER"))
        {
            type = Engine.DbObject.User;
        }
        else if (IsWord("DOMAIN"))
        {
            type = Engine.DbObject.Domain;
        }
        else
        {
            throw DbException.GetSyntaxError(sql, Current.Start);
        }

        Ddl.SetComment command = new(session);
        if (column)
        {
            string objectName = ReadIdentifier();
            string? tmpSchemaName = null;
            Expect(Token.Dot, ".");
            string columnName = ReadIdentifier();
            if (ReadIf(Token.Dot))
            {
                tmpSchemaName = objectName;
                objectName = columnName;
                columnName = ReadIdentifier();
                if (ReadIf(Token.Dot))
                {
                    if (!tmpSchemaName.Equals(
                        session.GetDatabase().GetShortName(), StringComparison.OrdinalIgnoreCase))
                    {
                        throw DbException.Get(Api.ErrorCode.DatabaseNotFound1, tmpSchemaName);
                    }

                    tmpSchemaName = objectName;
                    objectName = columnName;
                    columnName = ReadIdentifier();
                }
            }

            command.SetSchemaName(tmpSchemaName ?? session.GetCurrentSchema().GetName());
            command.SetObjectName(objectName);
            command.SetColumn(true);
            command.SetColumnName(columnName);
        }
        else
        {
            string objectName = ReadIdentifierWithSchema(session, out Schemas.Schema schema);
            command.SetSchemaName(schema.GetName());
            command.SetObjectName(objectName);
        }

        command.SetObjectType(type);
        ExpectWord("IS");
        command.SetCommentExpression(ReadExpression());
        return command;
    }

    /// <summary>
    /// Builds the command of a DROP ALL OBJECTS statement, as the reference's parseDrop does.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the command</returns>
    private Ddl.DropDatabase PrepareDropAllObjects(SessionLocal session)
    {
        ExpectWord("DROP");
        ExpectWord("ALL");
        ExpectWord("OBJECTS");
        Ddl.DropDatabase command = new(session);
        command.SetDropAllObjects(true);
        return command;
    }

    /// <summary>
    /// Builds the command of a MERGE INTO ... USING ... ON ... WHEN ... statement, as the
    /// reference's parseMerge and parseMergeUsing do.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the command</returns>
    private Prepared PrepareMergeUsing(SessionLocal session)
    {
        ExpectWord("MERGE");
        ExpectWord("INTO");
        Tables.TableFilter targetTableFilter = ReadSimpleTableFilter(session);
        ExpectWord("USING");
        Dml.MergeUsing command = new(session, targetTableFilter);
        command.SetSourceTableFilter(ReadTableReference(session, select: null));
        ExpectWord("ON");
        command.SetOnCondition(ReadQueryOr());

        ExpectWord("WHEN");
        do
        {
            bool matched = IsWord("MATCHED");
            if (matched)
            {
                ReadWhenMatched(command);
            }
            else
            {
                ReadWhenNotMatched(command);
            }
        }
        while (IsWord("WHEN", consume: true));

        command.SetSql(sql, null);
        return command;
    }

    /// <summary>
    /// Reads a WHEN MATCHED clause, as the reference's parseWhenMatched does.
    /// </summary>
    /// <param name="command">the statement</param>
    private void ReadWhenMatched(Dml.MergeUsing command)
    {
        Expressions.Expression? and = ReadIf(ParserUtil.And) ? ReadQueryOr() : null;
        ExpectWord("THEN");
        Dml.MergeUsing.When when;
        if (IsWord("UPDATE"))
        {
            Dml.MergeUsing.WhenMatchedThenUpdate update = new(command);
            update.SetSetClauseList(ReadUpdateSetClause(command.GetTargetTableFilter()));
            when = update;
        }
        else
        {
            ExpectWord("DELETE");
            when = new Dml.MergeUsing.WhenMatchedThenDelete(command);
        }

        // The reference reads a WHERE after a matched clause only in the modes whose
        // mergeWhere flag is set. The port has the regular mode only, where it is not.
        when.SetAndCondition(and);
        command.AddWhen(when);
    }

    /// <summary>
    /// Reads a WHEN NOT MATCHED clause, as the reference's parseWhenNotMatched does.
    /// </summary>
    /// <param name="command">the statement</param>
    private void ReadWhenNotMatched(Dml.MergeUsing command)
    {
        ExpectWord("NOT");
        ExpectWord("MATCHED");
        Expressions.Expression? and = ReadIf(ParserUtil.And) ? ReadQueryOr() : null;
        ExpectWord("THEN");
        ExpectWord("INSERT");
        Tables.Column[]? columns = ReadIf(Token.OpenParen)
            ? ReadColumnList(command.GetTargetTableFilter().GetTable()!)
            : null;
        bool? overridingSystem = ReadIfOverriding();
        ExpectWord("VALUES");
        Expect(Token.OpenParen, "(");
        List<Expressions.Expression> values = [];
        if (!ReadIf(Token.CloseParen))
        {
            do
            {
                values.Add(ReadExpressionOrDefault());
            }
            while (ReadIfMore());
        }

        Dml.MergeUsing.WhenNotMatched when = new(command, columns, overridingSystem, [.. values]);
        when.SetAndCondition(and);
        command.AddWhen(when);
    }

    /// <summary>
    /// Reads a table and its alias into a filter, as the reference's readSimpleTableFilter does.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the filter</returns>
    private Tables.TableFilter ReadSimpleTableFilter(SessionLocal session)
    {
        string name = ReadIdentifierWithSchemaName(out string? schemaName);
        Schemas.Schema schema = schemaName is null
            ? session.GetCurrentSchema()
            : session.GetDatabase().GetSchema(schemaName);
        Tables.Table table = ReadTableOrView(session, schema, name);
        return new(session, table, ReadFromAlias(), rightsChecked: true, select: null, 0, null);
    }

    /// <summary>
    /// Reads a SET clause list, as the reference's readUpdateSetClause does.
    /// </summary>
    /// <remarks>
    /// The simple form is ported: a column, and what it becomes. The row-value form
    /// <c>(a, b) = (x, y)</c> and the array element form <c>a[1] = x</c> wait for the
    /// expression list and the array index reader; the port's UPDATE does not read them either.
    /// </remarks>
    /// <param name="filter">the filter of the updated table</param>
    /// <returns>the assignments</returns>
    private Dml.SetClauseList ReadUpdateSetClause(Tables.TableFilter filter)
    {
        ExpectWord("SET");
        Dml.SetClauseList list = new(filter.GetTable()!);
        do
        {
            if (CurrentType == Token.OpenParen)
            {
                throw new TODOException(
                    "Parser.readUpdateSetClause: row-value assignment needs ExpressionList");
            }

            if (CurrentType is Token.OpenBracket)
            {
                throw new TODOException(
                    "Parser.readUpdateSetClause: array element assignment needs the array index reader");
            }

            Tables.Column column = ParseColumn(filter.GetTable()!);
            Expect(Token.Equal, "=");
            list.AddSingle(column, null, ReadExpressionOrDefault());
        }
        while (ReadIf(Token.Comma));

        return list;
    }

    /// <summary>
    /// Reads a column of a table, as the reference's parseColumn does.
    /// </summary>
    /// <param name="table">the table</param>
    /// <returns>the column</returns>
    private Tables.Column ParseColumn(Tables.Table table)
    {
        if (CurrentType == ParserUtil._ROWID_)
        {
            Read();
            return table.GetRowIdColumn()!;
        }

        return table.GetColumn(ReadIdentifier());
    }

    /// <summary>
    /// Reads a list of columns of a table, as the reference's parseColumnList does.
    /// </summary>
    /// <remarks>
    /// The opening parenthesis is the caller's: the reference reads it before calling and reads
    /// the closing one here, which is what makes <c>()</c> a list of no columns.
    /// </remarks>
    /// <param name="table">the table</param>
    /// <returns>the columns</returns>
    private Tables.Column[] ReadColumnList(Tables.Table table)
    {
        List<Tables.Column> columns = [];
        HashSet<Tables.Column> set = [];
        if (!ReadIf(Token.CloseParen))
        {
            do
            {
                Tables.Column column = ParseColumn(table);
                if (!set.Add(column))
                {
                    throw DbException.Get(Api.ErrorCode.DuplicateColumnName1, column.GetName());
                }

                columns.Add(column);
            }
            while (ReadIfMore());
        }

        return [.. columns];
    }

    /// <summary>
    /// Reads OVERRIDING USER VALUE or OVERRIDING SYSTEM VALUE, if one is written.
    /// </summary>
    /// <returns>false for USER, true for SYSTEM, null when neither is written</returns>
    private bool? ReadIfOverriding()
    {
        bool? overridingSystem = null;
        if (ReadIfWords("OVERRIDING", "USER", "VALUE"))
        {
            overridingSystem = false;
        }
        else if (ReadIfWords("OVERRIDING", "SYSTEM", "VALUE"))
        {
            overridingSystem = true;
        }

        return overridingSystem;
    }

    /// <summary>
    /// Reads an expression, or the DEFAULT keyword, as the reference's readExpressionOrDefault
    /// does.
    /// </summary>
    /// <returns>the expression</returns>
    private Expressions.Expression ReadExpressionOrDefault()
    {
        if (IsWord("DEFAULT"))
        {
            return Expressions.ValueExpression.Default;
        }

        return ReadQueryOr();
    }

    /// <summary>
    /// Builds the command of a CREATE SCHEMA statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the command</returns>
    private Ddl.CreateSchema PrepareCreateSchema(SessionLocal session)
    {
        ExpectWord("CREATE");
        ExpectWord("SCHEMA");
        bool ifNotExists = ReadIfNotExists();
        string schemaName = ReadIdentifier();
        Ddl.CreateSchema command = new(session);
        command.SetIfNotExists(ifNotExists);
        command.SetSchemaName(schemaName);
        if (IsWord("AUTHORIZATION"))
        {
            // The reference gives the schema to the user the clause names and refuses the
            // statement when there is no such user. This port has no users yet, so the name
            // is read and kept, and the schema has no owner.
            Read();
            command.SetAuthorization(ReadIdentifier());
        }

        return command;
    }

    /// <summary>
    /// Builds the command of a CREATE SEQUENCE statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the command</returns>
    private Ddl.CreateSequence PrepareCreateSequence(SessionLocal session)
    {
        ExpectWord("CREATE");
        ExpectWord("SEQUENCE");
        bool ifNotExists = ReadIfNotExists();
        string sequenceName = ReadIdentifierWithSchema(session, out Schemas.Schema schema);
        Ddl.CreateSequence command = new(session, schema);
        command.SetIfNotExists(ifNotExists);
        command.SetSequenceName(sequenceName);
        Ddl.SequenceOptions options = new();
        ReadSequenceOptions(options, command, allowDataType: true, forAlterColumn: false);
        command.SetOptions(options);
        return command;
    }

    /// <summary>
    /// Builds the command of an ALTER SEQUENCE statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the command</returns>
    private Ddl.AlterSequence PrepareAlterSequence(SessionLocal session)
    {
        ExpectWord("ALTER");
        ExpectWord("SEQUENCE");
        bool ifExists = ReadIfExists(false);
        string sequenceName = ReadIdentifierWithSchema(session, out Schemas.Schema schema);
        Ddl.AlterSequence command = new(session, schema);
        command.SetSequenceName(sequenceName);
        command.SetIfExists(ifExists);
        Ddl.SequenceOptions options = new();
        ReadSequenceOptions(options, command: null, allowDataType: false, forAlterColumn: false);
        command.SetOptions(options);
        return command;
    }

    /// <summary>
    /// Builds the command of a DROP SEQUENCE statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the command</returns>
    private Ddl.DropSequence PrepareDropSequence(SessionLocal session)
    {
        ExpectWord("DROP");
        ExpectWord("SEQUENCE");
        bool ifExists = ReadIfExists(false);
        string sequenceName = ReadIdentifierWithSchema(session, out Schemas.Schema schema);
        Ddl.DropSequence command = new(session, schema);
        command.SetSequenceName(sequenceName);
        command.SetIfExists(ifExists);
        return command;
    }

    /// <summary>
    /// Builds the command of a TRUNCATE TABLE statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the command</returns>
    private Ddl.TruncateTable PrepareTruncateTable(SessionLocal session)
    {
        ExpectWord("TRUNCATE");
        ExpectWord("TABLE");
        string tableName = ReadIdentifier();
        Schemas.Schema schema = CurrentSchema(session);
        Tables.Table table = ReadTableOrView(session, schema, tableName);
        bool restart = false;
        if (IsWord("CONTINUE") && IsWord("IDENTITY"))
        {
            restart = false;
        }
        else if (IsWord("RESTART") && IsWord("IDENTITY"))
        {
            restart = true;
        }

        Ddl.TruncateTable command = new(session);
        command.SetTable(table);
        command.SetRestart(restart);
        return command;
    }

    /// <summary>
    /// Whether the token opens a SELECT statement: the word SELECT, written plain.
    /// </summary>
    private static bool IsSelectFirst(Token token)
        => !token.IsQuoted
            && ("SELECT".Equals(token.AsIdentifier, StringComparison.OrdinalIgnoreCase)
                || "WITH".Equals(token.AsIdentifier, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads a flat SELECT straight into the engine operator: one table, a column list,
    /// an optional WHERE and an optional ORDER BY.
    /// </summary>
    /// <remarks>
    /// The reference's <c>parseSelect</c> reads the same way, clause by clause, and resolves
    /// names while it reads. What is read here is what the engine runs today: no joins, no
    /// groups, no aggregates, no limits. A clause that is valid SQL but not that is refused
    /// as not ported, which is not a syntax error — the statement is fine, the reader is not
    /// done. The names stay names: init turns them into positions and the mapping pass binds
    /// them to columns.
    /// <para>
    /// The method names carry a Query infix because the short ones belong to the parse-only
    /// layer of this same class, which reads the same clauses into statement records. The
    /// logic inside mirrors the reference all the same.
    /// </para>
    /// </remarks>
    /// <returns>the prepared statement</returns>
    /// <param name="endOfStatement">whether the select must be the whole statement</param>
    private Query.Query ParseQuerySelect(bool endOfStatement = true, bool prepare = true)
    {
        SessionLocal current = session ?? throw new TODOException("Parser.parseQuerySelect: this parser has no session");
        ExpectWord("SELECT");
        Query.Select select = new(current);
        select.SetSql(sql, null);
        if (IsWord("DISTINCT"))
        {
            select.SetDistinct();
        }

        // The star of a select list, with the table it names: null when there is no star,
        // the empty string for the star of every table, and the alias for the star of one
        // table ("SELECT t.* FROM T AS t").
        string? star = null;
        if (ReadIf(Token.Asterisk))
        {
            star = string.Empty;
        }
        else if (IsNameToken()
            && position + 2 < tokens.Count
            && tokens[position + 1].TokenType == Token.Dot
            && tokens[position + 2].TokenType == Token.Asterisk)
        {
            star = ReadIdentifier();
            Read();
            Read();
        }

        List<Expressions.Expression> selectItems = [];
        if (star is null)
        {
            do
            {
                Expressions.Expression item;
                if (IsAggregateCall(out Expressions.Aggregate.AggregateType aggregateType))
                {
                    item = ReadQueryAggregate(select, aggregateType);
                }
                else
                {
                    item = ReadQueryOr(select);
                }

                // The reference reads a select item as an expression, and an expression may
                // be a comparison: "SELECT COUNT(*) > 0 FROM T" is a select item that
                // answers a boolean. The aggregate is read inside the term reader there, so
                // the comparison that follows it is read as part of the same expression.
                while (CurrentType is Token.Equal or Token.NotEqual or Token.Smaller
                    or Token.Bigger or Token.SmallerEqual or Token.BiggerEqual)
                {
                    int compareType = ReadQueryComparisonType();
                    item = new Expressions.Conditions.Comparison(
                        compareType, item, ReadQueryTerm(select), whenOperand: false);
                }

                string? alias = null;
                if (IsWord("AS"))
                {
                    alias = ReadIdentifier();
                }
                else if (IsNameToken() && !IsClauseWord(Current.AsIdentifier!))
                {
                    // The reference reads the alias of a select item the same way it reads the
                    // alias of a table: with the AS or without it, and the two forms mean
                    // the same thing.
                    alias = ReadIdentifier();
                }

                if (alias is not null)
                {
                    item = new Expressions.Alias(item, alias, aliasColumnName: true);
                }

                selectItems.Add(item);
            }
            while (ReadIf(Token.Comma));
        }

        List<Tables.TableFilter> fromFilters = [];
        if (IsWord("FROM"))
        {
            // The reference reads the FROM part as a list of tables and the joins between
            // them, and gives the query the filters of all of them.
            do
            {
                Tables.TableFilter top = ReadTableReference(current, select);
                select.AddTableFilter(top, isTop: true);
                fromFilters.Add(top);
                bool isOuter = false;
                for (; ; )
                {
                    Tables.TableFilter? nested = top.GetNestedJoin();
                    if (nested is not null)
                    {
                        foreach (Tables.TableFilter f in CollectFilters(nested))
                        {
                            select.AddTableFilter(f, isTop: false);
                            fromFilters.Add(f);
                        }
                    }

                    Tables.TableFilter? join = top.GetJoin();
                    if (join is null)
                    {
                        break;
                    }

                    isOuter |= join.IsJoinOuter();
                    if (isOuter)
                    {
                        select.AddTableFilter(join, isTop: false);
                        fromFilters.Add(join);
                    }
                    else
                    {
                        // The reference makes an inner join flat, so that the optimizer can
                        // work better: the condition of the join becomes the condition of
                        // the query, and the joined table becomes a table of its own.
                        Expressions.Expression? on = join.GetJoinCondition();
                        if (on is not null)
                        {
                            select.AddCondition(on);
                        }

                        join.RemoveJoinCondition();
                        top.RemoveJoin();
                        select.AddTableFilter(join, isTop: true);
                        fromFilters.Add(join);
                    }

                    top = join;
                }
            }
            while (ReadIf(Token.Comma));
        }
        else
        {
            // Select without FROM: the reference reads an expression select with an
            // implicit DUAL table, one empty row to evaluate the expressions on.
            Tables.TableFilter filter = new(
                current, new Tables.DualTable(current.GetDatabase()), null,
                rightsChecked: true, select, 0, null);
            select.AddTableFilter(filter, isTop: true);
            fromFilters.Add(filter);
        }

        List<Expressions.Expression> selectList = [];
        if (star is not null)
        {
            if (fromFilters.Count == 0)
            {
                throw DbException.GetSyntaxError(sql, Current.Start, "statement");
            }

            // The reference expands the star in init; there is no star expression here to
            // carry there, so the columns land directly. A star with a table in front of
            // it names the columns of that table.
            foreach (Tables.TableFilter f in fromFilters)
            {
                if (star.Length > 0
                    && !f.GetTableAlias().Equals(star, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (Tables.Column column in f.GetTable().GetColumns())
                {
                    selectList.Add(new Expressions.ExpressionColumn(column, f, 0));
                }
            }
        }
        else
        {
            selectList.AddRange(selectItems);
        }

        select.SetExpressions([.. selectList]);

        if (CurrentType == ParserUtil.Where)
        {
            Read();
            select.AddCondition(ReadQueryOr());
        }

        if (CurrentType == ParserUtil.Group)
        {
            Read();
            ExpectWord("BY");
            select.SetGroupQuery();
            List<Expressions.Expression> group = [];
            do
            {
                // A bare number groups by position only where the mode says so, and the
                // flag is not ported: in regular mode it groups by a constant instead,
                // which is a guess this reader refuses to make either way.
                if (Current.GetValue(provider) is Values.ValueInteger)
                {
                    throw new TODOException(
                        "Parser.parseQuerySelect: GROUP BY position needs Mode.groupByColumnIndex");
                }

                group.Add(ReadQueryOr());
            }
            while (ReadIf(Token.Comma));

            select.SetGroupBy(group);
        }

        if (CurrentType == ParserUtil.Having)
        {
            Read();
            select.SetGroupQuery();
            select.SetHaving(ReadQueryOr(select));
        }

        if (CurrentType == ParserUtil.Order)
        {
            Read();
            ExpectWord("BY");
            List<Query.QueryOrderBy> order = [];
            do
            {
                Query.QueryOrderBy item = new();
                if (Current.GetValue(provider) is Values.ValueInteger position)
                {
                    Read();
                    item.columnIndexExpr = Expressions.ValueExpression.Get(position);
                }
                else
                {
                    string orderColumn = ReadQueryColumnName(out string? orderTable);
                    item.expression = new Expressions.ExpressionColumn(orderColumn, orderTable, 0);
                }

                item.sortType = ReadQuerySortType();
                order.Add(item);
            }
            while (ReadIf(Token.Comma));

            select.SetOrder(order);
        }

        // Standard SQL OFFSET / FETCH, then MySQL-style LIMIT, in the reference's order and
        // only when no fetch was set: vacuous for a flat select read once (no union branch
        // could have set one), but the shape is kept so the union port finds it in place.
        // The counts are expressions, as in the reference: a limit may name a parameter, and
        // the value of a parameter is not known until the statement runs.
        bool hasOffsetOrFetch = false;
        if (CurrentType == ParserUtil.Offset)
        {
            Read();
            hasOffsetOrFetch = true;
            select.SetOffset(ReadQueryLimitExpression());
            _ = IsWord("ROW") || IsWord("ROWS");
        }

        if (CurrentType == ParserUtil.Fetch)
        {
            Read();
            hasOffsetOrFetch = true;
            if (!IsWord("FIRST"))
            {
                ExpectWord("NEXT");
            }

            if (IsWord("ROW") || IsWord("ROWS"))
            {
                select.SetFetch(Expressions.ValueExpression.Get(Values.ValueInteger.Get(1)));
            }
            else
            {
                select.SetFetch(ReadQueryLimitExpression());
                if (IsWord("PERCENT"))
                {
                    select.SetFetchPercent(true);
                }

                if (!IsWord("ROW"))
                {
                    ExpectWord("ROWS");
                }
            }

            if (IsWord("WITH"))
            {
                ExpectWord("TIES");
                throw new TODOException("Parser.parseQuerySelect: FETCH WITH TIES is not ported");
            }

            ExpectWord("ONLY");
        }

        // The mode comes from the cast provider rather than the session: the tokenizer
        // gates the LIMIT keyword on the same flag, and SessionLocal.GetMode refuses until
        // Mode is ported.
        if (!hasOffsetOrFetch && provider.GetMode().Limit && IsWord("LIMIT"))
        {
            Expressions.Expression limit = ReadQueryLimitExpression();
            if (CurrentType == ParserUtil.Offset)
            {
                Read();
                select.SetOffset(ReadQueryLimitExpression());
            }
            else if (ReadIf(Token.Comma))
            {
                // MySQL: [offset, ] rowcount.
                select.SetOffset(limit);
                limit = ReadQueryLimitExpression();
            }

            select.SetFetch(limit);
        }

        bool union = IsWord("UNION", consume: false);
        if (!union && endOfStatement && CurrentType != Token.EndOfInput && CurrentType != Token.Semicolon)
        {
            string word = Current.AsIdentifier ?? Current.ToString() ?? "statement";
            if (IsClauseWord(word))
            {
                throw new TODOException("Parser.parseQuerySelect: " + word + " is not ported");
            }

            throw DbException.GetSyntaxError(sql, Current.Start, "statement");
        }

        select.Init();
        // Predicate subqueries are initialized now and prepared after outer column mapping,
        // as in H2's init -> mapColumns -> prepare lifecycle.
        if (prepare)
        {
            select.Prepare();
        }
        if (!union)
        {
            return select;
        }

        Query.Query left = select;
        while (IsWord("UNION"))
        {
            // DISTINCT is the default; ALL keeps every row. Each side after the first
            // is another full select, so a chain nests to the right.
            bool distinct = !IsWord("ALL");
            if (distinct)
            {
                _ = IsWord("DISTINCT");
            }

            Query.Query right = ParseQuerySelect(endOfStatement: endOfStatement, prepare: prepare);
            left = new Query.SelectUnion(left, right, distinct);
        }

        return left;
    }

    /// <summary>
    /// Reads a LIMIT, OFFSET or FETCH count: an expression, as the reference reads it.
    /// </summary>
    /// <remarks>
    /// The reference reads any expression here and evaluates it per run; the engine holds
    /// only a long, so anything but a literal refuses by name rather than binding wrong.
    /// A negative passes through and runs as no limit, which is what the engine already
    /// does with a negative fetch.
    /// </remarks>
    /// <returns>the count</returns>
    private Expressions.Expression ReadQueryLimitExpression()
    {
        return ReadQueryOr().Optimize(session ?? throw new TODOException(
            "Parser.readQueryLimit: this parser has no session"));
    }

    /// <summary>
    /// Reads a LIMIT, OFFSET or FETCH count: an integer literal, optionally negative.
    /// </summary>
    /// <remarks>
    /// The reference reads any expression here and evaluates it per run; the engine holds
    /// only a long, so anything but a literal refuses by name rather than binding wrong.
    /// A negative passes through and runs as no limit, which is what the engine already
    /// does with a negative fetch.
    /// </remarks>
    /// <returns>the count</returns>
    private long ReadQueryLimitValue()
    {
        bool negative = ReadIf(Token.MinusSign);
        if (Current.GetValue(provider) is Values.ValueInteger small)
        {
            Read();
            return negative ? -small.GetInt() : small.GetInt();
        }

        if (Current.GetValue(provider) is Values.ValueBigint big)
        {
            Read();
            long value = big.GetLong();
            return negative ? -value : value;
        }

        throw new TODOException("Parser.parseQuerySelect: LIMIT/OFFSET takes an integer literal here");
    }

    /// <summary>
    /// Whether the current token can be a name: a plain identifier or a keyword, which the
    /// reference also allows as a name. Punctuation is never one, which is what keeps a comma
    /// or a parenthesis from being read as an alias.
    /// </summary>
    private bool IsNameToken()
        => CurrentType == ParserUtil.Identifier
            || (CurrentType >= ParserUtil.FirstKeyword && CurrentType <= ParserUtil.LastKeyword);

    /// <summary>
    /// Whether the word opens a clause this reader does not read: such a word is never a table
    /// alias, a column alias, or the end of anything.
    /// </summary>
    private static bool IsClauseWord(string word)
        => word.Equals("FROM", StringComparison.OrdinalIgnoreCase)
            || word.Equals("WHERE", StringComparison.OrdinalIgnoreCase)
            || word.Equals("ORDER", StringComparison.OrdinalIgnoreCase)
            || word.Equals("GROUP", StringComparison.OrdinalIgnoreCase)
            || word.Equals("HAVING", StringComparison.OrdinalIgnoreCase)
            || word.Equals("WINDOW", StringComparison.OrdinalIgnoreCase)
            || word.Equals("LIMIT", StringComparison.OrdinalIgnoreCase)
            || word.Equals("OFFSET", StringComparison.OrdinalIgnoreCase)
            || word.Equals("FETCH", StringComparison.OrdinalIgnoreCase)
            || word.Equals("UNION", StringComparison.OrdinalIgnoreCase)
            || word.Equals("EXCEPT", StringComparison.OrdinalIgnoreCase)
            || word.Equals("INTERSECT", StringComparison.OrdinalIgnoreCase)
            || word.Equals("MINUS", StringComparison.OrdinalIgnoreCase)
            || word.Equals("FOR", StringComparison.OrdinalIgnoreCase)
            || word.Equals("SET", StringComparison.OrdinalIgnoreCase)
            || word.Equals("JOIN", StringComparison.OrdinalIgnoreCase)
            || word.Equals("LEFT", StringComparison.OrdinalIgnoreCase)
            || word.Equals("RIGHT", StringComparison.OrdinalIgnoreCase)
            || word.Equals("INNER", StringComparison.OrdinalIgnoreCase)
            || word.Equals("CROSS", StringComparison.OrdinalIgnoreCase)
            || word.Equals("NATURAL", StringComparison.OrdinalIgnoreCase)
            || word.Equals("ON", StringComparison.OrdinalIgnoreCase)
            || word.Equals("USING", StringComparison.OrdinalIgnoreCase);
    /// <summary>
    /// Reads a column name with an optional table prefix.
    /// </summary>
    /// <param name="tableAlias">the table prefix, or null</param>
    /// <returns>the column name</returns>
    private string ReadQueryColumnName(out string? tableAlias)
    {
        string first = ReadIdentifier();
        tableAlias = null;
        if (ReadIf(Token.Dot))
        {
            tableAlias = first;
            return ReadIdentifier();
        }

        return first;
    }

    /// <summary>
    /// Whether the current token names an aggregate this reader builds, without consuming it.
    /// A name followed by anything else is a column that happens to share the spelling.
    /// </summary>
    /// <param name="aggregateType">the aggregate type, when this is a call</param>
    /// <returns>true for a COUNT, SUM, MIN, MAX, AVG, STDDEV, VAR, ANY, EVERY, MEDIAN, ANY_VALUE, PERCENTILE_CONT, PERCENTILE_DISC, LISTAGG or two-argument regression call</returns>
    private bool IsAggregateCall(out Expressions.Aggregate.AggregateType aggregateType)
    {
        aggregateType = default;
        string? name = Current.AsIdentifier;
        if (name is null || tokens.Count <= position + 1
            || tokens[position + 1].TokenType != Token.OpenParen)
        {
            return false;
        }

        if (name.Equals("COUNT", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.Count;
        }
        else if (name.Equals("SUM", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.Sum;
        }
        else if (name.Equals("MIN", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.Min;
        }
        else if (name.Equals("MAX", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.Max;
        }
        else if (name.Equals("AVG", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.Avg;
        }
        else if (name.Equals("STDDEV_POP", StringComparison.OrdinalIgnoreCase)
            || name.Equals("STDDEVP", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.StddevPop;
        }
        else if (name.Equals("STDDEV_SAMP", StringComparison.OrdinalIgnoreCase)
            || name.Equals("STDDEV", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.StddevSamp;
        }
        else if (name.Equals("VAR_POP", StringComparison.OrdinalIgnoreCase)
            || name.Equals("VARP", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.VarPop;
        }
        else if (name.Equals("VAR_SAMP", StringComparison.OrdinalIgnoreCase)
            || name.Equals("VAR", StringComparison.OrdinalIgnoreCase)
            || name.Equals("VARIANCE", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.VarSamp;
        }
        else if (name.Equals("ANY", StringComparison.OrdinalIgnoreCase)
            || name.Equals("SOME", StringComparison.OrdinalIgnoreCase)
            || name.Equals("BOOL_OR", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.Any;
        }
        else if (name.Equals("EVERY", StringComparison.OrdinalIgnoreCase)
            || name.Equals("BOOL_AND", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.Every;
        }
        else if (name.Equals("MEDIAN", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.Median;
        }
        else if (name.Equals("ANY_VALUE", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.AnyValue;
        }
        else if (name.Equals("PERCENTILE_CONT", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.PercentileCont;
        }
        else if (name.Equals("PERCENTILE_DISC", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.PercentileDisc;
        }
        else if (name.Equals("LISTAGG", StringComparison.OrdinalIgnoreCase)
            || name.Equals("GROUP_CONCAT", StringComparison.OrdinalIgnoreCase)
            || name.Equals("STRING_AGG", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.Listagg;
        }
        else if (name.Equals("ARRAY_AGG", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.ArrayAgg;
        }
        else if (name.Equals("HISTOGRAM", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.Histogram;
        }
        else if (name.Equals("MODE", StringComparison.OrdinalIgnoreCase)
            || name.Equals("STATS_MODE", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.Mode;
        }
        else if (name.Equals("JSON_ARRAYAGG", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.JsonArrayagg;
        }
        else if (name.Equals("JSON_OBJECTAGG", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.JsonObjectagg;
        }
        else if (name.Equals("BIT_AND_AGG", StringComparison.OrdinalIgnoreCase)
            || name.Equals("BIT_AND", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.BitAndAgg;
        }
        else if (name.Equals("BIT_OR_AGG", StringComparison.OrdinalIgnoreCase)
            || name.Equals("BIT_OR", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.BitOrAgg;
        }
        else if (name.Equals("BIT_XOR_AGG", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.BitXorAgg;
        }
        else if (name.Equals("BIT_NAND_AGG", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.BitNandAgg;
        }
        else if (name.Equals("BIT_NOR_AGG", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.BitNorAgg;
        }
        else if (name.Equals("BIT_XNOR_AGG", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.BitXnorAgg;
        }
        else if (name.Equals("REGR_COUNT", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.RegrCount;
        }
        else if (name.Equals("REGR_AVGX", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.RegrAvgx;
        }
        else if (name.Equals("REGR_AVGY", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.RegrAvgy;
        }
        else if (name.Equals("REGR_SXX", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.RegrSxx;
        }
        else if (name.Equals("REGR_SYY", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.RegrSyy;
        }
        else if (name.Equals("COVAR_POP", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.CovarPop;
        }
        else if (name.Equals("COVAR_SAMP", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.CovarSamp;
        }
        else if (name.Equals("CORR", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.Corr;
        }
        else if (name.Equals("REGR_SLOPE", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.RegrSlope;
        }
        else if (name.Equals("REGR_INTERCEPT", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.RegrIntercept;
        }
        else if (name.Equals("REGR_R2", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.RegrR2;
        }
        else if (name.Equals("REGR_SXY", StringComparison.OrdinalIgnoreCase))
        {
            aggregateType = Expressions.Aggregate.AggregateType.RegrSxy;
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads a COUNT, SUM, MIN, MAX, AVG, STDDEV, VAR, ANY, EVERY, MEDIAN, ANY_VALUE, PERCENTILE_CONT, PERCENTILE_DISC, LISTAGG or two-argument regression call: the aggregates the engine runs.
    /// </summary>
    /// <remarks>
    /// The reference reads every aggregate name here and builds the matching type; only
    /// the ported ones are built, anything else never reaches this branch. DISTINCT and ALL
    /// spellings follow the reference; FILTER and OVER wait for their machinery, and the
    /// select is marked a group query the way readOver does.
    /// </remarks>
    /// <param name="select">the select being read</param>
    /// <param name="aggregateType">the aggregate type</param>
    /// <returns>the aggregate</returns>
    private Expressions.Aggregate.Aggregate ReadQueryAggregate(
        Query.Select select, Expressions.Aggregate.AggregateType aggregateType)
    {
        string functionName = Current.AsIdentifier!;
        Read();
        Expect(Token.OpenParen, "(");
        Expressions.Aggregate.Aggregate aggregate;
        if (aggregateType == Expressions.Aggregate.AggregateType.Count && ReadIf(Token.Asterisk))
        {
            Expect(Token.CloseParen, ")");
            aggregate = new Expressions.Aggregate.Aggregate(
                Expressions.Aggregate.AggregateType.CountAll, [], select, distinct: false);
        }
        else if (aggregateType is Expressions.Aggregate.AggregateType.PercentileCont
            or Expressions.Aggregate.AggregateType.PercentileDisc)
        {
            // readWithinGroup in its simple form: the fraction, then
            // WITHIN GROUP (ORDER BY expression sortType). DISTINCT never
            // applies here, the reference hardcodes it to false.
            Expressions.Expression fraction = ReadQueryOr();
            Expect(Token.CloseParen, ")");
            ExpectWord("WITHIN");
            ExpectWord("GROUP");
            Expect(Token.OpenParen, "(");
            ExpectWord("ORDER");
            ExpectWord("BY");
            Query.QueryOrderBy order = new()
            {
                expression = ReadQueryOr(),
                sortType = ReadQuerySortType(),
            };
            Expect(Token.CloseParen, ")");
            aggregate = new Expressions.Aggregate.Aggregate(
                aggregateType, [fraction], select, distinct: false);
            aggregate.SetOrderByList([order]);
        }
        else if (aggregateType == Expressions.Aggregate.AggregateType.Listagg)
        {
            aggregate = ReadQueryListagg(select, functionName);
        }
        else if (aggregateType == Expressions.Aggregate.AggregateType.ArrayAgg)
        {
            // ARRAY_AGG(expression [ORDER BY ...]): the argument is read the same way as
            // any single-argument aggregate, but the ORDER BY list inside the
            // parentheses has no reader yet — the reference reads it here via
            // readIfOrderBy, and without it the list cannot be kept.
            bool distinct = IsWord("DISTINCT");
            if (!distinct)
            {
                _ = IsWord("ALL");
            }

            Expressions.Expression arg = ReadQueryOr();
            if (IsWord("ORDER", consume: false))
            {
                throw new TODOException("Parser.parseQuerySelect: ARRAY_AGG ORDER BY is not ported");
            }

            Expect(Token.CloseParen, ")");
            aggregate = new Expressions.Aggregate.Aggregate(
                aggregateType, [arg], select, distinct);
        }
        else if (aggregateType == Expressions.Aggregate.AggregateType.Mode)
        {
            // MODE takes no argument on the expression list: the reference builds the aggregate
            // with an empty args array and carries the value on the ORDER BY list. DISTINCT
            // never applies here, the reference hardcodes it to false. With ORDER BY the value
            // repeats inside the clause, as readAggregateOrder wants; the sort type is parsed
            // after it, before the closing parenthesis.
            Expressions.Expression arg = ReadQueryOr();
            if (IsWord("ORDER", consume: false))
            {
                ExpectWord("ORDER");
                ExpectWord("BY");
                _ = ReadQueryOr();
            }

            int orderSortType = ReadQuerySortType();
            Expect(Token.CloseParen, ")");
            aggregate = new Expressions.Aggregate.Aggregate(
                aggregateType, [], select, distinct: false);
            aggregate.SetOrderByList(
            [
                new Query.QueryOrderBy
                {
                    expression = arg,
                    sortType = orderSortType,
                },
            ]);
        }
        else if (aggregateType == Expressions.Aggregate.AggregateType.JsonArrayagg)
        {
            // JSON_ARRAYAGG([DISTINCT] expression [NULL|ABSENT ON NULL] [ORDER BY ...]):
            // the reference reads an ORDER BY list inside the parentheses, which this reader
            // does not build yet, so that clause refuses by name. The ABSENT ON NULL flag is
            // the default for an array aggregate.
            bool distinct = IsWord("DISTINCT");
            if (!distinct)
            {
                _ = IsWord("ALL");
            }

            Expressions.Expression arg = ReadQueryOr();
            if (IsWord("ORDER", consume: false))
            {
                throw new TODOException("Parser.parseQuerySelect: JSON_ARRAYAGG ORDER BY is not ported");
            }

            aggregate = new Expressions.Aggregate.Aggregate(
                aggregateType, [arg], select, distinct);
            aggregate.SetFlags(Util.Json.JsonConstructorUtils.JsonAbsentOnNull);
            ReadJsonFunctionFlags(aggregate, forArray: true);
            Expect(Token.CloseParen, ")");
        }
        else if (aggregateType == Expressions.Aggregate.AggregateType.JsonObjectagg)
        {
            // JSON_OBJECTAGG([KEY] key VALUE value | key : value) [flags]: without the
            // KEY/VALUE keywords the default separator is ':'. The value's NULL is kept
            // unless ABSENT ON NULL is asked for; the key's NULL always refuses.
            bool withKey = IsWord("KEY");
            Expressions.Expression key = ReadQueryOr();
            if (withKey)
            {
                ExpectWord("VALUE");
            }
            else if (!IsWord("VALUE"))
            {
                Expect(Token.Colon, ":");
            }

            Expressions.Expression value = ReadQueryOr();
            aggregate = new Expressions.Aggregate.Aggregate(
                aggregateType, [key, value], select, distinct: false);
            ReadJsonFunctionFlags(aggregate, forArray: false);
            Expect(Token.CloseParen, ")");
        }
        else if (aggregateType is Expressions.Aggregate.AggregateType.RegrCount
            or Expressions.Aggregate.AggregateType.RegrAvgx
            or Expressions.Aggregate.AggregateType.RegrAvgy
            or Expressions.Aggregate.AggregateType.RegrSxx
            or Expressions.Aggregate.AggregateType.RegrSyy
            or Expressions.Aggregate.AggregateType.CovarPop
            or Expressions.Aggregate.AggregateType.CovarSamp
            or Expressions.Aggregate.AggregateType.Corr
            or Expressions.Aggregate.AggregateType.RegrSlope
            or Expressions.Aggregate.AggregateType.RegrIntercept
            or Expressions.Aggregate.AggregateType.RegrR2
            or Expressions.Aggregate.AggregateType.RegrSxy)
        {
            // The two-argument regressions: the values first, then a comma, then the
            // second one. DISTINCT never applies here, the reference hardcodes it to false.
            Expressions.Expression first = ReadQueryOr();
            Expect(Token.Comma, ",");
            Expressions.Expression second = ReadQueryOr();
            Expect(Token.CloseParen, ")");
            aggregate = new Expressions.Aggregate.Aggregate(
                aggregateType, [first, second], select, distinct: false);
        }
        else
        {
            bool distinct = IsWord("DISTINCT");
            if (!distinct)
            {
                _ = IsWord("ALL");
            }

            Expressions.Expression arg = ReadQueryOr();
            Expect(Token.CloseParen, ")");
            aggregate = new Expressions.Aggregate.Aggregate(
                aggregateType, [arg], select, distinct);
        }

        if (IsWord("FILTER", consume: false) || IsWord("OVER", consume: false))
        {
            throw new TODOException("Parser.parseQuerySelect: FILTER and OVER are not ported");
        }

        select.SetGroupQuery();
        return aggregate;
    }

    /// <summary>
    /// Reads the NULL/ABSENT ON NULL and WITH/WITHOUT UNIQUE KEYS clauses of a JSON aggregate.
    /// </summary>
    /// <param name="function">the aggregate being read</param>
    /// <param name="forArray">whether the aggregate is an array aggregate</param>
    private void ReadJsonFunctionFlags(Expressions.Aggregate.Aggregate function, bool forArray)
    {
        bool result = false;
        int flags = function.GetFlags();
        if (ReadIf(ParserUtil.Null))
        {
            ExpectWord("ON");
            Expect(ParserUtil.Null, "NULL");
            flags &= ~Util.Json.JsonConstructorUtils.JsonAbsentOnNull;
            result = true;
        }
        else if (IsWord("ABSENT", consume: false))
        {
            ExpectWord("ABSENT");
            ExpectWord("ON");
            Expect(ParserUtil.Null, "NULL");
            flags |= Util.Json.JsonConstructorUtils.JsonAbsentOnNull;
            result = true;
        }

        if (!forArray)
        {
            if (ReadIfWords("WITH", "UNIQUE", "KEYS"))
            {
                flags |= Util.Json.JsonConstructorUtils.JsonWithUniqueKeys;
                result = true;
            }
            else if (ReadIfWords("WITHOUT", "UNIQUE", "KEYS"))
            {
                flags &= ~Util.Json.JsonConstructorUtils.JsonWithUniqueKeys;
                result = true;
            }
        }

        if (result)
        {
            function.SetFlags(flags);
        }
    }

    /// <summary>
    /// Reads a run of keyword words in order, consuming them only when the whole run matches.
    /// </summary>
    /// <param name="words">the words, in order</param>
    /// <returns>whether the run matched</returns>
    private bool ReadIfWords(params string[] words)
    {
        for (int i = 0; i < words.Length; i++)
        {
            if (!IsWord(words[i], consume: false))
            {
                return false;
            }
        }

        for (int i = 0; i < words.Length; i++)
        {
            ExpectWord(words[i]);
        }

        return true;
    }

    /// <summary>
    /// Reads a LISTAGG separator: a string literal or a parameter.
    /// </summary>
    /// <remarks>
    /// Parameters are not read on this layer, so in practice only constants pass;
    /// anything else is a syntax error here, as in the reference.
    /// </remarks>
    /// <returns>the separator expression</returns>
    private Expressions.Expression ReadListaggSeparator()
    {
        Expressions.Expression separator = ReadQueryOr();
        if (!separator.IsConstant())
        {
            throw DbException.GetSyntaxError(sql, Current.Start, "separator");
        }

        return separator;
    }

    /// <summary>
    /// Reads a LISTAGG call with its MySQL and PostgreSQL spellings.
    /// </summary>
    /// <remarks>
    /// An optional DISTINCT or ALL, the value, then an optional separator spelled
    /// per dialect. ON OVERFLOW, ORDER BY inside the parentheses and WITHIN GROUP
    /// wait for their machinery.
    /// </remarks>
    /// <param name="select">the select being read</param>
    /// <param name="functionName">the spelling used: LISTAGG, GROUP_CONCAT or STRING_AGG</param>
    /// <returns>the aggregate</returns>
    private Expressions.Aggregate.Aggregate ReadQueryListagg(Query.Select select, string functionName)
    {
        bool distinct = false;
        Expressions.Expression arg;
        Expressions.Aggregate.ListaggArguments extraArguments = new();
        if (functionName.Equals("STRING_AGG", StringComparison.OrdinalIgnoreCase))
        {
            // PostgreSQL compatibility: string_agg(expression, delimiter)
            arg = ReadQueryOr();
            Expect(Token.Comma, ",");
            extraArguments.SetSeparator(ReadListaggSeparator());
            if (CurrentType == ParserUtil.Order)
            {
                throw new TODOException("Parser.parseQuerySelect: ORDER BY in STRING_AGG is not ported");
            }
        }
        else
        {
            distinct = IsWord("DISTINCT");
            if (!distinct)
            {
                _ = IsWord("ALL");
            }

            arg = ReadQueryOr();
            if (functionName.Equals("GROUP_CONCAT", StringComparison.OrdinalIgnoreCase))
            {
                if (CurrentType == ParserUtil.Order)
                {
                    throw new TODOException("Parser.parseQuerySelect: ORDER BY in GROUP_CONCAT is not ported");
                }

                if (IsWord("SEPARATOR"))
                {
                    extraArguments.SetSeparator(ReadListaggSeparator());
                }
            }
            else
            {
                if (ReadIf(Token.Comma))
                {
                    extraArguments.SetSeparator(ReadListaggSeparator());
                }

                if (IsWord("ON", consume: false))
                {
                    throw new TODOException("Parser.parseQuerySelect: LISTAGG ON OVERFLOW is not ported");
                }
            }
        }

        Expect(Token.CloseParen, ")");
        if (IsWord("WITHIN", consume: false))
        {
            throw new TODOException("Parser.parseQuerySelect: WITHIN GROUP in LISTAGG is not ported");
        }

        Expressions.Aggregate.Aggregate aggregate = new(
            Expressions.Aggregate.AggregateType.Listagg, [arg], select, distinct);
        aggregate.SetExtraArguments(extraArguments);
        return aggregate;
    }

    /// <summary>
    /// Reads a CAST call: the value, the type it becomes and, in the reference, the format
    /// template it is written with.
    /// </summary>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    /// <returns>the cast</returns>
    private Expressions.Expression ReadCastExpression(Query.Select? aggregateSelect)
    {
        Read();
        Expect(Token.OpenParen, "(");
        Expressions.Expression arg = ReadQueryOr(aggregateSelect);
        Expect(ParserUtil.As, "AS");
        TypeInfo type = ReadColumnWithType();
        Expressions.Expression? template = null;
        if (IsWord("FORMAT", consume: false))
        {
            Read();
            template = ReadQueryOr(aggregateSelect);
        }

        Expect(Token.CloseParen, ")");
        return new Expressions.Functions.CastSpecification(arg, type, template);
    }

    /// <summary>
    /// Reads the direction and null ordering of one ORDER BY item.
    /// </summary>
    /// <returns>the sort type mask</returns>
    private int ReadQuerySortType()
    {
        int sortType = Result.SortOrder.Ascending;
        if (IsWord("DESC"))
        {
            sortType = Result.SortOrder.Descending;
        }
        else if (IsWord("ASC"))
        {
        }

        if (IsWord("NULLS"))
        {
            if (IsWord("FIRST"))
            {
                sortType |= Result.SortOrder.NullsFirst;
            }
            else
            {
                ExpectWord("LAST");
                sortType |= Result.SortOrder.NullsLast;
            }
        }

        return sortType;
    }

    /// <summary>
    /// Reads an OR of ANDs, as the reference's precedence does.
    /// </summary>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    private Expressions.Expression ReadQueryOr(Query.Select? aggregateSelect = null)
    {
        Expressions.Expression left = ReadQueryAnd(aggregateSelect);
        while (CurrentType == ParserUtil.Or)
        {
            Read();
            left = new Expressions.Conditions.ConditionAndOr(
                Expressions.Conditions.ConditionAndOr.Or, left, ReadQueryAnd(aggregateSelect));
        }

        return left;
    }

    /// <summary>
    /// Reads an AND of comparisons.
    /// </summary>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    private Expressions.Expression ReadQueryAnd(Query.Select? aggregateSelect = null)
    {
        Expressions.Expression left = ReadQueryComparison(aggregateSelect);
        while (CurrentType == ParserUtil.And)
        {
            Read();
            left = new Expressions.Conditions.ConditionAndOr(
                Expressions.Conditions.ConditionAndOr.And, left, ReadQueryComparison(aggregateSelect));
        }

        return left;
    }

    /// <summary>
    /// Reads a sum of products, as the reference's readSum does.
    /// </summary>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    private Expressions.Expression ReadQuerySum(Query.Select? aggregateSelect = null)
    {
        Expressions.Expression r = ReadQueryFactor(aggregateSelect);
        while (true)
        {
            if (CurrentType == Token.PlusSign)
            {
                Read();
                r = new Expressions.BinaryOperation(
                    Expressions.BinaryOperation.OpType.PLUS, r, ReadQueryFactor(aggregateSelect));
            }
            else if (CurrentType == Token.MinusSign)
            {
                Read();
                r = new Expressions.BinaryOperation(
                    Expressions.BinaryOperation.OpType.MINUS, r, ReadQueryFactor(aggregateSelect));
            }
            else
            {
                return r;
            }
        }
    }

    /// <summary>
    /// Reads a product of terms, as the reference's readFactor does.
    /// </summary>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    private Expressions.Expression ReadQueryFactor(Query.Select? aggregateSelect = null)
    {
        Expressions.Expression r = ReadQueryTerm(aggregateSelect);
        while (true)
        {
            if (CurrentType == Token.Asterisk)
            {
                Read();
                r = new Expressions.BinaryOperation(
                    Expressions.BinaryOperation.OpType.MULTIPLY, r, ReadQueryTerm(aggregateSelect));
            }
            else if (CurrentType == Token.Slash)
            {
                Read();
                r = new Expressions.BinaryOperation(
                    Expressions.BinaryOperation.OpType.DIVIDE, r, ReadQueryTerm(aggregateSelect));
            }
            else if (CurrentType == Token.Percent)
            {
                Read();
                r = new Expressions.Functions.MathFunction(
                    r, ReadQueryTerm(aggregateSelect), Expressions.Functions.MathFunction.Mod);
            }
            else
            {
                return r;
            }
        }
    }

    /// <summary>
    /// Reads a comparison with an optional IS NULL tail.
    /// </summary>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    private Expressions.Expression ReadQueryComparison(Query.Select? aggregateSelect = null)
    {
        // NOT belongs to the condition level, as in the reference: "NOT x = y" negates the
        // whole comparison, not just the operand before it.
        if (CurrentType == ParserUtil.Not)
        {
            Read();
            return new Expressions.Conditions.ConditionNot(ReadQueryComparison(aggregateSelect));
        }

        Expressions.Expression left = ReadQuerySum(aggregateSelect);
        while (true)
        {
            if (CurrentType is Token.Equal or Token.NotEqual or Token.Smaller or Token.Bigger
                or Token.SmallerEqual or Token.BiggerEqual)
            {
                int compareType = ReadQueryComparisonType();
                left = new Expressions.Conditions.Comparison(
                    compareType, left, ReadQuerySum(aggregateSelect), whenOperand: false);
                continue;
            }

            if (CurrentType == ParserUtil.Is)
            {
                Read();
                bool not = ReadIf(ParserUtil.Not);
                Expect(ParserUtil.Null, "NULL");
                left = new Expressions.Conditions.ConditionIsNull(left, not, whenOperand: false);
                continue;
            }

            if (CurrentType == ParserUtil.Between
                || CurrentType == ParserUtil.Not && tokens[position + 1].TokenType == ParserUtil.Between)
            {
                bool not = ReadIf(ParserUtil.Not);
                Expect(ParserUtil.Between, "BETWEEN");
                bool symmetric = IsWord("SYMMETRIC");
                if (!symmetric)
                {
                    _ = IsWord("ASYMMETRIC");
                }

                Expressions.Expression a = ReadQuerySum(aggregateSelect);
                ExpectWord("AND");
                left = new Expressions.Conditions.BetweenPredicate(
                    left, not, whenOperand: false, symmetric, a, ReadQuerySum(aggregateSelect));
                continue;
            }

            if (CurrentType == ParserUtil.In
                || CurrentType == ParserUtil.Not && tokens[position + 1].TokenType == ParserUtil.In)
            {
                bool not = ReadIf(ParserUtil.Not);
                Expect(ParserUtil.In, "IN");
                Expect(Token.OpenParen, "(");
                if (IsDerivedQuery())
                {
                    Query.Query query = ParseQueryExpression(prepare: false);
                    Expect(Token.CloseParen, ")");
                    left = new Expressions.Conditions.ConditionInQuery(
                        left, not, whenOperand: false, query, all: false,
                        Expressions.Conditions.Comparison.Equal);
                    continue;
                }

                List<Expressions.Expression> values = [];
                do
                {
                    values.Add(ReadQuerySum(aggregateSelect));
                }
                while (ReadIf(Token.Comma));

                Expect(Token.CloseParen, ")");
                left = new Expressions.Conditions.ConditionInList(
                    left, not, whenOperand: false, values);
                continue;
            }

            if (CurrentType == ParserUtil.Like
                || IsWord("ILIKE", consume: false)
                || IsWord("REGEXP", consume: false)
                || CurrentType == ParserUtil.Not && IsLikeFollowing())
            {
                bool not = ReadIf(ParserUtil.Not);
                if (ReadIf(ParserUtil.Like))
                {
                    left = ReadLikePredicate(
                        left, Expressions.Conditions.CompareLike.LikeType.Like, not);
                }
                else if (IsWord("ILIKE", consume: false))
                {
                    ExpectWord("ILIKE");
                    left = ReadLikePredicate(
                        left, Expressions.Conditions.CompareLike.LikeType.ILike, not);
                }
                else
                {
                    ExpectWord("REGEXP");
                    Expressions.Expression b = ReadQuerySum(aggregateSelect);
                    left = new Expressions.Conditions.CompareLike(
                        session!.GetCompareMode(), left, not, whenOperand: false, b, escape: null,
                        Expressions.Conditions.CompareLike.LikeType.Regexp);
                }

                continue;
            }

            break;
        }

        return left;
    }

    /// <summary>
    /// Reads the comparison operator.
    /// </summary>
    private int ReadQueryComparisonType()
    {
        int type = CurrentType;
        Read();
        return type switch
        {
            Token.Equal => Expressions.Conditions.Comparison.Equal,
            Token.NotEqual => Expressions.Conditions.Comparison.NotEqual,
            Token.Smaller => Expressions.Conditions.Comparison.Smaller,
            Token.Bigger => Expressions.Conditions.Comparison.Bigger,
            Token.SmallerEqual => Expressions.Conditions.Comparison.SmallerEqual,
            Token.BiggerEqual => Expressions.Conditions.Comparison.BiggerEqual,
            _ => throw DbException.GetInternalError("comparison type " + type),
        };
    }

    /// <summary>
    /// Reads one operand: a parenthesized expression, a NOT, a literal, a parameter, or a
    /// column in name form.
    /// </summary>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    private Expressions.Expression ReadQueryTerm(Query.Select? aggregateSelect = null)
    {
        if (aggregateSelect is not null && IsAggregateCall(out Expressions.Aggregate.AggregateType havingAggregate))
        {
            return ReadQueryAggregate(aggregateSelect, havingAggregate);
        }

        if (ReadIf(Token.OpenParen))
        {
            Expressions.Expression nested = ReadQueryOr(aggregateSelect);
            Expect(Token.CloseParen, ")");
            return nested;
        }

        if (IsWord("CASE", consume: false))
        {
            return ReadQueryCase(aggregateSelect);
        }

        if (CurrentType == ParserUtil.Null)
        {
            Read();
            return Expressions.ValueExpression.Null;
        }

        if (CurrentType is ParserUtil.True or ParserUtil.False)
        {
            bool value = CurrentType == ParserUtil.True;
            Read();
            return value ? Expressions.ValueExpression.True : Expressions.ValueExpression.False;
        }

        if (CurrentType is ParserUtil.CurrentDate or ParserUtil.CurrentTime or ParserUtil.Localtime
            or ParserUtil.CurrentTimestamp or ParserUtil.Localtimestamp)
        {
            int function = CurrentType switch
            {
                ParserUtil.CurrentDate => Expressions.Functions.CurrentDateTimeValueFunction.CurrentDate,
                ParserUtil.CurrentTime => Expressions.Functions.CurrentDateTimeValueFunction.CurrentTime,
                ParserUtil.Localtime => Expressions.Functions.CurrentDateTimeValueFunction.Localtime,
                ParserUtil.CurrentTimestamp => Expressions.Functions.CurrentDateTimeValueFunction.CurrentTimestamp,
                _ => Expressions.Functions.CurrentDateTimeValueFunction.Localtimestamp,
            };
            Read();
            return ReadCurrentDateTimeValueFunction(function, ReadIf(Token.OpenParen));
        }

        if (CurrentType == Token.MinusSign)
        {
            Read();
            Values.Value? minusLiteral = Current.GetValue(provider);
            if (minusLiteral is not null)
            {
                // A negative literal is a literal: there is nothing in "-1" that needs a run.
                Values.Value negated = minusLiteral.Negate();
                int negatedType = negated.GetValueType();
                if (negatedType == Values.Value.Bigint && negated.GetLong() == int.MinValue)
                {
                    // The reference converts Integer.MIN_VALUE to type 'int'.
                    negated = Values.ValueInteger.Get(int.MinValue);
                }
                else if (negatedType == Values.Value.Numeric
                    && negated.GetBigDecimal().CompareTo(Values.Value.MinLongDecimal) == 0)
                {
                    // The reference converts Long.MIN_VALUE to type 'long'.
                    negated = Values.ValueBigint.Min;
                }

                Read();
                return Expressions.ValueExpression.Get(negated);
            }

            // The reference negates the term with a unary operation.
            return new Expressions.UnaryOperation(ReadQueryTerm(aggregateSelect));
        }

        if (CurrentType == Token.PlusSign)
        {
            // The reference reads the sign and takes the term as it is.
            Read();
            return ReadQueryTerm(aggregateSelect);
        }

        if (Current is Token.ParameterToken parameter)
        {
            Read();
            Expressions.Parameter expression = new(parameter.Index);
            parameterList.Add(expression);
            return expression;
        }

        if (CurrentType == Token.At)
        {
            // The reference binds @name to a Variable, which is not ported: a session variable
            // is storage on the session plus an assignment statement, and neither is here.
            // A named parameter it is not.
            throw new TODOException("Parser.readTerm: variables (@name) are not ported");
        }

        Values.Value? literal = Current.GetValue(provider);
        if (literal is not null)
        {
            Read();
            return Expressions.ValueExpression.Get(literal);
        }

        if (Current.AsIdentifier is not null)
        {
            int index = position;
            string? name = Current.AsIdentifier;
            if (name is not null
                && (name.Equals("NEXT", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("CURRENT", StringComparison.OrdinalIgnoreCase)))
            {
                // The reference reads the value of a sequence in its term reader: NEXT VALUE
                // FOR takes the next value of the sequence, CURRENT VALUE FOR the one it
                // handed out last in this session.
                bool current = name.Equals("CURRENT", StringComparison.OrdinalIgnoreCase);
                Read();
                if (IsWord("VALUE") && IsWord("FOR"))
                {
                    Schemas.Sequence sequence = ReadSequence(
                        session ?? throw new TODOException(
                            "Parser.readTerm: this parser has no session"));
                    return current
                        ? new Expressions.SequenceValue(sequence)
                        : new Expressions.SequenceValue(sequence, null);
                }

                position = index;
            }

            // The reference reads a literal with a type in front of it in the same place: the
            // name says the type and the string that follows says the value.
            Expressions.Expression? typed = ReadTypedLiteral(name!);
            if (typed is not null)
            {
                return typed;
            }

            if (tokens[position + 1].TokenType == Token.OpenParen)
            {
                if (aggregateSelect is null && IsAggregateCall(out _))
                {
                    // The reference reads an aggregate through readFunction as well, and the
                    // select it belongs to decides whether it is legal there; outside the select
                    // list that answer is different SQL, not a missing function.
                    throw new TODOException(
                        "Parser.parseQuerySelect: aggregate calls outside the select list are not ported");
                }

                switch (CurrentType)
                {
                    case ParserUtil.Cast:
                        // The reference reads a cast in its term reader, and the select list
                        // is read through the same reader, so a cast is a term here too.
                        return ReadCastExpression(aggregateSelect);

                    case ParserUtil.Day:
                    case ParserUtil.Hour:
                    case ParserUtil.Minute:
                    case ParserUtil.Month:
                    case ParserUtil.Second:
                    case ParserUtil.Year:
                        // The reference's readKeywordCompatibilityFunctionOrColumn: these six are
                        // keywords, and one followed by its parenthesis goes straight into the
                        // compatibility switch, not into readFunction.
                        return ReadKeywordCompatibilityFunction(aggregateSelect);

                    case ParserUtil.Identifier:
                        return ReadFunction(aggregateSelect);

                    case ParserUtil.Left:
                        Read();
                        Expect(Token.OpenParen, "(");
                        return new Expressions.Functions.StringFunction2(
                            ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                            Expressions.Functions.StringFunction2.Left);

                    case ParserUtil.Right:
                        Read();
                        Expect(Token.OpenParen, "(");
                        return new Expressions.Functions.StringFunction2(
                            ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                            Expressions.Functions.StringFunction2.Right);

                    default:
                        // Every other function spelled as a keyword — the CURRENT_* value
                        // functions, CAST, ROW, TABLE and the rest — is read by the reference's
                        // own keyword branches, which are not ported. Naming the keyword beats
                        // answering that no such function exists.
                        throw new TODOException(
                            "Parser.readTerm: the keyword function " + Current.AsIdentifier + " is not ported");
                }
            }

            string columnName = ReadQueryColumnName(out string? tableAlias);
            return new Expressions.ExpressionColumn(columnName, tableAlias, 0);
        }

        throw new TODOException("Parser.parseQuerySelect: this expression is not ported");
    }

    /// <summary>
    /// Reads the name of a sequence and finds it, as the reference's readSequence.
    /// </summary>
    /// <param name="schema">the schema</param>
    /// <returns>the sequence</returns>
    private Schemas.Sequence ReadSequence(SessionLocal session)
    {
        string sequenceName = ReadIdentifierWithSchema(session, out Schemas.Schema schema);
        Schemas.Sequence? sequence = schema.FindSequence(sequenceName);
        if (sequence is null)
        {
            throw DbException.Get(Api.ErrorCode.SequenceNotFound1, sequenceName);
        }

        return sequence;
    }

    /// <summary>
    /// Reads a function call: the name is the current token and is followed by its opening
    /// parenthesis. The name is matched against the reference's two switches — the built-in
    /// functions and the compatibility spellings — and a name that neither knows is
    /// FUNCTION_NOT_FOUND, which is the answer the reference gives once its own lookups are
    /// exhausted.
    /// </summary>
    /// <remarks>
    /// The reference looks for aggregates and for user-defined functions around these switches;
    /// here the ported aggregates are read by the select list before this method is reached, and
    /// no user-defined function can exist because the port has no CREATE ALIAS, so only their
    /// absence is left to state. A name that is still a reference aggregate when this method
    /// sees it belongs to a type this reader does not build, and it refuses by name rather than
    /// pretending the function is missing.
    /// </remarks>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    /// <returns>the function expression</returns>
    private Expressions.Expression ReadFunction(Query.Select? aggregateSelect)
    {
        string name = Current.AsIdentifier!;
        Read();
        Expect(Token.OpenParen, "(");
        string upperName = StringUtils.ToUpperEnglish(name);
        Expressions.Expression? expression = ReadBuiltinFunctionIf(upperName, aggregateSelect);
        if (expression is not null)
        {
            return expression;
        }

        expression = ReadWindowFunctionIf(upperName);
        if (expression is not null)
        {
            return expression;
        }

        expression = ReadCompatibilityFunction(upperName, aggregateSelect);
        if (expression is not null)
        {
            return expression;
        }

        if (Expressions.Aggregate.Aggregate.GetAggregateType(upperName) is not null)
        {
            throw new TODOException("Parser.readFunction: the aggregate " + name + " is not ported");
        }

        if (Expressions.Functions.BuiltinFunctions.IsBuiltinFunction(provider.GetMode(), upperName))
        {
            // A name the reference calls a built-in but that has no branch above is not "not
            // found" — the branch is missing, and the answer has to say which.
            throw new TODOException(
                "Parser.readFunction: the built-in function " + name + " is not ported");
        }

        throw DbException.Get(Api.ErrorCode.FunctionNotFound1, name);
    }

    /// <summary>
    /// Reads a window function, which the reference recognizes by the same names as its
    /// <see cref="Expressions.Analysis.WindowFunctionType"/> and reads through
    /// readWindowFunction. None is ported, so a name that is one of them is refused by name
    /// rather than reported as missing.
    /// </summary>
    /// <param name="name">the name in upper case</param>
    /// <returns>the expression, or null when the name is not a window function</returns>
    private Expressions.Expression? ReadWindowFunctionIf(string name)
    {
        if (Expressions.Analysis.WindowFunctionTypeExtensions.Get(name) is null)
        {
            return null;
        }

        throw new TODOException("Parser.readWindowFunction: window functions are not ported");
    }

    /// <summary>
    /// Reads a built-in function, the reference's readBuiltinFunctionIf. Returns null when the
    /// name is not read here, so that the caller may try the next switch.
    /// </summary>
    /// <param name="upperName">the name in upper case</param>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    /// <returns>the expression, or null</returns>
    private Expressions.Expression? ReadBuiltinFunctionIf(string upperName, Query.Select? aggregateSelect)
    {
        switch (upperName)
        {
            case "ABS":
                return new Expressions.Functions.MathFunction(
                    ReadSingleArgument(aggregateSelect), null, Expressions.Functions.MathFunction.Abs);
            case "MOD":
                return new Expressions.Functions.MathFunction(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.MathFunction.Mod);
            case "SIN":
                return new Expressions.Functions.MathFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.MathFunction1.Sin);
            case "COS":
                return new Expressions.Functions.MathFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.MathFunction1.Cos);
            case "TAN":
                return new Expressions.Functions.MathFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.MathFunction1.Tan);
            case "COT":
                return new Expressions.Functions.MathFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.MathFunction1.Cot);
            case "SINH":
                return new Expressions.Functions.MathFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.MathFunction1.Sinh);
            case "COSH":
                return new Expressions.Functions.MathFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.MathFunction1.Cosh);
            case "TANH":
                return new Expressions.Functions.MathFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.MathFunction1.Tanh);
            case "ASIN":
                return new Expressions.Functions.MathFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.MathFunction1.Asin);
            case "ACOS":
                return new Expressions.Functions.MathFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.MathFunction1.Acos);
            case "ATAN":
                return new Expressions.Functions.MathFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.MathFunction1.Atan);
            case "ATAN2":
                return new Expressions.Functions.MathFunction2(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.MathFunction2.Atan2);
            case "LOG":
                {
                    Expressions.Expression arg1 = ReadQueryOr(aggregateSelect);
                    if (ReadIf(Token.Comma))
                    {
                        return new Expressions.Functions.MathFunction2(
                            arg1, ReadSingleArgument(aggregateSelect), Expressions.Functions.MathFunction2.Log);
                    }

                    Expect(Token.CloseParen, ")");
                    return new Expressions.Functions.MathFunction1(arg1,
                        provider.GetMode().LogIsLogBase10
                            ? Expressions.Functions.MathFunction1.Log10
                            : Expressions.Functions.MathFunction1.Ln);
                }

            case "LOG10":
                return new Expressions.Functions.MathFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.MathFunction1.Log10);
            case "LN":
                return new Expressions.Functions.MathFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.MathFunction1.Ln);
            case "EXP":
                return new Expressions.Functions.MathFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.MathFunction1.Exp);
            case "POWER":
                return new Expressions.Functions.MathFunction2(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.MathFunction2.Power);
            case "SQRT":
                return new Expressions.Functions.MathFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.MathFunction1.Sqrt);
            case "FLOOR":
                return new Expressions.Functions.MathFunction(
                    ReadSingleArgument(aggregateSelect), null, Expressions.Functions.MathFunction.Floor);
            case "CEIL":
            case "CEILING":
                return new Expressions.Functions.MathFunction(
                    ReadSingleArgument(aggregateSelect), null, Expressions.Functions.MathFunction.Ceil);
            case "ROUND":
                return new Expressions.Functions.MathFunction(
                    ReadQueryOr(aggregateSelect), ReadIfArgument(aggregateSelect),
                    Expressions.Functions.MathFunction.Round);
            case "ROUNDMAGIC":
                return new Expressions.Functions.MathFunction(
                    ReadSingleArgument(aggregateSelect), null, Expressions.Functions.MathFunction.Roundmagic);
            case "SIGN":
                return new Expressions.Functions.MathFunction(
                    ReadSingleArgument(aggregateSelect), null, Expressions.Functions.MathFunction.Sign);
            case "TRUNC":
            case "TRUNCATE":
                return new Expressions.Functions.MathFunction(
                    ReadQueryOr(aggregateSelect), ReadIfArgument(aggregateSelect),
                    Expressions.Functions.MathFunction.Trunc);
            case "DEGREES":
                return new Expressions.Functions.MathFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.MathFunction1.Degrees);
            case "RADIANS":
                return new Expressions.Functions.MathFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.MathFunction1.Radians);

            case "BITAND":
                return new Expressions.Functions.BitFunction(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.BitFunction.Bitand);
            case "BITOR":
                return new Expressions.Functions.BitFunction(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.BitFunction.Bitor);
            case "BITXOR":
                return new Expressions.Functions.BitFunction(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.BitFunction.Bitxor);
            case "BITNOT":
                return new Expressions.Functions.BitFunction(
                    ReadSingleArgument(aggregateSelect), null, Expressions.Functions.BitFunction.Bitnot);
            case "BITNAND":
                return new Expressions.Functions.BitFunction(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.BitFunction.Bitnand);
            case "BITNOR":
                return new Expressions.Functions.BitFunction(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.BitFunction.Bitnor);
            case "BITXNOR":
                return new Expressions.Functions.BitFunction(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.BitFunction.Bitxnor);
            case "BITGET":
                return new Expressions.Functions.BitFunction(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.BitFunction.Bitget);
            case "BITCOUNT":
                return new Expressions.Functions.BitFunction(
                    ReadSingleArgument(aggregateSelect), null, Expressions.Functions.BitFunction.Bitcount);
            case "LSHIFT":
                return new Expressions.Functions.BitFunction(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.BitFunction.Lshift);
            case "RSHIFT":
                return new Expressions.Functions.BitFunction(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.BitFunction.Rshift);
            case "ULSHIFT":
                return new Expressions.Functions.BitFunction(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.BitFunction.Ulshift);
            case "URSHIFT":
                return new Expressions.Functions.BitFunction(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.BitFunction.Urshift);
            case "ROTATELEFT":
                return new Expressions.Functions.BitFunction(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.BitFunction.Rotateleft);
            case "ROTATERIGHT":
                return new Expressions.Functions.BitFunction(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.BitFunction.Rotateright);

            case "EXTRACT":
                {
                    int field = ReadDateTimeField();
                    ExpectWord("FROM");
                    return new Expressions.Functions.DateTimeFunction(
                        Expressions.Functions.DateTimeFunction.Extract, field,
                        ReadSingleArgument(aggregateSelect), null);
                }

            case "DATE_TRUNC":
                return new Expressions.Functions.DateTimeFunction(
                    Expressions.Functions.DateTimeFunction.DateTrunc, ReadDateTimeField(),
                    ReadLastArgument(aggregateSelect), null);
            case "DATEADD":
            case "TIMESTAMPADD":
                return new Expressions.Functions.DateTimeFunction(
                    Expressions.Functions.DateTimeFunction.Dateadd, ReadDateTimeField(),
                    ReadNextArgument(aggregateSelect), ReadLastArgument(aggregateSelect));
            case "DATEDIFF":
            case "TIMESTAMPDIFF":
                return new Expressions.Functions.DateTimeFunction(
                    Expressions.Functions.DateTimeFunction.Datediff, ReadDateTimeField(),
                    ReadNextArgument(aggregateSelect), ReadLastArgument(aggregateSelect));
            case "LAST_DAY":
                return new Expressions.Functions.DateTimeFunction(
                    Expressions.Functions.DateTimeFunction.LastDay, -1,
                    ReadSingleArgument(aggregateSelect), null);

            // DateTimeFormatFunction (not ported).
            case "FORMATDATETIME":
            case "PARSEDATETIME":
                throw new TODOException(
                    "Parser.readBuiltinFunctionIf: DateTimeFormatFunction is not ported");

            case "DAYNAME":
                return new Expressions.Functions.DayMonthNameFunction(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.DayMonthNameFunction.DayName);
            case "MONTHNAME":
                return new Expressions.Functions.DayMonthNameFunction(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.DayMonthNameFunction.MonthName);

            case "CARDINALITY":
                return new Expressions.Functions.CardinalityExpression(
                    ReadSingleArgument(aggregateSelect), false);
            case "ARRAY_MAX_CARDINALITY":
                return new Expressions.Functions.CardinalityExpression(
                    ReadSingleArgument(aggregateSelect), true);

            case "LOCATE":
                return new Expressions.Functions.StringFunction(
                    ReadQueryOr(aggregateSelect), ReadNextArgument(aggregateSelect),
                    ReadIfArgument(aggregateSelect), Expressions.Functions.StringFunction.Locate);
            case "INSERT":
                return new Expressions.Functions.StringFunction(
                    ReadQueryOr(aggregateSelect), ReadNextArgument(aggregateSelect),
                    ReadNextArgument(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.StringFunction.Insert);
            case "REPLACE":
                return new Expressions.Functions.StringFunction(
                    ReadQueryOr(aggregateSelect), ReadNextArgument(aggregateSelect),
                    ReadIfArgument(aggregateSelect), Expressions.Functions.StringFunction.Replace);
            case "LPAD":
                return new Expressions.Functions.StringFunction(
                    ReadQueryOr(aggregateSelect), ReadNextArgument(aggregateSelect),
                    ReadIfArgument(aggregateSelect), Expressions.Functions.StringFunction.Lpad);
            case "RPAD":
                return new Expressions.Functions.StringFunction(
                    ReadQueryOr(aggregateSelect), ReadNextArgument(aggregateSelect),
                    ReadIfArgument(aggregateSelect), Expressions.Functions.StringFunction.Rpad);
            case "TRANSLATE":
                return new Expressions.Functions.StringFunction(
                    ReadQueryOr(aggregateSelect), ReadNextArgument(aggregateSelect),
                    ReadLastArgument(aggregateSelect), Expressions.Functions.StringFunction.Translate);

            case "UPPER":
                return new Expressions.Functions.StringFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.StringFunction1.Upper);
            case "LOWER":
                return new Expressions.Functions.StringFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.StringFunction1.Lower);
            case "ASCII":
                return new Expressions.Functions.StringFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.StringFunction1.Ascii);
            case "CHAR":
            case "CHR":
                return new Expressions.Functions.StringFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.StringFunction1.Char);
            case "STRINGENCODE":
                return new Expressions.Functions.StringFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.StringFunction1.Stringencode);
            case "STRINGDECODE":
                return new Expressions.Functions.StringFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.StringFunction1.Stringdecode);
            case "STRINGTOUTF8":
                return new Expressions.Functions.StringFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.StringFunction1.Stringtoutf8);
            case "UTF8TOSTRING":
                return new Expressions.Functions.StringFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.StringFunction1.Utf8tostring);
            case "HEXTORAW":
                return new Expressions.Functions.StringFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.StringFunction1.Hextoraw);
            case "RAWTOHEX":
                return new Expressions.Functions.StringFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.StringFunction1.Rawtohex);
            case "SPACE":
                return new Expressions.Functions.StringFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.StringFunction1.Space);
            case "QUOTE_IDENT":
                return new Expressions.Functions.StringFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.StringFunction1.QuoteIdent);

            case "REPEAT":
                return new Expressions.Functions.StringFunction2(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.StringFunction2.Repeat);

            case "SUBSTRING":
                return ReadSubstringFunction(aggregateSelect);

            // ToCharFunction (not ported).
            case "TO_CHAR":
                throw new TODOException("Parser.readBuiltinFunctionIf: ToCharFunction is not ported");

            case "CHAR_LENGTH":
            case "CHARACTER_LENGTH":
            case "LENGTH":
                // The reference reads these with readIfSingleArgument, which leaves the argument
                // null for LENGTH(); its LengthFunction then fails while mapping columns, and the
                // JDBC layer reports that NullPointerException as the general error. The port
                // keeps the same shape: the null goes into the function and the failure happens
                // where the reference has it, not here.
                return new Expressions.Functions.LengthFunction(
                    ReadIfSingleArgument(aggregateSelect)!, Expressions.Functions.LengthFunction.CharLength);
            case "OCTET_LENGTH":
                return new Expressions.Functions.LengthFunction(
                    ReadIfSingleArgument(aggregateSelect)!, Expressions.Functions.LengthFunction.OctetLength);
            case "BIT_LENGTH":
                return new Expressions.Functions.LengthFunction(
                    ReadIfSingleArgument(aggregateSelect)!, Expressions.Functions.LengthFunction.BitLength);

            case "TRIM":
                return ReadTrimFunction(aggregateSelect);
            case "LTRIM":
                return new Expressions.Functions.TrimFunction(
                    ReadQueryOr(aggregateSelect), ReadIfArgument(aggregateSelect),
                    Expressions.Functions.TrimFunction.Leading | Expressions.Functions.TrimFunction.MultiCharacter);
            case "RTRIM":
                return new Expressions.Functions.TrimFunction(
                    ReadQueryOr(aggregateSelect), ReadIfArgument(aggregateSelect),
                    Expressions.Functions.TrimFunction.Trailing | Expressions.Functions.TrimFunction.MultiCharacter);
            case "BTRIM":
                return new Expressions.Functions.TrimFunction(
                    ReadQueryOr(aggregateSelect), ReadIfArgument(aggregateSelect),
                    Expressions.Functions.TrimFunction.Leading | Expressions.Functions.TrimFunction.Trailing
                        | Expressions.Functions.TrimFunction.MultiCharacter);

            // RegexpFunction (not ported).
            case "REGEXP_LIKE":
            case "REGEXP_REPLACE":
            case "REGEXP_SUBSTR":
                throw new TODOException("Parser.readBuiltinFunctionIf: RegexpFunction is not ported");

            // XMLFunction (not ported).
            case "XMLATTR":
            case "XMLCDATA":
            case "XMLCOMMENT":
            case "XMLNODE":
            case "XMLSTARTDOC":
            case "XMLTEXT":
                throw new TODOException("Parser.readBuiltinFunctionIf: XMLFunction is not ported");

            // ArrayFunction (not ported).
            case "TRIM_ARRAY":
            case "ARRAY_CONTAINS":
            case "ARRAY_SLICE":
                throw new TODOException("Parser.readBuiltinFunctionIf: ArrayFunction is not ported");

            case "COMPRESS":
                return new Expressions.Functions.CompressFunction(
                    ReadQueryOr(aggregateSelect), ReadIfArgument(aggregateSelect),
                    Expressions.Functions.CompressFunction.Compress);
            case "EXPAND":
                return new Expressions.Functions.CompressFunction(
                    ReadSingleArgument(aggregateSelect), null, Expressions.Functions.CompressFunction.Expand);

            case "SOUNDEX":
                return new Expressions.Functions.SoundexFunction(
                    ReadSingleArgument(aggregateSelect), null, Expressions.Functions.SoundexFunction.Soundex);
            case "DIFFERENCE":
                return new Expressions.Functions.SoundexFunction(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.SoundexFunction.Difference);

            // JsonConstructorFunction (not ported).
            case "JSON_OBJECT":
            case "JSON_ARRAY":
                throw new TODOException(
                    "Parser.readBuiltinFunctionIf: JsonConstructorFunction is not ported");

            // CryptFunction (not ported).
            case "ENCRYPT":
            case "DECRYPT":
                throw new TODOException("Parser.readBuiltinFunctionIf: CryptFunction is not ported");

            case "COALESCE":
                return ReadCoalesceFunction(Expressions.Functions.CoalesceFunction.Coalesce, aggregateSelect);
            case "GREATEST":
                return ReadCoalesceFunction(Expressions.Functions.CoalesceFunction.Greatest, aggregateSelect);
            case "LEAST":
                return ReadCoalesceFunction(Expressions.Functions.CoalesceFunction.Least, aggregateSelect);
            case "NULLIF":
                return new Expressions.Functions.NullIfFunction(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect));
            case "CONCAT":
                return ReadConcatFunction(Expressions.Functions.ConcatFunction.Concat, aggregateSelect);
            case "CONCAT_WS":
                return ReadConcatFunction(Expressions.Functions.ConcatFunction.ConcatWs, aggregateSelect);

            // HashFunction (not ported).
            case "HASH":
            case "ORA_HASH":
                throw new TODOException("Parser.readBuiltinFunctionIf: HashFunction is not ported");

            case "RAND":
            case "RANDOM":
                return new Expressions.Functions.RandFunction(
                    ReadIfSingleArgument(aggregateSelect), Expressions.Functions.RandFunction.Rand);
            case "SECURE_RAND":
                return new Expressions.Functions.RandFunction(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.RandFunction.SecureRand);
            case "RANDOM_UUID":
                return new Expressions.Functions.RandFunction(
                    ReadIfSingleArgument(aggregateSelect), Expressions.Functions.RandFunction.RandomUuid);
            case "UUID":
                Expect(Token.CloseParen, ")");
                return new Expressions.Functions.RandFunction(
                    null, Expressions.Functions.RandFunction.RandomUuid);

            // SessionControlFunction (not ported).
            case "ABORT_SESSION":
            case "CANCEL_SESSION":
                throw new TODOException(
                    "Parser.readBuiltinFunctionIf: SessionControlFunction is not ported");

            // SysInfoFunction (not ported).
            case "AUTOCOMMIT":
            case "DATABASE_PATH":
            case "H2VERSION":
            case "LOCK_MODE":
            case "LOCK_TIMEOUT":
            case "MEMORY_FREE":
            case "MEMORY_USED":
            case "READONLY":
            case "SESSION_ID":
            case "TRANSACTION_ID":
                throw new TODOException("Parser.readBuiltinFunctionIf: SysInfoFunction is not ported");

            // TableInfoFunction (not ported).
            case "DISK_SPACE_USED":
            case "ESTIMATED_ENVELOPE":
                throw new TODOException("Parser.readBuiltinFunctionIf: TableInfoFunction is not ported");

            // FileFunction (not ported).
            case "FILE_READ":
            case "FILE_WRITE":
                throw new TODOException("Parser.readBuiltinFunctionIf: FileFunction is not ported");

            // DataTypeSQLFunction (not ported).
            case "DATA_TYPE_SQL":
                throw new TODOException("Parser.readBuiltinFunctionIf: DataTypeSQLFunction is not ported");

            // DBObjectFunction (not ported).
            case "DB_OBJECT_ID":
            case "DB_OBJECT_SQL":
            case "DB_OBJECT_SIZE":
            case "DB_OBJECT_TOTAL_SIZE":
            case "DB_OBJECT_APPROXIMATE_SIZE":
            case "DB_OBJECT_APPROXIMATE_TOTAL_SIZE":
                throw new TODOException("Parser.readBuiltinFunctionIf: DBObjectFunction is not ported");

            // CSVWriteFunction (not ported).
            case "CSVWRITE":
                throw new TODOException("Parser.readBuiltinFunctionIf: CSVWriteFunction is not ported");

            // SignalFunction (not ported).
            case "SIGNAL":
                throw new TODOException("Parser.readBuiltinFunctionIf: SignalFunction is not ported");

            // TruncateValueFunction (not ported).
            case "TRUNCATE_VALUE":
                throw new TODOException(
                    "Parser.readBuiltinFunctionIf: TruncateValueFunction is not ported");

            case "ZERO":
                Expect(Token.CloseParen, ")");
                return Expressions.ValueExpression.Get(ValueInteger.Get(0));
            case "PI":
                Expect(Token.CloseParen, ")");
                return Expressions.ValueExpression.Get(ValueDouble.Get(Math.PI));

            // GCDFunction (not ported).
            case "GCD":
            case "LCM":
                throw new TODOException("Parser.readBuiltinFunctionIf: GCDFunction is not ported");

            default:
                return null;
        }
    }

    /// <summary>
    /// Reads a compatibility sequence value function, the reference's
    /// readCompatibilitySequenceValueFunction.
    /// </summary>
    /// <param name="current">whether the current value is read instead of the next one</param>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    /// <returns>the expression</returns>
    private Expressions.Expression ReadCompatibilitySequenceValueFunction(
        bool current, Query.Select? aggregateSelect)
    {
        Expressions.Expression arg1 = ReadQueryOr(aggregateSelect);
        Expressions.Expression? arg2 = null;
        if (ReadIf(Token.Comma))
        {
            arg2 = ReadQueryOr(aggregateSelect);
        }

        Expect(Token.CloseParen, ")");
        return new Expressions.Functions.CompatibilitySequenceValueFunction(arg1, arg2, current);
    }

    /// <summary>
    /// Reads a compatibility function, the reference's readCompatibilityFunction. Returns null
    /// when the name is not read here.
    /// </summary>
    /// <param name="name">the name in upper case</param>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    /// <returns>the expression, or null</returns>
    private Expressions.Expression? ReadCompatibilityFunction(string name, Query.Select? aggregateSelect)
    {
        switch (name)
        {
            // ConcatenationOperation (not ported).
            case "ARRAY_APPEND":
            case "ARRAY_CAT":
                throw new TODOException(
                    "Parser.readCompatibilityFunction: ConcatenationOperation is not ported");

            // ArrayElementReference (not ported).
            case "ARRAY_GET":
                throw new TODOException(
                    "Parser.readCompatibilityFunction: ArrayElementReference is not ported");

            case "ARRAY_LENGTH":
                return new Expressions.Functions.CardinalityExpression(
                    ReadSingleArgument(aggregateSelect), false);

            // SimpleCase (not ported through this spelling).
            case "DECODE":
                throw new TODOException("Parser.readCompatibilityFunction: DECODE is not ported");

            // SearchedCase (not ported through this spelling).
            case "CASEWHEN":
            case "NVL2":
                throw new TODOException("Parser.readCompatibilityFunction: " + name + " is not ported");

            // CastSpecification (not ported through this spelling).
            case "CONVERT":
                throw new TODOException("Parser.readCompatibilityFunction: CONVERT is not ported");

            case "IFNULL":
                return new Expressions.Functions.CoalesceFunction(
                    Expressions.Functions.CoalesceFunction.Coalesce,
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect));
            case "NVL":
                return ReadCoalesceFunction(
                    Expressions.Functions.CoalesceFunction.Coalesce, aggregateSelect);

            // CurrentGeneralValueSpecification (not ported).
            case "DATABASE":
            case "SCHEMA":
                throw new TODOException(
                    "Parser.readCompatibilityFunction: CurrentGeneralValueSpecification is not ported");

            // CurrentDateTimeValueFunction.
            case "CURDATE":
                return ReadCurrentDateTimeValueFunction(
                    Expressions.Functions.CurrentDateTimeValueFunction.CurrentDate, hasParen: true);
            case "CURTIME":
                return ReadCurrentDateTimeValueFunction(
                    Expressions.Functions.CurrentDateTimeValueFunction.Localtime, hasParen: true);
            case "NOW":
                return ReadCurrentDateTimeValueFunction(
                    Expressions.Functions.CurrentDateTimeValueFunction.Localtimestamp, hasParen: true);

            // ModeFunction.getCompatibilityDateTimeValueFunction (not ported).
            case "TODAY":
            case "SYSDATE":
            case "SYSTIMESTAMP":
                throw new TODOException(
                    "Parser.readCompatibilityFunction: the mode date-time value functions are not ported");

            // EXTRACT
            case "DAY":
            case "DAY_OF_MONTH":
            case "DAYOFMONTH":
                return new Expressions.Functions.DateTimeFunction(
                    Expressions.Functions.DateTimeFunction.Extract, Expressions.Functions.DateTimeFunction.Day,
                    ReadSingleArgument(aggregateSelect), null);
            case "DAY_OF_WEEK":
            case "DAYOFWEEK":
                return new Expressions.Functions.DateTimeFunction(
                    Expressions.Functions.DateTimeFunction.Extract, Expressions.Functions.DateTimeFunction.DayOfWeek,
                    ReadSingleArgument(aggregateSelect), null);
            case "DAY_OF_YEAR":
            case "DAYOFYEAR":
                return new Expressions.Functions.DateTimeFunction(
                    Expressions.Functions.DateTimeFunction.Extract, Expressions.Functions.DateTimeFunction.DayOfYear,
                    ReadSingleArgument(aggregateSelect), null);
            case "HOUR":
                return new Expressions.Functions.DateTimeFunction(
                    Expressions.Functions.DateTimeFunction.Extract, Expressions.Functions.DateTimeFunction.Hour,
                    ReadSingleArgument(aggregateSelect), null);
            case "ISO_DAY_OF_WEEK":
                return new Expressions.Functions.DateTimeFunction(
                    Expressions.Functions.DateTimeFunction.Extract,
                    Expressions.Functions.DateTimeFunction.IsoDayOfWeek,
                    ReadSingleArgument(aggregateSelect), null);
            case "ISO_WEEK":
                return new Expressions.Functions.DateTimeFunction(
                    Expressions.Functions.DateTimeFunction.Extract, Expressions.Functions.DateTimeFunction.IsoWeek,
                    ReadSingleArgument(aggregateSelect), null);
            case "ISO_YEAR":
                return new Expressions.Functions.DateTimeFunction(
                    Expressions.Functions.DateTimeFunction.Extract,
                    Expressions.Functions.DateTimeFunction.IsoWeekYear,
                    ReadSingleArgument(aggregateSelect), null);
            case "MINUTE":
                return new Expressions.Functions.DateTimeFunction(
                    Expressions.Functions.DateTimeFunction.Extract, Expressions.Functions.DateTimeFunction.Minute,
                    ReadSingleArgument(aggregateSelect), null);
            case "MONTH":
                return new Expressions.Functions.DateTimeFunction(
                    Expressions.Functions.DateTimeFunction.Extract, Expressions.Functions.DateTimeFunction.Month,
                    ReadSingleArgument(aggregateSelect), null);
            case "QUARTER":
                return new Expressions.Functions.DateTimeFunction(
                    Expressions.Functions.DateTimeFunction.Extract, Expressions.Functions.DateTimeFunction.Quarter,
                    ReadSingleArgument(aggregateSelect), null);
            case "SECOND":
                return new Expressions.Functions.DateTimeFunction(
                    Expressions.Functions.DateTimeFunction.Extract, Expressions.Functions.DateTimeFunction.Second,
                    ReadSingleArgument(aggregateSelect), null);
            case "WEEK":
                return new Expressions.Functions.DateTimeFunction(
                    Expressions.Functions.DateTimeFunction.Extract, Expressions.Functions.DateTimeFunction.Week,
                    ReadSingleArgument(aggregateSelect), null);
            case "YEAR":
                return new Expressions.Functions.DateTimeFunction(
                    Expressions.Functions.DateTimeFunction.Extract, Expressions.Functions.DateTimeFunction.Year,
                    ReadSingleArgument(aggregateSelect), null);

            // LOCATE, with INSTR's arguments swapped.
            case "INSTR":
                {
                    Expressions.Expression arg1 = ReadQueryOr(aggregateSelect);
                    return new Expressions.Functions.StringFunction(
                        ReadNextArgument(aggregateSelect), arg1, ReadIfArgument(aggregateSelect),
                        Expressions.Functions.StringFunction.Locate);
                }

            case "POSITION":
                // The reference reads the first argument with readConcat(), which is not ported.
                throw new TODOException(
                    "Parser.readCompatibilityFunction: POSITION needs readConcat");

            case "LCASE":
                return new Expressions.Functions.StringFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.StringFunction1.Lower);
            case "UCASE":
                return new Expressions.Functions.StringFunction1(
                    ReadSingleArgument(aggregateSelect), Expressions.Functions.StringFunction1.Upper);

            case "SUBSTR":
                return ReadSubstringFunction(aggregateSelect);

            // CompatibilitySequenceValueFunction.
            case "CURRVAL":
            case "NEXTVAL":
                return ReadCompatibilitySequenceValueFunction(name == "CURRVAL", aggregateSelect);

            default:
                return null;
        }
    }

    /// <summary>
    /// Reads a date-part keyword that is a function when it is followed by its parenthesis, the
    /// reference's readKeywordCompatibilityFunctionOrColumn.
    /// </summary>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    /// <returns>the function expression</returns>
    private Expressions.Expression ReadKeywordCompatibilityFunction(Query.Select? aggregateSelect)
    {
        string name = Current.AsIdentifier!;
        Read();
        Expect(Token.OpenParen, "(");
        Expressions.Expression? expression =
            ReadCompatibilityFunction(StringUtils.ToUpperEnglish(name), aggregateSelect);
        if (expression is not null)
        {
            return expression;
        }

        // Every name that reaches here is one of the six keyword date parts, and each has its
        // branch in the switch above, so this stands for a name that got in without one.
        throw DbException.GetSyntaxError(sql, Current.Start, "date-time field");
    }

    private Expressions.Expression ReadSingleArgument(Query.Select? aggregateSelect)
    {
        Expressions.Expression arg = ReadQueryOr(aggregateSelect);
        Expect(Token.CloseParen, ")");
        return arg;
    }

    private Expressions.Expression ReadNextArgument(Query.Select? aggregateSelect)
    {
        Expect(Token.Comma, ",");
        return ReadQueryOr(aggregateSelect);
    }

    private Expressions.Expression ReadLastArgument(Query.Select? aggregateSelect)
    {
        Expect(Token.Comma, ",");
        Expressions.Expression arg = ReadQueryOr(aggregateSelect);
        Expect(Token.CloseParen, ")");
        return arg;
    }

    /// <summary>
    /// Reads a date-time field, the reference's readDateTimeField.
    /// </summary>
    /// <returns>the field</returns>
    private int ReadDateTimeField()
    {
        int field = -1;
        switch (CurrentType)
        {
            case ParserUtil.Identifier:
                if (!Current.IsQuoted)
                {
                    field = Expressions.Functions.DateTimeFunction.GetField(Current.AsIdentifier!);
                }

                break;
            case Token.Literal:
                Values.Value? literal = Current.GetValue(provider);
                if (literal?.GetValueType() == Values.Value.Varchar)
                {
                    field = Expressions.Functions.DateTimeFunction.GetField(literal.GetString()!);
                }

                break;
            case ParserUtil.Year:
                field = Expressions.Functions.DateTimeFunction.Year;
                break;
            case ParserUtil.Month:
                field = Expressions.Functions.DateTimeFunction.Month;
                break;
            case ParserUtil.Day:
                field = Expressions.Functions.DateTimeFunction.Day;
                break;
            case ParserUtil.Hour:
                field = Expressions.Functions.DateTimeFunction.Hour;
                break;
            case ParserUtil.Minute:
                field = Expressions.Functions.DateTimeFunction.Minute;
                break;
            case ParserUtil.Second:
                field = Expressions.Functions.DateTimeFunction.Second;
                break;
        }

        if (field < 0)
        {
            throw DbException.GetSyntaxError(sql, Current.Start, "date-time field");
        }

        Read();
        return field;
    }

    private Expressions.Expression? ReadIfSingleArgument(Query.Select? aggregateSelect)
    {
        if (ReadIf(Token.CloseParen))
        {
            return null;
        }

        Expressions.Expression arg = ReadQueryOr(aggregateSelect);
        Expect(Token.CloseParen, ")");
        return arg;
    }

    private Expressions.Expression? ReadIfArgument(Query.Select? aggregateSelect)
    {
        Expressions.Expression? arg = ReadIf(Token.Comma) ? ReadQueryOr(aggregateSelect) : null;
        Expect(Token.CloseParen, ")");
        return arg;
    }

    private Expressions.Expression ReadTrimFunction(Query.Select? aggregateSelect)
    {
        int flags;
        bool needFrom;
        if (IsWord("LEADING"))
        {
            flags = Expressions.Functions.TrimFunction.Leading;
            needFrom = true;
        }
        else if (IsWord("TRAILING"))
        {
            flags = Expressions.Functions.TrimFunction.Trailing;
            needFrom = true;
        }
        else
        {
            needFrom = IsWord("BOTH");
            flags = Expressions.Functions.TrimFunction.Leading | Expressions.Functions.TrimFunction.Trailing;
        }

        Expressions.Expression from;
        Expressions.Expression? space = null;
        if (needFrom)
        {
            if (!IsWord("FROM"))
            {
                space = ReadQueryOr(aggregateSelect);
                ExpectWord("FROM");
            }

            from = ReadQueryOr(aggregateSelect);
        }
        else
        {
            if (IsWord("FROM"))
            {
                from = ReadQueryOr(aggregateSelect);
            }
            else
            {
                from = ReadQueryOr(aggregateSelect);
                if (IsWord("FROM"))
                {
                    space = from;
                    from = ReadQueryOr(aggregateSelect);
                }
                else if (ReadIf(Token.Comma))
                {
                    space = ReadQueryOr(aggregateSelect);
                }
            }
        }

        Expect(Token.CloseParen, ")");
        return new Expressions.Functions.TrimFunction(from, space, flags);
    }

    private Expressions.Expression ReadSubstringFunction(Query.Select? aggregateSelect)
    {
        // Standard variants are:
        // SUBSTRING(X FROM 1)
        // SUBSTRING(X FROM 1 FOR 1)
        // Different non-standard variants include:
        // SUBSTRING(X,1)
        // SUBSTRING(X,1,1)
        // SUBSTRING(X FOR 1) -- Postgres
        Expressions.Functions.SubstringFunction function = new();
        function.AddParameter(ReadQueryOr(aggregateSelect));
        if (IsWord("FROM"))
        {
            function.AddParameter(ReadQueryOr(aggregateSelect));
            if (IsWord("FOR"))
            {
                function.AddParameter(ReadQueryOr(aggregateSelect));
            }
        }
        else if (IsWord("FOR"))
        {
            function.AddParameter(Expressions.ValueExpression.Get(ValueInteger.Get(1)));
            function.AddParameter(ReadQueryOr(aggregateSelect));
        }
        else
        {
            ReadIf(Token.Comma);
            function.AddParameter(ReadQueryOr(aggregateSelect));
            if (ReadIf(Token.Comma))
            {
                function.AddParameter(ReadQueryOr(aggregateSelect));
            }
        }

        Expect(Token.CloseParen, ")");
        function.DoneWithParameters();
        return function;
    }

    /// <summary>
    /// Reads a comma, or the closing parenthesis that ends a list; the reference's
    /// ParserBase.readIfMore, which the variadic parameter loops ask.
    /// </summary>
    /// <returns>true when a comma was read, false when the list ended</returns>
    private bool ReadIfMore()
    {
        if (ReadIf(Token.Comma))
        {
            return true;
        }

        Expect(Token.CloseParen, ")");
        return false;
    }

    private Expressions.Expression ReadRemainingParameters(
        Expressions.OperationN f, Query.Select? aggregateSelect)
    {
        while (ReadIfMore())
        {
            f.AddParameter(ReadQueryOr(aggregateSelect));
        }

        f.DoneWithParameters();
        return f;
    }

    private Expressions.Expression ReadCoalesceFunction(int function, Query.Select? aggregateSelect)
    {
        Expressions.Functions.CoalesceFunction f = new(function);
        f.AddParameter(ReadQueryOr(aggregateSelect));
        ReadRemainingParameters(f, aggregateSelect);
        if (function == Expressions.Functions.CoalesceFunction.Greatest
            || function == Expressions.Functions.CoalesceFunction.Least)
        {
            f.SetIgnoreNulls(ReadIgnoreNulls(provider.GetMode().GreatestLeastIgnoreNulls));
        }

        return f;
    }

    private Expressions.Expression ReadConcatFunction(int function, Query.Select? aggregateSelect)
    {
        Expressions.Functions.ConcatFunction f = new(function);
        f.AddParameter(ReadQueryOr(aggregateSelect));
        f.AddParameter(ReadNextArgument(aggregateSelect));
        if (function == Expressions.Functions.ConcatFunction.ConcatWs)
        {
            f.AddParameter(ReadNextArgument(aggregateSelect));
        }

        return ReadRemainingParameters(f, aggregateSelect);
    }

    private bool ReadIgnoreNulls(bool ignoreNulls)
    {
        if (ReadIfWords("IGNORE", "NULLS"))
        {
            return true;
        }

        if (ReadIfWords("RESPECT", "NULLS"))
        {
            return false;
        }

        return ignoreNulls;
    }

    /// <summary>
    /// Whether the token after the current NOT opens a LIKE / ILIKE / REGEXP comparison.
    /// </summary>
    private bool IsLikeFollowing()
    {
        if (position + 1 >= tokens.Count)
        {
            return false;
        }

        string? next = tokens[position + 1].AsIdentifier;
        return next is "LIKE" or "ILIKE" or "REGEXP";
    }

    /// <summary>
    /// Reads a LIKE / ILIKE right-hand side, with an optional ESCAPE clause.
    /// </summary>
    /// <param name="left">the expression before LIKE</param>
    /// <param name="likeType">the type of comparison</param>
    /// <param name="not">whether this is NOT LIKE</param>
    /// <returns>the predicate</returns>
    private Expressions.Expression ReadLikePredicate(
        Expressions.Expression left, Expressions.Conditions.CompareLike.LikeType likeType, bool not)
    {
        Expressions.Expression right = ReadQueryTerm(null);
        Expressions.Expression? escape = IsWord("ESCAPE") ? ReadQueryTerm(null) : null;
        return new Expressions.Conditions.CompareLike(
            session!.GetCompareMode(), left, not, whenOperand: false, right, escape, likeType);
    }

    /// <summary>
    /// Reads a CASE: either a searched CASE (CASE WHEN condition THEN result ...) or a simple
    /// CASE (CASE operand WHEN value [, value ...] THEN result ...).
    /// </summary>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    /// <returns>the case expression</returns>
    private Expressions.Expression ReadQueryCase(Query.Select? aggregateSelect)
    {
        ExpectWord("CASE");
        if (IsWord("WHEN", consume: false))
        {
            Expressions.SearchedCase result = new();
            do
            {
                ExpectWord("WHEN");
                result.AddParameter(ReadQueryOr(aggregateSelect));
                ExpectWord("THEN");
                result.AddParameter(ReadQueryOr(aggregateSelect));
            }
            while (IsWord("WHEN", consume: false));

            if (IsWord("ELSE", consume: false))
            {
                ExpectWord("ELSE");
                result.AddParameter(ReadQueryOr(aggregateSelect));
            }

            ExpectWord("END");
            result.DoneWithParameters();
            return result;
        }

        Expressions.Expression operand = ReadQueryOr(aggregateSelect);
        ExpectWord("WHEN");
        Expressions.SimpleCase.SimpleWhen when = ReadSimpleWhenClause(aggregateSelect);
        Expressions.SimpleCase.SimpleWhen? current = when;
        while (IsWord("WHEN", consume: false))
        {
            ExpectWord("WHEN");
            Expressions.SimpleCase.SimpleWhen next = ReadSimpleWhenClause(aggregateSelect);
            current!.SetWhen(next);
            current = next;
        }

        Expressions.Expression? elseResult = null;
        if (IsWord("ELSE", consume: false))
        {
            ExpectWord("ELSE");
            elseResult = ReadQueryOr(aggregateSelect);
        }

        ExpectWord("END");
        return new Expressions.SimpleCase(operand, when, elseResult);
    }

    /// <summary>
    /// Reads one WHEN of a simple CASE: one or more compared values, then THEN and a result.
    /// </summary>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    /// <returns>the WHEN</returns>
    private Expressions.SimpleCase.SimpleWhen ReadSimpleWhenClause(Query.Select? aggregateSelect)
    {
        List<Expressions.Expression> operands = [ReadQueryOr(aggregateSelect)];
        while (ReadIf(Token.Comma))
        {
            operands.Add(ReadQueryOr(aggregateSelect));
        }

        ExpectWord("THEN");
        return new Expressions.SimpleCase.SimpleWhen(
            operands.ToArray(), ReadQueryOr(aggregateSelect));
    }

    /// <summary>
    /// Builds the CREATE TABLE command for a statement the parser read.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="statement">the parsed statement</param>
    /// <returns>the prepared statement</returns>
    private Prepared PrepareCreateTable(SessionLocal session, CreateTableStatement statement)
    {
        Schemas.Schema schema = statement.Schema is null
            ? CurrentSchema(session)
            : session.GetDatabase().GetSchema(statement.Schema);
        Ddl.CreateTable create = new(session, schema);
        create.SetSql(statement.Sql, null);
        create.SetTableName(statement.Name);
        create.SetIfNotExists(statement.IfNotExists);
        int id = 0;
        foreach (ColumnDefinition column in statement.Columns)
        {
            Tables.Column tableColumn = new(column.Name, ParseDataType(column.Type), null, id++);
            if (column.NotNull)
            {
                tableColumn.SetNullable(false);
            }

            if (column.IdentityOptions is not null)
            {
                // An identity column is never nullable, and the sequence of it is created
                // when the table is.
                tableColumn.SetIdentityOptions(column.IdentityOptions, column.IdentityGeneratedAlways);
            }

            if (column.Generated is not null)
            {
                tableColumn.SetGeneratedExpression(column.Generated);
            }

            if (column.Default is not null)
            {
                tableColumn.SetDefaultExpression(session, column.Default);
            }

            if (column.OnUpdate is not null)
            {
                tableColumn.SetOnUpdateExpression(session, column.OnUpdate);
            }

            if (column.DefaultOnNull)
            {
                tableColumn.SetDefaultOnNull(true);
            }

            create.AddColumn(tableColumn);
            if (column.PrimaryKey)
            {
                // One column-level PRIMARY KEY is one constraint over one column, as in the
                // reference's readColumnConstraints: a second one over different columns is
                // then two primary keys and error 90017, not one composite key. The column
                // goes in by name, as in the reference; the constraint resolves it against
                // the table when it runs.
                create.AddConstraintCommand(CreateAddConstraintCommand(session, statement.Name,
                    false, new TableConstraintDefinition(column.ConstraintName, false,
                        [tableColumn.GetName()], column.PrimaryKeyHash)));
            }

            if (column.RefTable is not null)
            {
                // One column-level REFERENCES is one foreign key over one column, as in the
                // reference's readColumnConstraints.
                create.AddConstraintCommand(CreateAddConstraintCommand(session, statement.Name,
                    false, new TableConstraintDefinition(column.ConstraintName, false,
                        [tableColumn.GetName()], Hash: false,
                        CommandInterface.AlterTableAddConstraintReferential, Comment: null,
                        column.RefTableSchema, column.RefTable, column.RefColumns,
                        column.RefUpdateAction, column.RefDeleteAction, CheckExisting: true)));
            }
        }

        foreach (TableConstraintDefinition constraint in statement.Constraints)
        {
            create.AddConstraintCommand(
                CreateAddConstraintCommand(session, statement.Name, false, constraint));
        }

        return create;
    }

    /// <summary>
    /// Builds the ALTER TABLE command for a statement the parser read.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="statement">the parsed statement</param>
    /// <returns>the prepared statement</returns>
    private Prepared PrepareAlterTable(SessionLocal session, AlterTableStatement statement)
    {
        if (statement.AlterColumn is not null)
        {
            Schemas.Schema schema = CurrentSchema(session);
            Tables.Table? table = schema.FindTableOrView(session, statement.Name);
            if (table is null)
            {
                if (statement.IfTableExists)
                {
                    return new Dml.NoOperation(session);
                }

                throw DbException.Get(Api.ErrorCode.TableOrViewNotFound1, statement.Name);
            }

            Tables.Column? column = null;
            foreach (Tables.Column candidate in table.GetColumns())
            {
                if (candidate.GetName().Equals(statement.AlterColumn.ColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    column = candidate;
                    break;
                }
            }

            if (column is null)
            {
                throw DbException.Get(Api.ErrorCode.ColumnNotFound1, statement.AlterColumn.ColumnName);
            }

            if (statement.AlterColumn.DropIdentity)
            {
                Ddl.AlterTableAlterColumn dropIdentity = new(session, schema);
                dropIdentity.SetSql(statement.Sql, null);
                dropIdentity.SetTableName(statement.Name);
                dropIdentity.SetIfTableExists(statement.IfTableExists);
                dropIdentity.SetOldColumn(column);
                dropIdentity.SetType(CommandInterface.AlterTableAlterColumnDropIdentity);
                return dropIdentity;
            }

            Ddl.AlterSequence alter = new(session, schema);
            alter.SetSql(statement.Sql, null);
            alter.SetColumn(column, statement.AlterColumn.Always);
            alter.SetOptions(statement.AlterColumn.Options);
            return alter;
        }

        if (statement.DropPrimaryKey)
        {
            Schemas.Schema schema = CurrentSchema(session);
            Tables.Table? table = schema.FindTableOrView(session, statement.Name);
            if (table is null)
            {
                if (statement.IfTableExists)
                {
                    return new Dml.NoOperation(session);
                }

                throw DbException.Get(Api.ErrorCode.TableOrViewNotFound1, statement.Name);
            }

            // The reference turns DROP PRIMARY KEY into a DROP INDEX on the primary key's
            // index name, so the constraint goes away through the same path as any index.
            Ddl.DropIndex dropPrimaryKey = new(session, schema);
            dropPrimaryKey.SetSql(statement.Sql, null);
            dropPrimaryKey.SetIndexName(table.GetPrimaryKey().GetName()!);
            return dropPrimaryKey;
        }

        if (statement.AddConstraint is not null)
        {
            Ddl.AlterTableAddConstraint add = CreateAddConstraintCommand(
                session, statement.Name, statement.IfTableExists, statement.AddConstraint);
            add.SetSql(statement.Sql, null);
            return add;
        }

        Ddl.AlterTableDropConstraint drop = new(
            session, CurrentSchema(session), statement.DropConstraintIfExists);
        drop.SetSql(statement.Sql, null);
        drop.SetTableName(statement.Name);
        drop.SetIfTableExists(statement.IfTableExists);
        drop.SetConstraintName(statement.DropConstraintName!);
        if (statement.DropAction is ConstraintActionType action)
        {
            drop.SetDropAction(action);
        }

        return drop;
    }

    /// <summary>
    /// Reads the options of a sequence, as the reference's parseSequenceOptions.
    /// </summary>
    /// <param name="options">the options to fill</param>
    /// <param name="command">the statement the options belong to, or null</param>
    /// <param name="allowDataType">whether the type of the sequence may be named</param>
    /// <param name="forAlterColumn">whether the options belong to an identity column</param>
    private bool ReadSequenceOptions(
        Ddl.SequenceOptions options,
        Ddl.CreateSequence? command,
        bool allowDataType,
        bool forAlterColumn)
    {
        bool result = false;
        while (true)
        {
            if (allowDataType && IsWord("AS"))
            {
                TypeInfo dataType = ParseDataType(ReadTypeName());
                if (!DataType.IsNumericType(dataType.GetValueType()))
                {
                    throw DbException.GetUnsupportedException(
                        "CREATE SEQUENCE AS " + dataType.GetSql(new StringBuilder(), HasSql.TraceSqlFlags));
                }

                options.SetDataType(dataType);
            }
            else if (IsWord("START") && IsWord("WITH"))
            {
                options.SetStartValue(ReadExpression());
            }
            else if (IsWord("RESTART"))
            {
                options.SetRestartValue(IsWord("WITH")
                    ? ReadExpression()
                    : Expressions.ValueExpression.Get(Values.ValueNull.Instance));
            }
            else if (command is not null && ReadCreateSequenceOption(command))
            {
                // The option belongs to the statement itself.
            }
            else if (forAlterColumn)
            {
                int index = position;
                if (IsWord("SET"))
                {
                    if (!ReadBasicSequenceOption(options))
                    {
                        position = index;
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
            else if (!ReadBasicSequenceOption(options))
            {
                break;
            }

            result = true;
        }

        return result;
    }

    /// <summary>
    /// Reads an option of the CREATE SEQUENCE statement itself, as the reference's
    /// parseCreateSequenceOption.
    /// </summary>
    /// <param name="command">the statement</param>
    /// <returns>true if an option was read</returns>
    private bool ReadCreateSequenceOption(Ddl.CreateSequence command)
    {
        // The reference reads the ownership option as one token, spelled with an underscore:
        // "BELONGS_TO_TABLE". The three-word spelling of the documentation is a syntax error
        // there, and the oracle says so too.
        if (IsWord("BELONGS_TO_TABLE"))
        {
            command.SetBelongsToTable(true);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads one option of a sequence, as the reference's parseBasicSequenceOption.
    /// </summary>
    /// <param name="options">the options to fill</param>
    /// <returns>true if an option was read</returns>
    private bool ReadBasicSequenceOption(Ddl.SequenceOptions options)
    {
        if (IsWord("INCREMENT"))
        {
            _ = IsWord("BY");
            options.SetIncrement(ReadExpression());
        }
        else if (IsWord("MINVALUE"))
        {
            options.SetMinValue(ReadExpression());
        }
        else if (IsWord("MAXVALUE"))
        {
            options.SetMaxValue(ReadExpression());
        }
        else if (IsWord("CYCLE"))
        {
            options.SetCycle(Schemas.Sequence.Cycle.CYCLE);
        }
        else if (IsWord("NO"))
        {
            if (IsWord("MINVALUE"))
            {
                options.SetMinValue(Expressions.ValueExpression.Get(Values.ValueNull.Instance));
            }
            else if (IsWord("MAXVALUE"))
            {
                options.SetMaxValue(Expressions.ValueExpression.Get(Values.ValueNull.Instance));
            }
            else if (IsWord("CYCLE"))
            {
                options.SetCycle(Schemas.Sequence.Cycle.NO_CYCLE);
            }
            else if (IsWord("CACHE"))
            {
                options.SetCacheSize(Expressions.ValueExpression.Get(Values.ValueBigint.Get(1)));
            }
            else
            {
                throw DbException.GetSyntaxError(sql, Current.Start, "sequence option");
            }
        }
        else if (IsWord("EXHAUSTED"))
        {
            options.SetCycle(Schemas.Sequence.Cycle.EXHAUSTED);
        }
        else if (IsWord("CACHE"))
        {
            options.SetCacheSize(ReadExpression());
        }
        else if (IsWord("NOMINVALUE"))
        {
            options.SetMinValue(Expressions.ValueExpression.Get(Values.ValueNull.Instance));
        }
        else if (IsWord("NOMAXVALUE"))
        {
            options.SetMaxValue(Expressions.ValueExpression.Get(Values.ValueNull.Instance));
        }
        else if (IsWord("NOCYCLE"))
        {
            options.SetCycle(Schemas.Sequence.Cycle.NO_CYCLE);
        }
        else if (IsWord("NOCACHE"))
        {
            options.SetCacheSize(Expressions.ValueExpression.Get(Values.ValueBigint.Get(1)));
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Builds the command that adds a primary key, as both CREATE TABLE and ALTER TABLE do.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="tableName">the table</param>
    /// <param name="ifTableExists">whether the table was written with IF EXISTS</param>
    /// <param name="constraint">the parsed constraint</param>
    /// <returns>the command</returns>
    private Ddl.AlterTableAddConstraint CreateAddConstraintCommand(SessionLocal session,
        string tableName, bool ifTableExists, TableConstraintDefinition constraint)
    {
        Ddl.AlterTableAddConstraint key = new(
            session, CurrentSchema(session), constraint.Type,
            constraint.IfNotExists);
        key.SetTableName(tableName);
        key.SetIfTableExists(ifTableExists);
        if (constraint.Name is not null)
        {
            key.SetConstraintName(constraint.Name);
        }

        if (constraint.Comment is not null)
        {
            key.SetComment(constraint.Comment);
        }

        if (constraint.Type == CommandInterface.AlterTableAddConstraintPrimaryKey)
        {
            key.SetPrimaryKeyHash(constraint.Hash);
            List<Tables.IndexColumn> keyColumns = [];
            foreach (string columnName in constraint.Columns)
            {
                keyColumns.Add(new Tables.IndexColumn(columnName));
            }

            key.SetIndexColumns([.. keyColumns]);
            return key;
        }

        List<Tables.IndexColumn> refColumns = [];
        foreach (string columnName in constraint.Columns)
        {
            refColumns.Add(new Tables.IndexColumn(columnName));
        }

        key.SetIndexColumns([.. refColumns]);
        Schemas.Schema refSchema = constraint.RefTableSchema is null
            ? CurrentSchema(session)
            : session.GetDatabase().GetSchema(constraint.RefTableSchema);
        key.SetRefTableName(refSchema, constraint.RefTable ?? tableName);
        if (constraint.RefColumns is not null)
        {
            List<Tables.IndexColumn> refIndexColumns = [];
            foreach (string columnName in constraint.RefColumns)
            {
                refIndexColumns.Add(new Tables.IndexColumn(columnName));
            }

            key.SetRefIndexColumns([.. refIndexColumns]);
        }

        key.SetUpdateAction(constraint.UpdateAction);
        key.SetDeleteAction(constraint.DeleteAction);
        key.SetCheckExisting(constraint.CheckExisting);
        return key;
    }

    /// <summary>
    /// Parses a data type as it was written: a name from the type table with an
    /// optional precision and scale.
    /// </summary>
    /// <param name="typeName">the type, such as INT or VARCHAR(100)</param>
    /// <returns>the type info</returns>
    private TypeInfo ParseDataType(string typeName)
    {
        string name = typeName;
        long precision = -1;
        int scale = -1;
        int paren = typeName.IndexOf('(');
        if (paren >= 0)
        {
            name = typeName[..paren];
            string[] args = typeName[(paren + 1)..^1].Split(',');
            if (args.Length > 2)
            {
                throw DbException.GetSyntaxError(sql, 0, typeName);
            }

            if (!long.TryParse(args[0].Trim(), out precision))
            {
                throw DbException.GetSyntaxError(sql, 0, typeName);
            }

            if (args.Length > 1 && !int.TryParse(args[1].Trim(), out scale))
            {
                throw DbException.GetSyntaxError(sql, 0, typeName);
            }
        }

        DataType? dataType = DataType.GetTypeByName(name.Trim().ToUpperInvariant());
        if (dataType is null)
        {
            throw DbException.GetSyntaxError(sql, 0, typeName);
        }

        // DECIMAL and DEC are the same type as NUMERIC and say so: the extended parameters
        // of the type are what tell the two apart, and the reference keeps that difference
        // in them.
        string upperName = name.Trim().ToUpperInvariant();
        ExtTypeInfo? extTypeInfo = upperName is "DECIMAL" or "DEC"
            ? ExtTypeInfoNumeric.Decimal
            : null;

        return TypeInfo.GetTypeInfo(dataType.Type, precision, scale, extTypeInfo);
    }

    /// <summary>
    /// Builds the DROP TABLE command for a statement the parser read.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="statement">the parsed statement</param>
    /// <returns>the prepared statement</returns>
    /// <summary>
    /// Builds the INSERT command for a statement the parser read.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="statement">the parsed statement</param>
    /// <returns>the prepared statement</returns>
    private Prepared PrepareInsert(SessionLocal session, InsertStatement statement)
    {
        Schemas.Schema schema = statement.Schema is null
            ? CurrentSchema(session)
            : session.GetDatabase().GetSchema(statement.Schema);
        Tables.Table table = ReadTableOrView(session, schema, statement.Name);
        Dml.Insert insert = new(session);
        insert.SetSql(statement.Sql, null);
        insert.SetTable(table);
        Tables.Column[] tableColumns = table.GetColumns();
        Tables.Column[] columns;
        if (statement.Columns.Count == 0)
        {
            columns = tableColumns;
        }
        else
        {
            columns = new Tables.Column[statement.Columns.Count];
            for (int i = 0; i < statement.Columns.Count; i++)
            {
                string name = statement.Columns[i];
                Tables.Column? match = null;
                foreach (Tables.Column candidate in tableColumns)
                {
                    if (candidate.GetName().Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        match = candidate;
                        break;
                    }
                }

                columns[i] = match
                    ?? throw DbException.GetSyntaxError(sql, 0, name);
            }
        }

        insert.SetColumns(columns);
        foreach (List<Expressions.Expression> row in statement.Rows)
        {
            insert.AddRow([.. row]);
        }

        return insert;
    }

    private Prepared PrepareDropTable(SessionLocal session, DropTableStatement statement)
    {
        Ddl.DropTable drop = new(session);
        drop.SetSql(statement.Sql, null);
        drop.SetIfExists(statement.IfExists);
        drop.AddTable(CurrentSchema(session), statement.Name);
        return drop;
    }

    /// <summary>
    /// Builds the CREATE INDEX command for a statement the parser read.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="statement">the parsed statement</param>
    /// <returns>the prepared statement</returns>
    private Prepared PrepareCreateIndex(SessionLocal session, CreateIndexStatement statement)
    {
        Ddl.CreateIndex create = new(session, CurrentSchema(session));
        create.SetSql(statement.Sql, null);
        create.SetIfTableExists(statement.IfTableExists);
        create.SetIfNotExists(statement.IfNotExists);
        create.SetTableName(statement.TableName);
        if (statement.Name is not null)
        {
            create.SetIndexName(statement.Name);
        }

        create.SetIndexColumns([.. statement.Columns]);
        if (statement.Unique)
        {
            create.SetUnique(statement.NullsDistinct ?? NullsDistinct.Distinct, statement.Columns.Count);
        }

        return create;
    }

    /// <summary>
    /// Builds the DROP INDEX command for a statement the parser read.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="statement">the parsed statement</param>
    /// <returns>the prepared statement</returns>
    private Prepared PrepareDropIndex(SessionLocal session, DropIndexStatement statement)
    {
        Ddl.DropIndex drop = new(session, CurrentSchema(session));
        drop.SetSql(statement.Sql, null);
        drop.SetIfExists(statement.IfExists);
        drop.SetIndexName(statement.Name);
        return drop;
    }

    /// <summary>
    /// Builds the UPDATE command for a statement the parser read.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="statement">the parsed statement</param>
    /// <returns>the prepared statement</returns>
    private Prepared PrepareUpdate(SessionLocal session, UpdateStatement statement)
    {
        Schemas.Schema schema = statement.Schema is null
            ? CurrentSchema(session)
            : session.GetDatabase().GetSchema(statement.Schema);
        Tables.Table table = ReadTableOrView(session, schema, statement.Name);
        Dml.Update update = new(session);
        update.SetSql(statement.Sql, null);
        update.SetTableFilter(new Tables.TableFilter(
            session, table, statement.Alias ?? table.GetTableName(),
            rightsChecked: true, select: null, 0, null));
        Dml.SetClauseList assignments = new(table);
        foreach ((string name, Expression value) in statement.Assignments)
        {
            Tables.Column? match = null;
            foreach (Tables.Column candidate in table.GetColumns())
            {
                if (candidate.GetName().Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    match = candidate;
                    break;
                }
            }

            if (match is null)
            {
                throw DbException.GetSyntaxError(sql, 0, name);
            }

            assignments.AddSingle(match, null, value);
        }

        update.SetSetClauseList(assignments);
        if (statement.Where is not null)
        {
            update.SetCondition(statement.Where);
        }

        return update;
    }

    /// <summary>
    /// Builds the DELETE command for a statement the parser read.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="statement">the parsed statement</param>
    /// <returns>the prepared statement</returns>
    private Prepared PrepareDelete(SessionLocal session, DeleteStatement statement)
    {
        Schemas.Schema schema = statement.Schema is null
            ? CurrentSchema(session)
            : session.GetDatabase().GetSchema(statement.Schema);
        Tables.Table table = ReadTableOrView(session, schema, statement.Name);
        Dml.Delete delete = new(session);
        delete.SetSql(statement.Sql, null);
        delete.SetTableFilter(new Tables.TableFilter(
            session, table, statement.Alias ?? table.GetTableName(),
            rightsChecked: true, select: null, 0, null));
        if (statement.Where is not null)
        {
            delete.SetCondition(statement.Where);
        }

        return delete;
    }

    /// <summary>
    /// Resolves a table or view by name, telling an empty database from a missing table
    /// the way the reference does: no user tables at all is its own error code.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="schema">the schema</param>
    /// <param name="tableName">the table name</param>
    /// <returns>the table or view</returns>
    private Tables.Table ReadTableOrView(SessionLocal session, Schemas.Schema schema, string tableName)
    {
        Tables.Table? table = schema.FindTableOrView(session, tableName);
        if (table is not null)
        {
            return table;
        }

        // The reference asks the query scope for a table of a common table expression after
        // the schema has not answered.
        if (ctes.TryGetValue(tableName, out Tables.CTE? cte))
        {
            return cte;
        }

        if (session.GetDatabase().GetFirstUserTable() is null)
        {
            throw DbException.Get(Api.ErrorCode.TableOrViewNotFoundDatabaseEmpty1, tableName);
        }

        throw DbException.Get(Api.ErrorCode.TableOrViewNotFound1, tableName);
    }

    /// <summary>
    /// The schema a statement is read against.
    /// </summary>
    /// <remarks>
    /// The reference asks the session, and the session knows it because the database named one
    /// when it opened. A DDL statement prepared against the wrong schema would create its table
    /// somewhere nobody asked for, which is why this asks rather than picking.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <returns>the schema</returns>
    private Schemas.Schema CurrentSchema(SessionLocal session) => session.GetCurrentSchema();

    private Parser(string sql, CastDataProvider provider, List<Token> tokens)
        : this(sql, provider, tokens, null)
    {
    }

    /// <summary>
    /// Parses a statement.
    /// </summary>
    /// <param name="sql">the statement text</param>
    /// <param name="provider">where casts and the mode come from</param>
    /// <param name="parameters">written back with the parameters the statement names</param>
    /// <returns>the statement</returns>
    public static Statement Parse(string sql, CastDataProvider provider, BitList parameters)
    {
        Parser parser = new(sql, provider, Tokenizer.TokenizeForTest(sql, false, parameters, provider));
        CheckParameterIndex(parameters);
        Statement statement = parser.ReadStatement();
        statement.Sql = sql;
        return statement;
    }

    /// <summary>
    /// Checks the number of parameters the statement names.
    /// </summary>
    /// <remarks>
    /// The reference reads the length of the set of used parameters after the statement is
    /// tokenized and refuses a statement that names more parameters than the limit allows. The
    /// set grows with the statement, so the limit is the number of parameters and not the size
    /// of a buffer.
    /// </remarks>
    /// <param name="parameters">the parameters the statement names</param>
    private static void CheckParameterIndex(BitList parameters)
    {
        int length = parameters.Count;
        if (length > Engine.Constants.MaxParameterIndex)
        {
            throw DbException.GetInvalidValueException("parameter index", length);
        }
    }

    private Token Current => tokens[position];

    private int CurrentType => tokens[position].TokenType;

    private void Read()
    {
        if (position < tokens.Count - 1)
        {
            position++;
        }
    }

    private bool ReadIf(int tokenType)
    {
        if (CurrentType == tokenType)
        {
            Read();
            return true;
        }

        return false;
    }

    private void Expect(int tokenType, string what)
    {
        if (!ReadIf(tokenType))
        {
            throw DbException.GetSyntaxError(sql, Current.Start, what);
        }
    }

    /// <summary>
    /// Whether the current token is the given word. This works for a keyword (which carries its
    /// text) and for a plain identifier alike, which is what makes one method enough for both
    /// the tokenizer's words and the statement words.
    /// </summary>
    private bool IsWord(string word, bool consume = true)
    {
        string? name = Current.AsIdentifier;
        if (name is null || !name.Equals(word, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (consume)
        {
            Read();
        }

        return true;
    }

    private void ExpectWord(string word)
    {
        if (!IsWord(word))
        {
            throw DbException.GetSyntaxError(sql, Current.Start, word);
        }
    }

    private Statement ReadStatement()
    {
        if (IsWord("CREATE", consume: false))
        {
            Read();
            if (IsWord("TABLE"))
            {
                return ReadCreateTable();
            }

            if (IsWord("UNIQUE", consume: false) || IsWord("INDEX", consume: false))
            {
                return ReadCreateIndex();
            }

            if (IsWord("HASH", consume: false) || IsWord("SPATIAL", consume: false))
            {
                throw new TODOException(
                    "Parser.parseCreate: HASH and SPATIAL indexes are not ported");
            }

            throw DbException.GetSyntaxError(sql, Current.Start, "TABLE");
        }

        if (IsWord("ALTER", consume: false))
        {
            Read();
            ExpectWord("TABLE");
            return ReadAlterTable();
        }

        if (IsWord("DROP"))
        {
            if (IsWord("TABLE", consume: false))
            {
                return ReadDropTable();
            }

            if (IsWord("INDEX", consume: false))
            {
                return ReadDropIndex();
            }

            throw DbException.GetSyntaxError(sql, Current.Start, "TABLE");
        }

        if (IsWord("INSERT"))
        {
            return ReadInsert();
        }

        if (IsWord("UPDATE"))
        {
            return ReadUpdate();
        }

        if (IsWord("DELETE"))
        {
            return ReadDelete();
        }

        if (IsWord("SELECT"))
        {
            return ReadSelect();
        }

        throw DbException.GetSyntaxError(sql, Current.Start, "statement");
    }

    private CreateTableStatement ReadCreateTable()
    {
        CreateTableStatement statement = new();
        if (CurrentType == ParserUtil.If)
        {
            Read();
            _ = ReadIf(ParserUtil.Not);
            Expect(ParserUtil.Exists, "EXISTS");
            statement.IfNotExists = true;
        }

        statement.Name = ReadIdentifierWithSchemaName(out string? schemaName);
        statement.Schema = schemaName;
        Expect(Token.OpenParen, "(");
        do
        {
            // The reference tries every item as a table constraint first and falls back to a
            // column; the set of words that can start one is the same one parseTableConstraintIf
            // switches on. A column named like one of them is written quoted, which is another
            // token type, so the words are unambiguous here.
            if (CurrentType is ParserUtil.Constraint or ParserUtil.Primary or ParserUtil.Foreign)
            {
                statement.Constraints.Add(ReadTableConstraint(statement.Name));
                continue;
            }

            if (CurrentType is ParserUtil.Unique or ParserUtil.Check)
            {
                throw new TODOException(
                    "Parser.readCreateTable: table constraints other than the primary and foreign keys are not ported");
            }

            string name = ReadIdentifier();
            string type = ReadTypeName();
            bool notNull = false;
            bool primaryKey = false;
            bool primaryKeyHash = false;
            string? constraintName = null;

            // The reference's parseColumnForTable: NOT NULL, then the default or the
            // generated expression, then ON UPDATE, and the constraints after that.
            if (CurrentType == ParserUtil.Not)
            {
                Read();
                Expect(ParserUtil.Null, "NULL");
                notNull = true;
            }

            Ddl.SequenceOptions? identityOptions = null;
            bool identityGeneratedAlways = false;
            Expression? defaultExpression = null;
            Expression? generatedExpression = null;
            Expression? onUpdateExpression = null;
            bool defaultOnNull = false;
            if (IsWord("AS"))
            {
                generatedExpression = ReadExpression();
            }
            else if (IsWord("DEFAULT"))
            {
                if (IsWord("ON") && IsWord("NULL"))
                {
                    defaultOnNull = true;
                }
                else
                {
                    defaultExpression = ReadExpression();
                }
            }
            else if (IsWord("GENERATED"))
            {
                bool always = IsWord("ALWAYS");
                if (!always)
                {
                    ExpectWord("BY");
                    ExpectWord("DEFAULT");
                }

                ExpectWord("AS");
                if (IsWord("IDENTITY"))
                {
                    Ddl.SequenceOptions options = new();
                    if (ReadIf(Token.OpenParen))
                    {
                        ReadSequenceOptions(options, command: null, allowDataType: false, forAlterColumn: false);
                        Expect(Token.CloseParen, ")");
                    }

                    identityOptions = options;
                    identityGeneratedAlways = always;
                }
                else if (!always)
                {
                    throw DbException.GetSyntaxError(sql, Current.Start, "IDENTITY");
                }
                else
                {
                    generatedExpression = ReadExpression();
                }
            }

            if (generatedExpression is null && IsWord("ON") && IsWord("UPDATE"))
            {
                onUpdateExpression = ReadExpression();
            }

            string? refTableSchema = null;
            string? refTable = null;
            List<string>? refColumns = null;
            Constraints.ConstraintActionType refUpdateAction = Constraints.ConstraintActionType.NO_ACTION;
            Constraints.ConstraintActionType refDeleteAction = Constraints.ConstraintActionType.NO_ACTION;
            while (CurrentType is ParserUtil.Not or ParserUtil.Null or ParserUtil.Primary
                or ParserUtil.Key or ParserUtil.Unique or ParserUtil.Constraint)
            {
                switch (CurrentType)
                {
                    case ParserUtil.Not:
                        Read();
                        Expect(ParserUtil.Null, "NULL");
                        notNull = true;
                        break;
                    case ParserUtil.Primary:
                        Read();
                        Expect(ParserUtil.Key, "KEY");
                        primaryKey = true;
                        primaryKeyHash = IsWord("HASH");
                        break;
                    case ParserUtil.Unique:
                        throw new TODOException(
                            "Parser.readCreateTable: column constraints other than the primary key are not ported");
                    case ParserUtil.Constraint:
                        Read();
                        constraintName = ReadIdentifier();
                        if (CurrentType == ParserUtil.Primary)
                        {
                            Read();
                            Expect(ParserUtil.Key, "KEY");
                            primaryKey = true;
                            primaryKeyHash = IsWord("HASH");
                            break;
                        }

                        // A named REFERENCES, as the reference's readColumnConstraints reads it:
                        // the name was the constraint's, and what follows names the parent.
                        if (!IsWord("REFERENCES"))
                        {
                            throw DbException.GetSyntaxError(sql, Current.Start, "PRIMARY");
                        }

                        ReadReferences(statement.Name, out refTableSchema, out string namedRefTable,
                            out refColumns, out refUpdateAction, out refDeleteAction);
                        refTable = namedRefTable;
                        break;
                    default:
                        Read();
                        break;
                }
            }

            if (refTable is null && IsWord("REFERENCES"))
            {
                ReadReferences(statement.Name, out refTableSchema, out string plainRefTable,
                    out refColumns, out refUpdateAction, out refDeleteAction);
                refTable = plainRefTable;
            }

            statement.Columns.Add(new ColumnDefinition(
                name, type, notNull, primaryKey, constraintName, primaryKeyHash,
                identityOptions, identityGeneratedAlways, defaultExpression, generatedExpression,
                onUpdateExpression, defaultOnNull, refTableSchema, refTable, refColumns,
                refUpdateAction, refDeleteAction));
        }
        while (ReadIf(Token.Comma));

        Expect(Token.CloseParen, ")");
        return statement;
    }

    /// <summary>
    /// Reads a table-level constraint of a CREATE TABLE.
    /// </summary>
    /// <remarks>
    /// PRIMARY KEY and FOREIGN KEY run: the reference's parseTableConstraintIf also reads UNIQUE
    /// and CHECK, and the three kinds go to the same AlterTableAddConstraint whose UNIQUE and
    /// CHECK branches are not ported, so the parser refuses those two where the reference would
    /// accept the statements. The column lists are read as names; the tables resolve them when
    /// the command runs, as they do on the JVM. The table name is the statement's own: a
    /// REFERENCES without a table names the table being created, as it does on the JVM.
    /// </remarks>
    /// <param name="tableName">the name of the table being created or changed</param>
    /// <returns>the constraint</returns>
    private TableConstraintDefinition ReadTableConstraint(string tableName)
    {
        string? name = null;
        bool ifNotExists = false;
        string? comment = null;
        if (CurrentType == ParserUtil.Constraint)
        {
            Read();
            if (CurrentType == ParserUtil.If)
            {
                Read();
                Expect(ParserUtil.Not, "NOT");
                Expect(ParserUtil.Exists, "EXISTS");
                ifNotExists = true;
            }

            name = ReadIdentifier();
            if (IsWord("COMMENT"))
            {
                comment = ReadStringLiteralValue()
                    ?? throw DbException.GetSyntaxError(sql, Current.Start, "string");
            }
        }

        if (CurrentType == ParserUtil.Foreign)
        {
            Read();
            ExpectWord("KEY");
            Expect(Token.OpenParen, "(");
            List<string> columns = [];
            do
            {
                columns.Add(ReadIdentifier());
            }
            while (ReadIf(Token.Comma));

            Expect(Token.CloseParen, ")");
            if (IsWord("INDEX", consume: false))
            {
                throw new TODOException(
                    "Parser.readTableConstraint: the INDEX clause of a foreign key is not ported");
            }

            ExpectWord("REFERENCES");
            ReadReferences(tableName, out string? refSchemaName, out string refTable, out List<string>? refColumns,
                out Constraints.ConstraintActionType updateAction, out Constraints.ConstraintActionType deleteAction);
            bool checkExisting = true;
            if (IsWord("NOCHECK"))
            {
                checkExisting = false;
            }
            else
            {
                _ = IsWord("CHECK");
            }

            return new TableConstraintDefinition(name, ifNotExists, columns, Hash: false,
                CommandInterface.AlterTableAddConstraintReferential, comment,
                refSchemaName, refTable, refColumns, updateAction, deleteAction, checkExisting);
        }

        Expect(ParserUtil.Primary, "PRIMARY");
        Expect(ParserUtil.Key, "KEY");
        bool hash = IsWord("HASH");
        Expect(Token.OpenParen, "(");
        List<string> keyColumns = [];
        do
        {
            keyColumns.Add(ReadIdentifier());
        }
        while (ReadIf(Token.Comma));

        Expect(Token.CloseParen, ")");
        if (IsWord("INDEX", consume: false))
        {
            throw new TODOException(
                "Parser.readTableConstraint: the INDEX clause of a primary key is not ported");
        }

        return new TableConstraintDefinition(name, ifNotExists, keyColumns, hash,
            CommandInterface.AlterTableAddConstraintPrimaryKey, comment);
    }

    /// <summary>
    /// Reads what REFERENCES names and what happens to the rows that point at the row: the
    /// table, its columns, and the ON UPDATE and ON DELETE actions.
    /// </summary>
    /// <remarks>
    /// The reference's parseReferences. A table in parentheses is the statement's own table;
    /// NOT DEFERRABLE and DEFERRABLE are read and ignored, as they are on the JVM.
    /// </remarks>
    /// <param name="tableName">the name of the statement's own table</param>
    /// <param name="refSchemaName">the schema of the referenced table, or null</param>
    /// <param name="refTable">the referenced table</param>
    /// <param name="refColumns">the referenced columns, or null for the primary key</param>
    /// <param name="updateAction">what an update of the referenced row does</param>
    /// <param name="deleteAction">what a delete of the referenced row does</param>
    private void ReadReferences(
        string tableName,
        out string? refSchemaName,
        out string refTable,
        out List<string>? refColumns,
        out Constraints.ConstraintActionType updateAction,
        out Constraints.ConstraintActionType deleteAction)
    {
        if (ReadIf(Token.OpenParen))
        {
            refSchemaName = null;
            refTable = tableName;
            refColumns = [];
            do
            {
                refColumns.Add(ReadIdentifier());
            }
            while (ReadIf(Token.Comma));

            Expect(Token.CloseParen, ")");
        }
        else
        {
            refTable = ReadIdentifierWithSchemaName(out refSchemaName);
            refColumns = null;
            if (ReadIf(Token.OpenParen))
            {
                refColumns = [];
                do
                {
                    refColumns.Add(ReadIdentifier());
                }
                while (ReadIf(Token.Comma));

                Expect(Token.CloseParen, ")");
            }
        }

        if (IsWord("INDEX", consume: false))
        {
            throw new TODOException(
                "Parser.readReferences: the INDEX clause of a foreign key is not ported");
        }

        updateAction = Constraints.ConstraintActionType.NO_ACTION;
        deleteAction = Constraints.ConstraintActionType.NO_ACTION;
        if (AtOn("UPDATE"))
        {
            Read();
            Read();
            updateAction = ReadReferentialAction();
            if (AtOn("DELETE"))
            {
                Read();
                Read();
                deleteAction = ReadReferentialAction();
            }
        }
        else if (AtOn("DELETE"))
        {
            Read();
            Read();
            deleteAction = ReadReferentialAction();
            if (AtOn("UPDATE"))
            {
                Read();
                Read();
                updateAction = ReadReferentialAction();
            }
        }

        if (IsWord("NOT", consume: false) && NextIs("DEFERRABLE"))
        {
            Read();
            Read();
        }
        else
        {
            _ = IsWord("DEFERRABLE");
        }
    }

    /// <summary>
    /// Whether ON and the given word stand next.
    /// </summary>
    /// <param name="word">the word after ON</param>
    /// <returns>true when they do</returns>
    private bool AtOn(string word) => IsWord("ON", consume: false) && NextIs(word);

    /// <summary>
    /// Reads what a delete or an update of the referenced row does to the rows that point at
    /// it, as the reference's parseAction does.
    /// </summary>
    /// <returns>the action</returns>
    private Constraints.ConstraintActionType ReadReferentialAction()
    {
        if (IsWord("CASCADE"))
        {
            return Constraints.ConstraintActionType.CASCADE;
        }

        if (IsWord("RESTRICT"))
        {
            return Constraints.ConstraintActionType.RESTRICT;
        }

        if (IsWord("NO", consume: false) && NextIs("ACTION"))
        {
            Read();
            Read();
            return Constraints.ConstraintActionType.NO_ACTION;
        }

        ExpectWord("SET");
        if (IsWord("NULL"))
        {
            return Constraints.ConstraintActionType.SET_NULL;
        }

        ExpectWord("DEFAULT");
        return Constraints.ConstraintActionType.SET_DEFAULT;
    }

    /// <summary>
    /// Reads ALTER TABLE.
    /// </summary>
    /// <remarks>
    /// Only the constraint forms run: ADD of a table-level constraint and DROP CONSTRAINT. The
    /// reference's parseAlterTable also has ADD COLUMN, DROP COLUMN, SET, RENAME, ALTER COLUMN
    /// and DROP PRIMARY KEY (which resolves the table while reading and turns into a DROP INDEX);
    /// each of those is a valid statement whose command is not ported, so each refuses by name
    /// instead of failing as a syntax error.
    /// </remarks>
    /// <returns>the statement</returns>
    private AlterTableStatement ReadAlterTable()
    {
        AlterTableStatement statement = new();
        if (CurrentType == ParserUtil.If)
        {
            Read();
            Expect(ParserUtil.Exists, "EXISTS");
            statement.IfTableExists = true;
        }

        statement.Name = ReadIdentifier();
        if (IsWord("ALTER"))
        {
            Read();
            _ = IsWord("COLUMN");
            _ = ReadIfExists(false);
            string columnName = ReadIdentifier();
            if (IsWord("DROP", consume: false) && NextIs("IDENTITY"))
            {
                Read();
                Read();
                statement.AlterColumn = new AlterTableAlterColumnDefinition
                {
                    ColumnName = columnName,
                    DropIdentity = true,
                };
                return statement;
            }

            bool? always = null;
            if (IsWord("SET", consume: false) && NextIs("GENERATED"))
            {
                Read();
                Read();
                if (IsWord("ALWAYS"))
                {
                    always = true;
                }
                else
                {
                    ExpectWord("BY");
                    ExpectWord("DEFAULT");
                    always = false;
                }
            }

            Ddl.SequenceOptions options = new();
            bool hasOptions = ReadSequenceOptions(
                options, command: null, allowDataType: false, forAlterColumn: true);
            if (!hasOptions && always is null)
            {
                // The other changes of a column — its type, its default, its nullability, its
                // name — are not ported yet, and the port says so the way the database says
                // that it does not read a statement: a syntax error. The list of what is
                // missing is in MIGRATION_STATUS.md and in issue #13 of the repository.
                throw DbException.GetSyntaxError(sql, Current.Start);
            }

            statement.AlterColumn = new AlterTableAlterColumnDefinition
            {
                ColumnName = columnName,
                Always = always,
                Options = options,
            };
            return statement;
        }

        if (IsWord("ADD"))
        {
            if (CurrentType is ParserUtil.Constraint or ParserUtil.Primary or ParserUtil.Foreign)
            {
                statement.AddConstraint = ReadTableConstraint(statement.Name);
                return statement;
            }

            // Adding a column is not ported yet; the port refuses it the way the database
            // refuses a statement it does not read.
            throw DbException.GetSyntaxError(sql, Current.Start);
        }

        if (IsWord("DROP"))
        {
            if (IsWord("CONSTRAINT"))
            {
                bool ifExists = ReadIfExists(false);
                statement.DropConstraintName = ReadIdentifier();
                statement.DropConstraintIfExists = ReadIfExists(ifExists);
                statement.DropAction = IsWord("CASCADE")
                    ? ConstraintActionType.CASCADE
                    : IsWord("RESTRICT") ? ConstraintActionType.RESTRICT : null;
                return statement;
            }

            if (CurrentType == ParserUtil.Primary)
            {
                Read();
                ExpectWord("KEY");
                statement.DropPrimaryKey = true;
                return statement;
            }

            // Dropping a column is not ported yet; the port refuses it the way the database
            // refuses a statement it does not read.
            throw DbException.GetSyntaxError(sql, Current.Start);
        }

        // The other changes of a table are not ported yet; the port refuses them the way the
        // database refuses a statement it does not read.
        throw DbException.GetSyntaxError(sql, Current.Start);
    }

    /// <summary>
    /// Reads the optional IF EXISTS words.
    /// </summary>
    /// <param name="ifExists">the value to keep when they are not written</param>
    /// <returns>whether IF EXISTS was written or the value was already true</returns>
    private bool ReadIfExists(bool ifExists)
    {
        if (CurrentType == ParserUtil.If)
        {
            Read();
            Expect(ParserUtil.Exists, "EXISTS");
            ifExists = true;
        }

        return ifExists;
    }

    /// <summary>
    /// Reads the IF NOT EXISTS of a statement, as the reference's readIfNotExists.
    /// </summary>
    /// <returns>true if it was written</returns>
    private bool ReadIfNotExists()
    {
        if (CurrentType == ParserUtil.If)
        {
            Read();
            Expect(ParserUtil.Not, "NOT");
            Expect(ParserUtil.Exists, "EXISTS");
            return true;
        }

        return false;
    }

    private DropTableStatement ReadDropTable()
    {
        ExpectWord("TABLE");
        bool ifExists = false;
        if (CurrentType == ParserUtil.If)
        {
            Read();
            Expect(ParserUtil.Exists, "EXISTS");
            ifExists = true;
        }

        return new DropTableStatement { Name = ReadIdentifier(), IfExists = ifExists };
    }

    /// <summary>
    /// Reads CREATE [UNIQUE] INDEX.
    /// </summary>
    /// <remarks>
    /// The name is optional: without one the reference generates it when the index is created.
    /// HASH and SPATIAL are only keywords in compatibility modes and are refused by name. The
    /// reference reads them with <c>readIfCompat</c>, which this parser does not have. The
    /// INCLUDE clause and the PostgreSQL <c>USING</c> keywords other than BTREE are valid
    /// statements whose commands are not ported.
    /// </remarks>
    /// <returns>the statement</returns>
    private CreateIndexStatement ReadCreateIndex()
    {
        CreateIndexStatement statement = new();
        if (IsWord("UNIQUE"))
        {
            statement.Unique = true;
            statement.NullsDistinct = ReadNullsDistinct(NullsDistinct.Distinct);
        }

        ExpectWord("INDEX");
        if (!IsWord("ON", consume: false))
        {
            if (CurrentType == ParserUtil.If)
            {
                Read();
                Expect(ParserUtil.Not, "NOT");
                Expect(ParserUtil.Exists, "EXISTS");
                statement.IfNotExists = true;
            }

            statement.Name = ReadIdentifier();
        }

        ExpectWord("ON");
        statement.TableName = ReadIdentifier();
        if (IsWord("COMMENT", consume: false))
        {
            throw new TODOException("Parser.parseCreate: CREATE INDEX COMMENT is not ported");
        }

        if (!ReadIf(Token.OpenParen))
        {
            ExpectWord("USING");
            if (!IsWord("BTREE"))
            {
                throw new TODOException(
                    "Parser.parseCreate: CREATE INDEX USING other than BTREE is not ported");
            }

            Expect(Token.OpenParen, "(");
        }

        do
        {
            string columnName = ReadIdentifier();
            statement.Columns.Add(new IndexColumn(columnName, ReadSortType()));
        }
        while (ReadIf(Token.Comma));

        Expect(Token.CloseParen, ")");
        if (statement.Unique && IsWord("INCLUDE", consume: false))
        {
            throw new TODOException("Parser.parseCreate: CREATE INDEX INCLUDE is not ported");
        }

        return statement;
    }

    /// <summary>
    /// Reads the NULLS DISTINCT / NULLS NOT DISTINCT / NULLS ALL DISTINCT clause.
    /// </summary>
    /// <param name="defaultDistinct">the rule to keep when NULLS is not written</param>
    /// <returns>the rule</returns>
    private NullsDistinct ReadNullsDistinct(NullsDistinct defaultDistinct)
    {
        if (!IsWord("NULLS"))
        {
            return defaultDistinct;
        }

        if (IsWord("DISTINCT"))
        {
            return NullsDistinct.Distinct;
        }

        if (IsWord("NOT"))
        {
            ExpectWord("DISTINCT");
            return NullsDistinct.NotDistinct;
        }

        if (IsWord("ALL"))
        {
            ExpectWord("DISTINCT");
            return NullsDistinct.AllDistinct;
        }

        throw DbException.GetSyntaxError(sql, Current.Start);
    }

    /// <summary>
    /// Reads the sort order and NULLS placement of one index column.
    /// </summary>
    /// <returns>the sort type</returns>
    private int ReadSortType()
    {
        int sortType = !IsWord("ASC") && IsWord("DESC")
            ? Result.SortOrder.Descending
            : Result.SortOrder.Ascending;
        if (IsWord("NULLS"))
        {
            if (IsWord("FIRST"))
            {
                sortType |= Result.SortOrder.NullsFirst;
            }
            else
            {
                ExpectWord("LAST");
                sortType |= Result.SortOrder.NullsLast;
            }
        }

        return sortType;
    }

    /// <summary>
    /// Reads DROP INDEX.
    /// </summary>
    /// <remarks>
    /// The MySQL form <c>DROP INDEX name ON table</c> is accepted and the table is ignored;
    /// the index name is what identifies it.
    /// </remarks>
    /// <returns>the statement</returns>
    private DropIndexStatement ReadDropIndex()
    {
        ExpectWord("INDEX");
        bool ifExists = ReadIfExists(false);
        string name = ReadIdentifier();
        ifExists = ReadIfExists(ifExists);
        if (IsWord("ON"))
        {
            _ = ReadIdentifier();
        }

        return new DropIndexStatement { Name = name, IfExists = ifExists };
    }

    private InsertStatement ReadInsert()
    {
        ExpectWord("INTO");
        InsertStatement statement = new() { Name = ReadIdentifierWithSchemaName(out string? insertSchema) };
        statement.Schema = insertSchema;
        if (CurrentType == Token.OpenParen)
        {
            Read();
            do
            {
                statement.Columns.Add(ReadIdentifier());
            }
            while (ReadIf(Token.Comma));

            Expect(Token.CloseParen, ")");
        }

        ExpectWord("VALUES");
        do
        {
            Expect(Token.OpenParen, "(");
            List<Expression> row = [];
            do
            {
                row.Add(ReadExpression());
            }
            while (ReadIf(Token.Comma));

            Expect(Token.CloseParen, ")");
            statement.Rows.Add(row);
        }
        while (ReadIf(Token.Comma));

        return statement;
    }

    private UpdateStatement ReadUpdate()
    {
        UpdateStatement statement = new() { Name = ReadIdentifierWithSchemaName(out string? updateSchema) };
        statement.Schema = updateSchema;
        statement.Alias = ReadFromAlias();
        ExpectWord("SET");
        do
        {
            string column = ReadIdentifier();
            Expect(Token.Equal, "=");
            statement.Assignments.Add((column, ReadExpression()));
        }
        while (ReadIf(Token.Comma));

        statement.Where = ReadWhere();
        return statement;
    }

    private DeleteStatement ReadDelete()
    {
        ExpectWord("FROM");
        DeleteStatement statement = new() { Name = ReadIdentifierWithSchemaName(out string? deleteSchema) };
        statement.Schema = deleteSchema;
        statement.Alias = ReadFromAlias();
        statement.Where = ReadWhere();
        return statement;
    }

    private SelectStatement ReadSelect()
    {
        SelectStatement statement = new();
        do
        {
            if (ReadIf(Token.Asterisk))
            {
                statement.Items.Add(new SelectItem(null, false));
                continue;
            }

            if (IsWord("COUNT", consume: false)
                && tokens[position + 1].TokenType == Token.OpenParen
                && tokens[position + 2].TokenType == Token.Asterisk)
            {
                Read();
                Read();
                Read();
                Expect(Token.CloseParen, ")");
                statement.Items.Add(new SelectItem(null, true));
                continue;
            }

            statement.Items.Add(new SelectItem(ReadIdentifier(), false));
        }
        while (ReadIf(Token.Comma));

        ExpectWord("FROM");
        statement.Name = ReadIdentifier();
        statement.Where = ReadWhere();

        if (CurrentType == ParserUtil.Order)
        {
            Read();
            ExpectWord("BY");
            do
            {
                string column = ReadIdentifier();
                bool ascending = true;
                if (IsWord("DESC", consume: false))
                {
                    Read();
                    ascending = false;
                }
                else if (IsWord("ASC", consume: false))
                {
                    Read();
                }

                statement.OrderBy.Add((column, ascending));
            }
            while (ReadIf(Token.Comma));
        }

        return statement;
    }

    private Expression? ReadWhere()
    {
        if (CurrentType != ParserUtil.Where)
        {
            return null;
        }

        Read();
        return ReadExpression();
    }

    /// <summary>
    /// Reads an expression: the reference's precedence, as far as this layer needs it. OR binds
    /// loosest, then AND, then a comparison, then a term.
    /// </summary>
    /// <remarks>
    /// One chain, as in the reference (issue #12 C1): the statement layer used to carry a second,
    /// weaker copy that stopped at IS NULL, so a WHERE with LIKE / BETWEEN / IN after an UPDATE or
    /// DELETE was a syntax error the query layer did not have. Passing no aggregate select keeps
    /// the statement callers on the non-aggregate arms.
    /// </remarks>
    public Expression ReadExpression() => ReadQueryOr();

    private string ReadIdentifier()
    {
        string? name = Current.AsIdentifier;
        if (name is null)
        {
            throw DbException.GetSyntaxError(sql, Current.Start, "identifier");
        }

        Read();
        return name;
    }

    /// <summary>
    /// Reads a type as the name it was written with, keeping the precision and scale that follow.
    /// A column's type is a name until the statement runs, and the name is what the user wrote.
    /// </summary>
    private string ReadTypeName()
    {
        string name = ReadIdentifier();
        string suffix = string.Empty;
        while (CurrentType == Token.OpenParen)
        {
            Read();
            StringBuilder arguments = new();
            while (true)
            {
                Value? value = Current.GetValue(provider);
                _ = arguments.Append(value is null ? ReadIdentifier() : value.GetString());

                // A literal was read rather than an identifier, and ReadIdentifier has already
                // consumed its token; this is the other half of that pair.
                if (value is not null)
                {
                    Read();
                }

                if (!ReadIf(Token.Comma))
                {
                    break;
                }

                // "10, 2" and not "10,2": this string is what a DDL statement prints back, and
                // the reference writes the separator with a space.
                _ = arguments.Append(", ");
            }

            Expect(Token.CloseParen, ")");
            suffix += "(" + arguments + ")";
        }

        if (name.Equals("DOUBLE", StringComparison.OrdinalIgnoreCase) && IsWord("PRECISION", consume: false))
        {
            Read();
            name = "DOUBLE PRECISION";
        }
        else if (name.Equals("TIMESTAMP", StringComparison.OrdinalIgnoreCase) && IsWord("WITH", consume: false))
        {
            Read();
            if (IsWord("TIME", consume: false))
            {
                Read();
                if (IsWord("ZONE", consume: false))
                {
                    Read();
                    name = "TIMESTAMP WITH TIME ZONE";
                }
            }
        }

        return suffix.Length == 0 ? name : name + suffix;
    }
}
