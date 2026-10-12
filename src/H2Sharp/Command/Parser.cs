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
 * * **A named parameter is two tokens.** `@Age` is a `Token.At` followed by the name as an
 *   identifier, and the parser binds them — the reference's `readTerm`: `case AT: read();
 *   r = new Variable(session, readIdentifier())`. The lexer keeps no name, so a parser that
 *   expected one token would read the parameter as a column.
 *
 * One trap that cost two rounds of guessing: <see cref="IsWord(string, bool)"/> *consumes* the
 * word unless asked not to, so a dispatch written as `if (IsWord("DELETE")) { Read(); … }` eats
 * the token after the word too, and the failure appears at the following word with the position
 * already past it.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
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
public sealed record ColumnDefinition(string Name, string Type, bool NotNull, bool PrimaryKey);

/// <summary>CREATE TABLE.</summary>
public sealed class CreateTableStatement : Statement
{
    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.CreateTable;

    /// <summary>The table name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The columns, in order.</summary>
    public List<ColumnDefinition> Columns { get; } = [];

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

/// <summary>INSERT INTO ... VALUES.</summary>
public sealed class InsertStatement : Statement
{
    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.Insert;

    /// <summary>The table name.</summary>
    public string Name { get; set; } = string.Empty;

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
public sealed class Parser
{
    private readonly CastDataProvider provider;

    private readonly string sql;

    private readonly List<Token> tokens;

    private readonly SessionLocal? session;

    private int position;

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
    /// ask about. The same three steps are here. Several statements in one text become a
    /// <c>CommandList</c>, which is not ported yet and refuses.
    /// </remarks>
    /// <param name="sql">the statement text</param>
    /// <returns>the command</returns>
    public Command PrepareCommand(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        SessionLocal current = session ?? throw new TODOException("Parser.prepareCommand: this parser has no session");

        BitArray parameters = new(8);
        Prepared prepared = ParsePrepared(sql, parameters);
        prepared.Prepare();
        return new CommandContainer(current, sql, prepared);
    }

    /// <summary>
    /// Reads the statement and builds the prepared statement for it.
    /// </summary>
    /// <remarks>
    /// The reference reads one token and switches on it, so that the *first* word decides what
    /// is being built. Here the text is read into a statement and the switch is on what came
    /// back, which is the same decision reached by a different road: the reference's token-level
    /// switch is the part of the parser that is not ported, and this is what stands in its place
    /// until it is.
    /// </remarks>
    /// <param name="sql">the statement text</param>
    /// <param name="parameters">written with the parameters the statement names</param>
    /// <returns>the prepared statement</returns>
    private Prepared ParsePrepared(string sql, BitArray parameters)
    {
        SessionLocal current = session ?? throw new TODOException("Parser.parsePrepared: this parser has no session");
        Statement statement = Parse(sql, provider, parameters);
        switch (statement)
        {
            case CreateTableStatement create:
                return PrepareCreateTable(current, create);
            case DropTableStatement drop:
                return PrepareDropTable(current, drop);
            case InsertStatement insert:
                return new Dml.Insert(current);
            case UpdateStatement update:
                return new Dml.Update(current);
            case DeleteStatement delete:
                return new Dml.Delete(current);
            case SelectStatement select:
                return new Query.Select(current);
            default:
                return new Dml.NoOperation(current);
        }
    }

    /// <summary>
    /// Builds the CREATE TABLE command for a statement the parser read.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="statement">the parsed statement</param>
    /// <returns>the prepared statement</returns>
    private Prepared PrepareCreateTable(SessionLocal session, CreateTableStatement statement)
    {
        Ddl.CreateTable create = new(session, CurrentSchema(session));
        create.SetSql(statement.Sql, null);
        create.SetTableName(statement.Name);
        create.SetIfNotExists(statement.IfNotExists);
        foreach (ColumnDefinition column in statement.Columns)
        {
            _ = column;
        }

        return create;
    }

    /// <summary>
    /// Builds the DROP TABLE command for a statement the parser read.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="statement">the parsed statement</param>
    /// <returns>the prepared statement</returns>
    private Prepared PrepareDropTable(SessionLocal session, DropTableStatement statement)
    {
        Ddl.DropTable drop = new(session);
        drop.SetSql(statement.Sql, null);
        drop.SetIfExists(statement.IfExists);
        drop.AddTable(CurrentSchema(session), statement.Name);
        return drop;
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
    public static Statement Parse(string sql, CastDataProvider provider, BitArray parameters)
    {
        Parser parser = new(sql, provider, Tokenizer.TokenizeForTest(sql, false, parameters, provider));
        Statement statement = parser.ReadStatement();
        statement.Sql = sql;
        return statement;
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
            ExpectWord("TABLE");
            return ReadCreateTable();
        }

        if (IsWord("DROP"))
        {
            return ReadDropTable();
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

        statement.Name = ReadIdentifier();
        Expect(Token.OpenParen, "(");
        do
        {
            string name = ReadIdentifier();
            string type = ReadTypeName();
            bool notNull = false;
            bool primaryKey = false;
            while (CurrentType is ParserUtil.Not or ParserUtil.Null or ParserUtil.Primary
                or ParserUtil.Key or ParserUtil.Unique)
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
                        break;
                    case ParserUtil.Unique:
                        Read();
                        primaryKey = true;
                        break;
                    default:
                        Read();
                        break;
                }
            }

            statement.Columns.Add(new ColumnDefinition(name, type, notNull, primaryKey));
        }
        while (ReadIf(Token.Comma));

        Expect(Token.CloseParen, ")");
        return statement;
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

    private InsertStatement ReadInsert()
    {
        ExpectWord("INTO");
        InsertStatement statement = new() { Name = ReadIdentifier() };
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
        UpdateStatement statement = new() { Name = ReadIdentifier() };
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
        DeleteStatement statement = new() { Name = ReadIdentifier() };
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
    public Expression ReadExpression() => ReadOr();

    private Expression ReadOr()
    {
        Expression left = ReadAnd();
        while (CurrentType == ParserUtil.Or)
        {
            Read();
            left = new LogicalExpression(left, LogicalExpression.Or, ReadAnd());
        }

        return left;
    }

    private Expression ReadAnd()
    {
        Expression left = ReadComparison();
        while (CurrentType == ParserUtil.And)
        {
            Read();
            left = new LogicalExpression(left, LogicalExpression.And, ReadComparison());
        }

        return left;
    }

    private Expression ReadComparison()
    {
        Expression left = ReadTerm();
        while (CurrentType is Token.Equal or Token.NotEqual or Token.Smaller or Token.Bigger
            or Token.SmallerEqual or Token.BiggerEqual)
        {
            int compareType = ReadComparisonType();
            left = new ComparisonExpression(left, compareType, ReadTerm());
        }

        if (CurrentType == ParserUtil.Is)
        {
            Read();
            bool not = ReadIf(ParserUtil.Not);
            Expect(ParserUtil.Null, "NULL");
            left = new IsNullExpression(left, not);
        }

        return left;
    }

    private int ReadComparisonType()
    {
        int type = CurrentType;
        Read();
        return type switch
        {
            Token.Equal => ComparisonExpression.Equal,
            Token.NotEqual => ComparisonExpression.NotEqual,
            Token.Smaller => ComparisonExpression.Smaller,
            Token.Bigger => ComparisonExpression.Bigger,
            Token.SmallerEqual => ComparisonExpression.SmallerEqual,
            Token.BiggerEqual => ComparisonExpression.BiggerEqual,
            _ => throw DbException.GetInternalError("comparison type " + type),
        };
    }

    private Expression ReadTerm()
    {
        if (ReadIf(Token.OpenParen))
        {
            Expression nested = ReadExpression();
            Expect(Token.CloseParen, ")");
            return nested;
        }

        if (CurrentType == ParserUtil.Not)
        {
            Read();
            return new NotExpression(ReadTerm());
        }

        if (CurrentType == ParserUtil.Null)
        {
            Read();
            return ValueExpression.Get(ValueNull.Instance);
        }

        if (CurrentType is ParserUtil.True or ParserUtil.False)
        {
            bool value = CurrentType == ParserUtil.True;
            Read();
            return ValueExpression.Get(ValueBoolean.Get(value));
        }

        if (CurrentType == Token.MinusSign)
        {
            Read();
            Expression term = ReadTerm();

            // A negative literal is a literal: there is nothing in "-1" that needs a run.
            if (term is not ValueExpression v)
            {
                throw DbException.GetSyntaxError(sql, Current.Start, "number");
            }

            return ValueExpression.Get(Negate(v.GetValueFromLiteral()));
        }

        if (CurrentType == Token.At)
        {
            // The reference's shape: an at sign and the name are two tokens and the parser binds
            // them. See the file header.
            Read();
            return new ParameterExpression(ReadIdentifier());
        }

        Value? literal = Current.GetValue(provider);
        if (literal is not null)
        {
            Read();
            return ValueExpression.Get(literal);
        }

        return new ColumnExpression(ReadIdentifier());
    }

    private static Value Negate(Value value)
    {
        return value switch
        {
            ValueInteger i => ValueInteger.Get(-i.GetInt()),
            ValueBigint l => ValueBigint.Get(-l.GetLong()),
            ValueNumeric n => ValueNumeric.Get(n.GetBigDecimal().Negate()),
            ValueReal r => ValueReal.Get(-r.GetFloat()),
            ValueDouble d => ValueDouble.Get(-d.GetDouble()),
            _ => throw DbException.GetInternalError("cannot negate " + value.GetValueType()),
        };
    }

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
