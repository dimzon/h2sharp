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
public sealed record ColumnDefinition(
    string Name,
    string Type,
    bool NotNull,
    bool PrimaryKey,
    string? ConstraintName,
    bool PrimaryKeyHash);

/// <summary>One table-level constraint of a CREATE TABLE.</summary>
/// <param name="Name">the name written after CONSTRAINT, or null</param>
/// <param name="IfNotExists">whether IF NOT EXISTS was written</param>
/// <param name="Columns">the covered columns, by name</param>
/// <param name="Hash">whether HASH was written</param>
public sealed record TableConstraintDefinition(
    string? Name,
    bool IfNotExists,
    List<string> Columns,
    bool Hash);

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
}

/// <summary>CREATE TABLE.</summary>
public sealed class CreateTableStatement : Statement
{
    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.CreateTable;

    /// <summary>The table name.</summary>
    public string Name { get; set; } = string.Empty;

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

        BitArray parameters = new(8);
        List<Token> tokens = Tokenizer.TokenizeForTest(sql, false, parameters, provider);
        Parser parser = new(sql, provider, tokens, current);
        List<Prepared> prepared = [];
        while (!parser.AtEnd())
        {
            Prepared next = parser.ParseOneStatement();
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
            prepared = ParseQuerySelect();
        }
        else
        {
            Statement statement = ReadStatement();
            statement.Sql = sql;
            prepared = statement switch
            {
                CreateTableStatement create => PrepareCreateTable(current, create),
                AlterTableStatement alter => PrepareAlterTable(current, alter),
                DropTableStatement drop => PrepareDropTable(current, drop),
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
    private Query.Query ParseQuerySelect()
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
            fromTable = ReadTableOrView(current, schema, tableName);
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

        bool union = IsWord("UNION", consume: false);
        if (!union && CurrentType != Token.EndOfInput && CurrentType != Token.Semicolon)
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

            Query.Query right = ParseQuerySelect();
            left = new Query.SelectUnion(left, right, distinct);
        }

        return left;
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
        // NOT belongs to the condition level, as in the reference: "NOT x = y" negates the
        // whole comparison, not just the operand before it.
        if (CurrentType == ParserUtil.Not)
        {
            Read();
            return new Expressions.Conditions.ConditionNot(ReadQueryComparison(aggregateSelect));
        }

        Expressions.Expression left = ReadQueryTerm(aggregateSelect);
        while (true)
        {
            if (CurrentType is Token.Equal or Token.NotEqual or Token.Smaller or Token.Bigger
                or Token.SmallerEqual or Token.BiggerEqual)
            {
                int compareType = ReadQueryComparisonType();
                left = new Expressions.Conditions.Comparison(
                    compareType, left, ReadQueryTerm(aggregateSelect), whenOperand: false);
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

                Expressions.Expression a = ReadQueryTerm(aggregateSelect);
                ExpectWord("AND");
                left = new Expressions.Conditions.BetweenPredicate(
                    left, not, whenOperand: false, symmetric, a, ReadQueryTerm(aggregateSelect));
                continue;
            }

            if (CurrentType == ParserUtil.In
                || CurrentType == ParserUtil.Not && tokens[position + 1].TokenType == ParserUtil.In)
            {
                bool not = ReadIf(ParserUtil.Not);
                Expect(ParserUtil.In, "IN");
                Expect(Token.OpenParen, "(");
                if (IsWord("SELECT", consume: false))
                {
                    throw new TODOException("Parser.parseQuerySelect: IN with a subquery is not ported");
                }

                List<Expressions.Expression> values = [];
                do
                {
                    values.Add(ReadQueryTerm(aggregateSelect));
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
                    Expressions.Expression b = ReadQueryTerm(aggregateSelect);
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
        Ddl.CreateTable create = new(session, CurrentSchema(session));
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
            session, CurrentSchema(session), CommandInterface.AlterTableAddConstraintPrimaryKey,
            constraint.IfNotExists);
        key.SetTableName(tableName);
        key.SetIfTableExists(ifTableExists);
        if (constraint.Name is not null)
        {
            key.SetConstraintName(constraint.Name);
        }

        key.SetPrimaryKeyHash(constraint.Hash);
        List<Tables.IndexColumn> keyColumns = [];
        foreach (string columnName in constraint.Columns)
        {
            keyColumns.Add(new Tables.IndexColumn(columnName));
        }

        key.SetIndexColumns([.. keyColumns]);
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
        Schemas.Schema schema = CurrentSchema(session);
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
    /// Builds the UPDATE command for a statement the parser read.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="statement">the parsed statement</param>
    /// <returns>the prepared statement</returns>
    private Prepared PrepareUpdate(SessionLocal session, UpdateStatement statement)
    {
        Schemas.Schema schema = CurrentSchema(session);
        Tables.Table table = ReadTableOrView(session, schema, statement.Name);
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
        Schemas.Schema schema = CurrentSchema(session);
        Tables.Table table = ReadTableOrView(session, schema, statement.Name);
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

        if (IsWord("ALTER", consume: false))
        {
            Read();
            ExpectWord("TABLE");
            return ReadAlterTable();
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
            // The reference tries every item as a table constraint first and falls back to a
            // column; the set of words that can start one is the same one parseTableConstraintIf
            // switches on. A column named like one of them is written quoted, which is another
            // token type, so the words are unambiguous here.
            if (CurrentType is ParserUtil.Constraint or ParserUtil.Primary)
            {
                statement.Constraints.Add(ReadTableConstraint());
                continue;
            }

            if (CurrentType is ParserUtil.Unique or ParserUtil.Foreign or ParserUtil.Check)
            {
                throw new TODOException(
                    "Parser.readCreateTable: table constraints other than the primary key are not ported");
            }

            string name = ReadIdentifier();
            string type = ReadTypeName();
            bool notNull = false;
            bool primaryKey = false;
            bool primaryKeyHash = false;
            string? constraintName = null;
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
                        Expect(ParserUtil.Primary, "PRIMARY");
                        Expect(ParserUtil.Key, "KEY");
                        primaryKey = true;
                        primaryKeyHash = IsWord("HASH");
                        break;
                    default:
                        Read();
                        break;
                }
            }

            statement.Columns.Add(new ColumnDefinition(name, type, notNull, primaryKey, constraintName, primaryKeyHash));
        }
        while (ReadIf(Token.Comma));

        Expect(Token.CloseParen, ")");
        return statement;
    }

    /// <summary>
    /// Reads a table-level constraint of a CREATE TABLE.
    /// </summary>
    /// <remarks>
    /// Only PRIMARY KEY runs: the reference's parseTableConstraintIf also reads UNIQUE, FOREIGN
    /// KEY and CHECK, and those four go to the same AlterTableAddConstraint whose other
    /// branches are not ported, so the parser refuses them where the reference would accept the
    /// statements. The column list is read as names; the table resolves them when the command
    /// runs, as it does on the JVM.
    /// </remarks>
    /// <returns>the constraint</returns>
    private TableConstraintDefinition ReadTableConstraint()
    {
        string? name = null;
        bool ifNotExists = false;
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
        }

        Expect(ParserUtil.Primary, "PRIMARY");
        Expect(ParserUtil.Key, "KEY");
        bool hash = IsWord("HASH");
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
                "Parser.readTableConstraint: the INDEX clause of a primary key is not ported");
        }

        return new TableConstraintDefinition(name, ifNotExists, columns, hash);
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
        if (IsWord("ADD"))
        {
            if (CurrentType is ParserUtil.Constraint or ParserUtil.Primary)
            {
                statement.AddConstraint = ReadTableConstraint();
                return statement;
            }

            throw new TODOException("Parser.readAlterTable: ADD COLUMN is not ported");
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
                throw new TODOException(
                    "Parser.readAlterTable: DROP PRIMARY KEY is not ported, it goes through DROP INDEX");
            }

            throw new TODOException("Parser.readAlterTable: DROP COLUMN is not ported");
        }

        throw new TODOException("Parser.readAlterTable: only ADD and DROP CONSTRAINT are ported");
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
    /// <remarks>
    /// One chain, as in the reference (issue #12 C1): the statement layer used to carry a second,
    /// weaker copy that stopped at IS NULL, so a WHERE with LIKE / BETWEEN / IN after an UPDATE or
    /// DELETE was a syntax error the query layer did not have. Passing no aggregate select keeps
    /// the statement callers on the non-aggregate arms.
    /// </remarks>
    public Expression ReadExpression() => ReadQueryOr();

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
