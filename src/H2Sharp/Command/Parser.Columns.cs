/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.command.Parser, H2 2.5: column definitions and ALTER COLUMN.
 * These readers return the reference's Prepared / Column directly. */
using System;
using System.Collections.Generic;
using H2Sharp.Command.Ddl;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Command;

public sealed partial class Parser
{
    private Prepared ParseAlterTable()
    {
        int index = position;
        ExpectWord("ALTER");
        ExpectWord("TABLE");
        bool ifTableExists = ReadIfExists(false);
        string tableName = ReadIdentifierWithSchema(session!, out Schema schema);
        if (IsWord("ALTER")) return ParseAlterTableAlter(schema, tableName, ifTableExists);
        if (IsWord("SET")) return ParseAlterTableSet(schema, tableName, ifTableExists);
        if (IsWord("RENAME")) return ParseAlterTableRename(schema, tableName, ifTableExists);
        if (IsWord("ADD", consume: false)
            && !NextIs("CONSTRAINT") && !NextIs("PRIMARY") && !NextIs("FOREIGN")
            && !NextIs("UNIQUE") && !NextIs("CHECK"))
        {
            Read();
            return ParseAlterTableAdd(schema, tableName, ifTableExists);
        }
        // Other ALTER TABLE branches still use their existing reader; no new DTO is added.
        position = index;
        AlterTableStatement statement = (AlterTableStatement)ReadStatement();
        statement.Sql = sql;
        return PrepareAlterTable(session!, statement);
    }

    private Prepared ParseAlterTableAdd(Schema schema, string tableName, bool ifTableExists)
    {
        IsWord("COLUMN");
        AlterTableAlterColumn command = new(session!, schema);
        command.SetType(CommandInterface.AlterTableAddColumn);
        command.SetTableName(tableName);
        command.SetIfTableExists(ifTableExists);
        if (ReadIf(Token.OpenParen))
        {
            command.SetIfNotExists(false);
            do
            {
                ParseTableColumnDefinition(command, schema, tableName, false);
            }
            while (ReadIfMore());
        }
        else
        {
            command.SetIfNotExists(ReadIfNotExists());
            ParseTableColumnDefinition(command, schema, tableName, false);
            if (IsWord("USING"))
            {
                command.SetUsingExpression(ReadExpression());
            }
        }

        if (IsWord("BEFORE"))
        {
            command.SetAddBefore(ReadIdentifier());
        }
        else if (IsWord("AFTER"))
        {
            command.SetAddAfter(ReadIdentifier());
        }
        else if (IsWord("FIRST"))
        {
            command.SetAddFirst();
        }

        return command;
    }

    private Prepared ParseAlterTableSet(Schema schema, string tableName, bool ifTableExists)
    {
        ExpectWord("REFERENTIAL_INTEGRITY");
        bool value = ReadBooleanSetting();
        Dml.AlterTableSet command = new(session!, schema,
            CommandInterface.AlterTableSetReferentialIntegrity, value);
        command.SetTableName(tableName);
        command.SetIfTableExists(ifTableExists);
        if (IsWord("CHECK"))
        {
            command.SetCheckExisting(true);
        }
        else if (IsWord("NOCHECK"))
        {
            command.SetCheckExisting(false);
        }

        return command;
    }

    private Prepared ParseAlterTableRename(Schema schema, string tableName, bool ifTableExists)
    {
        if (IsWord("COLUMN"))
        {
            string columnName = ReadIdentifier();
            ExpectWord("TO");
            AlterTableRenameColumn column = new(session!, schema);
            column.SetTableName(tableName);
            column.SetIfTableExists(ifTableExists);
            column.SetOldColumnName(columnName);
            column.SetNewColumnName(ReadIdentifier());
            return column;
        }

        if (IsWord("CONSTRAINT"))
        {
            _ = ReadIdentifierWithSchema(session!, out Schema constraintSchema);
            if (!ReferenceEquals(constraintSchema, schema))
            {
                throw DbException.Get(Api.ErrorCode.SchemaNameMustMatch);
            }

            throw new TODOException("Parser.parseAlterTableRename: constraint rename is not ported");
        }

        ExpectWord("TO");
        string newName = ReadIdentifierWithSchemaName(out string? newSchemaName);
        if (newSchemaName is not null
            && !newSchemaName.Equals(schema.GetName(), StringComparison.Ordinal))
        {
            throw DbException.Get(Api.ErrorCode.SchemaNameMustMatch);
        }

        AlterTableRename command = new(session!, schema);
        command.SetTableName(tableName);
        command.SetIfTableExists(ifTableExists);
        command.SetNewTableName(newName);
        return command;
    }

    private Prepared ParseAlterTableAlter(Schema schema, string tableName, bool ifTableExists)
    {
        IsWord("COLUMN");
        bool ifExists = ReadIfExists(false);
        string columnName = ReadIdentifier();
        Column? column = ColumnIfTableExists(schema, tableName, columnName, ifTableExists, ifExists);
        if (IsWord("RENAME"))
        {
            ExpectWord("TO");
            AlterTableRenameColumn rename = new(session!, schema);
            rename.SetTableName(tableName);
            rename.SetIfTableExists(ifTableExists);
            rename.SetIfExists(ifExists);
            rename.SetOldColumnName(columnName);
            rename.SetNewColumnName(ReadIdentifier());
            return rename;
        }
        if (IsWord("DROP"))
        {
            if (IsWord("DEFAULT"))
            {
                if (ReadIfWords("ON", "NULL"))
                {
                    AlterTableAlterColumn command = GetAlterTableAlterColumnDropDefaultExpression(schema, tableName, ifTableExists, column,
                        CommandInterface.AlterTableAlterColumnDefaultOnNull);
                    command.SetBooleanFlag(false);
                    return command;
                }
                return GetAlterTableAlterColumnDropDefaultExpression(schema, tableName, ifTableExists, column,
                    CommandInterface.AlterTableAlterColumnDefault);
            }
            if (IsWord("EXPRESSION"))
                return GetAlterTableAlterColumnDropDefaultExpression(schema, tableName, ifTableExists, column,
                    CommandInterface.AlterTableAlterColumnDropExpression);
            if (IsWord("IDENTITY"))
                return GetAlterTableAlterColumnDropDefaultExpression(schema, tableName, ifTableExists, column,
                    CommandInterface.AlterTableAlterColumnDropIdentity);
            if (ReadIfWords("ON", "UPDATE"))
                return GetAlterTableAlterColumnDropDefaultExpression(schema, tableName, ifTableExists, column,
                    CommandInterface.AlterTableAlterColumnOnUpdate);
            ExpectWord("NOT");
            ExpectWord("NULL");
            return GetAlterTableAlterColumnDropDefaultExpression(schema, tableName, ifTableExists, column,
                CommandInterface.AlterTableAlterColumnDropNotNull);
        }
        if (IsWord("TYPE")) return ParseAlterTableAlterColumnDataType(schema, tableName, columnName, ifTableExists, ifExists);
        if (IsWord("SELECTIVITY"))
        {
            AlterTableAlterColumn command = new(session!, schema);
            command.SetTableName(tableName);
            command.SetIfTableExists(ifTableExists);
            command.SetType(CommandInterface.AlterTableAlterColumnSelectivity);
            command.SetOldColumn(column);
            command.SetSelectivity(ReadExpression());
            return command;
        }
        Prepared? identity = ParseAlterTableAlterColumnIdentity(schema, tableName, ifTableExists, column);
        if (identity is not null) return identity;
        if (IsWord("SET")) return ParseAlterTableAlterColumnSet(schema, tableName, ifTableExists, ifExists, columnName, column);
        return ParseAlterTableAlterColumnType(schema, tableName, columnName, ifTableExists, ifExists, true);
    }

    private AlterTableAlterColumn GetAlterTableAlterColumnDropDefaultExpression(Schema schema, string tableName,
        bool ifTableExists, Column? column, int type)
    {
        AlterTableAlterColumn command = new(session!, schema);
        command.SetTableName(tableName);
        command.SetIfTableExists(ifTableExists);
        command.SetOldColumn(column);
        command.SetType(type);
        command.SetDefaultExpression(null);
        return command;
    }

    private Prepared? ParseAlterTableAlterColumnIdentity(Schema schema, string tableName, bool ifTableExists, Column? column)
    {
        bool? always = null;
        if (ReadIfWords("SET", "GENERATED"))
        {
            if (IsWord("ALWAYS")) always = true;
            else { ExpectWord("BY"); ExpectWord("DEFAULT"); always = false; }
        }
        SequenceOptions options = new();
        if (!ReadSequenceOptions(options, null, false, true) && always is null) return null;
        if (column is null) return new Dml.NoOperation(session!);
        if (!column.IsIdentity())
        {
            AlterTableAlterColumn command = new(session!, schema);
            ParseAlterColumnUsingIf(command);
            command.SetTableName(tableName);
            command.SetIfTableExists(ifTableExists);
            command.SetType(CommandInterface.AlterTableAlterColumnChangeType);
            command.SetOldColumn(column);
            Column newColumn = column.GetClone();
            newColumn.SetIdentityOptions(options, always == true);
            command.SetNewColumn(newColumn);
            return command;
        }
        AlterSequence alter = new(session!, schema);
        alter.SetColumn(column, always);
        alter.SetOptions(options);
        return alter;
    }

    private Prepared ParseAlterTableAlterColumnSet(Schema schema, string tableName, bool ifTableExists,
        bool ifExists, string columnName, Column? column)
    {
        if (ReadIfWords("DATA", "TYPE"))
            return ParseAlterTableAlterColumnDataType(schema, tableName, columnName, ifTableExists, ifExists);
        AlterTableAlterColumn command = new(session!, schema);
        command.SetTableName(tableName);
        command.SetIfTableExists(ifTableExists);
        command.SetOldColumn(column);
        NullConstraintType nullConstraint = ParseNotNullConstraint();
        if (nullConstraint == NullConstraintType.NullIsAllowed) command.SetType(CommandInterface.AlterTableAlterColumnDropNotNull);
        else if (nullConstraint == NullConstraintType.NullIsNotAllowed) command.SetType(CommandInterface.AlterTableAlterColumnNotNull);
        else if (IsWord("DEFAULT"))
        {
            if (ReadIfWords("ON", "NULL"))
            {
                command.SetType(CommandInterface.AlterTableAlterColumnDefaultOnNull);
                command.SetBooleanFlag(true);
            }
            else
            {
                command.SetType(CommandInterface.AlterTableAlterColumnDefault);
                command.SetDefaultExpression(ReadExpression());
            }
        }
        else if (ReadIfWords("ON", "UPDATE"))
        {
            command.SetType(CommandInterface.AlterTableAlterColumnOnUpdate);
            command.SetDefaultExpression(ReadExpression());
        }
        else if (IsWord("INVISIBLE"))
        {
            command.SetType(CommandInterface.AlterTableAlterColumnVisibility);
            command.SetBooleanFlag(false);
        }
        else if (IsWord("VISIBLE"))
        {
            command.SetType(CommandInterface.AlterTableAlterColumnVisibility);
            command.SetBooleanFlag(true);
        }
        return command;
    }

    private Table? TableIfTableExists(Schema schema, string tableName, bool ifTableExists)
    {
        Table? table = schema.FindTableOrView(session, tableName);
        if (table is null && !ifTableExists) throw DbException.Get(Api.ErrorCode.TableOrViewNotFound1, tableName);
        return table;
    }

    private Column? ColumnIfTableExists(Schema schema, string tableName, string columnName, bool ifTableExists, bool ifExists)
        => TableIfTableExists(schema, tableName, ifTableExists)?.GetColumn(columnName, ifExists);

    private AlterTableAlterColumn ParseAlterTableAlterColumnType(Schema schema, string tableName, string columnName,
        bool ifTableExists, bool ifExists, bool preserveNotNull)
    {
        Column? oldColumn = ColumnIfTableExists(schema, tableName, columnName, ifTableExists, ifExists);
        Column newColumn = ParseColumnForTable(columnName, !preserveNotNull || oldColumn is null || oldColumn.IsNullable() == Column.Nullable);
        AlterTableAlterColumn command = new(session!, schema);
        ParseAlterColumnUsingIf(command);
        command.SetTableName(tableName);
        command.SetIfTableExists(ifTableExists);
        command.SetType(CommandInterface.AlterTableAlterColumnChangeType);
        command.SetOldColumn(oldColumn);
        command.SetNewColumn(newColumn);
        return command;
    }

    private AlterTableAlterColumn ParseAlterTableAlterColumnDataType(Schema schema, string tableName, string columnName,
        bool ifTableExists, bool ifExists)
    {
        Column? oldColumn = ColumnIfTableExists(schema, tableName, columnName, ifTableExists, ifExists);
        Column newColumn = ParseColumnWithType(columnName);
        if (oldColumn is not null)
        {
            if (oldColumn.IsNullable() == Column.NotNullable) newColumn.SetNullable(false);
            if (!oldColumn.GetVisible()) newColumn.SetVisible(false);
            Expression? expression = oldColumn.GetDefaultExpression();
            if (expression is not null)
            {
                if (oldColumn.IsGenerated()) newColumn.SetGeneratedExpression(expression);
                else newColumn.SetDefaultExpression(session!, expression);
            }
            expression = oldColumn.GetOnUpdateExpression();
            if (expression is not null) newColumn.SetOnUpdateExpression(session!, expression);
            Sequence? sequence = oldColumn.GetSequence();
            if (sequence is not null) newColumn.SetIdentityOptions(new SequenceOptions(sequence, newColumn.GetTypeInfo()), oldColumn.IsGeneratedAlways());
            if (oldColumn.GetComment() is string comment) newColumn.SetComment(comment);
        }
        AlterTableAlterColumn command = new(session!, schema);
        ParseAlterColumnUsingIf(command);
        command.SetTableName(tableName);
        command.SetIfTableExists(ifTableExists);
        command.SetType(CommandInterface.AlterTableAlterColumnChangeType);
        command.SetOldColumn(oldColumn);
        command.SetNewColumn(newColumn);
        return command;
    }

    private void ParseAlterColumnUsingIf(AlterTableAlterColumn command)
    {
        if (IsWord("USING")) command.SetUsingExpression(ReadExpression());
    }

    private enum NullConstraintType { NullIsAllowed, NullIsNotAllowed, NoNullConstraintFound }

    private NullConstraintType ParseNotNullConstraint()
    {
        if (ReadIfWords("NOT", "NULL")) return NullConstraintType.NullIsNotAllowed;
        if (IsWord("NULL")) return NullConstraintType.NullIsAllowed;
        return NullConstraintType.NoNullConstraintFound;
    }

    private NullConstraintType ParseNotNullConstraint(NullConstraintType constraint)
        => constraint == NullConstraintType.NoNullConstraintFound ? ParseNotNullConstraint() : constraint;

    private Column ParseColumnWithType(string columnName)
    {
        TypeInfo? type = ReadIfDataType();
        if (type is not null) return new Column(columnName, type);
        string domainName = ReadIdentifierWithSchema(session!, out Schema schema);
        Domain domain = schema.FindDomain(domainName) ?? throw DbException.Get(Api.ErrorCode.DomainNotFound1, domainName);
        Column column = new(columnName, domain.GetDataType());
        column.SetDomain(domain);
        column.SetComment(domain.GetComment());
        return column;
    }

    /// <summary>Reads the optional (start[, increment]) after the IDENTITY type.</summary>
    private void ParseCompatibilityIdentityOptions(Column column)
    {
        SequenceOptions options = new();
        if (ReadIf(Token.OpenParen))
        {
            options.SetStartValue(Expressions.ValueExpression.Get(Values.ValueBigint.Get(ReadLong())));
            if (ReadIf(Token.Comma))
            {
                options.SetIncrement(Expressions.ValueExpression.Get(Values.ValueBigint.Get(ReadLong())));
            }

            Expect(Token.CloseParen, ")");
        }

        column.SetIdentityOptions(options, false);
    }

    private Column ParseColumnForTable(string columnName, bool defaultNullable)
    {
        Column column;
        Mode mode = session?.GetMode() ?? provider.GetMode();
        if (mode.IdentityDataType && ReadIfCompat("IDENTITY"))
        {
            column = new Column(columnName, TypeInfo.TypeBigint);
            ParseCompatibilityIdentityOptions(column);
            column.SetPrimaryKey(true);
        }
        else if (mode.SerialDataTypes && ReadIfCompat("BIGSERIAL"))
        {
            column = new Column(columnName, TypeInfo.TypeBigint);
            column.SetIdentityOptions(new SequenceOptions(), false);
        }
        else if (mode.SerialDataTypes && ReadIfCompat("SERIAL"))
        {
            column = new Column(columnName, TypeInfo.TypeInteger);
            column.SetIdentityOptions(new SequenceOptions(), false);
        }
        else
        {
            column = ParseColumnWithType(columnName);
        }

        if (IsWord("INVISIBLE")) column.SetVisible(false);
        else if (IsWord("VISIBLE")) column.SetVisible(true);
        bool defaultOnNull = false;
        NullConstraintType nullConstraint = ParseNotNullConstraint();
        if (IsWord("AS")) column.SetGeneratedExpression(ReadExpression());
        else if (IsWord("DEFAULT"))
        {
            if (ReadIfWords("ON", "NULL")) defaultOnNull = true;
            else column.SetDefaultExpression(session!, ReadExpression());
        }
        else if (IsWord("GENERATED"))
        {
            bool always = IsWord("ALWAYS");
            if (!always) { ExpectWord("BY"); ExpectWord("DEFAULT"); }
            ExpectWord("AS");
            if (IsWord("IDENTITY"))
            {
                SequenceOptions options = new();
                if (ReadIf(Token.OpenParen))
                {
                    ReadSequenceOptions(options, null, false, false);
                    Expect(Token.CloseParen, ")");
                }
                column.SetIdentityOptions(options, always);
            }
            else if (always) column.SetGeneratedExpression(ReadExpression());
            else throw DbException.GetSyntaxError(sql, Current.Start);
        }
        if (!column.IsIdentity() && !column.IsGenerated() && ReadIfWords("ON", "UPDATE"))
            column.SetOnUpdateExpression(session!, ReadExpression());
        if (!column.IsIdentity())
        {
            nullConstraint = ParseNotNullConstraint(nullConstraint);
        }
        if (nullConstraint == NullConstraintType.NullIsAllowed)
        {
            if (column.IsIdentity()) throw DbException.Get(Api.ErrorCode.ColumnMustNotBeNullable1, columnName);
            column.SetNullable(true);
        }
        else if (nullConstraint == NullConstraintType.NullIsNotAllowed) column.SetNullable(false);
        else if (!column.IsIdentity()) column.SetNullable(defaultNullable);
        if (!defaultOnNull && ReadIfWords("DEFAULT", "ON", "NULL")) defaultOnNull = true;
        column.SetDefaultOnNull(defaultOnNull);
        if (!column.IsGenerated() && IsWord("SEQUENCE"))
        {
            string sequenceName = ReadIdentifierWithSchema(session!, out Schema schema);
            column.SetSequence(schema.GetSequence(sequenceName), column.IsGeneratedAlways());
        }
        if (IsWord("SELECTIVITY")) column.SetSelectivity(ReadNonNegativeInt());
        if (IsWord("COMMENT"))
        {
            IsWord("IS");
            column.SetComment(ReadString());
        }
        return column;
    }

    private CreateTable ParseCreateTable(bool temporary, bool globalTemporary, bool persistIndexes)
    {
        bool ifNotExists = ReadIfNotExists();
        string tableName = ReadIdentifierWithSchema(session!, out Schema schema);
        CreateTable command = new(session!, schema);
        command.SetPersistIndexes(persistIndexes);
        command.SetTemporary(temporary);
        command.SetGlobalTemporary(globalTemporary);
        command.SetIfNotExists(ifNotExists);
        command.SetTableName(tableName);
        if (IsWord("COMMENT")) { IsWord("IS"); command.SetComment(ReadString()); }
        if (ReadIf(Token.OpenParen) && !ReadIf(Token.CloseParen))
        {
            do { ParseTableColumnDefinition(command, schema, tableName, true); }
            while (ReadIfMore());
        }
        if (!persistIndexes && ReadIfWords("NOT", "PERSISTENT")) command.SetPersistData(false);
        if (IsWord("AS"))
        {
            command.SetQuery(ParseQueryExpression(prepare: false));
            if (IsWord("WITH"))
            {
                command.SetWithNoData(IsWord("NO"));
                ExpectWord("DATA");
            }
        }
        return command;
    }

    private void ParseTableColumnDefinition(CommandWithColumns command, Schema schema, string tableName, bool forCreateTable)
    {
        if (CurrentType is ParserUtil.Constraint or ParserUtil.Primary or ParserUtil.Foreign
            or ParserUtil.Unique or ParserUtil.Check)
        {
            command.AddConstraintCommand(CreateAddConstraintCommand(session!, schema, tableName, false, ReadTableConstraint(tableName)));
            return;
        }
        string columnName = ReadIdentifier();
        if (forCreateTable && CurrentType is Token.Comma or Token.CloseParen)
        {
            // CREATE TABLE ... AS SELECT may name its columns without types; the query
            // supplies them (Parser.parseTableColumnDefinition).
            command.AddColumn(new Column(columnName, TypeInfo.TypeUnknown));
            return;
        }
        Column column = ParseColumnForTable(columnName, true);
        if (column.HasIdentityOptions() && column.IsPrimaryKey())
        {
            command.AddConstraintCommand(NewPrimaryKeyConstraintCommand(session!, schema, tableName, column));
        }

        command.AddColumn(column);
        ReadColumnConstraints(command, schema, tableName, column);
    }

    public static AlterTableAddConstraint NewPrimaryKeyConstraintCommand(SessionLocal session, Schema schema, string tableName, Column column)
    {
        column.SetPrimaryKey(false);
        AlterTableAddConstraint pk = new(session, schema, CommandInterface.AlterTableAddConstraintPrimaryKey, false);
        pk.SetTableName(tableName);
        pk.SetIndexColumns([new IndexColumn(column.GetName())]);
        return pk;
    }

    private void ReadColumnConstraints(CommandWithColumns command, Schema schema, string tableName, Column column)
    {
        while (true)
        {
            string? constraintName = IsWord("CONSTRAINT") ? ReadIdentifier() : null;
            if (ReadIfWords("PRIMARY", "KEY"))
            {
                AlterTableAddConstraint pk = NewPrimaryKeyConstraintCommand(session!, schema, tableName, column);
                if (constraintName is not null) pk.SetConstraintName(constraintName);
                pk.SetPrimaryKeyHash(IsWord("HASH"));
                command.AddConstraintCommand(pk);
            }
            else if (IsWord("REFERENCES"))
            {
                ReadReferences(tableName, out string? refSchema, out string refTable, out List<string>? refColumns,
                    out string? refIndexSchema, out string? refIndexName,
                    out Constraints.ConstraintActionType updateAction, out Constraints.ConstraintActionType deleteAction);
                TableConstraintDefinition definition = new(constraintName, false, [column.GetName()], false,
                    CommandInterface.AlterTableAddConstraintReferential, RefTableSchema: refSchema, RefTable: refTable,
                    RefColumns: refColumns, UpdateAction: updateAction, DeleteAction: deleteAction,
                    RefIndexSchema: refIndexSchema, RefIndexName: refIndexName);
                command.AddConstraintCommand(CreateAddConstraintCommand(session!, schema, tableName, false, definition));
            }
            else if (IsWord("UNIQUE"))
            {
                NullsDistinct nullsDistinct = ReadNullsDistinct(NullsDistinct.Distinct);
                TableConstraintDefinition definition = new(constraintName, false, [column.GetName()], false,
                    CommandInterface.AlterTableAddConstraintUnique, NullsDistinct: nullsDistinct);
                command.AddConstraintCommand(CreateAddConstraintCommand(session!, schema, tableName, false, definition));
            }
            else if (IsWord("CHECK"))
            {
                Expression checkExpression = ReadExpression();
                TableConstraintDefinition definition = new(constraintName, false, [], false,
                    CommandInterface.AlterTableAddConstraintCheck, CheckExpression: checkExpression);
                command.AddConstraintCommand(CreateAddConstraintCommand(session!, schema, tableName, false, definition));
            }
            else if (ReadIfWords("NOT", "NULL")) column.SetNullable(false);
            else if (IsWord("NULL")) column.SetNullable(true);
            else if (IsWord("COMMENT")) { IsWord("IS"); column.SetComment(ReadString()); }
            else if (constraintName is null && !column.IsIdentity()
                && (session?.GetMode() ?? provider.GetMode()).AutoIncrementClause
                && IsWord("AUTO_INCREMENT"))
            {
                ParseCompatibilityIdentityOptions(column);
            }
            else if (constraintName is null && !column.IsIdentity()
                && (session?.GetMode() ?? provider.GetMode()).IdentityClause
                && IsWord("IDENTITY"))
            {
                ParseCompatibilityIdentityOptions(column);
            }
            else
            {
                if (constraintName is not null) throw DbException.GetSyntaxError(sql, Current.Start);
                return;
            }
        }
    }

    private Dml.Insert ParseInsert(int start)
    {
        Dml.Insert command = new(session!);
        currentPrepared = command;
        ExpectWord("INTO");
        string tableName = ReadIdentifierWithSchema(session!, out Schema schema);
        Table table = schema.GetTableOrView(session, tableName);
        command.SetTable(table);
        List<Column>? columns = null;
        if (ReadIf(Token.OpenParen))
        {
            if (IsQueryQuick())
            {
                command.SetQuery(ParseQueryExpression(prepare: false));
                Expect(Token.CloseParen, ")");
                return command;
            }

            columns = [];
            if (!ReadIf(Token.CloseParen))
            {
                do
                {
                    Column column = table.GetColumn(ReadIdentifier());
                    if (columns.Contains(column))
                    {
                        throw Message.DbException.Get(
                            Api.ErrorCode.DuplicateColumnName1, column.GetName());
                    }

                    columns.Add(column);
                }
                while (ReadIf(Token.Comma));
                Expect(Token.CloseParen, ")");
            }
            command.SetColumns([.. columns]);
        }
        bool? overridingSystem = null;
        if (IsWord("OVERRIDING"))
        {
            overridingSystem = IsWord("SYSTEM");
            if (overridingSystem == false) ExpectWord("USER");
            ExpectWord("VALUE");
        }
        command.SetOverridingSystem(overridingSystem);
        bool requireQuery = false;
        if (ReadIfCompat("DIRECT"))
        {
            requireQuery = true;
            command.SetInsertFromSelect(true);
        }

        if (ReadIfCompat("SORTED"))
        {
            requireQuery = true;
        }

        if (!requireQuery && overridingSystem is null && ReadIfWords("DEFAULT", "VALUES"))
        {
            command.AddRow(System.Array.Empty<Expression>());
        }
        else if (!requireQuery && IsWord("VALUES"))
        {
            do
            {
                Expect(Token.OpenParen, "(");
                List<Expression> values = [];
                if (!ReadIf(Token.CloseParen))
                {
                    do { values.Add(ReadExpressionOrDefault()); } while (ReadIf(Token.Comma));
                    Expect(Token.CloseParen, ")");
                }
                command.AddRow(values.ToArray());
            }
            while (ReadIf(Token.Comma));
        }
        else if (!requireQuery && CurrentType == ParserUtil.Set)
        {
            if (columns is not null)
            {
                throw DbException.GetSyntaxError(sql, Current.Start);
            }

            Read();
            List<Column> setColumns = [];
            List<Expression> values = [];
            do
            {
                setColumns.Add(table.GetColumn(ReadIdentifier()));
                Expect(Token.Equal, "=");
                values.Add(ReadExpressionOrDefault());
            }
            while (ReadIf(Token.Comma));

            command.SetColumns([.. setColumns]);
            command.AddRow([.. values]);
        }
        else command.SetQuery(ParseQueryExpression(prepare: false));
        return command;
    }

    private Prepared ParseDrop()
    {
        ExpectWord("DROP");
        ExpectWord("TABLE");
        bool ifExists = ReadIfExists(false);
        DropTable command = new(session!);
        do
        {
            string tableName = ReadIdentifierWithSchema(session!, out Schema schema);
            command.AddTable(schema, tableName);
        }
        while (ReadIf(Token.Comma));
        ifExists = ReadIfExists(ifExists);
        command.SetIfExists(ifExists);
        if (IsWord("CASCADE")) { IsWord("CONSTRAINTS"); command.SetDropAction(Constraints.ConstraintActionType.CASCADE); }
        else if (IsWord("RESTRICT")) command.SetDropAction(Constraints.ConstraintActionType.RESTRICT);
        else if (IsWord("IGNORE")) command.SetDropAction(Constraints.ConstraintActionType.SET_DEFAULT);
        return command;
    }
}
