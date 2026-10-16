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

    /// <summary>
    /// The parameters read so far, in the order their placeholders appear.
    /// </summary>
    private readonly List<Expressions.Parameter> parameterList = [];

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
        List<Token> tokens = Tokenizer.TokenizeForTest(sql, false, parameters, provider);
        // A trailing semicolon is not a second statement; anything before it is a list,
        // and a list answers only its first statement while running the rest for effect.
        // That is CommandList, and it waits for its own slice: shared parameter numbering
        // across the statements, one visible result, cached as one command.
        int end = tokens.Count;
        while (end > 0 && tokens[end - 1].TokenType is Token.Semicolon or Token.EndOfInput)
        {
            end--;
        }

        if (tokens.Take(end).Any(token => token.TokenType == Token.Semicolon))
        {
            throw new TODOException("Parser.parsePrepared: multiple statements (CommandList) are not ported");
        }

        Parser parser = new(sql, provider, tokens, current);
        Prepared prepared;
        if (tokens.Count > 0 && IsSelectFirst(tokens[0]))
        {
            // The reference switches on the first token; the statement words arrive as
            // identifiers, so the peek is by text, unquoted. A flat SELECT builds the engine
            // operator directly, everything else goes through the parse-only statements below.
            prepared = parser.ParseQuerySelect();
        }
        else
        {
            Statement statement = parser.ReadStatement();
            statement.Sql = sql;
            prepared = statement switch
            {
                CreateTableStatement create => PrepareCreateTable(current, create),
                DropTableStatement drop => PrepareDropTable(current, drop),
                InsertStatement insert => PrepareInsert(current, insert),
                UpdateStatement update => PrepareUpdate(current, update),
                DeleteStatement delete => PrepareDelete(current, delete),
                SelectStatement select => new Query.Select(current),
                _ => new Dml.NoOperation(current),
            };
        }

        prepared.SetParameterList(parser.parameterList.Count == 0 ? null : parser.parameterList);
        return prepared;
    }

    /// <summary>
    /// Whether the token opens a SELECT statement: the word SELECT, written plain.
    /// </summary>
    private static bool IsSelectFirst(Token token)
        => !token.IsQuoted && "SELECT".Equals(token.AsIdentifier, StringComparison.OrdinalIgnoreCase);

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
    private Query.Select ParseQuerySelect()
    {
        SessionLocal current = session ?? throw new TODOException("Parser.parseQuerySelect: this parser has no session");
        ExpectWord("SELECT");
        Query.Select select = new(current);
        select.SetSql(sql, null);
        if (IsWord("DISTINCT"))
        {
            select.SetDistinct();
        }

        bool star = ReadIf(Token.Asterisk);
        List<Expressions.Expression> selectItems = [];
        if (!star)
        {
            do
            {
                Expressions.Expression item;
                if (IsAggregateCall(out Expressions.Aggregate.AggregateType aggregateType))
                {
                    item = ReadQueryAggregate(select, aggregateType);
                }
                else if (IsWord("CAST", consume: false))
                {
                    item = ReadQueryCast();
                }
                else
                {
                    item = ReadQueryOr(select);
                }

                string? alias = null;
                if (IsWord("AS"))
                {
                    alias = ReadIdentifier();
                }
                else if (IsNameToken() && !IsClauseWord(Current.AsIdentifier!))
                {
                    throw new TODOException("Parser.parseQuerySelect: bare column aliases are not ported");
                }

                if (alias is not null)
                {
                    item = new Expressions.Alias(item, alias, aliasColumnName: true);
                }

                selectItems.Add(item);
            }
            while (ReadIf(Token.Comma));
        }

        Tables.Table? fromTable = null;
        Tables.TableFilter filter;
        if (IsWord("FROM"))
        {
            string tableName = ReadIdentifier(); string? filterAlias = null;
            if (IsNameToken() && !IsClauseWord(Current.AsIdentifier!))
            {
                filterAlias = ReadIdentifier();
            }

            Schemas.Schema schema = current.GetCurrentSchema();
            fromTable = schema.GetTableOrView(current, tableName);
            filter = new(current, fromTable, filterAlias ?? tableName, rightsChecked: true, select, 0, null);
        }
        else
        {
            // Select without FROM: the reference reads an expression select with an
            // implicit DUAL table, one empty row to evaluate the expressions on.
            filter = new(
                current, new Tables.DualTable(current.GetDatabase()), null,
                rightsChecked: true, select, 0, null);
        }

        select.AddTableFilter(filter, isTop: true);

        List<Expressions.Expression> selectList = [];
        if (star)
        {
            if (fromTable is null)
            {
                throw DbException.GetSyntaxError(sql, Current.Start, "statement");
            }

            // The reference expands the star in init; there is no star expression here to
            // carry there, so the columns land directly.
            foreach (Tables.Column column in fromTable.GetColumns())
            {
                selectList.Add(new Expressions.ExpressionColumn(column, filter, 0));
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
                    item.expression = new Expressions.ExpressionColumn(ReadQueryColumnName(out string? orderTable), orderTable, 0);
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
        bool hasOffsetOrFetch = false;
        if (CurrentType == ParserUtil.Offset)
        {
            Read();
            hasOffsetOrFetch = true;
            select.SetOffset(ReadQueryLimitValue());
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
                select.SetLimit(1);
            }
            else
            {
                select.SetLimit(ReadQueryLimitValue());
                if (IsWord("PERCENT"))
                {
                    throw new TODOException("Parser.parseQuerySelect: FETCH PERCENT is not ported");
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
            long limit = ReadQueryLimitValue();
            if (CurrentType == ParserUtil.Offset)
            {
                Read();
                select.SetOffset(ReadQueryLimitValue());
            }
            else if (ReadIf(Token.Comma))
            {
                // MySQL: [offset, ] rowcount.
                select.SetOffset(limit);
                limit = ReadQueryLimitValue();
            }

            select.SetLimit(limit);
        }

        if (CurrentType != Token.EndOfInput)
        {
            string word = Current.AsIdentifier ?? Current.ToString() ?? "statement";
            if (IsClauseWord(word))
            {
                throw new TODOException("Parser.parseQuerySelect: " + word + " is not ported");
            }

            throw DbException.GetSyntaxError(sql, Current.Start, "statement");
        }

        select.Init();
        select.PrepareExpressions();
        select.PreparePlan(current, targetTableFilter: null);
        return select;
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
            || word.Equals("FOR", StringComparison.OrdinalIgnoreCase);

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
    /// <returns>true for a COUNT, SUM, MIN, MAX, AVG, STDDEV, VAR, ANY, EVERY, MEDIAN, ANY_VALUE, PERCENTILE_CONT, PERCENTILE_DISC or LISTAGG call</returns>
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
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads a COUNT, SUM, MIN, MAX, AVG, STDDEV, VAR, ANY, EVERY, MEDIAN, ANY_VALUE, PERCENTILE_CONT, PERCENTILE_DISC or LISTAGG call: the aggregates the engine runs.
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
    /// Reads a CAST call: the value, then the type it becomes.
    /// </summary>
    /// <returns>the cast</returns>
    private Expressions.Expression ReadQueryCast()
    {
        ExpectWord("CAST");
        Expect(Token.OpenParen, "(");
        Expressions.Expression arg = ReadQueryOr();
        ExpectWord("AS");
        TypeInfo type = ParseDataType(ReadTypeName());
        Expect(Token.CloseParen, ")");
        return new Expressions.Functions.CastSpecification(arg, type);
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
    /// Reads a comparison with an optional IS NULL tail.
    /// </summary>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    private Expressions.Expression ReadQueryComparison(Query.Select? aggregateSelect = null)
    {
        Expressions.Expression left = ReadQueryTerm(aggregateSelect);
        while (CurrentType is Token.Equal or Token.NotEqual or Token.Smaller or Token.Bigger
            or Token.SmallerEqual or Token.BiggerEqual)
        {
            int compareType = ReadQueryComparisonType();
            left = new Expressions.Conditions.Comparison(
                compareType, left, ReadQueryTerm(aggregateSelect), whenOperand: false);
        }

        if (CurrentType == ParserUtil.Is)
        {
            Read();
            bool not = ReadIf(ParserUtil.Not);
            Expect(ParserUtil.Null, "NULL");
            left = new Expressions.Conditions.ConditionIsNull(left, not, whenOperand: false);
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

        if (CurrentType == ParserUtil.Not)
        {
            Read();
            return new Expressions.Conditions.ConditionNot(ReadQueryTerm(aggregateSelect));
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

        if (CurrentType == Token.MinusSign)
        {
            Read();
            Expressions.Expression term = ReadQueryTerm(aggregateSelect);

            // A negative literal is a literal: there is nothing in "-1" that needs a run.
            if (term is not Expressions.ValueExpression negated)
            {
                throw DbException.GetSyntaxError(sql, Current.Start, "number");
            }

            return Expressions.ValueExpression.Get(Negate(negated.GetValueFromLiteral()));
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
            throw new TODOException("Parser.parseQuerySelect: named parameters are not ported");
        }

        Values.Value? literal = Current.GetValue(provider);
        if (literal is not null)
        {
            Read();
            return Expressions.ValueExpression.Get(literal);
        }

        if (Current.AsIdentifier is not null)
        {
            if (tokens[position + 1].TokenType == Token.OpenParen)
            {
                // A function call where only HAVING may aggregate: without the aggregate
                // select this is a WHERE or GROUP BY key, and naming the function beats a
                // syntax error about valid SQL.
                throw new TODOException("Parser.parseQuerySelect: " + Current.AsIdentifier + " is not ported");
            }

            return new Expressions.ExpressionColumn(ReadQueryColumnName(out string? tableAlias), tableAlias, 0);
        }

        throw new TODOException("Parser.parseQuerySelect: this expression is not ported");
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
        List<Tables.IndexColumn> primaryKey = [];
        int id = 0;
        foreach (ColumnDefinition column in statement.Columns)
        {
            Tables.Column tableColumn = new(column.Name, ParseDataType(column.Type), null, id++);
            if (column.NotNull)
            {
                tableColumn.SetNullable(false);
            }

            create.AddColumn(tableColumn);
            if (column.PrimaryKey)
            {
                primaryKey.Add(new Tables.IndexColumn(tableColumn));
            }
        }

        if (primaryKey.Count > 0)
        {
            Ddl.AlterTableAddConstraint key = new(
                session, CurrentSchema(session), CommandInterface.AlterTableAddConstraintPrimaryKey, false);
            key.SetIndexColumns([.. primaryKey]);
            create.AddConstraintCommand(key);
        }

        return create;
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

        return TypeInfo.GetTypeInfo(dataType.Type, precision, scale, null);
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
        Tables.Table table = CurrentSchema(session).GetTableOrView(session, statement.Name);
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
    /// Builds the UPDATE command for a statement the parser read.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="statement">the parsed statement</param>
    /// <returns>the prepared statement</returns>
    private Prepared PrepareUpdate(SessionLocal session, UpdateStatement statement)
    {
        Tables.Table table = CurrentSchema(session).GetTableOrView(session, statement.Name);
        Dml.Update update = new(session);
        update.SetSql(statement.Sql, null);
        update.SetTableFilter(new Tables.TableFilter(
            session, table, table.GetTableName(), rightsChecked: true, select: null, 0, null));
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
        Tables.Table table = CurrentSchema(session).GetTableOrView(session, statement.Name);
        Dml.Delete delete = new(session);
        delete.SetSql(statement.Sql, null);
        delete.SetTableFilter(new Tables.TableFilter(
            session, table, table.GetTableName(), rightsChecked: true, select: null, 0, null));
        if (statement.Where is not null)
        {
            delete.SetCondition(statement.Where);
        }

        return delete;
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
            left = new Expressions.Conditions.ConditionAndOr(
                Expressions.Conditions.ConditionAndOr.Or, left, ReadAnd());
        }

        return left;
    }

    private Expression ReadAnd()
    {
        Expression left = ReadComparison();
        while (CurrentType == ParserUtil.And)
        {
            Read();
            left = new Expressions.Conditions.ConditionAndOr(
                Expressions.Conditions.ConditionAndOr.And, left, ReadComparison());
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
            left = new Expressions.Conditions.Comparison(compareType, left, ReadTerm(), whenOperand: false);
        }

        if (CurrentType == ParserUtil.Is)
        {
            Read();
            bool not = ReadIf(ParserUtil.Not);
            Expect(ParserUtil.Null, "NULL");
            left = new Expressions.Conditions.ConditionIsNull(left, not, whenOperand: false);
        }

        return left;
    }

    private int ReadComparisonType()
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
            return new Expressions.Conditions.ConditionNot(ReadTerm());
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
            // A named parameter it is not (those are the ? this layer does not read yet).
            Read();
            _ = ReadIdentifier();
            throw new TODOException("Parser.readTerm: variables (@name) are not ported");
        }

        Value? literal = Current.GetValue(provider);
        if (literal is not null)
        {
            Read();
            return ValueExpression.Get(literal);
        }

        return new ExpressionColumn(ReadIdentifier(), null, 0);
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
