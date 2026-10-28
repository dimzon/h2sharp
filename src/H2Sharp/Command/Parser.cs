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
using H2Sharp.Extensions;
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
/// <param name="IndexSchema">the schema of the index named by INDEX, or null</param>
/// <param name="IndexName">the index named by INDEX, or null</param>
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
    bool CheckExisting = true,
    string? IndexSchema = null,
    string? IndexName = null,
    string? RefIndexSchema = null,
    string? RefIndexName = null,
    Engine.NullsDistinct? NullsDistinct = null,
    Expression? CheckExpression = null);

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

    /// <summary>The schema of the table, or null when the name says nothing.</summary>
    public string? Schema { get; set; }

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

    /// <summary>The columns written after DROP, or null when the statement does not drop columns.</summary>
    public List<string>? DropColumns { get; set; }

    /// <summary>Whether the column names were written with IF EXISTS.</summary>
    public bool DropColumnsIfExists { get; set; }

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

    /// <summary>Whether the indexes are persisted: false for CREATE MEMORY TABLE.</summary>
    public bool PersistIndexes { get; set; } = true;

    /// <summary>Whether the table is temporary.</summary>
    public bool Temporary { get; set; }

    /// <summary>Whether the temporary table is global.</summary>
    public bool GlobalTemporary { get; set; }

    /// <summary>Whether the data is persisted.</summary>
    public bool PersistData { get; set; } = true;

    /// <summary>Whether the table is transactional.</summary>
    public bool Transactional { get; set; }

    /// <summary>Whether the table is dropped at commit.</summary>
    public bool OnCommitDrop { get; set; }

    /// <summary>Whether the table is truncated at commit.</summary>
    public bool OnCommitTruncate { get; set; }

    /// <summary>The table engine, or null.</summary>
    public string? TableEngine { get; set; }

    /// <summary>The table engine parameters, or null.</summary>
    public List<string>? TableEngineParams { get; set; }
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

    /// <summary>The schema of the index name, or null when the name says nothing.</summary>
    public string? Schema { get; set; }

    /// <summary>The table the index is on.</summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>The schema of the table, or null when the name says nothing.</summary>
    public string? TableSchema { get; set; }

    /// <summary>The columns, in order, with their sort types.</summary>
    public List<IndexColumn> Columns { get; } = [];

    /// <summary>Whether UNIQUE was written.</summary>
    public bool Unique { get; set; }

    /// <summary>The number of leading columns that are part of the unique key.</summary>
    public int UniqueColumnCount { get; set; }

    /// <summary>How NULLs compare in a unique index, or null for a non-unique one.</summary>
    public NullsDistinct? NullsDistinct { get; set; }

    /// <summary>Whether IF NOT EXISTS was written.</summary>
    public bool IfNotExists { get; set; }

    /// <summary>Whether IF EXISTS was written before the table name.</summary>
    public bool IfTableExists { get; set; }

    /// <summary>Whether this statement creates the primary key index, as CREATE PRIMARY KEY does.</summary>
    public bool PrimaryKey { get; set; }
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

    /// <summary>Whether this is INSERT ... DEFAULT VALUES.</summary>
    public bool DefaultValues { get; set; }
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
    public List<(string Column, Expression[]? ArrayIndexes, Expression Value)> Assignments { get; } = [];

    /// <summary>Row-value assignments: one expression sets several columns.</summary>
    public List<(List<string> Columns, List<Expression[]?> AllIndexes, Expression Value)> RowAssignments { get; } = [];

    /// <summary>The condition, or null.</summary>
    public Expression? Where { get; set; }

    /// <summary>The optional TOP limit, or null.</summary>
    public Expression? Fetch { get; set; }
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

    /// <summary>The optional TOP limit, or null.</summary>
    public Expression? Fetch { get; set; }
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

    private string sql;

    private readonly List<Token> tokens;

    private readonly SessionLocal? session;

    private int position;

    /// <summary>
    /// The parameters read so far, in the order their placeholders appear.
    /// </summary>
    private List<Expressions.Parameter> parameterList = [];

    /// <summary>
    /// The indexes of the parameters used in the current scope, as the reference's
    /// <c>usedParameters</c>. A derived table can leave gaps in its own parameter list, and
    /// the values are copied into the query it reparses by index.
    /// </summary>
    private BitSet usedParameters = new();

    private bool recompileAlways;

    /// <summary>
    /// The select being read, as the reference's <c>currentSelect</c>.
    /// </summary>
    private Query.Select? currentSelect;

    /// <summary>
    /// The prepared statement being read, as the reference's <c>currentPrepared</c>.
    /// </summary>
    private Prepared? currentPrepared;

    /// <summary>Whether VALUE is the domain value in a domain constraint.</summary>
    private bool parseDomainConstraint;

    /// <summary>
    /// The common table expressions this statement names, by name.
    /// </summary>
    private readonly Dictionary<string, Tables.Table> ctes = [];

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

    /// <summary>Supplies the existing parameter objects for command recompilation.</summary>
    public void SetSuppliedParameters(List<Expressions.Parameter> suppliedParameters)
    {
        // ParserBase.setSuppliedParameters; Parameter.GetIndex is zero-based as in Java.
        int max = Expressions.Parameter.GetMaxIndex(suppliedParameters);
        if (max > suppliedParameters.Count)
        {
            parameterList = new(max);
            for (int i = 0; i < max; i++) parameterList.Add(null!);
            foreach (Expressions.Parameter parameter in suppliedParameters)
                parameterList[parameter.GetIndex()] = parameter;
        }
        else parameterList = suppliedParameters;
    }

    /// <summary>
    /// Starts a parameter scope, as the reference's <c>openParametersScope</c> does.
    /// </summary>
    /// <returns>the used-parameter set of the enclosing scope</returns>
    private BitSet OpenParametersScope()
    {
        BitSet outerUsedParameters = usedParameters;
        usedParameters = new BitSet();
        return outerUsedParameters;
    }

    /// <summary>
    /// Ends a parameter scope and returns the parameters used in it, as the reference's
    /// <c>closeParametersScope</c> does.
    /// </summary>
    /// <remarks>
    /// A derived table can have gaps in its parameter list: a statement that uses
    /// <c>?1</c> and <c>?3</c> but not <c>?2</c> hands its derived query two parameters, and
    /// the values are copied by their original indexes.
    /// </remarks>
    /// <param name="outerUsedParameters">the set returned by <see cref="OpenParametersScope"/></param>
    /// <returns>the parameters used in the scope, in index order</returns>
    private List<Expressions.Parameter> CloseParametersScope(BitSet outerUsedParameters)
    {
        BitSet innerUsedParameters = usedParameters;
        List<Expressions.Parameter> parameters = [];
        for (int i = innerUsedParameters.NextSetBit(0); i >= 0; i = innerUsedParameters.NextSetBit(i + 1))
        {
            parameters.Add(parameterList[i]);
            outerUsedParameters.Set(i);
        }

        usedParameters = outerUsedParameters;
        return parameters;
    }

    /// <summary>Parses one statement without preparing it, as H2 Parser.parse does.</summary>
    internal Prepared Parse(string sql, List<Token>? tokens)
    {
        SessionLocal current = session ?? throw new TODOException("Parser.parse: this parser has no session");
        if (tokens is null)
        {
            BitSet parameters = new();
            tokens = Tokenizer.TokenizeForTest(sql, false, parameters, provider);
            CheckParameterIndex(parameters);
        }
        Parser parser = new(sql, provider, tokens, current);
        parser.SetSuppliedParameters(parameterList);
        return parser.ParseOneStatement();
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
        => PrepareCommand(sql, suppliedParameters: null);

    /// <summary>
    /// Parses and prepares a statement text, optionally reusing the parameters of an earlier
    /// parse. The lazy remainder of a command list is read through this overload.
    /// </summary>
    /// <param name="sql">the statement text</param>
    /// <param name="suppliedParameters">the parameters to reuse, or null</param>
    /// <returns>the command</returns>
    internal Command PrepareCommand(string sql, List<Expressions.Parameter>? suppliedParameters)
    {
        ArgumentNullException.ThrowIfNull(sql);
        SessionLocal current = session ?? throw new TODOException("Parser.prepareCommand: this parser has no session");

        BitSet parameters = new();
        List<Token> tokens = Tokenizer.TokenizeForTest(sql, false, parameters, provider);
        CheckParameterIndex(parameters);
        Parser parser = new(sql, provider, tokens, current);
        if (suppliedParameters is null)
        {
            // The reference creates every parameter the tokenizer saw before it starts parsing.
            // The list is shared by all statements of the text, including the ones that are
            // deferred until execution.
            int count = parameters.Length;
            parser.parameterList = new List<Expressions.Parameter>(count);
            for (int i = 0; i < count; i++)
            {
                parser.parameterList.Add(new Expressions.Parameter(i));
            }
        }
        else
        {
            parser.parameterList = suppliedParameters;
        }

        List<Prepared> prepared = [];
        string? remaining = null;
        while (!parser.AtEnd())
        {
            int statementStart = parser.Current.Start;
            Prepared next;
            try
            {
                next = parser.ParseOneStatement();

                // The reference's prepareCommand: a statement ends at a semicolon or at the end
                // of the input, and anything else is a plain syntax error. The expected-token
                // list is empty on this path, so the reference's addExpected(SEMICOLON) adds
                // nothing and the failure is the one-argument form.
                if (parser.CurrentType != Token.Semicolon && parser.CurrentType != Token.EndOfInput)
                {
                    throw DbException.GetSyntaxError(sql, parser.Current.Start);
                }

                next.Prepare();
            }
            catch (H2SharpException e) when (prepared.Count > 0
                && e.H2ErrorCode != Api.ErrorCode.CannotMixIndexedAndUnindexedParams)
            {
                // A later statement may depend on what an earlier one creates. The reference
                // defers it, and the rest of the text, until the earlier statements have run.
                remaining = sql[statementStart..];
                break;
            }

            prepared.Add(next);
        }

        if (prepared.Count == 0)
        {
            return new CommandContainer(current, sql, new Dml.NoOperation(current));
        }

        if (prepared.Count == 1 && remaining is null)
        {
            return new CommandContainer(current, sql, prepared[0]);
        }

        return new CommandList(current, sql, prepared, parser.parameterList, remaining);
    }

    /// <summary>
    /// Parses a query and prepares its expressions, as the reference's
    /// prepareQueryExpression does. Rights and literals must already be checked.
    /// </summary>
    /// <param name="sql">the query SQL</param>
    /// <returns>the query</returns>
    public Query.Query PrepareQueryExpression(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        SessionLocal current = session ?? throw new TODOException("Parser.prepareQueryExpression: this parser has no session");
        BitSet parameters = new();
        List<Token> tokens = Tokenizer.TokenizeForTest(sql, false, parameters, provider);
        CheckParameterIndex(parameters);
        Parser parser = new(sql, provider, tokens, current);
        parser.SetSuppliedParameters(parameterList);
        Query.Query query = parser.ParseQueryExpression(prepare: false);
        query.PrepareExpressions();
        query.SetParameterList(parser.parameterList.Count == 0 ? null : parser.parameterList);
        if (!parser.AtEnd())
        {
            throw DbException.GetSyntaxError(sql, parser.Current.Start);
        }

        return query;
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
        BitSet parameters = new();
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
        // Reference parse(): currentSelect and currentPrepared belong to one statement.
        currentSelect = null;
        currentPrepared = null;
        while (position < tokens.Count && tokens[position].TokenType == Token.Semicolon)
        {
            position++;
        }

        int start = position;
        recompileAlways = false;

        Prepared prepared;
        if (position < tokens.Count && (IsDirectQuery(position)
            || CurrentType == Token.OpenParen))
        {
            prepared = ParseQueryExpression(prepare: false);
        }
        else if (IsWord("EXPLAIN", consume: false))
        {
            prepared = PrepareExplain(start);
        }
        else if (IsWord("ANALYZE", consume: false))
        {
            prepared = PrepareAnalyze(current);
        }
        else if (IsWord("SCRIPT", consume: false))
        {
            prepared = PrepareScript(current);
        }
        else if (IsCreateMaterializedView())
        {
            prepared = PrepareCreateMaterializedView(current);
        }
        else if (IsWord("CREATE", consume: false)
            && IsWordAt(position + 1, "OR") && IsWordAt(position + 2, "REPLACE")
            && IsWordAt(position + 3, "SYNONYM"))
        {
            prepared = PrepareCreateSynonym(current);
        }
        else if (IsWord("CREATE", consume: false)
            && (NextIs("VIEW") || NextIs("OR") || NextIs("FORCE")))
        {
            prepared = PrepareCreateView(current);
        }
        else if (IsWord("DROP", consume: false) && NextIs("MATERIALIZED"))
        {
            prepared = PrepareDropMaterializedView(current);
        }
        else if (IsWord("DROP", consume: false) && NextIs("VIEW"))
        {
            prepared = PrepareDropView(current);
        }
        else if (IsWord("REFRESH", consume: false))
        {
            prepared = PrepareRefreshMaterializedView(current);
        }
        else if (IsWord("ALTER", consume: false) && NextIs("VIEW"))
        {
            prepared = PrepareAlterView(current);
        }
        else if (IsWord("BACKUP", consume: false))
        {
            Read();
            ExpectWord("TO");
            Dml.BackupCommand command = new(current);
            command.SetSql(sql, null);
            command.SetFileName(ReadExpression());
            prepared = command;
        }
        else if (IsWord("CHECKPOINT", consume: false))
        {
            Read();
            int type = IsWord("SYNC") ? CommandInterface.CheckpointSync : CommandInterface.Checkpoint;
            prepared = new Dml.TransactionCommand(current, type);
        }
        else if (IsWord("SHUTDOWN", consume: false))
        {
            Read();
            int type = CommandInterface.Shutdown;
            if (IsWord("IMMEDIATELY")) type = CommandInterface.ShutdownImmediately;
            else if (IsWord("COMPACT")) type = CommandInterface.ShutdownCompact;
            else if (IsWord("DEFRAG")) type = CommandInterface.ShutdownDefrag;
            prepared = new Dml.TransactionCommand(current, type);
        }
        else if (IsWord("EXECUTE", consume: false) && NextIs("IMMEDIATE"))
        {
            Read();
            Read();
            prepared = new Dml.ExecuteImmediate(current, ReadExpression());
        }
        else if (IsWord("SHOW", consume: false))
        {
            prepared = PrepareShow(current);
        }
        else if (IsWord("SAVEPOINT")) prepared = ParseSavepoint();
        else if (IsWord("RELEASE")) prepared = ParseReleaseSavepoint();
        else if (IsWord("ROLLBACK")) prepared = ParseRollback();
        else if (IsWord("COMMIT")) prepared = ParseCommit();
        else if (IsWord("PREPARE")) prepared = ParsePrepare();
        else if (IsWord("BEGIN")) { IsWord("WORK"); prepared = new Dml.TransactionCommand(current, CommandInterface.Begin); }
        else if (IsWord("SET", consume: false))
        {
            Read();
            prepared = PrepareSet(current);
        }
        else if (IsWord("CREATE", consume: false) && NextIs("USER"))
        {
            throw new TODOException(
                "Parser.parseCreateUser: CreateUser and users are not ported (D-004)");
        }
        else if (IsWord("CREATE", consume: false) && NextIs("ROLE"))
        {
            throw new TODOException(
                "Parser.parseCreateRole: CreateRole and roles are not ported (D-004)");
        }
        else if (IsWord("ALTER", consume: false) && NextIs("USER"))
        {
            throw new TODOException(
                "Parser.parseAlterUser: AlterUser and users are not ported (D-004)");
        }
        else if (IsWord("DROP", consume: false) && NextIs("USER"))
        {
            throw new TODOException(
                "Parser.parseDropUser: DropUser and users are not ported (D-004)");
        }
        else if (IsWord("DROP", consume: false) && NextIs("ROLE"))
        {
            throw new TODOException(
                "Parser.parseDropRole: DropRole and roles are not ported (D-004)");
        }
        else if (IsWord("GRANT") || IsWord("REVOKE"))
        {
            throw new TODOException(
                "Parser.parseGrantRevoke: GrantRevoke, users, roles and rights are not ported (D-004)");
        }
        else if (IsWord("CREATE", consume: false) && NextIs("SYNONYM"))
        {
            prepared = PrepareCreateSynonym(current);
        }
        else if (IsWord("DROP", consume: false) && NextIs("SYNONYM"))
        {
            prepared = PrepareDropSynonym(current);
        }
        else if (IsWord("CREATE", consume: false) && NextIs("CONSTANT"))
        {
            prepared = PrepareCreateConstant(current);
        }
        else if (IsWord("DROP", consume: false) && NextIs("CONSTANT"))
        {
            prepared = PrepareDropConstant(current);
        }
        else if (IsWord("CREATE", consume: false) && NextIs("SEQUENCE"))
        {
            prepared = PrepareCreateSequence(current);
        }
        else if (IsWord("ALTER", consume: false) && NextIs("TABLE"))
        {
            prepared = ParseAlterTable();
        }
        else if (IsWord("ALTER", consume: false) && NextIs("INDEX"))
        {
            prepared = PrepareAlterIndex(current);
        }
        else if (IsWord("ALTER", consume: false) && NextIs("SCHEMA"))
        {
            prepared = PrepareAlterSchema(current);
        }
        else if (IsWord("INSERT"))
        {
            prepared = ParseInsert(start);
        }
        else if (IsWord("CALL"))
        {
            prepared = ParseCall();
        }
        else if (IsWord("CREATE", consume: false) && NextIsTable())
        {
            Read();
            bool memory = IsWord("MEMORY");
            bool cached = !memory && IsWord("CACHED");
            bool temporary = false;
            bool globalTemporary = false;
            if (IsWord("LOCAL"))
            {
                ExpectWord("TEMPORARY");
                temporary = true;
            }
            else if (IsWord("GLOBAL"))
            {
                ExpectWord("TEMPORARY");
                temporary = true;
                globalTemporary = true;
            }
            else if (IsWord("TEMP") || IsWord("TEMPORARY"))
            {
                temporary = true;
                globalTemporary = true;
            }

            bool persistIndexes = cached || !memory && current.GetDatabase().GetDefaultTableType() == Tables.Table.TYPE_CACHED;
            ExpectWord("TABLE");
            prepared = ParseCreateTable(temporary, globalTemporary, persistIndexes);
        }
        else if (IsWord("CREATE", consume: false)
            && (NextIs("DOMAIN") || NextIs("TYPE") || NextIs("DATATYPE")))
        {
            prepared = PrepareCreateDomain(current);
        }
        else if (IsWord("ALTER", consume: false) && NextIs("TYPE"))
        {
            prepared = PrepareAlterType(current);
        }
        else if (IsWord("ALTER", consume: false) && NextIs("DOMAIN"))
        {
            prepared = PrepareAlterDomain(current);
        }
        else if (IsWord("DROP", consume: false)
            && (NextIs("DOMAIN") || NextIs("TYPE") || NextIs("DATATYPE")))
        {
            prepared = PrepareDropDomain(current);
        }
        else if (IsWord("DROP", consume: false) && NextIs("TABLE"))
        {
            prepared = ParseDrop();
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
        else if (IsWord("DROP", consume: false) && NextIs("SCHEMA"))
        {
            prepared = PrepareDropSchema(current);
        }
        else if (IsWord("TRUNCATE", consume: false))
        {
            prepared = PrepareTruncateTable(current);
        }
        else if (IsWord("MERGE", consume: false))
        {
            prepared = PrepareMerge(current);
        }
        else if (IsWord("COMMENT", consume: false))
        {
            prepared = PrepareComment(current);
        }
        else
        {
            Statement statement = ReadStatement();
            statement.Sql = sql;
            prepared = PrepareParsedStatement(current, statement);
        }

        if (ReadIf(Token.OpenBrace))
        {
            // The {index: value} form binds a parameter in the statement text itself.
            do
            {
                int index = (int)ReadLong() - 1;
                if (index < 0 || index >= parameterList.Count)
                {
                    throw DbException.GetSyntaxError(sql, Current.Start);
                }

                Expressions.Parameter parameter = parameterList[index]
                    ?? throw DbException.GetSyntaxError(sql, Current.Start);
                Expect(Token.Colon, ":");
                Expressions.Expression value = ReadExpression().Optimize(current);
                parameter.SetValue(value.GetValue(current));
            }
            while (ReadIf(Token.Comma));

            Expect(Token.CloseBrace, "}");
            foreach (Expressions.Parameter parameter in parameterList)
            {
                parameter.CheckSet();
            }

            prepared.SetWithParamValues(true);
        }

        SetSql(prepared, start);
        prepared.SetPrepareAlways(recompileAlways);
        prepared.SetParameterList(parameterList.Count == 0 ? null : parameterList);
        return prepared;
    }

    /// <summary>
    /// Builds the command of a statement the parser read.
    /// </summary>
    private Prepared PrepareParsedStatement(SessionLocal current, Statement statement)
        => statement switch
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

    private bool IsCreateMaterializedView()
    {
        int i = position;
        if (!IsWordAt(i, "CREATE"))
        {
            return false;
        }

        i++;
        if (IsWordAt(i, "OR"))
        {
            i++;
            if (!IsWordAt(i++, "REPLACE"))
            {
                return false;
            }
        }

        if (IsWordAt(i, "FORCE"))
        {
            i++;
        }

        return IsWordAt(i, "MATERIALIZED") && IsWordAt(i + 1, "VIEW");
    }

    private bool IsWordAt(int index, string word)
        => index < tokens.Count
            && tokens[index].AsIdentifier is string name
            && name.Equals(word, StringComparison.OrdinalIgnoreCase);

    private Prepared PrepareCreateMaterializedView(SessionLocal current)
    {
        ExpectWord("CREATE");
        bool orReplace = false;
        if (IsWord("OR"))
        {
            ExpectWord("REPLACE");
            orReplace = true;
        }

        bool force = IsWord("FORCE");
        ExpectWord("MATERIALIZED");
        ExpectWord("VIEW");
        bool ifNotExists = ReadIfNotExists();
        string viewName = ReadIdentifierWithSchema(current, out Schemas.Schema schema);
        Ddl.CreateMaterializedView command = new(current, schema);
        command.SetViewName(viewName);
        command.SetIfNotExists(ifNotExists);
        command.SetOrReplace(orReplace);
        if (force)
        {
            throw DbException.GetUnsupportedException("CREATE FORCE MATERIALIZED VIEW");
        }

        if (IsWord("COMMENT"))
        {
            _ = IsWord("IS");
            command.SetComment(ReadString());
        }

        if (ReadIf(Token.OpenParen))
        {
            List<string> columns = [];
            do
            {
                columns.Add(ReadIdentifier());
            }
            while (ReadIf(Token.Comma));
            Expect(Token.CloseParen, ")");
            command.SetColumnNames(columns);
        }

        ExpectWord("AS");
        string selectSql = sql[Current.Start..];
        command.SetSelectSql(selectSql);
        Query.Query query = ParseQueryExpression(prepare: false);
        query.Prepare();
        command.SetSelect(query);
        return command;
    }

    private Prepared PrepareDropMaterializedView(SessionLocal current)
    {
        ExpectWord("DROP");
        ExpectWord("MATERIALIZED");
        ExpectWord("VIEW");
        bool ifExists = ReadIfExists(false);
        string viewName = ReadIdentifierWithSchema(current, out Schemas.Schema schema);
        Ddl.DropMaterializedView command = new(current, schema);
        command.SetViewName(viewName);
        command.SetIfExists(ifExists);
        return command;
    }

    private Prepared PrepareRefreshMaterializedView(SessionLocal current)
    {
        ExpectWord("REFRESH");
        ExpectWord("MATERIALIZED");
        ExpectWord("VIEW");
        string viewName = ReadIdentifierWithSchema(current, out Schemas.Schema schema);
        Tables.Table? table = schema.FindTableOrView(current, viewName);
        if (table is not Tables.MaterializedView view)
        {
            throw DbException.Get(Api.ErrorCode.ViewNotFound1, viewName);
        }

        Ddl.RefreshMaterializedView command = new(current, schema);
        command.SetView(view);
        return command;
    }

    /// <summary>
    /// Parses CREATE [OR REPLACE] [FORCE] VIEW.
    /// </summary>
    private Prepared PrepareCreateView(SessionLocal current)
    {
        ExpectWord("CREATE");
        bool orReplace = false;
        if (IsWord("OR"))
        {
            ExpectWord("REPLACE");
            orReplace = true;
        }

        bool force = IsWord("FORCE");
        ExpectWord("VIEW");
        bool ifNotExists = ReadIfNotExists();
        string viewName = ReadIdentifierWithSchema(current, out Schemas.Schema schema);
        Ddl.CreateView command = new(current, schema);
        command.SetViewName(viewName);
        command.SetIfNotExists(ifNotExists);
        command.SetOrReplace(orReplace);
        command.SetForce(force);
        if (IsWord("COMMENT"))
        {
            _ = IsWord("IS");
            command.SetComment(ReadString());
        }

        if (ReadIf(Token.OpenParen))
        {
            List<string> columns = [];
            do
            {
                columns.Add(ReadIdentifier());
            }
            while (ReadIf(Token.Comma));

            Expect(Token.CloseParen, ")");
            command.SetColumnNames([.. columns]);
        }

        ExpectWord("AS");
        string selectSql = sql[Current.Start..];
        try
        {
            current.SetParsingCreateView(true);
            Query.Query query;
            try
            {
                query = ParseQueryExpression(prepare: false);
                query.Prepare();
            }
            finally
            {
                current.SetParsingCreateView(false);
            }

            command.SetQuery(query);
        }
        catch (H2SharpException)
        {
            if (!force)
            {
                throw;
            }

            command.SetSelectSql(selectSql);
            while (CurrentType != Token.EndOfInput)
            {
                Read();
            }
        }

        return command;
    }

    /// <summary>Parses DROP VIEW.</summary>
    private Prepared PrepareDropView(SessionLocal current)
    {
        ExpectWord("DROP");
        ExpectWord("VIEW");
        bool ifExists = ReadIfExists(false);
        string viewName = ReadIdentifierWithSchema(current, out Schemas.Schema schema);
        Ddl.DropView command = new(current, schema);
        command.SetViewName(viewName);
        command.SetIfExists(ifExists);
        if (IsWord("CASCADE"))
        {
            command.SetDropAction(Constraints.ConstraintActionType.CASCADE);
        }
        else if (IsWord("RESTRICT"))
        {
            command.SetDropAction(Constraints.ConstraintActionType.RESTRICT);
        }

        return command;
    }

    /// <summary>Parses the MySQL-compatible SHOW forms.</summary>
    private Prepared PrepareAnalyze(SessionLocal current)
    {
        Read();
        Ddl.Analyze command = new(current);
        if (IsWord("TABLE"))
        {
            string name = ReadIdentifierWithSchema(current, out Schemas.Schema schema);
            command.SetTable(ReadTableOrView(current, schema, name));
        }

        if (IsWord("SAMPLE_SIZE"))
        {
            long sample = ReadLong();
            if (sample < 0 || sample > int.MaxValue)
            {
                throw DbException.GetInvalidValueException("non-negative int", sample);
            }

            command.SetTop((int)sample);
        }

        return command;
    }

    private Prepared PrepareShow(SessionLocal current)
    {
        ExpectWord("SHOW");
        if (IsWord("TABLES"))
        {
            string schema = current.GetCurrentSchemaName();
            if (IsWord("FROM"))
            {
                schema = ReadIdentifier();
            }

            string query = "SELECT TABLE_NAME, TABLE_SCHEMA FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA="
                + StringUtils.QuoteStringSql(schema) + " ORDER BY TABLE_NAME";
            return current.PrepareQueryExpression(query);
        }

        ExpectWord("COLUMNS");
        ExpectWord("FROM");
        string table = ReadIdentifierWithSchema(current, out Schemas.Schema tableSchema);
        string schemaName = tableSchema.GetName();
        if (IsWord("FROM"))
        {
            schemaName = ReadIdentifier();
        }

        string columns = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA="
            + StringUtils.QuoteStringSql(schemaName) + " AND TABLE_NAME="
            + StringUtils.QuoteStringSql(table) + " ORDER BY ORDINAL_POSITION";
        return current.PrepareQueryExpression(columns);
    }

    /// <summary>Parses ALTER INDEX RENAME.</summary>
    private Prepared PrepareAlterIndex(SessionLocal current)
    {
        ExpectWord("ALTER");
        ExpectWord("INDEX");
        bool ifExists = ReadIfExists(false);
        string indexName = ReadIdentifierWithSchema(current, out Schemas.Schema schema);
        ExpectWord("RENAME");
        ExpectWord("TO");
        Ddl.AlterIndexRename command = new(current);
        command.SetSql(sql, null);
        command.SetIfExists(ifExists);
        command.SetOldSchema(schema);
        command.SetOldName(indexName);
        command.SetNewName(ReadIdentifier());
        return command;
    }

    /// <summary>Parses ALTER SCHEMA RENAME.</summary>
    private Prepared PrepareAlterSchema(SessionLocal current)
    {
        ExpectWord("ALTER");
        ExpectWord("SCHEMA");
        bool ifExists = ReadIfExists(false);
        string schemaName = ReadIdentifier();
        ExpectWord("RENAME");
        ExpectWord("TO");
        string newName = ReadIdentifier();
        Schemas.Schema? schema = current.GetDatabase().FindSchema(schemaName);
        if (schema is null)
        {
            if (ifExists)
            {
                return new Dml.NoOperation(current);
            }

            throw DbException.Get(Api.ErrorCode.SchemaNotFound1, schemaName);
        }

        Ddl.AlterSchemaRename command = new(current);
        command.SetSql(sql, null);
        command.SetOldSchema(schema);
        command.SetNewName(newName);
        return command;
    }

    /// <summary>Parses ALTER VIEW ... RECOMPILE.</summary>
    private Prepared PrepareAlterView(SessionLocal current)
    {
        ExpectWord("ALTER");
        ExpectWord("VIEW");
        bool ifExists = ReadIfExists(false);
        string viewName = ReadIdentifierWithSchema(current, out Schemas.Schema schema);
        Table? table = schema.FindTableOrView(current, viewName);
        if (table is not null && table is not Tables.TableView)
        {
            throw DbException.Get(Api.ErrorCode.TableOrViewNotFound1, viewName);
        }

        if (IsWord("RENAME", consume: false))
        {
            Read();
            ExpectWord("TO");
            Ddl.AlterTableRename rename = new(current, schema);
            rename.SetTableName(viewName);
            rename.SetIfTableExists(ifExists);
            rename.SetNewTableName(ReadIdentifier());
            return rename;
        }

        ExpectWord("RECOMPILE");
        Ddl.AlterView command = new(current);
        command.SetIfExists(ifExists);
        command.SetView(table as Tables.TableView);
        return command;
    }

    /// <summary>
    /// Parses SCRIPT and its NODATA/NOPASSWORDS/NOSETTINGS/NOVERSION options, as the
    /// reference's parseScript does.
    /// </summary>
    private Prepared PrepareScript(SessionLocal current)
    {
        ExpectWord("SCRIPT");
        Dml.ScriptCommand command = new(current);
        bool data = true, passwords = true, settings = true, version = true;
        bool drop = false, simple = false, withColumns = false;
        if (IsWord("NODATA"))
        {
            data = false;
        }
        else
        {
            simple = IsWord("SIMPLE");
            withColumns = IsWord("COLUMNS");
        }

        if (IsWord("NOPASSWORDS")) passwords = false;
        if (IsWord("NOSETTINGS")) settings = false;
        if (IsWord("NOVERSION")) version = false;
        if (IsWord("DROP")) drop = true;
        if (IsWord("BLOCKSIZE")) command.SetLobBlockSize(ReadLong());
        command.SetData(data);
        command.SetPasswords(passwords);
        command.SetSettings(settings);
        command.SetVersion(version);
        command.SetDrop(drop);
        command.SetSimple(simple);
        command.SetWithColumns(withColumns);
        if (IsWord("TO") || IsWord("SCHEMA") || IsWord("TABLE"))
        {
            throw new TODOException(
                "Parser.parseScript: SCRIPT TO/SCHEMA/TABLE filters are not ported");
        }

        return command;
    }

    /// <summary>
    /// Parses EXPLAIN and EXPLAIN ANALYZE, as the reference's parseExplain does.
    /// </summary>
    private Prepared PrepareExplain(int start)
    {
        SessionLocal current = session
            ?? throw new TODOException("Parser.parseExplain: this parser has no session");
        ExpectWord("EXPLAIN");
        Dml.Explain command = new(current);
        if (IsWord("ANALYZE"))
        {
            command.SetExecuteCommand(true);
        }
        else
        {
            if (IsWord("PLAN"))
            {
                _ = IsWord("FOR");
            }
        }

        int nestedStart = position;
        Prepared nested;
        if (position < tokens.Count && (IsDirectQuery(position) || CurrentType == Token.OpenParen))
        {
            nested = ParseQueryExpression(prepare: false);
            if (nested is Query.Query query)
            {
                query.SetNeverLazy(true);
            }
        }
        else if (IsWord("INSERT"))
        {
            nested = ParseInsert(nestedStart);
        }
        else
        {
            Statement statement = ReadStatement();
            statement.Sql = sql;
            nested = PrepareParsedStatement(current, statement);
        }

        SetSql(nested, nestedStart);
        command.SetCommand(nested);
        return command;
    }

    private void SetSql(Prepared command, int start)
    {
        // H2 setSQL: reuse the full token stream for one statement, or clone the
        // current statement's tokens when the parser still has a batch to read.
        int beginIndex = tokens[start].Start, endIndex = Current.Start;
        while (beginIndex < endIndex && sql[beginIndex] <= ' ') beginIndex++;
        while (beginIndex < endIndex && sql[endIndex - 1] <= ' ') endIndex--;
        string text = sql[beginIndex..endIndex];
        List<Token> commandTokens;
        if (start == 0 && CurrentType == Token.EndOfInput)
        {
            commandTokens = tokens;
            if (beginIndex != 0)
            {
                for (int i = 0; i < commandTokens.Count - 1; i++)
                    commandTokens[i].SubtractFromStart(beginIndex);
            }
            Current.Start = text.Length;
            sql = text;
        }
        else
        {
            commandTokens = new(position - start + 1);
            for (int i = start; i < position; i++)
            {
                Token token = tokens[i].Clone();
                token.SubtractFromStart(beginIndex);
                commandTokens.Add(token);
            }
            commandTokens.Add(new Token.EndOfInputToken(text.Length));
        }
        command.SetSql(text, commandTokens);
    }

    private Dml.TransactionCommand ParseSavepoint()
    {
        Dml.TransactionCommand command = new(session!, CommandInterface.Savepoint);
        command.SetSavepointName(ReadIdentifier());
        return command;
    }

    private Dml.Call ParseCall()
    {
        Dml.Call command = new(session!);
        currentPrepared = command;
        if (IsWord("TABLE", consume: false) && position + 1 < tokens.Count
            && tokens[position + 1].TokenType == Token.OpenParen)
        {
            Read();
            Read();
            command.SetTableFunction(ReadTableFunction(Expressions.Functions.Table.ArrayTableFunction.Table));
        }
        else command.SetExpression(ReadExpression());
        return command;
    }
    private Prepared ParseReleaseSavepoint()
    {
        IsWord("SAVEPOINT"); ReadIdentifier();
        return new Dml.NoOperation(session!);
    }
    private Dml.TransactionCommand ParseCommit()
    {
        if (IsWord("TRANSACTION"))
        {
            Dml.TransactionCommand command = new(session!, CommandInterface.CommitTransaction);
            command.SetTransactionName(ReadIdentifier());
            return command;
        }

        IsWord("WORK");
        return new Dml.TransactionCommand(session!, CommandInterface.Commit);
    }

    private Prepared ParsePrepare()
    {
        if (IsWord("COMMIT"))
        {
            Dml.TransactionCommand command = new(session!, CommandInterface.PrepareCommit);
            command.SetTransactionName(ReadIdentifier());
            return command;
        }

        throw new TODOException("Parser.parsePrepare: PREPARE of a procedure is not ported");
    }

    private Dml.TransactionCommand ParseRollback()
    {
        if (IsWord("TRANSACTION"))
        {
            Dml.TransactionCommand transaction = new(session!, CommandInterface.RollbackTransaction);
            transaction.SetTransactionName(ReadIdentifier());
            return transaction;
        }

        IsWord("WORK");
        if (ReadIf(ParserUtil.To))
        {
            ExpectWord("SAVEPOINT");
            Dml.TransactionCommand command = new(session!, CommandInterface.RollbackToSavepoint);
            command.SetSavepointName(ReadIdentifier());
            return command;
        }
        return new Dml.TransactionCommand(session!, CommandInterface.Rollback);
    }

    /// <summary>
    /// Whether the word after the current one is the given word.
    /// </summary>
    /// <param name="word">the word</param>
    /// <returns>true if it is</returns>
    private bool NextIsTable()
    {
        string? name = position + 1 < tokens.Count ? tokens[position + 1].AsIdentifier : null;
        return name is not null && (name.Equals("TABLE", StringComparison.OrdinalIgnoreCase)
            || name.Equals("MEMORY", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CACHED", StringComparison.OrdinalIgnoreCase)
            || name.Equals("TEMP", StringComparison.OrdinalIgnoreCase)
            || name.Equals("TEMPORARY", StringComparison.OrdinalIgnoreCase)
            || name.Equals("GLOBAL", StringComparison.OrdinalIgnoreCase)
            || name.Equals("LOCAL", StringComparison.OrdinalIgnoreCase));
    }

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
    /// <summary>
    /// Compiles the query text of a view, as TableView.compileViewQuery does.
    /// </summary>
    internal static Query.Query CompileViewQuery(SessionLocal session, string sql)
    {
        BitSet parameters = new();
        List<Token> tokens = Tokenizer.TokenizeForTest(sql, false, parameters, session);
        Parser parser = new(sql, session.CastDataProvider, tokens, session);
        session.SetParsingCreateView(true);
        Query.Query query;
        try
        {
            query = parser.ParseQueryExpression(prepare: false);
        }
        finally
        {
            session.SetParsingCreateView(false);
        }

        if (parser.CurrentType != Token.EndOfInput)
        {
            throw DbException.GetSyntaxError(sql, parser.Current.Start);
        }

        // Prepare outside the view-parsing flag: the plan built while a view is parsed is a
        // fake plan and leaves the table filters unprepared, which a query later run from the
        // view index cannot use.
        query.Prepare();
        return query;
    }

    private Query.Query ParseQueryExpression(bool prepare = true)
    {
        if (IsWord("WITH", consume: false)) ReadWith();
        Query.Query query = ParseQueryExpressionBody();
        ParseEndOfQuery(query);
        query.Init();
        if (prepare) query.Prepare();
        return query;
    }

    private Query.Query ParseQueryExpressionBody()
    {
        Query.Query query = ParseQueryTerm();
        while (true)
        {
            Query.SelectUnion.UnionType type;
            if (ReadIf(ParserUtil.Union))
            {
                if (ReadIf(ParserUtil.All)) type = Query.SelectUnion.UnionType.UNION_ALL;
                else { ReadIf(ParserUtil.Distinct); type = Query.SelectUnion.UnionType.UNION; }
            }
            else if (ReadIf(ParserUtil.Except) || ReadIf(ParserUtil.Minus))
                type = Query.SelectUnion.UnionType.EXCEPT;
            else return query;
            query = new Query.SelectUnion(session!, type, query, ParseQueryTerm());
        }
    }
    private Query.Query ParseQueryTerm()
    {
        Query.Query query = ParseQueryPrimary();
        while (ReadIf(ParserUtil.Intersect))
            query = new Query.SelectUnion(session!, Query.SelectUnion.UnionType.INTERSECT, query, ParseQueryPrimary());
        return query;
    }
    private Query.Query ParseQueryPrimary()
    {
        if (ReadIf(Token.OpenParen))
        {
            Query.Query query = ParseQueryExpressionBody();
            ParseEndOfQuery(query);
            Expect(Token.CloseParen, ")");
            return query;
        }
        if (IsWord("VALUES", consume: false)) return ReadValues();
        if (IsWord("TABLE", consume: false)) return ReadExplicitTable();
        return ParseQuerySelect();
    }

    /// <summary>
    /// Reads the explicit table statement: <c>TABLE name</c>, a select of every column of
    /// the named table, as the reference's parseExplicitTable does. The ORDER BY, OFFSET
    /// and FETCH clauses that follow are read by <see cref="ParseEndOfQuery"/>.
    /// </summary>
    /// <returns>the select</returns>
    private Query.Query ReadExplicitTable()
    {
        SessionLocal current = session
            ?? throw new TODOException("Parser.parseQuerySelect: this parser has no session");
        IsWord("TABLE");
        Query.Select select = new(current);
        select.SetSql(sql, null);
        Tables.TableFilter filter = ReadTableReference(current, select);
        select.AddTableFilter(filter, isTop: true);
        List<Expressions.Expression> columns = [];
        foreach (Tables.Column column in filter.GetTable().GetColumns())
        {
            if (!column.GetVisible()) continue;
            columns.Add(new Expressions.ExpressionColumn(column, filter, 0));
        }

        select.SetExpressions([.. columns]);
        return select;
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
        bool recursive = IsWord("RECURSIVE");

        do
        {
            string name = ReadIdentifier();
            List<string>? columnNames = null;
            bool hasColumns;
            if (recursive)
            {
                Expect(Token.OpenParen, "(");
                hasColumns = true;
            }
            else
            {
                hasColumns = ReadIf(Token.OpenParen);
            }

            if (hasColumns)
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
            if (ctes.ContainsKey(name))
            {
                throw DbException.Get(Api.ErrorCode.TableOrViewAlreadyExists1, name);
            }

            if (recursive)
            {
                Column[] shadowColumns = new Column[columnNames!.Count];
                for (int i = 0; i < shadowColumns.Length; i++)
                {
                    shadowColumns[i] = new Column(columnNames[i], TypeInfo.TypeVarchar);
                }

                Tables.ShadowTable shadow = new(
                    current.GetDatabase().GetMainSchema()!, name, shadowColumns);
                ctes[name] = shadow;
                int queryStart = position;
                Query.Query firstQuery = ParseQueryExpression(prepare: false);
                Tables.Column[] templates = CreateQueryColumnTemplateList(columnNames, firstQuery, cte: true);
                Tables.CTE cte = new(
                    current.GetDatabase().GetMainSchema(), name, templates, firstQuery, current, true);
                Column[] resolutionColumns = new Column[columnNames.Count];
                for (int i = 0; i < resolutionColumns.Length; i++)
                {
                    resolutionColumns[i] = new Column(columnNames[i], TypeInfo.TypeUnknown, cte, i);
                }

                cte.SetColumnsForResolution(resolutionColumns);
                ctes[name] = cte;

                position = queryStart;
                Query.Query query = ParseQueryExpression();
                bool selfReferenced = query.GetTables().Contains(cte);
                Tables.Column[]? finalTemplates = CreateQueryColumnTemplateList(columnNames, query, cte: true);
                cte.Initialize(current, finalTemplates, query, selfReferenced);
            }
            else
            {
                Query.Query query = ParseQueryExpression();
                Tables.Column[]? columnTemplates = columnNames is null
                    ? null
                    : CreateQueryColumnTemplateList(columnNames, query, cte: true);
                ctes[name] = new Tables.CTE(
                    current.GetDatabase().GetMainSchema(), name, columnTemplates, query, current);
            }

            Expect(Token.CloseParen, ")");
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
        BitSet outerUsedParameters = OpenParametersScope();
        Query.Query query = ParseQueryExpression();
        List<Expressions.Parameter> queryParameters = CloseParametersScope(outerUsedParameters);
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

        Tables.Table table = query.ToTable(
            tableAlias, columnTemplates, queryParameters, forCreateView: false, topQuery: select);
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
    /// Whether the current token starts a query after any opening parentheses.
    /// </summary>
    /// <returns>true when a query follows</returns>
    private bool IsQueryQuick()
    {
        int offset = position;
        while (tokens[offset].TokenType == Token.OpenParen)
        {
            offset++;
        }

        return IsDirectQuery(offset);
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

        if ((IsWord("UNNEST", consume: false) || IsWord("TABLE", consume: false)
            || IsWord("TABLE_DISTINCT", consume: false)) && position + 1 < tokens.Count
            && tokens[position + 1].TokenType == Token.OpenParen)
        {
            string name = Current.AsIdentifier!;
            Read();
            Read();
            Expressions.Functions.Table.ArrayTableFunction function = name.Equals("UNNEST", StringComparison.OrdinalIgnoreCase)
                ? ReadUnnestFunction()
                : ReadTableFunction(name.Equals("TABLE", StringComparison.OrdinalIgnoreCase)
                    ? Expressions.Functions.Table.ArrayTableFunction.Table : Expressions.Functions.Table.ArrayTableFunction.TableDistinct);
            Tables.FunctionTable table = new(session.GetCurrentSchema(), session, function);
            Tables.TableFilter filter = new(session, table, name, true, select, 0, null);
            return ReadCorrelation(filter);
        }

        Tables.Table fromTable;
        bool hasAlias = true;
        string tableName;
        if (IsWord("VALUES", consume: false))
        {
            // The table of a table value constructor: the rows a VALUES clause names.
            BitSet outerUsedParameters = OpenParametersScope();
            Query.Query values = ReadValues();
            List<Expressions.Parameter> queryParameters = CloseParametersScope(outerUsedParameters);
            tableName = "_VALUES_" + Current.Start.ToString(System.Globalization.CultureInfo.InvariantCulture);
            fromTable = values.ToTable(
                tableName, null, queryParameters, forCreateView: false, topQuery: select);
            hasAlias = false;
        }
        else
        {
            tableName = ReadIdentifier();
            Schemas.Schema schema = session.GetCurrentSchema();
            bool schemaExplicit = false;
            string? tableSchemaName = null;
            if (ReadIf(Token.Dot))
            {
                // The reference reads the schema of the name and looks the table up in it.
                string first = tableName;
                tableName = ReadIdentifier();
                string schemaName;
                if (CurrentType == Token.Dot
                    && first.Equals(session.GetDatabase().GetShortName(), StringComparison.OrdinalIgnoreCase))
                {
                    Read();
                    schemaName = tableName;
                    tableName = ReadIdentifier();
                }
                else
                {
                    schemaName = first;
                }

                Schemas.Schema? foundSchema = session.GetDatabase().FindSchema(schemaName);
                if (foundSchema is null)
                {
                    if (!("SYS".Equals(schemaName, StringComparison.OrdinalIgnoreCase)
                        && "DUAL".Equals(tableName, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw DbException.Get(Api.ErrorCode.SchemaNotFound1, schemaName);
                    }

                    foundSchema = session.GetCurrentSchema();
                }

                schema = foundSchema;
                schemaExplicit = true;
                tableSchemaName = schemaName;
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
            else if ((!schemaExplicit || "SYS".Equals(tableSchemaName, StringComparison.OrdinalIgnoreCase))
                && "DUAL".Equals(tableName, StringComparison.OrdinalIgnoreCase))
            {
                // Parser.isDualTable: DUAL is a table of SYS, but H2 also answers it in the
                // current schema when the name was not qualified.
                fromTable = new Tables.DualTable(session.GetDatabase());
            }
            else if (IsWord("TABLE"))
            {
                // The data change delta table: FINAL TABLE (INSERT INTO ...). The rows of the
                // table are the rows the statement changed, and the name in front of it says
                // which version of them the caller gets. It may still have an alias, which the
                // normal alias reader below takes when one was written.
                fromTable = ReadDataChangeDeltaTable(session, schema, tableName);
            }
            else
            {
                fromTable = ReadTableOrView(session, schema, tableName, searchPath: !schemaExplicit);
            }
        }

        string? filterAlias;
        Tables.IndexHints? indexHints = null;
        if (hasAlias && ReadIfUseIndex())
        {
            // USE INDEX before any alias, as the reference reads it.
            filterAlias = null;
            indexHints = ParseIndexHints(fromTable);
        }
        else
        {
            filterAlias = hasAlias ? ReadFromAlias() : null;
            if (filterAlias is not null && ReadIfUseIndex())
            {
                indexHints = ParseIndexHints(fromTable);
            }
        }
        return new(
            session, fromTable, filterAlias ?? (hasAlias ? null : tableName),
            rightsChecked: true, select, 0, indexHints);
    }

    /// <summary>
    /// Whether USE INDEX follows, read as one piece: a USE without INDEX belongs to
    /// something else, as the reference's readIfUseIndex resets over.
    /// </summary>
    /// <returns>true when USE INDEX was read</returns>
    private bool ReadIfUseIndex()
    {
        int index = position;
        if (!IsWord("USE"))
        {
            return false;
        }
        if (!IsWord("INDEX"))
        {
            position = index;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Reads the index names of a USE INDEX hint, as the reference's parseIndexHints does.
    /// Unknown names are refused with the reference's error, and the stored names are the
    /// canonical ones from the table rather than the written spellings.
    /// </summary>
    /// <param name="table">the table the hint is for</param>
    /// <returns>the hint</returns>
    private Tables.IndexHints ParseIndexHints(Tables.Table table)
    {
        Expect(Token.OpenParen, "(");
        List<string> indexNames = [];
        if (!ReadIf(Token.CloseParen))
        {
            do
            {
                string indexName = ReadIdentifier();
                string? canonical = null;
                foreach (Indexes.Index index in table.GetIndexes())
                {
                    if (index.GetName() is string name
                        && name.Equals(indexName, StringComparison.Ordinal))
                    {
                        canonical = name;
                        break;
                    }
                }
                if (canonical is null)
                {
                    throw DbException.Get(Api.ErrorCode.IndexNotFound1, indexName);
                }
                indexNames.Add(canonical);
            }
            while (ReadIfMore());
        }
        return Tables.IndexHints.CreateUseIndexHints(indexNames);
    }

    private Expressions.Functions.Table.ArrayTableFunction ReadUnnestFunction()
    {
        Expressions.Functions.Table.ArrayTableFunction function = new(Expressions.Functions.Table.ArrayTableFunction.Unnest);
        List<Tables.Column> columns = [];
        if (!ReadIf(Token.CloseParen))
        {
            int i = 0;
            do
            {
                Expression expression = ReadQueryOr();
                TypeInfo columnType = TypeInfo.TypeNull;
                TypeInfo? expressionType = expression.GetTypeIfStaticallyKnown(session!);
                if (expressionType is not null)
                {
                    if (expressionType.GetValueType() == Value.Json) columnType = TypeInfo.TypeJson;
                    else if (expressionType.GetValueType() == Value.Array) columnType = (TypeInfo)expressionType.GetExtTypeInfo()!;
                }
                function.AddParameter(expression);
                columns.Add(new Tables.Column("C" + (++i).ToString(System.Globalization.CultureInfo.InvariantCulture), columnType));
            }
            while (ReadIfMore());
        }
        if (IsWord("WITH", consume: false) && NextIs("ORDINALITY"))
        {
            Read(); Read();
            columns.Add(new Tables.Column("NORD", TypeInfo.TypeInteger));
        }
        function.SetColumns(columns);
        function.DoneWithParameters();
        return function;
    }

    private Expressions.Functions.Table.ArrayTableFunction ReadTableFunction(int functionType)
    {
        Expressions.Functions.Table.ArrayTableFunction function = new(functionType);
        List<Tables.Column> columns = [];
        do
        {
            string name = ReadIdentifier();
            TypeInfo type = ReadIfDataType() ?? throw DbException.GetSyntaxError(sql, Current.Start, "data type");
            columns.Add(new Tables.Column(name, type));
            Expect(Token.Equal, "=");
            function.AddParameter(ReadQueryOr());
        }
        while (ReadIfMore());
        function.SetColumns(columns);
        function.DoneWithParameters();
        return function;
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

    private string ReadIdentifierWithSchemaName(SessionLocal? session, out string? schemaName)
    {
        string name = ReadIdentifier();
        schemaName = null;
        if (ReadIf(Token.Dot))
        {
            string first = name;
            name = ReadIdentifier();
            if (CurrentType == Token.Dot && session is not null
                && first.Equals(session.GetDatabase().GetShortName(), StringComparison.OrdinalIgnoreCase))
            {
                Read();
                schemaName = name;
                name = ReadIdentifier();
            }
            else
            {
                schemaName = first;
            }
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

        Prepared prepared;
        if (IsWord("MERGE", consume: false))
        {
            prepared = PrepareMerge(session);
        }
        else
        {
            Statement statement = ReadStatement();
            prepared = statement switch
            {
                InsertStatement insert => PrepareInsert(session, insert),
                UpdateStatement update => PrepareUpdate(session, update),
                DeleteStatement delete => PrepareDelete(session, delete),
                _ => throw DbException.GetSyntaxError(sql, Current.Start, "INSERT"),
            };
        }

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
            case "E":
                {
                    string? text = tokens[index].IsQuoted ? null : ReadStringLiteralValue();
                    if (text is null) { position = index; return null; }
                    // H2 readTermWithIdentifier: only doubled backslashes are reduced.
                    return Expressions.ValueExpression.Get(ValueVarchar.Get(StringUtils.ReplaceAll(text, "\\\\", "\\")));
                }
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
                    // The reference reads both spellings: the EWKT text and the raw EWKB bytes.
                    Values.Value? literal = Current.GetValue(provider);
                    Values.Value? geometry = literal?.GetValueType() switch
                    {
                        Values.Value.Varchar => Values.ValueGeometry.Get(literal.GetString()!),
                        Values.Value.Varbinary => Values.ValueGeometry.GetFromEWKB(literal.GetBytesNoCopy()!),
                        _ => null,
                    };
                    if (geometry is null)
                    {
                        position = index;
                        return null;
                    }

                    Read();
                    return Expressions.ValueExpression.Get(geometry);
                }

            case "JSON":
                {
                    // The reference reads the document as text or as the bytes of one: the same
                    // normalization takes either.
                    Values.Value? literal = Current.GetValue(provider);
                    Values.Value? json = literal?.GetValueType() switch
                    {
                        Values.Value.Varchar => Values.ValueJson.FromJson(literal.GetString()!),
                        Values.Value.Varbinary => Values.ValueJson.FromJson(literal.GetBytesNoCopy()!),
                        _ => null,
                    };
                    if (json is null)
                    {
                        position = index;
                        return null;
                    }

                    Read();
                    return Expressions.ValueExpression.Get(json);
                }

            case "UUID":
                {
                    string? uuid = ReadStringLiteralValue();
                    if (uuid is null)
                    {
                        position = index;
                        return null;
                    }

                    return Expressions.ValueExpression.Get(Values.ValueUuid.Get(uuid));
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
    /// Builds the command of a MERGE statement, as the reference's parseMerge does: with
    /// USING it is a MergeUsing, without it the MySQL-style Merge.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the prepared statement</returns>
    private Prepared PrepareMerge(SessionLocal session)
    {
        ExpectWord("MERGE");
        ExpectWord("INTO");
        Tables.TableFilter targetTableFilter = ReadSimpleTableFilter(session);
        if (ReadIf(ParserUtil.Using))
        {
            return PrepareMergeUsing(session, targetTableFilter);
        }

        return PrepareMergeInto(session, targetTableFilter);
    }

    /// <summary>
    /// Builds the command of a MySQL-style MERGE INTO ... statement, as the reference's
    /// parseMergeInto does.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="targetTableFilter">the target table filter</param>
    /// <returns>the prepared statement</returns>
    private Prepared PrepareMergeInto(SessionLocal session, Tables.TableFilter targetTableFilter)
    {
        Tables.Table table = targetTableFilter.GetTable()!;
        Dml.Merge command = new(session, isReplace: false);
        command.SetTable(table);
        if (ReadIf(Token.OpenParen))
        {
            if (IsDirectQuery(position))
            {
                command.SetQuery(ParseQueryExpression(prepare: false));
                Expect(Token.CloseParen, ")");
                command.SetSql(sql, null);
                return command;
            }

            command.SetColumns(ReadColumnList(table));
        }

        if (IsWord("KEY", consume: false) && position + 1 < tokens.Count
            && tokens[position + 1].TokenType == Token.OpenParen)
        {
            Read();
            Expect(Token.OpenParen, "(");
            command.SetKeys(ReadColumnList(table));
        }

        if (IsWord("VALUES"))
        {
            do
            {
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

                command.AddRow([.. values]);
            }
            while (ReadIf(Token.Comma));
        }
        else
        {
            command.SetQuery(ParseQueryExpression(prepare: false));
        }

        command.SetSql(sql, null);
        return command;
    }

    /// <summary>
    /// Builds the command of a MERGE INTO ... USING ... ON ... WHEN ... statement, as the
    /// reference's parseMergeUsing does.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="targetTableFilter">the target table filter</param>
    /// <returns>the prepared statement</returns>
    private Prepared PrepareMergeUsing(SessionLocal session, Tables.TableFilter targetTableFilter)
    {
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
        // mergeWhere flag is set (Legacy and Oracle), and only when the clause has no
        // AND condition of its own.
        if (and is null && command.GetSession().GetMode().MergeWhere && ReadIf(ParserUtil.Where))
        {
            and = ReadQueryOr();
        }

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

            Tables.Column column = ReadTableColumn(filter);
            List<Expression>? indexes = null;
            while (ReadIf(Token.OpenBracket))
            {
                (indexes ??= []).Add(ReadExpression());
                Expect(Token.CloseBracket, "]");
            }
            Expect(Token.Equal, "=");
            list.AddSingle(column, indexes?.ToArray(), ReadExpressionOrDefault());
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
    /// Reads a column of the given table, with an optional table (and schema) prefix,
    /// as the reference's readTableColumn does. The prefix must name this filter's table.
    /// </summary>
    /// <param name="filter">the filter the column belongs to</param>
    /// <returns>the column</returns>
    private Tables.Column ReadTableColumn(Tables.TableFilter filter)
    {
        string columnName = ReadIdentifier();
        if (ReadIf(Token.Dot))
        {
            columnName = ReadTableColumn(filter, columnName);
        }

        return filter.GetTable()!.GetColumn(columnName);
    }

    /// <summary>
    /// Reads the rest of a qualified column name after its first part.
    /// </summary>
    /// <param name="filter">the filter the column belongs to</param>
    /// <param name="tableAlias">the table part read so far</param>
    /// <returns>the column name</returns>
    private string ReadTableColumn(Tables.TableFilter filter, string tableAlias)
    {
        string columnName = ReadIdentifier();
        if (ReadIf(Token.Dot))
        {
            string schema = tableAlias;
            tableAlias = columnName;
            columnName = ReadIdentifier();
            if (ReadIf(Token.Dot))
            {
                if (!schema.Equals(
                    session?.GetDatabase().GetShortName(), StringComparison.OrdinalIgnoreCase))
                {
                    throw DbException.Get(Api.ErrorCode.DatabaseNotFound1, schema);
                }

                schema = tableAlias;
                tableAlias = columnName;
                columnName = ReadIdentifier();
            }

            string? schemaName = filter.GetTable()!.GetSchema()?.GetName();
            if (!schema.Equals(schemaName, StringComparison.Ordinal))
            {
                throw DbException.Get(Api.ErrorCode.SchemaNotFound1, schema);
            }
        }

        // Identifiers arrive folded to upper case, as in the reference, so the alias
        // comparison is exact rather than case-insensitive.
        if (!tableAlias.Equals(filter.GetTableAlias(), StringComparison.Ordinal))
        {
            throw DbException.Get(Api.ErrorCode.TableOrViewNotFound1, tableAlias);
        }

        return columnName;
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
            command.SetAuthorization(ReadIdentifier());
        }

        if (IsWord("WITH"))
        {
            do
            {
                _ = ReadIdentifier();
            }
            while (ReadIf(Token.Comma));
        }

        return command;
    }

    /// <summary>
    /// Builds the command of a CREATE DOMAIN statement.
    /// </summary>
    private Prepared PrepareCreateSynonym(SessionLocal session)
    {
        ExpectWord("CREATE");
        bool orReplace = ReadIfWords("OR", "REPLACE");
        ExpectWord("SYNONYM");
        bool ifNotExists = ReadIfNotExists();
        string name = ReadIdentifierWithSchema(session, out Schemas.Schema synonymSchema);
        ExpectWord("FOR");
        string tableName = ReadIdentifierWithSchema(session, out Schemas.Schema targetSchema);
        Ddl.CreateSynonym command = new(session, synonymSchema);
        command.SetName(name);
        command.SetSynonymFor(tableName);
        command.SetOrReplace(orReplace);
        command.SetSynonymForSchema(targetSchema);
        command.SetIfNotExists(ifNotExists);
        if (IsWord("COMMENT"))
        {
            command.SetComment(ReadString());
        }

        return command;
    }

    private Prepared PrepareDropSynonym(SessionLocal session)
    {
        ExpectWord("DROP");
        ExpectWord("SYNONYM");
        bool ifExists = ReadIfExists(false);
        string name = ReadIdentifierWithSchema(session, out Schemas.Schema schema);
        Ddl.DropSynonym command = new(session, schema);
        command.SetSynonymName(name);
        command.SetIfExists(ifExists);
        return command;
    }

    private Prepared PrepareCreateConstant(SessionLocal session)
    {
        ExpectWord("CREATE");
        ExpectWord("CONSTANT");
        bool ifNotExists = ReadIfNotExists();
        string name = ReadIdentifierWithSchema(session, out Schemas.Schema schema);
        ExpectWord("VALUE");
        Expression expression = ReadExpression();
        Ddl.CreateConstant command = new(session, schema);
        command.SetSql(sql, null);
        command.SetConstantName(name);
        command.SetExpression(expression);
        command.SetIfNotExists(ifNotExists);
        return command;
    }

    private Prepared PrepareDropConstant(SessionLocal session)
    {
        ExpectWord("DROP");
        ExpectWord("CONSTANT");
        bool ifExists = ReadIfExists(false);
        string name = ReadIdentifierWithSchema(session, out Schemas.Schema schema);
        ifExists = ReadIfExists(ifExists);
        Ddl.DropConstant command = new(session, schema);
        command.SetSql(sql, null);
        command.SetConstantName(name);
        command.SetIfExists(ifExists);
        return command;
    }

    private Ddl.CreateDomain PrepareCreateDomain(SessionLocal session)
    {
        ExpectWord("CREATE");
        if (!IsWord("DOMAIN") && !IsWord("TYPE"))
        {
            ExpectWord("DATATYPE");
        }

        bool ifNotExists = ReadIfNotExists();
        string domainName = ReadIdentifierWithSchema(session, out Schemas.Schema schema);
        Ddl.CreateDomain command = new(session, schema);
        command.SetIfNotExists(ifNotExists);
        command.SetTypeName(domainName);
        _ = IsWord("AS");

        TypeInfo? dataType = ReadIfDataType();
        if (dataType is not null)
        {
            command.SetDataType(dataType);
        }
        else
        {
            string parentName = ReadIdentifierWithSchema(session, out Schemas.Schema parentSchema);
            command.SetParentDomain(parentSchema.FindDomain(parentName)
                ?? throw DbException.Get(Api.ErrorCode.DomainNotFound1, parentName));
        }

        if (IsWord("DEFAULT"))
        {
            command.SetDefaultExpression(ReadExpression());
        }

        if (ReadIfWords("ON", "UPDATE"))
        {
            command.SetOnUpdateExpression(ReadExpression());
        }

        if (IsWord("COMMENT"))
        {
            command.SetComment(ReadString());
        }

        while (true)
        {
            string? constraintName;
            if (IsWord("CONSTRAINT"))
            {
                constraintName = ReadIdentifier();
                ExpectWord("CHECK");
            }
            else if (IsWord("CHECK"))
            {
                constraintName = null;
            }
            else
            {
                break;
            }

            Ddl.AlterDomainAddConstraint constraint = new(session, schema, ifNotExists);
            constraint.SetConstraintName(constraintName);
            constraint.SetDomainName(domainName);
            parseDomainConstraint = true;
            try
            {
                constraint.SetCheckExpression(ReadExpression());
            }
            finally
            {
                parseDomainConstraint = false;
            }

            command.AddConstraintCommand(constraint);
        }

        return command;
    }

    /// <summary>
    /// Builds the command of an ALTER TYPE statement.
    /// </summary>
    private Ddl.AlterType PrepareAlterType(SessionLocal session)
    {
        ExpectWord("ALTER");
        ExpectWord("TYPE");
        string typeName = ReadIdentifierWithSchema(session, out Schemas.Schema schema);
        if (IsWord("ADD"))
        {
            Ddl.AlterType command = new(session, schema);
            command.SetDomainName(typeName);
            ExpectWord("VALUE");
            command.SetValue(ReadString());
            return command;
        }

        throw DbException.GetSyntaxError(sql, Current.Start);
    }

    /// <summary>
    /// Builds the command of an ALTER DOMAIN statement.
    /// </summary>
    private Ddl.AlterDomain PrepareAlterDomain(SessionLocal session)
    {
        ExpectWord("ALTER");
        ExpectWord("DOMAIN");
        bool ifDomainExists = ReadIfExists(false);
        string domainName = ReadIdentifierWithSchema(session, out Schemas.Schema schema);
        if (IsWord("ADD"))
        {
            bool ifNotExists = false;
            string? constraintName = null;
            if (IsWord("CONSTRAINT"))
            {
                ifNotExists = ReadIfNotExists();
                constraintName = ReadIdentifierWithSchema(session, out Schemas.Schema constraintSchema);
                if (!ReferenceEquals(schema, constraintSchema))
                {
                    throw DbException.Get(Api.ErrorCode.SchemaNameMustMatch);
                }
            }

            ExpectWord("CHECK");
            Ddl.AlterDomainAddConstraint command = new(session, schema, ifNotExists);
            command.SetDomainName(domainName);
            command.SetConstraintName(constraintName);
            parseDomainConstraint = true;
            try
            {
                command.SetCheckExpression(ReadExpression());
            }
            finally
            {
                parseDomainConstraint = false;
            }

            command.SetIfDomainExists(ifDomainExists);
            if (IsWord("NOCHECK"))
            {
                command.SetCheckExisting(false);
            }
            else
            {
                _ = IsWord("CHECK");
                command.SetCheckExisting(true);
            }

            return command;
        }

        if (IsWord("DROP"))
        {
            if (IsWord("CONSTRAINT"))
            {
                bool ifConstraintExists = ReadIfExists(false);
                string constraintName = ReadIdentifierWithSchema(session, out Schemas.Schema constraintSchema);
                if (!ReferenceEquals(schema, constraintSchema))
                {
                    throw DbException.Get(Api.ErrorCode.SchemaNameMustMatch);
                }

                Ddl.AlterDomainDropConstraint command = new(session, schema, ifConstraintExists);
                command.SetConstraintName(constraintName);
                command.SetDomainName(domainName);
                command.SetIfDomainExists(ifDomainExists);
                return command;
            }

            if (IsWord("DEFAULT"))
            {
                Ddl.AlterDomainExpressions command = new(session, schema, CommandInterface.AlterDomainDefault);
                command.SetDomainName(domainName);
                command.SetIfDomainExists(ifDomainExists);
                command.SetExpression(null);
                return command;
            }

            if (ReadIfWords("ON", "UPDATE"))
            {
                Ddl.AlterDomainExpressions command = new(session, schema, CommandInterface.AlterDomainOnUpdate);
                command.SetDomainName(domainName);
                command.SetIfDomainExists(ifDomainExists);
                command.SetExpression(null);
                return command;
            }
        }
        else if (IsWord("RENAME"))
        {
            if (IsWord("CONSTRAINT"))
            {
                string constraintName = ReadIdentifierWithSchema(session, out Schemas.Schema constraintSchema);
                if (!ReferenceEquals(schema, constraintSchema))
                {
                    throw DbException.Get(Api.ErrorCode.SchemaNameMustMatch);
                }

                ExpectWord("TO");
                Ddl.AlterDomainRenameConstraint command = new(session, schema);
                command.SetDomainName(domainName);
                command.SetIfDomainExists(ifDomainExists);
                command.SetConstraintName(constraintName);
                command.SetNewConstraintName(ReadIdentifier());
                return command;
            }

            ExpectWord("TO");
            string newName = ReadIdentifierWithSchema(session, out Schemas.Schema newSchema);
            if (!ReferenceEquals(schema, newSchema))
            {
                throw DbException.Get(Api.ErrorCode.SchemaNameMustMatch);
            }

            Ddl.AlterDomainRename rename = new(session, schema);
            rename.SetDomainName(domainName);
            rename.SetIfDomainExists(ifDomainExists);
            rename.SetNewDomainName(newName);
            return rename;
        }
        else
        {
            ExpectWord("SET");
            if (IsWord("DEFAULT"))
            {
                Ddl.AlterDomainExpressions command = new(session, schema, CommandInterface.AlterDomainDefault);
                command.SetDomainName(domainName);
                command.SetIfDomainExists(ifDomainExists);
                command.SetExpression(ReadExpression());
                return command;
            }

            if (ReadIfWords("ON", "UPDATE"))
            {
                Ddl.AlterDomainExpressions command = new(session, schema, CommandInterface.AlterDomainOnUpdate);
                command.SetDomainName(domainName);
                command.SetIfDomainExists(ifDomainExists);
                command.SetExpression(ReadExpression());
                return command;
            }
        }

        throw DbException.GetSyntaxError(sql, Current.Start);
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
    /// Builds the command of a DROP DOMAIN statement.
    /// </summary>
    private Ddl.DropDomain PrepareDropDomain(SessionLocal session)
    {
        ExpectWord("DROP");
        if (!IsWord("DOMAIN") && !IsWord("TYPE"))
        {
            ExpectWord("DATATYPE");
        }

        bool ifExists = ReadIfExists(false);
        string domainName = ReadIdentifierWithSchema(session, out Schemas.Schema schema);
        Ddl.DropDomain command = new(session, schema);
        command.SetDomainName(domainName);
        command.SetIfExists(ifExists);
        if (IsWord("CASCADE"))
        {
            command.SetCascade(true);
        }
        else
        {
            _ = IsWord("RESTRICT");
        }

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
    /// Builds the command of a DROP SCHEMA statement.
    /// </summary>
    private Ddl.DropSchema PrepareDropSchema(SessionLocal session)
    {
        ExpectWord("DROP");
        ExpectWord("SCHEMA");
        bool ifExists = ReadIfExists(false);
        string schemaName = ReadIdentifier();
        Ddl.DropSchema command = new(session);
        command.SetSchemaName(schemaName);
        command.SetIfExists(ifExists);
        if (IsWord("CASCADE"))
        {
            command.SetDropAction(Constraints.ConstraintActionType.CASCADE);
        }
        else if (IsWord("RESTRICT"))
        {
            command.SetDropAction(Constraints.ConstraintActionType.RESTRICT);
        }

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
    private Query.Query ParseQuerySelect()
    {
        SessionLocal current = session ?? throw new TODOException("Parser.parseQuerySelect: this parser has no session");
        ExpectWord("SELECT");
        Query.Select select = new(current);
        select.SetSql(sql, null);
        // Reference parseSelect: currentSelect is the select while its clauses are read.
        Query.Select? oldSelect = currentSelect;
        currentSelect = select;
        BitSet outerUsedParameters = OpenParametersScope();
        try
        {
            Query.Query result = ParseQuerySelectBody(select);
            select.SetParameterList(CloseParametersScope(outerUsedParameters));
            return result;
        }
        finally
        {
            currentSelect = oldSelect;
        }
    }

    private Query.Query ParseQuerySelectBody(Query.Select select)
    {
        SessionLocal current = session ?? throw new TODOException("Parser.parseQuerySelect: this parser has no session");
        if (current.GetMode().TopInSelect && ReadIfCompat("TOP"))
        {
            Query.Select? oldSelect = currentSelect;
            // The reference clears currentSelect so an aggregate cannot be read as TOP's limit.
            currentSelect = null;
            select.SetFetch(ReadQueryTerm().Optimize(current));
            if (IsWord("PERCENT"))
            {
                select.SetFetchPercent(true);
            }

            if (ReadIfWords("WITH", "TIES"))
            {
                select.SetWithTies(true);
            }

            currentSelect = oldSelect;
        }

        if (IsWord("DISTINCT"))
        {
            select.SetDistinct();
        }

        // The star of a select list, with the table it names: null when there is no star,
        // the empty string for the star of every table, and the alias for the star of one
        // table ("SELECT t.* FROM T AS t").
        string? star = null;
        List<string>? exceptColumns = null;
        if (ReadIf(Token.Asterisk))
        {
            star = string.Empty;
        }

        if (star is not null && IsWord("EXCEPT"))
        {
            Expect(Token.OpenParen, "(");
            exceptColumns = [];
            do
            {
                exceptColumns.Add(ReadIdentifier());
            }
            while (ReadIf(Token.Comma));

            Expect(Token.CloseParen, ")");
        }

        List<Expressions.Expression> selectItems = [];

        Expressions.Expression ReadSelectItem()
        {
            Expressions.Expression item;
            if (ReadIf(Token.Asterisk))
            {
                item = new Expressions.ExpressionColumn("*", null, 0);
            }
            else if (IsNameToken()
                && position + 2 < tokens.Count
                && tokens[position + 1].TokenType == Token.Dot
                && tokens[position + 2].TokenType == Token.Asterisk)
            {
                string itemTable = ReadIdentifier();
                Read();
                Read();
                item = new Expressions.ExpressionColumn("*", itemTable, 0);
            }
            else
            {
                // H2 parseSelectExpressions reads every item through readExpression;
                // aggregates are terms too, so their postfixes and operators use that
                // reader.
                item = ReadQueryOr(select);
            }

            string? alias = null;
            if (item is not Expressions.ExpressionColumn marker
                || marker.GetOriginalColumnName() != "*")
            {
                if (IsWord("AS"))
                {
                    alias = ReadIdentifier();
                }
                else if (IsNameToken() && !IsClauseWord(Current.AsIdentifier!))
                {
                    // The reference reads the alias of a select item the same way it reads
                    // the alias of a table: with the AS or without it, and the two forms
                    // mean the same thing.
                    alias = ReadIdentifier();
                }
            }

            if (alias is not null)
            {
                item = new Expressions.Alias(item, alias, aliasColumnName: true);
            }

            return item;
        }

        if (star is null)
        {
            do
            {
                selectItems.Add(ReadSelectItem());
            }
            while (ReadIf(Token.Comma));
        }
        else if (ReadIf(Token.Comma))
        {
            do
            {
                selectItems.Add(ReadSelectItem());
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

        void ExpandStar(string? starAlias, List<string>? except)
        {
            if (fromFilters.Count == 0)
            {
                throw DbException.GetSyntaxError(sql, Current.Start, "statement");
            }

            foreach (Tables.TableFilter f in fromFilters)
            {
                if (starAlias is { Length: > 0 }
                    && !f.GetTableAlias().Equals(starAlias, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (Tables.Column column in f.GetTable().GetColumns())
                {
                    if (!column.GetVisible()) continue;
                    if (except is not null)
                    {
                        bool excluded = false;
                        foreach (string c in except)
                        {
                            if (c.Equals(column.GetName(), StringComparison.OrdinalIgnoreCase))
                            {
                                excluded = true;
                                break;
                            }
                        }

                        if (excluded)
                        {
                            continue;
                        }
                    }

                    selectList.Add(new Expressions.ExpressionColumn(column, f, 0));
                }
            }
        }

        if (star is not null)
        {
            ExpandStar(star, exceptColumns);
        }

        foreach (Expressions.Expression item in selectItems)
        {
            if (item is Expressions.ExpressionColumn marker
                && marker.GetOriginalColumnName() == "*")
            {
                ExpandStar(marker.GetOriginalTableAliasName(), null);
            }
            else
            {
                selectList.Add(item);
            }
        }

        select.SetExpressions([.. selectList]);

        if (CurrentType == ParserUtil.Where)
        {
            Read();
            select.AddCondition(ReadExpressionWithGlobalConditions(select));
        }

        if (CurrentType == ParserUtil.Group)
        {
            Read();
            ExpectWord("BY");
            select.SetGroupQuery();
            List<Expressions.Expression> group = [];
            do
            {
                Expressions.Expression groupExpression = ReadQueryOr();
                if (current.GetMode().GroupByColumnIndex
                    && groupExpression is Expressions.ValueExpression valueExpression
                    && valueExpression.GetTypeInfo().GetValueType() == Values.Value.Integer)
                {
                    // H2 parseQuerySelect: an integer is a select-list position in these
                    // modes; a wildcard cannot be addressed by position.
                    if (star is not null)
                    {
                        throw DbException.GetSyntaxError(sql, Current.Start, "GROUP BY");
                    }

                    int index = valueExpression.GetValue(current).GetInt();
                    if (index < 1 || index > selectList.Count)
                    {
                        throw DbException.Get(Api.ErrorCode.GroupByNotInTheResult,
                            index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            selectList.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }

                    group.Add(selectList[index - 1]);
                }
                else
                {
                    group.Add(groupExpression);
                }
            }
            while (ReadIf(Token.Comma));

            select.SetGroupBy(group);
        }

        if (CurrentType == ParserUtil.Having)
        {
            Read();
            select.SetGroupQuery();
            select.SetHaving(ReadExpressionWithGlobalConditions(select));
        }

        if (IsWord("WINDOW"))
        {
            do
            {
                int index = Current.Start;
                string name = ReadIdentifier();
                ExpectWord("AS");
                if (!select.AddWindow(name, ReadWindowSpecification(select)))
                    throw DbException.GetSyntaxError(sql, index, "unique identifier");
            } while (ReadIf(Token.Comma));
        }

        if (CurrentType == ParserUtil.Qualify)
        {
            Read();
            select.SetWindowQuery();
            select.SetQualify(ReadExpressionWithGlobalConditions(select));
        }

        return select;
    }

    private void ParseEndOfQuery(Query.Query query)
    {
        Query.Select? select = query as Query.Select;
        if (CurrentType == ParserUtil.Order)
        {
            Read();
            ExpectWord("BY");
            List<Query.QueryOrderBy> order = [];
            do
            {
                // H2 parseEndOfQuery reads the whole expression before deciding whether
                // an integer literal is a select-list position.
                bool canBeNumber = CurrentType == Token.Literal;
                Query.QueryOrderBy item = new();
                Expression expression = ReadQueryOr(select);
                if (canBeNumber && expression is Expressions.ValueExpression
                    && expression.GetTypeInfo().GetValueType() == Value.Integer)
                {
                    item.columnIndexExpr = expression;
                }
                else if (expression is Expressions.Parameter)
                {
                    recompileAlways = true;
                    item.columnIndexExpr = expression;
                }
                else
                {
                    item.expression = expression;
                }

                item.sortType = ReadQuerySortType();
                order.Add(item);
            }
            while (ReadIf(Token.Comma));

            query.SetOrder(order);
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
            query.SetOffset(ReadQueryLimitExpression());
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
                query.SetFetch(Expressions.ValueExpression.Get(Values.ValueInteger.Get(1)));
            }
            else
            {
                query.SetFetch(ReadQueryLimitExpression());
                if (IsWord("PERCENT"))
                {
                    query.SetFetchPercent(true);
                }

                if (!IsWord("ROW"))
                {
                    ExpectWord("ROWS");
                }
            }

            if (IsWord("WITH"))
            {
                ExpectWord("TIES");
                query.SetWithTies(true);
            }
            else
            {
                ExpectWord("ONLY");
            }
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
                query.SetOffset(ReadQueryLimitExpression());
            }
            else if (ReadIf(Token.Comma))
            {
                // MySQL: [offset, ] rowcount.
                query.SetOffset(limit);
                limit = ReadQueryLimitExpression();
            }

            query.SetFetch(limit);
        }

        if (IsWord("FOR"))
        {
            if (IsWord("UPDATE"))
            {
                if (ReadIfCompat("OF"))
                {
                    do
                    {
                        _ = ReadIdentifierWithSchema(
                            session ?? throw new TODOException(
                                "Parser.parseEndOfQuery: this parser has no session"),
                            out _);
                    }
                    while (ReadIf(Token.Comma));
                }

                Query.ForUpdate forUpdate;
                if (IsWord("NOWAIT"))
                {
                    forUpdate = Query.ForUpdate.NoWait;
                }
                else if (IsWord("WAIT"))
                {
                    if (CurrentType != Token.Literal
                        || Current.GetValue(provider)?.GetBigDecimal() is not { } timeout
                        || timeout.CompareTo(Util.BigDecimal.Parse("2147483.647")) > 0)
                    {
                        throw DbException.GetSyntaxError(
                            sql, Current.Start, "timeout (0..2147483.647)");
                    }

                    Read();
                    Query.ForUpdate? wait = null;
                    if (timeout.Signum() >= 0)
                    {
                        wait = Query.ForUpdate.Wait(
                            (int)timeout.Multiply(Util.BigDecimal.FromLong(1_000)).ToBigInteger());
                    }

                    forUpdate = wait ?? throw DbException.GetSyntaxError(
                        sql, Current.Start, "timeout (0..2147483.647)");
                }
                else if (ReadIfWords("SKIP", "LOCKED"))
                {
                    forUpdate = Query.ForUpdate.SkipLocked;
                }
                else
                {
                    forUpdate = Query.ForUpdate.Default;
                }

                if (select is null)
                {
                    throw new TODOException(
                        "Parser.parseEndOfQuery: FOR UPDATE on a set operation is not ported");
                }

                select.SetForUpdate(forUpdate);
            }
            else if (ReadIfCompat("READ") || ReadIfCompat("FETCH"))
            {
                ExpectWord("ONLY");
            }
        }
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
            || word.Equals("QUALIFY", StringComparison.OrdinalIgnoreCase)
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
            || word.Equals("USING", StringComparison.OrdinalIgnoreCase)
            || word.Equals("WITH", StringComparison.OrdinalIgnoreCase)
            || word.Equals("KEY", StringComparison.OrdinalIgnoreCase)
            || word.Equals("VALUES", StringComparison.OrdinalIgnoreCase);
    /// <summary>
    /// Reads a column name with optional schema and table prefixes.
    /// </summary>
    /// <param name="schemaName">the schema prefix, or null</param>
    /// <param name="tableAlias">the table prefix, or null</param>
    /// <returns>the column name</returns>
    private string ReadQueryColumnName(out string? schemaName, out string? tableAlias)
    {
        string first = ReadIdentifier();
        schemaName = null;
        tableAlias = null;
        if (ReadIf(Token.Dot))
        {
            tableAlias = first;
            if (!Current.IsQuoted
                && (CurrentType == ParserUtil._ROWID_
                    || Current.AsIdentifier?.Equals(Tables.Column.RowId, StringComparison.OrdinalIgnoreCase) == true))
            {
                return Tables.Column.RowId;
            }

            string second = ReadIdentifier();
            if (ReadIf(Token.Dot))
            {
                schemaName = tableAlias;
                tableAlias = second;
                return ReadIdentifier();
            }

            return second;
        }

        return first;
    }

    /// <summary>
    /// Reads an aggregate using the branches of org.h2.command.Parser.readAggregate.
    /// </summary>
    /// <remarks>
    /// The name and opening parenthesis have already been consumed by ReadFunction.
    /// Recognition uses Aggregate's complete registry; an unfinished implementation refuses
    /// in Aggregate itself. The select is explicit in this parser's expression reader.
    /// </remarks>
    /// <param name="select">the select being read</param>
    /// <param name="aggregateType">the aggregate type</param>
    /// <param name="aggregateName">the upper-case spelling of the call</param>
    /// <returns>the aggregate</returns>
    private Expressions.Expression ReadQueryAggregate(
        Query.Select? select, Expressions.Aggregate.AggregateType aggregateType, string aggregateName)
    {
        select ??= currentSelect;
        if (select is null)
        {
            throw DbException.GetSyntaxError(sql, Current.Start);
        }

        Expressions.Aggregate.Aggregate aggregate;
        switch (aggregateType)
        {
            case Expressions.Aggregate.AggregateType.Count:
                if (ReadIf(Token.Asterisk))
                {
                    aggregate = new(Expressions.Aggregate.AggregateType.CountAll, [], select, false);
                }
                else
                {
                    bool distinct = ReadDistinctAgg();
                    aggregate = new(aggregateType, [ReadQueryOr(select)], select, distinct);
                }
                break;
            case Expressions.Aggregate.AggregateType.CovarPop:
            case Expressions.Aggregate.AggregateType.CovarSamp:
            case Expressions.Aggregate.AggregateType.Corr:
            case Expressions.Aggregate.AggregateType.RegrSlope:
            case Expressions.Aggregate.AggregateType.RegrIntercept:
            case Expressions.Aggregate.AggregateType.RegrCount:
            case Expressions.Aggregate.AggregateType.RegrR2:
            case Expressions.Aggregate.AggregateType.RegrAvgx:
            case Expressions.Aggregate.AggregateType.RegrAvgy:
            case Expressions.Aggregate.AggregateType.RegrSxx:
            case Expressions.Aggregate.AggregateType.RegrSyy:
            case Expressions.Aggregate.AggregateType.RegrSxy:
                aggregate = new(aggregateType, [ReadQueryOr(select), ReadNextArgument(select)], select, false);
                break;
            case Expressions.Aggregate.AggregateType.Histogram:
                aggregate = new(aggregateType, [ReadQueryOr(select)], select, false);
                break;
            case Expressions.Aggregate.AggregateType.Listagg:
            {
                bool distinct = ReadDistinctAgg();
                Expression arg = ReadQueryOr(select);
                Expressions.Aggregate.ListaggArguments extra = new();
                List<Query.QueryOrderBy>? order;
                if (aggregateName == "STRING_AGG")
                {
                    Expect(Token.Comma, ",");
                    extra.SetSeparator(ReadListaggSeparator());
                    order = ReadIfOrderBy(select);
                }
                else if (aggregateName == "GROUP_CONCAT")
                {
                    order = ReadIfOrderBy(select);
                    if (IsWord("SEPARATOR")) extra.SetSeparator(ReadListaggSeparator());
                }
                else
                {
                    if (ReadIf(Token.Comma)) extra.SetSeparator(ReadListaggSeparator());
                    if (IsWord("ON"))
                    {
                        ExpectWord("OVERFLOW");
                        if (IsWord("TRUNCATE"))
                        {
                            extra.SetOnOverflowTruncate(true);
                            if (CurrentType == Token.Literal) extra.SetFilter(ReadListaggSeparator());
                            if (!IsWord("WITH"))
                            {
                                ExpectWord("WITHOUT");
                                extra.SetWithoutCount(true);
                            }
                            ExpectWord("COUNT");
                        }
                        else ExpectWord("ERROR");
                    }
                    order = null;
                }
                int index = position;
                Expect(Token.CloseParen, ")");
                if (order is null && IsWord("WITHIN", false))
                {
                    aggregate = ReadWithinGroup(select, aggregateType, [arg], distinct, extra, false, false);
                }
                else
                {
                    position = index;
                    aggregate = new(aggregateType, [arg], select, distinct);
                    aggregate.SetExtraArguments(extra);
                    if (order is not null) aggregate.SetOrderByList(order);
                }
                break;
            }
            case Expressions.Aggregate.AggregateType.ArrayAgg:
            {
                bool distinct = ReadDistinctAgg();
                aggregate = new(aggregateType, [ReadQueryOr(select)], select, distinct);
                aggregate.SetOrderByList(ReadIfOrderBy(select));
                break;
            }
            case Expressions.Aggregate.AggregateType.Rank:
            case Expressions.Aggregate.AggregateType.DenseRank:
            case Expressions.Aggregate.AggregateType.PercentRank:
            case Expressions.Aggregate.AggregateType.CumeDist:
            {
                if (CurrentType == Token.CloseParen) return ReadWindowFunctionIf(aggregateName, select)!;
                List<Expression> expressions = [];
                do { expressions.Add(ReadQueryOr(select)); } while (ReadIfMore());
                aggregate = ReadWithinGroup(select, aggregateType, [.. expressions], false, null, true, false);
                break;
            }
            case Expressions.Aggregate.AggregateType.PercentileCont:
            case Expressions.Aggregate.AggregateType.PercentileDisc:
            {
                Expression num = ReadQueryOr(select);
                Expect(Token.CloseParen, ")");
                aggregate = ReadWithinGroup(select, aggregateType, [num], false, null, false, true);
                break;
            }
            case Expressions.Aggregate.AggregateType.Mode:
                if (ReadIf(Token.CloseParen))
                {
                    aggregate = ReadWithinGroup(select, aggregateType, [], false, null, false, true);
                }
                else
                {
                    Expression expr = ReadQueryOr(select);
                    aggregate = new(aggregateType, [], select, false);
                    if (IsWord("ORDER"))
                    {
                        ExpectWord("BY");
                        Expression expr2 = ReadQueryOr(select);
                        string sql1 = expr.GetSql(HasSql.DefaultSqlFlags);
                        string sql2 = expr2.GetSql(HasSql.DefaultSqlFlags);
                        if (sql1 != sql2)
                        {
                            throw DbException.GetSyntaxError(Api.ErrorCode.IdenticalExpressionsShouldBeUsed,
                                sql, Current.Start, sql1, sql2);
                        }
                        ReadAggregateOrder(aggregate, expr, true);
                    }
                    else ReadAggregateOrder(aggregate, expr, false);
                }
                break;
            case Expressions.Aggregate.AggregateType.JsonObjectagg:
            {
                bool withKey = IsWord("KEY");
                Expression key = ReadQueryOr(select);
                if (withKey) ExpectWord("VALUE");
                else if (!(IsWord("VALUE") || (provider.GetMode().AcceptsCommaAsJsonKeyValueSeparator
                    && ReadIf(Token.Comma)))) Expect(Token.Colon, ":");
                aggregate = new(aggregateType, [key, ReadQueryOr(select)], select, false);
                ReadJsonObjectFunctionFlags(aggregate, false);
                break;
            }
            case Expressions.Aggregate.AggregateType.JsonArrayagg:
            {
                bool distinct = ReadDistinctAgg();
                aggregate = new(aggregateType, [ReadQueryOr(select)], select, distinct);
                aggregate.SetOrderByList(ReadIfOrderBy(select));
                aggregate.SetFlags(Util.Json.JsonConstructorUtils.JsonAbsentOnNull);
                ReadJsonObjectFunctionFlags(aggregate, true);
                break;
            }
            default:
            {
                bool distinct = ReadDistinctAgg();
                aggregate = new(aggregateType, [ReadQueryOr(select)], select, distinct);
                break;
            }
        }

        Expect(Token.CloseParen, ")");
        ReadFilterAndOver(aggregate, select);
        return aggregate;
    }

    // Port of Parser.readWithinGroup. The closing parenthesis is consumed by readAggregate.
    private Expressions.Aggregate.Aggregate ReadWithinGroup(Query.Select select,
        Expressions.Aggregate.AggregateType aggregateType, Expression[] args, bool distinct,
        object? extraArguments, bool forHypotheticalSet, bool simple)
    {
        ExpectWord("WITHIN");
        ExpectWord("GROUP");
        Expect(Token.OpenParen, "(");
        ExpectWord("ORDER");
        ExpectWord("BY");
        Expressions.Aggregate.Aggregate aggregate = new(aggregateType, args, select, distinct);
        aggregate.SetExtraArguments(extraArguments);
        if (forHypotheticalSet)
        {
            List<Query.QueryOrderBy> order = new(args.Length);
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) Expect(Token.Comma, ",");
                order.Add(new() { expression = ReadQueryOr(select), sortType = ReadQuerySortType() });
            }
            aggregate.SetOrderByList(order);
        }
        else if (simple) ReadAggregateOrder(aggregate, ReadQueryOr(select), true);
        else
        {
            List<Query.QueryOrderBy> order = [];
            do { order.Add(new() { expression = ReadQueryOr(select), sortType = ReadQuerySortType() }); }
            while (ReadIf(Token.Comma));
            aggregate.SetOrderByList(order);
        }
        return aggregate;
    }

    private void ReadAggregateOrder(Expressions.Aggregate.Aggregate aggregate, Expression expr, bool parseSortType)
    {
        aggregate.SetOrderByList([new Query.QueryOrderBy
        {
            expression = expr,
            sortType = parseSortType ? ReadQuerySortType() : 0,
        }]);
    }

    private bool ReadDistinctAgg()
    {
        if (IsWord("DISTINCT")) return true;
        _ = IsWord("ALL");
        return false;
    }

    private void ReadFilterAndOver(Expressions.Aggregate.AbstractAggregate aggregate, Query.Select select)
    {
        if (IsWord("FILTER"))
        {
            Expect(Token.OpenParen, "(");
            ExpectWord("WHERE");
            aggregate.SetFilterCondition(ReadQueryOr());
            Expect(Token.CloseParen, ")");
        }
        if (IsWord("OVER"))
        {
            aggregate.SetOverCondition(CurrentType == Token.OpenParen ? ReadWindowSpecification(select)
                : new Expressions.Analysis.Window(ReadIdentifier(), null, null, null));
            select.SetWindowQuery();
        }
        else select.SetGroupQuery();
    }

    private List<Query.QueryOrderBy>? ReadIfOrderBy(Query.Select? currentSelect = null)
    {
        if (!IsWord("ORDER")) return null;
        ExpectWord("BY");
        List<Query.QueryOrderBy> order = [];
        do { order.Add(new Query.QueryOrderBy { expression = ReadQueryOr(currentSelect), sortType = ReadQuerySortType() }); }
        while (ReadIf(Token.Comma));
        return order;
    }

    private Expressions.Analysis.Window ReadWindowSpecification(Query.Select? currentSelect = null)
    {
        Expect(Token.OpenParen, "(");
        string? parent = null;
        if (CurrentType == ParserUtil.Identifier && (Current.IsQuoted ||
            !IsWord("PARTITION", false) && !IsWord("ROWS", false) && !IsWord("RANGE", false) && !IsWord("GROUPS", false)))
            parent = ReadIdentifier();
        List<Expression>? partition = null;
        if (IsWord("PARTITION"))
        {
            ExpectWord("BY"); partition = [];
            do { partition.Add(ReadQueryOr(currentSelect)); } while (ReadIf(Token.Comma));
        }
        List<Query.QueryOrderBy>? order = ReadIfOrderBy(currentSelect);
        Expressions.Analysis.WindowFrame? frame = ReadWindowFrame();
        Expect(Token.CloseParen, ")");
        return new Expressions.Analysis.Window(parent, partition, order, frame);
    }

    private Expressions.Analysis.WindowFrame? ReadWindowFrame()
    {
        Expressions.Analysis.WindowFrameUnits units;
        if (IsWord("ROWS")) units = Expressions.Analysis.WindowFrameUnits.Rows;
        else if (IsWord("RANGE")) units = Expressions.Analysis.WindowFrameUnits.Range;
        else if (IsWord("GROUPS")) units = Expressions.Analysis.WindowFrameUnits.Groups;
        else return null;
        Expressions.Analysis.WindowFrameBound starting, following = null!;
        if (IsWord("BETWEEN"))
        {
            starting = ReadWindowFrameRange(false); ExpectWord("AND"); following = ReadWindowFrameRange(false);
        }
        else starting = ReadWindowFrameRange(true);
        int sqlIndex = Current.Start;
        Expressions.Analysis.WindowFrameExclusion exclusion = Expressions.Analysis.WindowFrameExclusion.ExcludeNoOthers;
        if (IsWord("EXCLUDE"))
        {
            if (IsWord("CURRENT")) { ExpectWord("ROW"); exclusion = Expressions.Analysis.WindowFrameExclusion.ExcludeCurrentRow; }
            else if (IsWord("GROUP")) exclusion = Expressions.Analysis.WindowFrameExclusion.ExcludeGroup;
            else if (IsWord("TIES")) exclusion = Expressions.Analysis.WindowFrameExclusion.ExcludeTies;
            else { ExpectWord("NO"); ExpectWord("OTHERS"); }
        }
        Expressions.Analysis.WindowFrame frame = new(units, starting, following, exclusion);
        if (!frame.IsValid()) throw DbException.GetSyntaxError(sql, sqlIndex);
        return frame;
    }

    private Expressions.Analysis.WindowFrameBound ReadWindowFrameRange(bool startingOnly)
    {
        if (IsWord("UNBOUNDED"))
        {
            if (IsWord("PRECEDING")) return new(Expressions.Analysis.WindowFrameBoundType.UnboundedPreceding, null);
            if (startingOnly) ExpectWord("PRECEDING");
            ExpectWord("FOLLOWING"); return new(Expressions.Analysis.WindowFrameBoundType.UnboundedFollowing, null);
        }
        if (IsWord("CURRENT")) { ExpectWord("ROW"); return new(Expressions.Analysis.WindowFrameBoundType.CurrentRow, null); }
        Expression value = ReadQueryOr();
        if (IsWord("PRECEDING")) return new(Expressions.Analysis.WindowFrameBoundType.Preceding, value);
        if (startingOnly) ExpectWord("PRECEDING");
        ExpectWord("FOLLOWING"); return new(Expressions.Analysis.WindowFrameBoundType.Following, value);
    }

    /// <summary>
    /// Reads the NULL/ABSENT ON NULL and WITH/WITHOUT UNIQUE KEYS clauses of a JSON expression.
    /// </summary>
    /// <param name="function">the expression being read</param>
    /// <param name="forArray">whether the expression is an array constructor or aggregate</param>
    /// <returns>true if flags were consumed</returns>
    private bool ReadJsonObjectFunctionFlags(Expressions.ExpressionWithFlags function, bool forArray)
    {
        bool result = false;
        int flags = function.GetFlags();
        if (ReadIfWords("NULL", "ON", "NULL"))
        {
            flags &= ~Util.Json.JsonConstructorUtils.JsonAbsentOnNull;
            result = true;
        }
        else if (ReadIfWords("ABSENT", "ON", "NULL"))
        {
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
        return result;
    }

    /// <summary>
    /// Reads a run of keyword words in order, consuming them only when the whole run matches.
    /// </summary>
    /// <param name="words">the words, in order</param>
    /// <returns>whether the run matched</returns>
    private bool ReadIfWords(params string[] words)
    {
        // The peek walks forward: each word is checked at its own position, and a quoted
        // name never matches, as in the reference's testToken.
        for (int i = 0; i < words.Length; i++)
        {
            if (position + i >= tokens.Count)
            {
                return false;
            }

            Token token = tokens[position + i];
            string? name = token.AsIdentifier;
            if (token.IsQuoted || name is null || !name.Equals(words[i], StringComparison.OrdinalIgnoreCase))
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
    private Expressions.Expression ReadExpressionWithGlobalConditions(Query.Select? select)
    {
        Expressions.Expression expression = ReadQueryOr(select);
        if (IsWord("_LOCAL_AND_GLOBAL_"))
        {
            expression = new Expressions.Conditions.ConditionLocalAndGlobal(
                expression, ReadQueryOr(select));
        }

        return expression;
    }

    private Expressions.Expression ReadQueryOr(Query.Select? aggregateSelect = null)
    {
        Expressions.Expression left = ReadQueryAnd(aggregateSelect);
        if (!ReadIf(ParserUtil.Or)) return left;
        Expressions.Expression right = ReadQueryAnd(aggregateSelect);
        if (!ReadIf(ParserUtil.Or))
        {
            return new Expressions.Conditions.ConditionAndOr(
                Expressions.Conditions.ConditionAndOr.Or, left, right);
        }
        // H2 readExpressionPart2: flatten at parse time, before mapColumns / optimize.
        List<Expressions.Expression> expressions = [left, right];
        do { expressions.Add(ReadQueryAnd(aggregateSelect)); } while (ReadIf(ParserUtil.Or));
        return new Expressions.Conditions.ConditionAndOrN(Expressions.Conditions.ConditionAndOr.Or, expressions);
    }

    /// <summary>
    /// Reads an AND of comparisons.
    /// </summary>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    private Expressions.Expression ReadQueryAnd(Query.Select? aggregateSelect = null)
    {
        Expressions.Expression left = ReadQueryComparison(aggregateSelect);
        if (!ReadIf(ParserUtil.And)) return left;
        Expressions.Expression right = ReadQueryComparison(aggregateSelect);
        if (!ReadIf(ParserUtil.And))
        {
            return new Expressions.Conditions.ConditionAndOr(
                Expressions.Conditions.ConditionAndOr.And, left, right);
        }
        // H2 readAnd: long generated predicates must not become a recursive binary spine.
        List<Expressions.Expression> expressions = [left, right];
        do { expressions.Add(ReadQueryComparison(aggregateSelect)); } while (ReadIf(ParserUtil.And));
        return new Expressions.Conditions.ConditionAndOrN(Expressions.Conditions.ConditionAndOr.And, expressions);
    }

    /// <summary>
    /// Reads concatenation and compatibility regex operators, as the reference's readConcat does.
    /// </summary>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    // H2 readConcat: concatenation binds less tightly than arithmetic.
    private Expressions.Expression ReadQueryConcat(Query.Select? aggregateSelect = null)
    {
        Expression op1 = ReadQuerySum(aggregateSelect);
        while (true)
        {
            switch (CurrentType)
            {
                case Token.Concatenation:
                    {
                        Read();
                        Expression op2 = ReadQuerySum(aggregateSelect);
                        if (ReadIf(Token.Concatenation))
                        {
                            Expressions.ConcatenationOperation concat = new();
                            concat.AddParameter(op1);
                            concat.AddParameter(op2);
                            do { concat.AddParameter(ReadQuerySum(aggregateSelect)); }
                            while (ReadIf(Token.Concatenation));
                            concat.DoneWithParameters();
                            op1 = concat;
                        }
                        else op1 = new Expressions.ConcatenationOperation(op1, op2);
                        break;
                    }
                case Token.Tilde:
                    op1 = ReadTildeCondition(op1, false, aggregateSelect);
                    break;
                case Token.NotTilde:
                    op1 = ReadTildeCondition(op1, true, aggregateSelect);
                    break;
                default:
                    return op1;
            }
        }
    }

    // H2 readTildeCondition: the asterisk selects a case-insensitive VARCHAR cast.
    private Expression ReadTildeCondition(Expression expression, bool not, Query.Select? aggregateSelect)
    {
        Read();
        if (ReadIf(Token.Asterisk))
            expression = new Expressions.Functions.CastSpecification(expression, TypeInfo.TypeVarcharIgnorecase);
        return new Expressions.Conditions.CompareLike(session!.GetCompareMode(), expression, not,
            whenOperand: false, ReadQuerySum(aggregateSelect), escape: null,
            Expressions.Conditions.CompareLike.LikeType.Regexp);
    }

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

        // H2 readCondition: EXISTS is a predicate, parsed before readConcat.
        if (ReadIf(ParserUtil.Exists))
        {
            Expect(Token.OpenParen, "(");
            Query.Query query = ParseQueryExpression(prepare: false);
            // Preserve the query shape (including UNION / DISTINCT), as in Java.
            Expect(Token.CloseParen, ")");
            return new Expressions.Conditions.ExistsPredicate(query);
        }

        // H2 readCondition: UNIQUE is a predicate over a subquery, parsed before readConcat.
        if (CurrentType == ParserUtil.Unique)
        {
            Read();
            Engine.NullsDistinct nullsDistinct = ReadNullsDistinct(Engine.NullsDistinct.Distinct);
            Expect(Token.OpenParen, "(");
            Query.Query subquery = ParseQueryExpression(prepare: false);
            Expect(Token.CloseParen, ")");
            return new Expressions.Conditions.UniquePredicate(subquery, nullsDistinct);
        }

        // H2 readCondition: INTERSECTS(operand, operand) is a spatial comparison, not a call.
        if (IsWord("INTERSECTS", consume: false)
            && position + 1 < tokens.Count && tokens[position + 1].TokenType == Token.OpenParen)
        {
            Read();
            Expect(Token.OpenParen, "(");
            Expression first = ReadQueryConcat(aggregateSelect);
            Expect(Token.Comma, ",");
            Expression second = ReadQueryConcat(aggregateSelect);
            Expect(Token.CloseParen, ")");
            return new Expressions.Conditions.Comparison(
                Expressions.Conditions.Comparison.SpatialIntersects, first, second, false);
        }

        Expressions.Expression left = ReadQueryConcat(aggregateSelect);
        while (true)
        {
            if (CurrentType is Token.Equal or Token.NotEqual or Token.Smaller or Token.Bigger
                or Token.SmallerEqual or Token.BiggerEqual)
            {
                int compareType = ReadQueryComparisonType();
                left = ReadComparison(left, compareType, aggregateSelect);
                continue;
            }

            if (CurrentType == ParserUtil.Is)
            {
                // Reference readConditionIs: NULL, DISTINCT, TRUE, FALSE, UNKNOWN, OF, JSON.
                Read();
                bool not = ReadIf(ParserUtil.Not);
                if (CurrentType == ParserUtil.Null)
                {
                    Read();
                    left = new Expressions.Conditions.ConditionIsNull(left, not, whenOperand: false);
                }
                else if (CurrentType == ParserUtil.Distinct)
                {
                    Read();
                    ExpectWord("FROM");
                    left = ReadComparison(left, not ? Expressions.Conditions.Comparison.EqualNullSafe
                        : Expressions.Conditions.Comparison.NotEqualNullSafe, aggregateSelect);
                }
                else if (CurrentType is ParserUtil.True or ParserUtil.False)
                {
                    bool value = CurrentType == ParserUtil.True;
                    Read();
                    left = new Expressions.Conditions.BooleanTest(left, not, whenOperand: false, value);
                }
                else if (CurrentType == ParserUtil.Unknown)
                {
                    Read();
                    left = new Expressions.Conditions.BooleanTest(left, not, whenOperand: false, null);
                }
                else if (IsWord("OF"))
                {
                    Expect(Token.OpenParen, "(");
                    List<TypeInfo> typeList = [];
                    do
                    {
                        typeList.Add(ReadDataType());
                    }
                    while (ReadIfMore());
                    left = new Expressions.Conditions.TypePredicate(left, not, whenOperand: false, [.. typeList]);
                }
                else if (IsWord("JSON"))
                {
                    left = ReadIsJsonPredicate(left, not);
                }
                else
                {
                    throw DbException.GetSyntaxError(sql, Current.Start, "NULL");
                }
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

                Expressions.Expression a = ReadQueryConcat(aggregateSelect);
                ExpectWord("AND");
                left = new Expressions.Conditions.BetweenPredicate(
                    left, not, whenOperand: false, symmetric, a, ReadQueryConcat(aggregateSelect));
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
                    values.Add(ReadQueryConcat(aggregateSelect));
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

    // H2 readComparison: quantified comparison accepts either a subquery or an array.
    private Expression ReadComparison(Expression left, int compareType, Query.Select? select)
    {
        bool all = IsWord("ALL", consume: false);
        if (all || IsWord("ANY", consume: false) || IsWord("SOME", consume: false))
        {
            Read();
            Expect(Token.OpenParen, "(");
            Expression result = IsDerivedQuery()
                ? new Expressions.Conditions.ConditionInQuery(left, false, false, ParseQueryExpression(prepare: false), all, compareType)
                : new Expressions.Conditions.ConditionInArray(left, false, ReadQueryOr(select), all, compareType);
            Expect(Token.CloseParen, ")");
            return result;
        }
        return new Expressions.Conditions.Comparison(compareType, left, ReadQueryConcat(select), false);
    }

    /// <summary>
    /// Reads the kind and uniqueness of an IS JSON predicate, as the reference's
    /// readJsonPredicate does.
    /// </summary>
    /// <param name="left">the expression to test</param>
    /// <param name="not">whether this is IS NOT JSON</param>
    /// <returns>the predicate</returns>
    private Expressions.Conditions.IsJsonPredicate ReadIsJsonPredicate(Expression left, bool not)
    {
        Util.Json.JsonItemType itemType;
        if (ReadIf(ParserUtil.Value))
        {
            itemType = Util.Json.JsonItemType.Value;
        }
        else if (ReadIf(ParserUtil.Array))
        {
            itemType = Util.Json.JsonItemType.Array;
        }
        else if (IsWord("OBJECT"))
        {
            itemType = Util.Json.JsonItemType.Object;
        }
        else if (IsWord("SCALAR"))
        {
            itemType = Util.Json.JsonItemType.Scalar;
        }
        else
        {
            itemType = Util.Json.JsonItemType.Value;
        }

        bool unique = false;
        if (ReadIfWords("WITH", "UNIQUE"))
        {
            _ = IsWord("KEYS");
            unique = true;
        }
        else if (ReadIfWords("WITHOUT", "UNIQUE"))
        {
            _ = IsWord("KEYS");
        }

        return new Expressions.Conditions.IsJsonPredicate(left, not, whenOperand: false, unique, itemType);
    }

    /// <summary>
    /// Reads one operand: a parenthesized expression, a NOT, a literal, a parameter, or a
    /// column in name form.
    /// </summary>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    private Expressions.Expression ReadQueryTerm(Query.Select? aggregateSelect = null)
    {
        Expression expression = ReadTermWithoutPostfix(aggregateSelect);
        for (;;)
        {
            if (ReadIf(Token.OpenBracket))
            {
                expression = new Expressions.ArrayElementReference(expression, ReadQueryOr(aggregateSelect));
                Expect(Token.CloseBracket, "]");
                continue;
            }

            if (ReadIf(Token.Dot))
            {
                expression = new Expressions.FieldReference(expression, ReadIdentifier());
                continue;
            }

            if (ReadIf(Token.ColonColon))
            {
                expression = ReadColonColonAfterTerm(expression);
                continue;
            }

            int timeZoneIndex = position;
            if (IsWord("AT"))
            {
                if (ReadIfWords("TIME", "ZONE"))
                {
                    expression = new Expressions.TimeZoneOperation(
                        expression, ReadQueryOr(aggregateSelect));
                    continue;
                }

                if (IsWord("LOCAL"))
                {
                    expression = new Expressions.TimeZoneOperation(expression, null);
                    continue;
                }

                position = timeZoneIndex;
            }

            // Parser.readTerm: FORMAT JSON is a postfix over any expression shape.
            if (ReadIfWords("FORMAT", "JSON"))
            {
                expression = new Expressions.Format(expression, Expressions.Format.FormatEnum.Json);
                continue;
            }

            break;
        }
        return expression;
    }

    // Java readTerm's postfix operations are applied after every term shape.
    private Expressions.Expression ReadTermWithoutPostfix(Query.Select? aggregateSelect)
    {
        if (parseDomainConstraint && Current.AsIdentifier?.Equals("VALUE", StringComparison.OrdinalIgnoreCase) == true)
        {
            Read();
            return new Expressions.DomainValueExpression();
        }

        if (ReadIf(ParserUtil.Array))
        {
            if (!ReadIf(Token.OpenBracket))
            {
                Expect(Token.OpenParen, "(");
                Query.Query query = ParseQueryExpression(prepare: false);
                Expect(Token.CloseParen, ")");
                return new Expressions.ArrayConstructorByQuery(query);
            }

            if (ReadIf(Token.CloseBracket))
            {
                return Expressions.ValueExpression.Get(ValueArray.Empty);
            }

            List<Expression> elements = [];
            do
            {
                elements.Add(ReadQueryOr(aggregateSelect));
            }
            while (ReadIf(Token.Comma));

            Expect(Token.CloseBracket, "]");
            return new Expressions.ExpressionList([.. elements], isArray: true);
        }

        if (ReadIf(Token.OpenParen))
        {
            // H2 readTermWithoutIdentifier: an empty parenthesized value is an empty ROW.
            if (ReadIf(Token.CloseParen))
            {
                return Expressions.ValueExpression.Get(ValueRow.Empty);
            }

            if (IsDerivedQuery())
            {
                Query.Query query = ParseQueryExpression(prepare: false);
                Expect(Token.CloseParen, ")");
                return new Expressions.Subquery(query);
            }

            Expressions.Expression nested = ReadQueryOr(aggregateSelect);
            if (ReadIf(Token.Comma))
            {
                List<Expression> list = [nested];
                do { list.Add(ReadQueryOr(aggregateSelect)); } while (ReadIf(Token.Comma));
                Expect(Token.CloseParen, ")");
                return new Expressions.ExpressionList([.. list], false);
            }
            Expect(Token.CloseParen, ")");
            if (nested is Expressions.BinaryOperation binaryOperation
                && binaryOperation.GetOperationType() == Expressions.BinaryOperation.OpType.MINUS)
            {
                TypeInfo? intervalType = ReadIntervalQualifier();
                if (intervalType != null) binaryOperation.SetForcedType(intervalType);
            }
            return nested;
        }

        if (ReadIf(ParserUtil.Interval))
        {
            return ReadInterval();
        }

        if (ReadIf(ParserUtil.Row))
        {
            Expect(Token.OpenParen, "(");
            List<Expression> list = [];
            if (!ReadIf(Token.CloseParen))
            {
                do { list.Add(ReadQueryOr(aggregateSelect)); } while (ReadIfMore());
            }
            return list.Count == 0 ? Expressions.ValueExpression.Get(ValueRow.Empty)
                : new Expressions.ExpressionList([.. list], false);
        }

        if (IsWord("JSON", consume: false) && position + 1 < tokens.Count
            && tokens[position + 1].GetValue(provider) is Value jsonLiteral
            && jsonLiteral.GetValueType() is Value.Varchar or Value.Varbinary)
        {
            Read(); Read();
            return Expressions.ValueExpression.Get(jsonLiteral.GetValueType() == Value.Varchar
                ? ValueJson.FromJson(jsonLiteral.GetString()!) : ValueJson.FromJson(jsonLiteral.GetBytesNoCopy()!));
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

        if (CurrentType == ParserUtil.Value && parseDomainConstraint)
        {
            Read();
            return new Expressions.DomainValueExpression();
        }

        if (CurrentType is ParserUtil.True or ParserUtil.False)
        {
            bool value = CurrentType == ParserUtil.True;
            Read();
            return value ? Expressions.ValueExpression.True : Expressions.ValueExpression.False;
        }

        if (CurrentType == ParserUtil.Unknown)
        {
            Read();
            return Expressions.TypedValueExpression.Unknown;
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

        if (CurrentType is ParserUtil.CurrentCatalog or ParserUtil.CurrentPath or ParserUtil.CurrentRole
            or ParserUtil.CurrentSchema or ParserUtil.CurrentUser or ParserUtil.User
            or ParserUtil.SessionUser or ParserUtil.SystemUser)
        {
            int specification = CurrentType switch
            {
                ParserUtil.CurrentCatalog => Expressions.Functions.CurrentGeneralValueSpecification.CurrentCatalog,
                ParserUtil.CurrentPath => Expressions.Functions.CurrentGeneralValueSpecification.CurrentPath,
                ParserUtil.CurrentRole => Expressions.Functions.CurrentGeneralValueSpecification.CurrentRole,
                ParserUtil.CurrentSchema => Expressions.Functions.CurrentGeneralValueSpecification.CurrentSchema,
                ParserUtil.CurrentUser or ParserUtil.User => Expressions.Functions.CurrentGeneralValueSpecification.CurrentUser,
                ParserUtil.SessionUser => Expressions.Functions.CurrentGeneralValueSpecification.SessionUser,
                _ => Expressions.Functions.CurrentGeneralValueSpecification.SystemUser,
            };
            Read();
            if (ReadIf(Token.OpenParen))
            {
                Expect(Token.CloseParen, ")");
            }

            return new Expressions.Functions.CurrentGeneralValueSpecification(specification);
        }

        if (CurrentType == Token.At)
        {
            Read();
            SessionLocal variableSession = session
                ?? throw new TODOException("Parser.readTermWithoutIdentifier: @variable: no session");
            Expressions.Variable variable = new(variableSession, ReadIdentifier());
            return ReadIf(Token.ColonEq)
                ? new Expressions.Functions.SetFunction(variable, ReadExpression())
                : variable;
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
            return ReadParameter();
        }

        if (CurrentType == Token.At)
        {
            // The reference binds @name to a Variable and := to a SetFunction; the assignment
            // operator is not ported, the variable is.
            SessionLocal current = session
                ?? throw new TODOException("Parser.readTerm: this parser has no session");
            Read();
            return new Expressions.Variable(current, ReadIdentifier());
        }

        Values.Value? literal = Current.GetValue(provider);
        if (literal is not null)
        {
            Read();
            return Expressions.ValueExpression.Get(literal);
        }

        if (CurrentType == ParserUtil.Rownum)
        {
            // Reference readTermWithoutIdentifier: ROWNUM uses the current select, or the
            // prepared statement when there is none (getCurrentPreparedOrSelect).
            Read();
            if (ReadIf(Token.OpenParen))
            {
                Expect(Token.CloseParen, ")");
            }

            Prepared? context = aggregateSelect ?? currentSelect ?? currentPrepared;
            if (context is null)
            {
                if (session is not null)
                {
                    // The reference would have currentPrepared here (it sets it inside
                    // parseInsert/parseUpdate and the rest). This port's parse-only layer
                    // builds those statement records without a command, so there is
                    // nothing to read the row number from yet.
                    throw new TODOException(
                        "Parser.readTerm: ROWNUM needs currentPrepared, which the DML parse-only layer does not set");
                }

                throw DbException.GetSyntaxError(sql, Current.Start, "SELECT");
            }

            return new Expressions.Rownum(context);
        }

        if (CurrentType == ParserUtil.Values
            && position + 1 < tokens.Count
            && tokens[position + 1].TokenType == Token.OpenParen
            && provider.GetMode().OnDuplicateKeyUpdate
            && currentPrepared is Dml.Insert insert
            && insert.GetTable() is Table insertTable)
        {
            Read();
            Expect(Token.OpenParen, "(");
            Tables.Column column = ReadTableColumn(new Tables.TableFilter(
                session!, insertTable, alias: null, rightsChecked: true, select: null, 0, indexHints: null));
            Expect(Token.CloseParen, ")");
            return new Modes.OnDuplicateKeyValues(column, insert);
        }

        if (Current.AsIdentifier is not null
            && position + 2 < tokens.Count
            && tokens[position + 1].TokenType == Token.Dot
            && tokens[position + 2].TokenType == ParserUtil._ROWID_)
        {
            string alias = ReadIdentifier();
            Read();
            Read();
            return Expressions.ExpressionColumn.RowId(alias);
        }

        if (CurrentType == ParserUtil._ROWID_)
        {
            Read();
            return Expressions.ExpressionColumn.RowId(null);
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
                switch (CurrentType)
                {
                    case ParserUtil.Any:
                    case ParserUtil.Some:
                        // Parser.readTermWithoutIdentifier has explicit keyword branches.
                        Read();
                        Expect(Token.OpenParen, "(");
                        return ReadQueryAggregate(aggregateSelect, Expressions.Aggregate.AggregateType.Any, "ANY");

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

                    case ParserUtil.Set:
                        // The reference's readSetFunction: SET(@var, value) assigns and answers
                        // the value. The user-defined function alias override around it needs
                        // CREATE ALIAS, which is not ported.
                        Read();
                        Expect(Token.OpenParen, "(");
                        return new Expressions.Functions.SetFunction(
                            ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect));

                    default:
                        // Every other function spelled as a keyword — the CURRENT_* value
                        // functions, CAST, ROW, TABLE and the rest — is read by the reference's
                        // own keyword branches, which are not ported. Naming the keyword beats
                        // answering that no such function exists.
                        throw new TODOException(
                            "Parser.readTerm: the keyword function " + Current.AsIdentifier + " is not ported");
                }
            }

            string columnName = ReadQueryColumnName(out string? schemaName, out string? tableAlias);
            if (columnName.Equals(Tables.Column.RowId, StringComparison.OrdinalIgnoreCase))
            {
                return Expressions.ExpressionColumn.RowId(tableAlias);
            }

            return new Expressions.ExpressionColumn(columnName, schemaName, tableAlias, 0);
        }

        throw new TODOException("Parser.parseQuerySelect: this expression is not ported");
    }

    /// <summary>
    /// Reads the type after a postfix :: cast, including PostgreSQL's REGCLASS expression.
    /// </summary>
    private Expressions.Expression ReadColonColonAfterTerm(Expressions.Expression r)
    {
        // The parser's mode comes from the cast provider, as in its other mode branches.
        if (provider.GetMode().Enum == Mode.ModeEnum.PostgreSql)
        {
            if (ReadIfCompat("PG_CATALOG"))
            {
                Expect(Token.Dot, ".");
            }

            if (ReadIfCompat("REGCLASS"))
            {
                return new Modes.Regclass(r);
            }
        }

        // ReadColumnWithType is the same type reader used by CAST in this parser port.
        return new Expressions.Functions.CastSpecification(r, ReadColumnWithType());
    }

    /// <summary>
    /// Reads the name of a sequence and finds it, as the reference's readSequence.
    /// </summary>
    /// <param name="schema">the schema</param>
    /// <returns>the sequence</returns>
    private Schemas.Sequence ReadSequence(SessionLocal session)
    {
        string sequenceName = ReadIdentifierWithSchemaName(out string? schemaName);
        if (schemaName is not null) return session.GetDatabase().GetSchema(schemaName).GetSequence(sequenceName);
        return FindSequence(session.GetCurrentSchemaName(), sequenceName)
            ?? throw DbException.Get(Api.ErrorCode.SequenceNotFound1, sequenceName);
    }

    private Schemas.Sequence? FindSequence(string schema, string sequenceName)
    {
        Schemas.Sequence? sequence = session!.GetDatabase().GetSchema(schema).FindSequence(sequenceName);
        if (sequence is not null) return sequence;
        string[]? path = session.GetSchemaSearchPath();
        if (path is not null)
        {
            foreach (string name in path)
            {
                sequence = session.GetDatabase().GetSchema(name).FindSequence(sequenceName);
                if (sequence is not null) return sequence;
            }
        }
        return null;
    }

    /// <summary>
    /// Reads a function call: the name is the current token and is followed by its opening
    /// parenthesis. The name is matched against the reference's two switches — the built-in
    /// functions and the compatibility spellings — and a name that neither knows is
    /// FUNCTION_NOT_FOUND, which is the answer the reference gives once its own lookups are
    /// exhausted.
    /// </summary>
    /// <remarks>
    /// Aggregate names come from the reference's registry, before built-ins, window functions
    /// and compatibility spellings. The implementation of an aggregate may still be missing;
    /// that gap belongs to Aggregate, not to a second list of names in the parser.
    /// </remarks>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    /// <returns>the function expression</returns>
    private Expressions.Expression ReadFunction(Query.Select? aggregateSelect)
    {
        string name = Current.AsIdentifier!;
        Read();
        Expect(Token.OpenParen, "(");
        string upperName = StringUtils.ToUpperEnglish(name);
        Expressions.Aggregate.AggregateType? aggregateType =
            Expressions.Aggregate.Aggregate.GetAggregateType(upperName);
        if (aggregateType is not null)
        {
            return ReadQueryAggregate(aggregateSelect, aggregateType.Value, upperName);
        }

        if (session is not null && session.Extensions.HasUdf(upperName))
        {
            return ReadUdfCall(upperName, aggregateSelect);
        }

        Expressions.Expression? expression = ReadBuiltinFunctionIf(upperName, aggregateSelect);
        if (expression is not null)
        {
            return expression;
        }

        expression = ReadWindowFunctionIf(upperName, aggregateSelect);
        if (expression is not null)
        {
            return expression;
        }

        expression = ReadCompatibilityFunction(upperName, aggregateSelect);
        if (expression is not null)
        {
            return expression;
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

    private Expressions.Expression ReadUdfCall(string name, Query.Select? aggregateSelect)
    {
        SessionLocal current = session
            ?? throw new TODOException("Parser.readUdfCall: this parser has no session");
        List<Expressions.Expression> args = [];
        if (!ReadIf(Token.CloseParen))
        {
            do
            {
                args.Add(ReadQueryOr(aggregateSelect));
            }
            while (ReadIf(Token.Comma));

            Expect(Token.CloseParen, ")");
        }

        UdfDefinition? definition = current.Extensions.FindUdf(name, args.Count);
        if (definition is null)
        {
            throw DbException.Get(Api.ErrorCode.MethodNotFound1,
                name + " (parameter count: " + args.Count + ")");
        }

        return new ClrFunction(definition, [.. args]);
    }

    /// <summary>
    /// Reads a window function, which the reference recognizes by the same names as its
    /// <see cref="Expressions.Analysis.WindowFunctionType"/> and reads through
    /// readWindowFunction. The current SELECT is passed explicitly in this parser port.
    /// </summary>
    /// <param name="name">the name in upper case</param>
    /// <returns>the expression, or null when the name is not a window function</returns>
    private Expressions.Expression? ReadWindowFunctionIf(string name, Query.Select? currentSelect)
    {
        Expressions.Analysis.WindowFunctionType? type = Expressions.Analysis.WindowFunctionTypeExtensions.Get(name);
        if (type is null) return null;
        if (currentSelect is null) throw DbException.GetSyntaxError(sql, Current.Start, "SELECT");
        int min = Expressions.Analysis.WindowFunction.GetMinArgumentCount(type.Value);
        int max = Expressions.Analysis.WindowFunction.GetMaxArgumentCount(type.Value);
        List<Expression> args = [];
        if (min > 0)
        {
            do { args.Add(ReadQueryOr(currentSelect)); }
            while (args.Count < max && ReadIf(Token.Comma));
            if (args.Count < min) throw DbException.GetSyntaxError(sql, Current.Start, "argument");
        }
        Expect(Token.CloseParen, ")");
        Expressions.Analysis.WindowFunction function = new(type.Value, currentSelect, min == 0 ? null : [.. args]);
        if (type == Expressions.Analysis.WindowFunctionType.NthValue && IsWord("FROM", consume: false)
            && (NextIs("FIRST") || NextIs("LAST")))
        {
            Read(); function.SetFromLast(IsWord("LAST")); if (IsWord("FIRST", consume: false)) Read();
        }
        if (type is Expressions.Analysis.WindowFunctionType.NthValue or Expressions.Analysis.WindowFunctionType.Lead
            or Expressions.Analysis.WindowFunctionType.Lag or Expressions.Analysis.WindowFunctionType.FirstValue
            or Expressions.Analysis.WindowFunctionType.LastValue)
        {
            if ((IsWord("IGNORE", consume: false) || IsWord("RESPECT", consume: false)) && NextIs("NULLS"))
            { bool ignore = IsWord("IGNORE"); if (!ignore) Read(); Read(); function.SetIgnoreNulls(ignore); }
        }
        ExpectWord("OVER");
        function.SetOverCondition(CurrentType == Token.OpenParen ? ReadWindowSpecification(currentSelect)
            : new Expressions.Analysis.Window(ReadIdentifier(), null, null, null));
        currentSelect.SetWindowQuery();
        return function;
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

            case "FORMATDATETIME":
                return ReadDateTimeFormatFunction(
                    Expressions.Functions.DateTimeFormatFunction.FormatDateTime, aggregateSelect);
            case "PARSEDATETIME":
                return ReadDateTimeFormatFunction(
                    Expressions.Functions.DateTimeFormatFunction.ParseDateTime, aggregateSelect);

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

            case "TO_CHAR":
                {
                    Expressions.Expression value = ReadQueryOr(aggregateSelect);
                    Expressions.Expression? format = ReadIf(Token.Comma)
                        ? ReadQueryOr(aggregateSelect) : null;
                    Expressions.Expression? nls = ReadIf(Token.Comma)
                        ? ReadQueryOr(aggregateSelect) : null;
                    Expect(Token.CloseParen, ")");
                    return new Expressions.Functions.ToCharFunction(value, format, nls);
                }

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

            case "REGEXP_LIKE":
                return ReadParameters(new Expressions.Functions.RegexpFunction(Expressions.Functions.RegexpFunction.REGEXP_LIKE), aggregateSelect);
            case "REGEXP_REPLACE":
                return ReadParameters(new Expressions.Functions.RegexpFunction(Expressions.Functions.RegexpFunction.REGEXP_REPLACE), aggregateSelect);
            case "REGEXP_SUBSTR":
                return ReadParameters(new Expressions.Functions.RegexpFunction(Expressions.Functions.RegexpFunction.REGEXP_SUBSTR), aggregateSelect);

            case "XMLATTR":
                return ReadParameters(new Expressions.Functions.XMLFunction(Expressions.Functions.XMLFunction.XMLATTR), aggregateSelect);
            case "XMLCDATA":
                return ReadParameters(new Expressions.Functions.XMLFunction(Expressions.Functions.XMLFunction.XMLCDATA), aggregateSelect);
            case "XMLCOMMENT":
                return ReadParameters(new Expressions.Functions.XMLFunction(Expressions.Functions.XMLFunction.XMLCOMMENT), aggregateSelect);
            case "XMLNODE":
                return ReadParameters(new Expressions.Functions.XMLFunction(Expressions.Functions.XMLFunction.XMLNODE), aggregateSelect);
            case "XMLSTARTDOC":
                return ReadParameters(new Expressions.Functions.XMLFunction(Expressions.Functions.XMLFunction.XMLSTARTDOC), aggregateSelect);
            case "XMLTEXT":
                return ReadParameters(new Expressions.Functions.XMLFunction(Expressions.Functions.XMLFunction.XMLTEXT), aggregateSelect);

            case "TRIM_ARRAY":
                return new Expressions.Functions.ArrayFunction(ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect), null,
                    Expressions.Functions.ArrayFunction.TrimArray);
            case "ARRAY_CONTAINS":
                return new Expressions.Functions.ArrayFunction(ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect), null,
                    Expressions.Functions.ArrayFunction.ArrayContains);
            case "ARRAY_SLICE":
                return new Expressions.Functions.ArrayFunction(ReadQueryOr(aggregateSelect), ReadNextArgument(aggregateSelect),
                    ReadLastArgument(aggregateSelect), Expressions.Functions.ArrayFunction.ArraySlice);

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

            case "JSON_OBJECT":
            {
                Expressions.Functions.JsonConstructorFunction function = new(false);
                if (CurrentType != Token.CloseParen && !ReadJsonObjectFunctionFlags(function, false))
                {
                    do
                    {
                        bool withKey = IsWord("KEY");
                        function.AddParameter(ReadQueryOr(aggregateSelect));
                        if (withKey) ExpectWord("VALUE");
                        else if (!(IsWord("VALUE") || (provider.GetMode().AcceptsCommaAsJsonKeyValueSeparator
                            && ReadIf(Token.Comma)))) Expect(Token.Colon, ":");
                        function.AddParameter(ReadQueryOr(aggregateSelect));
                    } while (ReadIf(Token.Comma));
                    ReadJsonObjectFunctionFlags(function, false);
                }
                Expect(Token.CloseParen, ")");
                function.DoneWithParameters();
                return function;
            }
            case "JSON_ARRAY":
            {
                Expressions.Functions.JsonConstructorFunction function = new(true);
                function.SetFlags(Util.Json.JsonConstructorUtils.JsonAbsentOnNull);
                if (CurrentType != Token.CloseParen && !ReadJsonObjectFunctionFlags(function, true))
                {
                    do { function.AddParameter(ReadQueryOr(aggregateSelect)); } while (ReadIf(Token.Comma));
                    ReadJsonObjectFunctionFlags(function, true);
                }
                Expect(Token.CloseParen, ")");
                function.DoneWithParameters();
                return function;
            }

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

            case "HASH":
                return new Expressions.Functions.HashFunction(
                    ReadQueryOr(aggregateSelect), ReadNextArgument(aggregateSelect),
                    ReadIfArgument(aggregateSelect));
            case "ORA_HASH":
                throw new TODOException(
                    "Parser.readBuiltinFunctionIf: ORA_HASH is provided by H2Sharp.JavaLegacy");

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

            // SessionControlFunction.
            case "ABORT_SESSION":
                return new Expressions.Functions.SessionControlFunction(
                    ReadSingleArgument(aggregateSelect),
                    Expressions.Functions.SessionControlFunction.AbortSession);
            case "CANCEL_SESSION":
                return new Expressions.Functions.SessionControlFunction(
                    ReadSingleArgument(aggregateSelect),
                    Expressions.Functions.SessionControlFunction.CancelSession);

            // TableInfoFunction (the disk-space half; the envelope half needs spatial
            // indexes, which are not ported, and answers null without one).
            case "DISK_SPACE_USED":
                return new Expressions.Functions.TableInfoFunction(
                    ReadSingleArgument(aggregateSelect), null,
                    Expressions.Functions.TableInfoFunction.DiskSpaceUsed);
            case "ESTIMATED_ENVELOPE":
                return new Expressions.Functions.TableInfoFunction(
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect),
                    Expressions.Functions.TableInfoFunction.EstimatedEnvelope);

            // FileFunction (not ported).
            case "FILE_READ":
            case "FILE_WRITE":
                throw new TODOException("Parser.readBuiltinFunctionIf: FileFunction is not ported");

            // DataTypeSQLFunction.
            case "DATA_TYPE_SQL":
                return new Expressions.Functions.DataTypeSQLFunction(
                    ReadQueryOr(aggregateSelect), ReadNextArgument(aggregateSelect),
                    ReadNextArgument(aggregateSelect), ReadLastArgument(aggregateSelect));

            // CSVWriteFunction (not ported).
            case "CSVWRITE":
                throw new TODOException("Parser.readBuiltinFunctionIf: CSVWriteFunction is not ported");

            // SignalFunction (not ported).
            case "SIGNAL":
                throw new TODOException("Parser.readBuiltinFunctionIf: SignalFunction is not ported");

            // TruncateValueFunction.
            case "TRUNCATE_VALUE":
                return new Expressions.Functions.TruncateValueFunction(
                    ReadQueryOr(aggregateSelect), ReadNextArgument(aggregateSelect),
                    ReadLastArgument(aggregateSelect));

            case "ZERO":
                Expect(Token.CloseParen, ")");
                return Expressions.ValueExpression.Get(ValueInteger.Get(0));
            case "PI":
                Expect(Token.CloseParen, ")");
                return Expressions.ValueExpression.Get(ValueDouble.Get(Math.PI));

            // SysInfoFunction (zero-argument database and session information).
            case "AUTOCOMMIT":
                Expect(Token.CloseParen, ")");
                return new Expressions.Functions.SysInfoFunction(
                    Expressions.Functions.SysInfoFunction.Autocommit);
            case "DATABASE_PATH":
                Expect(Token.CloseParen, ")");
                return new Expressions.Functions.SysInfoFunction(
                    Expressions.Functions.SysInfoFunction.DatabasePath);
            case "H2VERSION":
                Expect(Token.CloseParen, ")");
                return new Expressions.Functions.SysInfoFunction(
                    Expressions.Functions.SysInfoFunction.H2version);
            case "LOCK_MODE":
                Expect(Token.CloseParen, ")");
                return new Expressions.Functions.SysInfoFunction(
                    Expressions.Functions.SysInfoFunction.LockMode);
            case "LOCK_TIMEOUT":
                Expect(Token.CloseParen, ")");
                return new Expressions.Functions.SysInfoFunction(
                    Expressions.Functions.SysInfoFunction.LockTimeout);
            case "MEMORY_FREE":
                Expect(Token.CloseParen, ")");
                return new Expressions.Functions.SysInfoFunction(
                    Expressions.Functions.SysInfoFunction.MemoryFree);
            case "MEMORY_USED":
                Expect(Token.CloseParen, ")");
                return new Expressions.Functions.SysInfoFunction(
                    Expressions.Functions.SysInfoFunction.MemoryUsed);
            case "READONLY":
                Expect(Token.CloseParen, ")");
                return new Expressions.Functions.SysInfoFunction(
                    Expressions.Functions.SysInfoFunction.Readonly);
            case "SESSION_ID":
                Expect(Token.CloseParen, ")");
                return new Expressions.Functions.SysInfoFunction(
                    Expressions.Functions.SysInfoFunction.SessionId);
            case "TRANSACTION_ID":
                Expect(Token.CloseParen, ")");
                return new Expressions.Functions.SysInfoFunction(
                    Expressions.Functions.SysInfoFunction.TransactionId);

            // DBObjectFunction (two- or three-argument database object lookup).
            case "DB_OBJECT_ID":
            case "DB_OBJECT_SQL":
            case "DB_OBJECT_SIZE":
            case "DB_OBJECT_TOTAL_SIZE":
            case "DB_OBJECT_APPROXIMATE_SIZE":
            case "DB_OBJECT_APPROXIMATE_TOTAL_SIZE":
            {
                int function = upperName switch
                {
                    "DB_OBJECT_ID" => Expressions.Functions.DBObjectFunction.DbObjectId,
                    "DB_OBJECT_SQL" => Expressions.Functions.DBObjectFunction.DbObjectSql,
                    "DB_OBJECT_SIZE" => Expressions.Functions.DBObjectFunction.DbObjectSize,
                    "DB_OBJECT_TOTAL_SIZE" => Expressions.Functions.DBObjectFunction.DbObjectTotalSize,
                    "DB_OBJECT_APPROXIMATE_SIZE" => Expressions.Functions.DBObjectFunction.DbObjectApproximateSize,
                    _ => Expressions.Functions.DBObjectFunction.DbObjectApproximateTotalSize,
                };
                Expression objectType = ReadQueryOr(aggregateSelect);
                Expect(Token.Comma, ",");
                Expression arg1 = ReadQueryOr(aggregateSelect);
                Expression? arg2 = null;
                if (ReadIf(Token.Comma))
                {
                    arg2 = ReadQueryOr(aggregateSelect);
                }

                Expect(Token.CloseParen, ")");
                return new Expressions.Functions.DBObjectFunction(objectType, arg1, arg2, function);
            }

            case "GCD":
                return ReadGCDFunction(Expressions.Functions.GCDFunction.Gcd, aggregateSelect);
            case "LCM":
                return ReadGCDFunction(Expressions.Functions.GCDFunction.Lcm, aggregateSelect);

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
            case "ARRAY_APPEND":
            case "ARRAY_CAT":
                return new Expressions.ConcatenationOperation(ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect));

            case "ARRAY_GET":
                return new Expressions.ArrayElementReference(ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect));

            case "ARRAY_LENGTH":
                return new Expressions.Functions.CardinalityExpression(
                    ReadSingleArgument(aggregateSelect), false);

            // SimpleCase.
            case "DECODE":
            {
                Expression caseOperand = ReadQueryOr(aggregateSelect);
                bool canOptimize = caseOperand.IsConstant()
                    && !caseOperand.GetValue(session!).ContainsNull();
                Expression a = ReadNextArgument(aggregateSelect);
                Expression b = ReadNextArgument(aggregateSelect);
                Expressions.SimpleCase.SimpleWhen when = DecodeToWhen(caseOperand, canOptimize, a, b);
                Expressions.SimpleCase.SimpleWhen current = when;
                Expression? elseResult = null;
                while (ReadIf(Token.Comma))
                {
                    a = ReadQueryOr(aggregateSelect);
                    if (ReadIf(Token.Comma))
                    {
                        b = ReadQueryOr(aggregateSelect);
                        Expressions.SimpleCase.SimpleWhen next = DecodeToWhen(caseOperand, canOptimize, a, b);
                        current.SetWhen(next);
                        current = next;
                    }
                    else
                    {
                        elseResult = a;
                        break;
                    }
                }

                Expect(Token.CloseParen, ")");
                return new Expressions.SimpleCase(caseOperand, when, elseResult);
            }

            // SearchedCase.
            case "CASEWHEN":
                return ReadCompatibilityCase(ReadQueryOr(aggregateSelect), aggregateSelect);
            case "NVL2":
                return ReadCompatibilityCase(
                    new Expressions.Conditions.ConditionIsNull(ReadQueryOr(aggregateSelect), not: true, whenOperand: false),
                    aggregateSelect);

            // CastSpecification. The swapped CONVERT parameters belong to the modes
            // with swapConvertFunctionParameters, which this port does not run.
            case "CONVERT":
            {
                Expression arg = ReadQueryOr(aggregateSelect);
                Expect(Token.Comma, ",");
                TypeInfo type = ReadColumnWithType();
                Expect(Token.CloseParen, ")");
                return new Expressions.Functions.CastSpecification(arg, type);
            }

            case "IFNULL":
                return new Expressions.Functions.CoalesceFunction(
                    Expressions.Functions.CoalesceFunction.Coalesce,
                    ReadQueryOr(aggregateSelect), ReadLastArgument(aggregateSelect));
            case "NVL":
                return ReadCoalesceFunction(
                    Expressions.Functions.CoalesceFunction.Coalesce, aggregateSelect);

            // CurrentGeneralValueSpecification.
            case "DATABASE":
                Expect(Token.CloseParen, ")");
                return new Expressions.Functions.CurrentGeneralValueSpecification(
                    Expressions.Functions.CurrentGeneralValueSpecification.CurrentCatalog);

            // CurrentGeneralValueSpecification.
            case "SCHEMA":
                Expect(Token.CloseParen, ")");
                return new Expressions.Functions.CurrentGeneralValueSpecification(
                    Expressions.Functions.CurrentGeneralValueSpecification.CurrentSchema);

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
                // The reference reads the first argument with readConcat, because IN would be
                // read too early by readExpression; ReadQueryConcat is that reader here.
                {
                    Expressions.Expression arg1 = ReadQueryConcat(aggregateSelect);
                    if (!ReadIf(Token.Comma))
                    {
                        Expect(ParserUtil.In, "IN");
                    }

                    return new Expressions.Functions.StringFunction(
                        arg1, ReadSingleArgument(aggregateSelect), null,
                        Expressions.Functions.StringFunction.Locate);
                }

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

    /// <summary>Reads an interval literal after the INTERVAL keyword.</summary>
    private Expressions.Expression ReadInterval()
    {
        bool negative = ReadIf(Token.MinusSign);
        if (!negative) ReadIf(Token.PlusSign);
        Value? literal = Current.GetValue(provider);
        if (literal == null || literal.GetValueType() != Value.Varchar)
            throw DbException.GetSyntaxError(sql, Current.Start, "string");
        string text = literal.GetString()!;
        Read();
        TypeInfo? typeInfo = ReadIntervalQualifier();
        try
        {
            ValueInterval interval = Util.IntervalUtils.ParseInterval(
                Api.IntervalQualifiers.ValueOf(typeInfo!.GetValueType() - Value.IntervalYear), negative, text);
            if (typeInfo.GetDeclaredPrecision() != -1L || typeInfo.GetDeclaredScale() != -1)
                return Expressions.TypedValueExpression.Get(interval.CastToValue(typeInfo, provider), typeInfo);
            return Expressions.ValueExpression.Get(interval);
        }
        catch (Exception error)
        {
            throw DbException.Get(Api.ErrorCode.InvalidDatetimeConstant2, error, "INTERVAL", text);
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

    private Expressions.Functions.FunctionN ReadDateTimeFormatFunction(
        int function, Query.Select? aggregateSelect)
    {
        Expressions.Functions.DateTimeFormatFunction formatFunction = new(function);
        do
        {
            formatFunction.AddParameter(ReadQueryOr(aggregateSelect));
        }
        while (ReadIf(Token.Comma));
        Expect(Token.CloseParen, ")");
        return formatFunction;
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

    // Parser.readGCDFunction: require two arguments, then use readRemainingParameters.
    private Expression ReadGCDFunction(int function, Query.Select? aggregateSelect)
    {
        Expressions.Functions.GCDFunction f = new(function);
        f.AddParameter(ReadQueryOr(aggregateSelect));
        Expect(Token.Comma, ",");
        f.AddParameter(ReadQueryOr(aggregateSelect));
        return ReadRemainingParameters(f, aggregateSelect);
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

    // H2 2.5 Parser.readParameters; the select is explicit in this parser's expression reader.
    private T ReadParameters<T>(T expression, Query.Select? aggregateSelect)
        where T : Expressions.IExpressionWithVariableParameters
    {
        if (!ReadIf(Token.CloseParen))
        {
            do { expression.AddParameter(ReadQueryOr(aggregateSelect)); } while (ReadIfMore());
        }
        expression.DoneWithParameters();
        return expression;
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

    /// <summary>
    /// Builds one WHEN of a DECODE call, as the reference's decodeToWhen does: a constant
    /// comparison operand stays a plain equality, anything else becomes null-safe.
    /// </summary>
    /// <param name="caseOperand">the DECODE operand</param>
    /// <param name="canOptimize">whether the operand is a non-null constant</param>
    /// <param name="whenOperand">the WHEN value</param>
    /// <param name="result">the THEN value</param>
    /// <returns>the WHEN</returns>
    private static Expressions.SimpleCase.SimpleWhen DecodeToWhen(
        Expression caseOperand, bool canOptimize, Expression whenOperand, Expression result)
    {
        // GetValue runs only on constants here (the disjunction short-circuits otherwise),
        // which do not read the session, as in the reference.
        if (!canOptimize && (!whenOperand.IsConstant()
            || whenOperand.GetValue(null!).ContainsNull()))
        {
            whenOperand = new Expressions.Conditions.Comparison(
                Expressions.Conditions.Comparison.EqualNullSafe, caseOperand, whenOperand, whenOperand: true);
        }

        return new Expressions.SimpleCase.SimpleWhen(whenOperand, result);
    }

    /// <summary>
    /// Builds a CASE WHEN out of one condition and two results, as the reference's
    /// readCompatibilityCase does.
    /// </summary>
    /// <param name="when">the condition</param>
    /// <param name="aggregateSelect">the select when aggregates may appear, null otherwise</param>
    /// <returns>the CASE expression</returns>
    private Expressions.Expression ReadCompatibilityCase(Expression when, Query.Select? aggregateSelect)
        => new Expressions.SearchedCase([when, ReadNextArgument(aggregateSelect), ReadLastArgument(aggregateSelect)]);

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
        Expressions.Expression right = ReadQueryConcat();
        Expressions.Expression? escape = IsWord("ESCAPE") ? ReadQueryConcat() : null;
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
        create.SetPersistIndexes(statement.PersistIndexes);
        int id = 0;
        foreach (ColumnDefinition column in statement.Columns)
        {
            Tables.Column tableColumn;
            Schemas.Domain? columnDomain = schema.FindDomain(column.Type);
            if (columnDomain is not null)
            {
                tableColumn = new Tables.Column(column.Name, columnDomain.GetDataType(), null, id++);
                tableColumn.SetComment(columnDomain.GetComment());
                tableColumn.SetDomain(columnDomain);
            }
            else
            {
                tableColumn = new Tables.Column(column.Name, ParseDataType(column.Type), null, id++);
            }
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
                create.AddConstraintCommand(CreateAddConstraintCommand(session, schema, statement.Name,
                    false, new TableConstraintDefinition(column.ConstraintName, false,
                        [tableColumn.GetName()], column.PrimaryKeyHash)));
            }

            if (column.RefTable is not null)
            {
                // One column-level REFERENCES is one foreign key over one column, as in the
                // reference's readColumnConstraints.
                create.AddConstraintCommand(CreateAddConstraintCommand(session, schema, statement.Name,
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
                CreateAddConstraintCommand(session, schema, statement.Name, false, constraint));
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
        Schemas.Schema schema = statement.Schema is null
            ? CurrentSchema(session)
            : session.GetDatabase().GetSchema(statement.Schema);
        if (statement.AlterColumn is not null)
        {
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
                session, schema, statement.Name, statement.IfTableExists, statement.AddConstraint);
            add.SetSql(statement.Sql, null);
            return add;
        }

        if (statement.DropColumns is not null)
        {
            Ddl.AlterTableAlterColumn dropColumns = new(session, schema);
            dropColumns.SetSql(statement.Sql, null);
            dropColumns.SetTableName(statement.Name);
            dropColumns.SetIfTableExists(statement.IfTableExists);
            dropColumns.SetType(CommandInterface.AlterTableDropColumn);
            dropColumns.SetColumnsToRemove(statement.DropColumns, statement.DropColumnsIfExists);
            return dropColumns;
        }

        Ddl.AlterTableDropConstraint drop = new(
            session, schema, statement.DropConstraintIfExists);
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
                    : Expressions.ValueExpression.Default);
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
                options.SetMinValue(Expressions.ValueExpression.Null);
            }
            else if (IsWord("MAXVALUE"))
            {
                options.SetMaxValue(Expressions.ValueExpression.Null);
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
            options.SetMinValue(Expressions.ValueExpression.Null);
        }
        else if (IsWord("NOMAXVALUE"))
        {
            options.SetMaxValue(Expressions.ValueExpression.Null);
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
        Schemas.Schema schema, string tableName, bool ifTableExists, TableConstraintDefinition constraint)
    {
        Ddl.AlterTableAddConstraint key = new(
            session, schema, constraint.Type, constraint.IfNotExists);
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

        if (constraint.IndexName is not null)
        {
            Schemas.Schema indexSchema = constraint.IndexSchema is null
                ? CurrentSchema(session)
                : session.GetDatabase().GetSchema(constraint.IndexSchema);
            key.SetIndex(indexSchema.FindIndex(session, constraint.IndexName)
                ?? throw DbException.Get(Api.ErrorCode.IndexNotFound1, constraint.IndexName));
        }

        if (constraint.Type == CommandInterface.AlterTableAddConstraintCheck)
        {
            key.SetCheckExpression(constraint.CheckExpression!);
            key.SetCheckExisting(constraint.CheckExisting);
            return key;
        }

        if (constraint.Type == CommandInterface.AlterTableAddConstraintPrimaryKey
            || constraint.Type == CommandInterface.AlterTableAddConstraintUnique)
        {
            if (constraint.Type == CommandInterface.AlterTableAddConstraintUnique)
            {
                key.SetNullsDistinct(constraint.NullsDistinct ?? Engine.NullsDistinct.Distinct);
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

        if (constraint.RefIndexName is not null)
        {
            Schemas.Schema refIndexSchema = constraint.RefIndexSchema is null
                ? (constraint.RefTableSchema is null
                    ? CurrentSchema(session)
                    : session.GetDatabase().GetSchema(constraint.RefTableSchema))
                : session.GetDatabase().GetSchema(constraint.RefIndexSchema);
            key.SetRefIndex(refIndexSchema.FindIndex(session, constraint.RefIndexName));
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
        // The existing statement DTO retains text. Reuse H2's token-based reader rather than
        // a second type grammar; this also retains array component types and cardinalities.
        Parser parser = new(typeName, provider,
            Tokenizer.TokenizeForTest(typeName, false, new BitSet(), provider), session);
        TypeInfo type = parser.ReadIfDataType()
            ?? throw DbException.GetSyntaxError(typeName, parser.Current.Start, "data type");
        if (parser.CurrentType != Token.EndOfInput)
        {
            throw DbException.GetSyntaxError(typeName, parser.Current.Start);
        }

        return type;
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
            HashSet<Tables.Column> seen = [];
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

                if (match is null)
                {
                    throw DbException.GetSyntaxError(sql, 0, name);
                }

                if (!seen.Add(match))
                {
                    throw DbException.Get(Api.ErrorCode.DuplicateColumnName1, name);
                }

                columns[i] = match;
            }
        }

        if (statement.Columns.Count > 0)
        {
            insert.SetColumns(columns);
        }
        if (statement.DefaultValues)
        {
            // Parser.parseInsert: DEFAULT VALUES adds an empty row; Insert.doPrepare then
            // chooses the table's columns and ConvertInsertRow supplies defaults/identity.
            // Materializing a NULL expression per column would turn an identity column
            // into an explicit NULL and raise 23502.
            insert.AddRow(System.Array.Empty<Expressions.Expression>());
        }

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
        Schemas.Schema schema = statement.Schema is null
            ? CurrentSchema(session)
            : session.GetDatabase().GetSchema(statement.Schema);
        if (statement.TableSchema is not null)
        {
            schema = session.GetDatabase().GetSchema(statement.TableSchema);
        }

        Ddl.CreateIndex create = new(session, schema);
        create.SetSql(statement.Sql, null);
        create.SetIfTableExists(statement.IfTableExists);
        create.SetIfNotExists(statement.IfNotExists);
        create.SetTableName(statement.TableName);
        if (statement.Name is not null)
        {
            create.SetIndexName(statement.Name);
        }

        if (statement.PrimaryKey)
        {
            create.SetPrimaryKey(true);
            create.SetUnique(null, statement.UniqueColumnCount);
        }

        create.SetIndexColumns([.. statement.Columns]);
        if (statement.Unique)
        {
            create.SetUnique(statement.NullsDistinct ?? NullsDistinct.Distinct, statement.UniqueColumnCount);
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
            session, table, statement.Alias,
            rightsChecked: true, select: null, 0, null));
        Dml.SetClauseList assignments = new(table);
        foreach ((string name, Expression[]? indexes, Expression value) in statement.Assignments)
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

            assignments.AddSingle(match, indexes, value);
        }

        foreach ((List<string> names, List<Expression[]?> allIndexes, Expression value) in statement.RowAssignments)
        {
            List<Tables.Column> matched = [];
            foreach (string name in names)
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

                matched.Add(match);
            }

            assignments.AddMultiple(matched, allIndexes, value);
        }

        update.SetSetClauseList(assignments);
        if (statement.Fetch is not null)
        {
            update.SetFetch(statement.Fetch);
        }

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
            session, table, statement.Alias,
            rightsChecked: true, select: null, 0, null));
        if (statement.Fetch is not null)
        {
            delete.SetFetch(statement.Fetch);
        }

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
    private Tables.Table ReadTableOrView(
        SessionLocal session, Schemas.Schema schema, string tableName, bool searchPath = false)
    {
        Tables.Table? table = schema.ResolveTableOrView(session, tableName);
        if (table is not null)
        {
            return table;
        }

        // The reference asks the query scope for a table of a common table expression after
        // the schema has not answered.
        if (ctes.TryGetValue(tableName, out Tables.Table? cte))
        {
            return cte;
        }

        List<Schemas.Schema> schemas = [schema];
        if (searchPath && session.GetSchemaSearchPath() is { } path)
        {
            foreach (string schemaName in path)
            {
                Schemas.Schema pathSchema = session.GetDatabase().GetSchema(schemaName);
                table = pathSchema.FindTableOrView(session, tableName);
                if (table is not null)
                {
                    return table;
                }

                if (!schemas.Contains(pathSchema))
                {
                    schemas.Add(pathSchema);
                }
            }
        }

        if (session.GetDatabase().GetFirstUserTable() is null)
        {
            throw DbException.Get(Api.ErrorCode.TableOrViewNotFoundDatabaseEmpty1, tableName);
        }

        SortedSet<string> candidates = new(StringComparer.Ordinal);
        foreach (Schemas.Schema candidateSchema in schemas)
        {
            foreach (Tables.Table candidate in candidateSchema.GetAllTablesAndViews(session))
            {
                string candidateName = candidate.GetTableName();
                if (candidateName.Equals(tableName, StringComparison.OrdinalIgnoreCase))
                {
                    _ = candidates.Add(candidateName);
                }
            }
        }

        if (candidates.Count == 0)
        {
            throw DbException.Get(Api.ErrorCode.TableOrViewNotFound1, tableName);
        }

        throw DbException.Get(Api.ErrorCode.TableOrViewNotFoundWithCandidates2,
            tableName, string.Join(", ", candidates));
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
    public static Statement Parse(string sql, CastDataProvider provider, BitSet parameters)
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
    private static void CheckParameterIndex(BitSet parameters)
    {
        int length = parameters.Length;
        if (length > Engine.Constants.MaxParameterIndex)
        {
            throw DbException.GetInvalidValueException("parameter index", length);
        }
    }

    // ParserBase.readParameter returns the same object for every occurrence of an index.
    private Expressions.Parameter ReadParameter()
    {
        int index = ((Token.ParameterToken)Current).Index - 1;
        Read();
        usedParameters.Set(index);
        while (parameterList.Count <= index)
            parameterList.Add(new Expressions.Parameter(parameterList.Count));
        return parameterList[index];
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

    // ParserBase.readIfCompat(String): compatibility words never match quoted identifiers.
    private bool ReadIfCompat(string tokenName)
    {
        return !Current.IsQuoted && IsWord(tokenName);
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

            // The reference reads MEMORY or CACHED before it knows whether a table or an index
            // follows; they only occur before TABLE. A MEMORY table is stored without its index
            // content, which the reference spells as persistIndexes=false.
            bool memory = false;
            if (IsWord("MEMORY"))
            {
                memory = true;
            }
            else
            {
                _ = IsWord("CACHED");
            }

            if (IsWord("TABLE"))
            {
                return ReadCreateTable(!memory);
            }

            if (IsWord("UNIQUE", consume: false) || IsWord("INDEX", consume: false))
            {
                return ReadCreateIndex(primaryKey: false);
            }

            // CREATE PRIMARY KEY is the quirks-mode spelling the system table uses for the
            // index a primary key constraint owns. Database startup runs in quirks mode, so a
            // SYS row written by the reference replays; a client connection does not.
            if (session is not null && session.IsQuirksMode() && IsWord("PRIMARY", consume: false))
            {
                Read();
                ExpectWord("KEY");
                return ReadCreateIndex(primaryKey: true);
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

    private CreateTableStatement ReadCreateTable(bool persistIndexes)
    {
        CreateTableStatement statement = new() { PersistIndexes = persistIndexes };
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
                    // The reference also accepts DEFAULT expression DEFAULT ON NULL as
                    // a postfix column constraint.
                    if (defaultExpression is not null && IsWord("DEFAULT") && IsWord("ON") && IsWord("NULL"))
                    {
                        defaultOnNull = true;
                    }
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
                            out refColumns, out _, out _, out refUpdateAction, out refDeleteAction);
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
                    out refColumns, out _, out _, out refUpdateAction, out refDeleteAction);
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
        if (IsWord("ENGINE"))
        {
            statement.TableEngine = ReadIdentifier();
        }

        if (IsWord("WITH"))
        {
            List<string> engineParams = [];
            do
            {
                engineParams.Add(ReadIdentifier());
            }
            while (ReadIf(Token.Comma));
            statement.TableEngineParams = engineParams;
        }

        if (statement.Temporary)
        {
            if (IsWord("ON"))
            {
                ExpectWord("COMMIT");
                if (IsWord("DROP"))
                {
                    statement.OnCommitDrop = true;
                }
                else if (IsWord("DELETE"))
                {
                    ExpectWord("ROWS");
                    statement.OnCommitTruncate = true;
                }
            }
            else if (IsWord("NOT"))
            {
                if (IsWord("PERSISTENT"))
                {
                    statement.PersistData = false;
                }
                else
                {
                    ExpectWord("LOGGED");
                }
            }

            if (IsWord("TRANSACTIONAL"))
            {
                statement.Transactional = true;
            }
        }
        else if (!statement.PersistIndexes && IsWord("NOT"))
        {
            ExpectWord("PERSISTENT");
            statement.PersistData = false;
            if (IsWord("TRANSACTIONAL"))
            {
                statement.Transactional = true;
            }
        }
        else if (IsWord("NOT", consume: false))
        {
            Read();
            ExpectWord("PERSISTENT");
            statement.PersistData = false;
            if (IsWord("TRANSACTIONAL"))
            {
                statement.Transactional = true;
            }
        }

        return statement;
    }

    /// <summary>
    /// Reads a table-level constraint of a CREATE TABLE.
    /// </summary>
    /// <remarks>
    /// PRIMARY KEY, UNIQUE, CHECK and FOREIGN KEY run: the three kinds go to the same
    /// AlterTableAddConstraint whose branches read their columns and expressions. The column
    /// lists are read as names; the tables resolve them when the command runs, as they do on
    /// the JVM. The table name is the statement's own: a REFERENCES without a table names the
    /// table being created, as it does on the JVM.
    /// </remarks>
    /// <param name="tableName">the name of the table being created or changed</param>
    /// <returns>the constraint</returns>
    private TableConstraintDefinition ReadTableConstraint(string tableName)
    {
        string? name = null;
        bool ifNotExists = false;
        string? comment = null;
        string? indexSchema = null;
        string? indexName = null;
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

            name = ReadIdentifierWithSchemaName(out _);
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
                Read();
                indexName = ReadIdentifierWithSchemaName(out indexSchema);
            }

            ExpectWord("REFERENCES");
            ReadReferences(tableName, out string? refSchemaName, out string refTable, out List<string>? refColumns,
                out string? refIndexSchema, out string? refIndexName,
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
                refSchemaName, refTable, refColumns, updateAction, deleteAction, checkExisting,
                indexSchema, indexName, refIndexSchema, refIndexName);
        }

        if (CurrentType == ParserUtil.Unique)
        {
            Read();
            NullsDistinct nullsDistinct = ReadNullsDistinct(NullsDistinct.Distinct);
            if (IsWord("KEY") || IsWord("INDEX"))
            {
                Read();
            }

            Expect(Token.OpenParen, "(");
            List<string> uniqueColumns = [];
            do
            {
                uniqueColumns.Add(ReadIdentifier());
            }
            while (ReadIf(Token.Comma));

            Expect(Token.CloseParen, ")");
            if (IsWord("INDEX", consume: false))
            {
                Read();
                indexName = ReadIdentifierWithSchemaName(out indexSchema);
            }

            bool checkUniqueExisting = true;
            if (IsWord("NOCHECK"))
            {
                checkUniqueExisting = false;
            }
            else
            {
                _ = IsWord("CHECK");
            }

            return new TableConstraintDefinition(name, ifNotExists, uniqueColumns, Hash: false,
                CommandInterface.AlterTableAddConstraintUnique, comment,
                CheckExisting: checkUniqueExisting, IndexSchema: indexSchema, IndexName: indexName,
                NullsDistinct: nullsDistinct);
        }

        if (CurrentType == ParserUtil.Check)
        {
            Read();
            Expression checkExpression = ReadExpression();
            bool checkExisting = true;
            if (IsWord("NOCHECK"))
            {
                checkExisting = false;
            }
            else
            {
                _ = IsWord("CHECK");
            }

            return new TableConstraintDefinition(name, ifNotExists, [], Hash: false,
                CommandInterface.AlterTableAddConstraintCheck, comment,
                CheckExisting: checkExisting, CheckExpression: checkExpression);
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
            Read();
            indexName = ReadIdentifierWithSchemaName(out indexSchema);
        }

        return new TableConstraintDefinition(name, ifNotExists, keyColumns, hash,
            CommandInterface.AlterTableAddConstraintPrimaryKey, comment,
            IndexSchema: indexSchema, IndexName: indexName);
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
        out string? refIndexSchema,
        out string? refIndexName,
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
            Read();
            refIndexName = ReadIdentifierWithSchemaName(out refIndexSchema);
        }
        else
        {
            refIndexSchema = null;
            refIndexName = null;
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

        statement.Name = ReadIdentifierWithSchemaName(out string? schemaName);
        statement.Schema = schemaName;
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

            _ = IsWord("COLUMN");
            bool columnsIfExists = ReadIfExists(false);
            bool openingBracket = ReadIf(Token.OpenParen);
            List<string> columns = [];
            do
            {
                columns.Add(ReadIdentifier());
            }
            while (ReadIf(Token.Comma));

            if (openingBracket)
            {
                Expect(Token.CloseParen, ")");
            }

            statement.DropColumns = columns;
            statement.DropColumnsIfExists = columnsIfExists;
            return statement;
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
    private CreateIndexStatement ReadCreateIndex(bool primaryKey)
    {
        CreateIndexStatement statement = new() { PrimaryKey = primaryKey };
        if (!primaryKey)
        {
            if (IsWord("UNIQUE"))
            {
                statement.Unique = true;
                statement.NullsDistinct = ReadNullsDistinct(NullsDistinct.Distinct);
            }

            ExpectWord("INDEX");
        }

        if (!IsWord("ON", consume: false))
        {
            if (CurrentType == ParserUtil.If)
            {
                Read();
                Expect(ParserUtil.Not, "NOT");
                Expect(ParserUtil.Exists, "EXISTS");
                statement.IfNotExists = true;
            }

            statement.Name = ReadIdentifierWithSchemaName(session, out string? indexSchemaName);
            statement.Schema = indexSchemaName;
        }

        ExpectWord("ON");
        statement.TableName = ReadIdentifierWithSchemaName(session, out string? tableSchemaName);
        statement.TableSchema = tableSchemaName;
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
        statement.UniqueColumnCount = statement.Columns.Count;
        if (statement.Unique && IsWord("INCLUDE"))
        {
            Expect(Token.OpenParen, "(");
            do
            {
                string columnName = ReadIdentifier();
                statement.Columns.Add(new IndexColumn(columnName, ReadSortType()));
            }
            while (ReadIf(Token.Comma));

            Expect(Token.CloseParen, ")");
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
                string column = ReadIdentifier();
                if (statement.Columns.Exists(
                    name => name.Equals(column, StringComparison.OrdinalIgnoreCase)))
                {
                    throw DbException.Get(Api.ErrorCode.DuplicateColumnName1, column);
                }

                statement.Columns.Add(column);
            }
            while (ReadIf(Token.Comma));

            Expect(Token.CloseParen, ")");
        }

        if (IsWord("DEFAULT"))
        {
            ExpectWord("VALUES");
            statement.DefaultValues = true;
            return statement;
        }

        if (CurrentType == ParserUtil.Set)
        {
            if (statement.Columns.Count > 0)
            {
                throw DbException.GetSyntaxError(sql, Current.Start);
            }

            Read();
            List<Expression> setRow = [];
            do
            {
                statement.Columns.Add(ReadIdentifier());
                Expect(Token.Equal, "=");
                setRow.Add(ReadExpressionOrDefault());
            }
            while (ReadIf(Token.Comma));

            statement.Rows.Add(setRow);
            return statement;
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
        Expression? fetch = null;
        if ((session?.GetMode() ?? provider.GetMode()).TopInDml && ReadIfCompat("TOP"))
        {
            fetch = ReadQueryTerm();
        }

        UpdateStatement statement = new()
        {
            Name = ReadIdentifierWithSchemaName(out string? updateSchema),
            Fetch = fetch,
        };
        statement.Schema = updateSchema;
        // The reference resolves the target before it reads SET; doing it here keeps the
        // error of a missing table ahead of a syntax error in the rest of the statement.
        if (session is not null)
        {
            Schemas.Schema schema = updateSchema is null
                ? CurrentSchema(session)
                : session.GetDatabase().GetSchema(updateSchema);
            _ = ReadTableOrView(session, schema, statement.Name);
        }

        statement.Alias = ReadFromAlias();
        ExpectWord("SET");
        do
        {
            if (ReadIf(Token.OpenParen))
            {
                List<string> columns = [];
                List<Expression[]?> allIndexes = [];
                do
                {
                    columns.Add(ReadIdentifier());
                    allIndexes.Add(ReadUpdateSetClauseArrayIndexes());
                }
                while (ReadIf(Token.Comma));

                Expect(Token.CloseParen, ")");
                Expect(Token.Equal, "=");
                statement.RowAssignments.Add((columns, allIndexes, ReadExpression()));
                continue;
            }

            string column = ReadIdentifier();
            Expression[]? indexes = ReadUpdateSetClauseArrayIndexes();
            Expect(Token.Equal, "=");
            statement.Assignments.Add((column, indexes,
                indexes is null ? ReadExpressionOrDefault() : ReadExpression()));
        }
        while (ReadIf(Token.Comma));

        statement.Where = ReadWhere();
        if (CurrentType == ParserUtil.Order)
        {
            Read();
            ExpectWord("BY");
            do
            {
                _ = ReadExpression();
                _ = IsWord("ASC") || IsWord("DESC");
            }
            while (ReadIf(Token.Comma));
        }

        if (statement.Fetch is null)
        {
            statement.Fetch = ReadFetchOrLimit();
        }

        return statement;
    }

    private Expression[]? ReadUpdateSetClauseArrayIndexes()
    {
        if (!ReadIf(Token.OpenBracket))
        {
            return null;
        }

        List<Expression> list = [];
        do
        {
            list.Add(ReadExpression());
            Expect(Token.CloseBracket, "]");
        }
        while (ReadIf(Token.OpenBracket));

        return [.. list];
    }

    private DeleteStatement ReadDelete()
    {
        Expression? fetch = null;
        if ((session?.GetMode() ?? provider.GetMode()).TopInDml && ReadIfCompat("TOP"))
        {
            fetch = ReadQueryTerm();
        }

        ExpectWord("FROM");
        DeleteStatement statement = new()
        {
            Name = ReadIdentifierWithSchemaName(out string? deleteSchema),
            Fetch = fetch,
        };
        statement.Schema = deleteSchema;
        statement.Alias = ReadFromAlias();
        statement.Where = ReadWhere();
        if (statement.Fetch is null)
        {
            statement.Fetch = ReadFetchOrLimit();
        }

        return statement;
    }

    /// <summary>
    /// Reads FETCH FIRST/NEXT or LIMIT after a data change statement, as the reference's
    /// readFetchOrLimit does.
    /// </summary>
    private Expression? ReadFetchOrLimit()
    {
        Expression? fetch = null;
        if (CurrentType == ParserUtil.Fetch)
        {
            Read();
            if (!IsWord("FIRST"))
            {
                ExpectWord("NEXT");
            }

            if (IsWord("ROW") || IsWord("ROWS"))
            {
                fetch = Expressions.ValueExpression.Get(Values.ValueInteger.Get(1));
            }
            else
            {
                fetch = ReadQueryLimitExpression();
                if (!IsWord("ROW"))
                {
                    ExpectWord("ROWS");
                }
            }

            ExpectWord("ONLY");
        }
        else if ((session?.GetMode() ?? provider.GetMode()).Limit && IsWord("LIMIT"))
        {
            fetch = ReadQueryLimitExpression();
        }

        return fetch;
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
        int start = Current.Start;
        int index = position;
        if (ReadIfDataType() is not null)
        {
            return sql[start..Current.Start].Trim();
        }

        position = index;
        string name = ReadIdentifierWithSchemaName(out string? schemaName);
        return schemaName is null ? name : schemaName + "." + name;
    }
}
