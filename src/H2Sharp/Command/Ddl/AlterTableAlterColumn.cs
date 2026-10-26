/*
 * Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.command.ddl.AlterTableAlterColumn, H2 2.5.
 * Copyright 2026 H2Sharp contributors.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;
using Index = H2Sharp.Indexes.Index;

namespace H2Sharp.Command.Ddl;

/// <summary>ALTER TABLE ALTER COLUMN, with the reference's table-copy path.</summary>
public sealed class AlterTableAlterColumn : CommandWithColumns
{
    private string tableName = string.Empty;
    private Column? oldColumn;
    private Column? newColumn;
    private int type;
    private Expression? defaultExpression;
    private Expression? newSelectivity;
    private Expression? usingExpression;
    private bool ifTableExists;
    private bool booleanFlag;
    private bool ifNotExists;
    private bool addFirst;
    private string? addBefore;
    private string? addAfter;
    private readonly List<Column> columnsToAdd = [];

    public AlterTableAlterColumn(SessionLocal session, Schema schema) : base(session, schema) { }
    public void SetIfTableExists(bool b) => ifTableExists = b;
    public void SetTableName(string tableName) => this.tableName = tableName;
    public void SetOldColumn(Column? oldColumn) => this.oldColumn = oldColumn;
    public void SetType(int type) => this.type = type;
    public void SetSelectivity(Expression selectivity) => newSelectivity = selectivity;
    public void SetDefaultExpression(Expression? defaultExpression) => this.defaultExpression = defaultExpression;
    public void SetUsingExpression(Expression usingExpression) => this.usingExpression = usingExpression;
    public void SetNewColumn(Column newColumn) => this.newColumn = newColumn;
    public void SetBooleanFlag(bool booleanFlag) => this.booleanFlag = booleanFlag;
    public void SetIfNotExists(bool value) => ifNotExists = value;
    public void SetAddFirst() => addFirst = true;
    public void SetAddBefore(string column) => addBefore = column;
    public void SetAddAfter(string column) => addAfter = column;
    public override void AddColumn(Column column) => columnsToAdd.Add(column);

    public override long Update()
    {
        SessionLocal session = GetSession();
        Database db = session.GetDatabase();
        Table? table = GetSchema().FindTableOrView(session, tableName);
        if (table is null)
        {
            if (ifTableExists) return 0;
            throw DbException.Get(ErrorCode.TableOrViewNotFound1, tableName);
        }
        // D-004: SCHEMA_OWNER rights have no user object to check.
        table.CheckSupportAlter();
        table.Lock(session, Table.ExclusiveLock);
        if (newColumn is not null)
        {
            CheckDefaultReferencesTable(table, newColumn.GetDefaultExpression());
            CheckClustering(newColumn);
        }
        foreach (Column column in columnsToAdd)
        {
            CheckDefaultReferencesTable(table, column.GetDefaultExpression());
            CheckClustering(column);
        }
        switch (type)
        {
            case CommandInterface.AlterTableAddColumn:
                bool exists = false;
                if (ifNotExists && columnsToAdd.Count == 1)
                {
                    foreach (Column c in table.GetColumns())
                    {
                        if (c.GetName().Equals(columnsToAdd[0].GetName(), StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }
                }

                if (!exists)
                {
                    CopyData(table, null, true);
                }

                break;
            case CommandInterface.AlterTableAlterColumnNotNull:
                if (oldColumn is null || oldColumn.IsNullable() == Column.NotNullable) break;
                CheckNoNullValues(table);
                oldColumn.SetNullable(false);
                db.UpdateMeta(session, table);
                break;
            case CommandInterface.AlterTableAlterColumnDropNotNull:
                if (oldColumn is null || oldColumn.IsNullable() == Column.Nullable) break;
                CheckNullable(table);
                oldColumn.SetNullable(true);
                db.UpdateMeta(session, table);
                break;
            case CommandInterface.AlterTableAlterColumnDefault:
            case CommandInterface.AlterTableAlterColumnDropExpression:
                if (oldColumn is null || oldColumn.IsIdentity()) break;
                if (defaultExpression is not null)
                {
                    if (oldColumn.IsGenerated()) break;
                    CheckDefaultReferencesTable(table, defaultExpression);
                    oldColumn.SetDefaultExpression(session, defaultExpression);
                }
                else
                {
                    if ((type == CommandInterface.AlterTableAlterColumnDropExpression) != oldColumn.IsGenerated()) break;
                    oldColumn.SetDefaultExpression(session, null);
                }
                db.UpdateMeta(session, table);
                break;
            case CommandInterface.AlterTableAlterColumnDropIdentity:
                if (oldColumn is null) break;
                Sequence? sequence = oldColumn.GetSequence();
                if (sequence is null) break;
                oldColumn.SetSequence(null, false);
                RemoveSequence(table, sequence);
                db.UpdateMeta(session, table);
                break;
            case CommandInterface.AlterTableAlterColumnOnUpdate:
                if (oldColumn is null) break;
                if (defaultExpression is not null)
                {
                    if (oldColumn.IsIdentity() || oldColumn.IsGenerated()) break;
                    CheckDefaultReferencesTable(table, defaultExpression);
                    oldColumn.SetOnUpdateExpression(session, defaultExpression);
                }
                else oldColumn.SetOnUpdateExpression(session, null);
                db.UpdateMeta(session, table);
                break;
            case CommandInterface.AlterTableAlterColumnChangeType:
                if (oldColumn is null) break;
                if (oldColumn.IsWideningConversion(newColumn!) && usingExpression is null)
                {
                    ConvertIdentityColumn(table, oldColumn, newColumn!);
                    oldColumn.Copy(newColumn!);
                    db.UpdateMeta(session, table);
                }
                else
                {
                    oldColumn.SetSequence(null, false);
                    oldColumn.SetDefaultExpression(session, null);
                    if (oldColumn.IsNullable() == Column.Nullable && newColumn!.IsNullable() == Column.NotNullable)
                        CheckNoNullValues(table);
                    else if (oldColumn.IsNullable() == Column.NotNullable && newColumn!.IsNullable() == Column.Nullable)
                        CheckNullable(table);
                    if (oldColumn.GetVisible() != newColumn!.GetVisible()) oldColumn.SetVisible(newColumn.GetVisible());
                    ConvertIdentityColumn(table, oldColumn, newColumn);
                    CopyData(table, null, true);
                }
                table.SetModified();
                break;
            case CommandInterface.AlterTableAlterColumnSelectivity:
                if (oldColumn is null) break;
                oldColumn.SetSelectivity(newSelectivity!.Optimize(session).GetValue(session).GetInt());
                db.UpdateMeta(session, table);
                break;
            case CommandInterface.AlterTableAlterColumnVisibility:
                if (oldColumn is null) break;
                if (oldColumn.GetVisible() != booleanFlag)
                {
                    oldColumn.SetVisible(booleanFlag);
                    table.SetModified();
                    db.UpdateMeta(session, table);
                }
                break;
            case CommandInterface.AlterTableAlterColumnDefaultOnNull:
                if (oldColumn is null) break;
                if (oldColumn.IsDefaultOnNull() != booleanFlag)
                {
                    oldColumn.SetDefaultOnNull(booleanFlag);
                    table.SetModified();
                    db.UpdateMeta(session, table);
                }
                break;
            default: throw DbException.GetInternalError("type=" + type);
        }
        return 0;
    }

    private static void CheckDefaultReferencesTable(Table table, Expression? expression)
    {
        if (expression is null) return;
        // D-011: Table is not DbObject; the existing visitor uses HashSet<object>.
        HashSet<object> dependencies = [];
        expression.IsEverything(ExpressionVisitor.GetDependenciesVisitor(dependencies));
        if (dependencies.Contains(table)) throw DbException.Get(ErrorCode.ColumnIsReferenced1, expression.GetTraceSql());
    }

    private void CheckClustering(Column column)
    {
        if (GetSession().GetDatabase().GetCluster() != Constants.ClusteringDisabled && column.HasIdentityOptions())
            throw DbException.GetUnsupportedException("CLUSTERING && identity columns");
    }

    private void ConvertIdentityColumn(Table table, Column oldColumn, Column newColumn)
    {
        if (!newColumn.HasIdentityOptions()) return;
        if (newColumn.IsPrimaryKey() && !oldColumn.IsPrimaryKey())
            AddConstraintCommand(Parser.NewPrimaryKeyConstraintCommand(GetSession(), table.GetSchema()!, table.GetTableName(), newColumn));
        newColumn.InitializeSequence(GetSession(), GetSchema(), GetObjectId(), table.IsTemporary());
    }

    private void RemoveSequence(Table table, Sequence sequence)
    {
        table.RemoveSequence(sequence);
        sequence.SetBelongsToTable(false);
        GetSession().GetDatabase().RemoveSchemaObject(GetSession(), sequence);
    }

    private void CopyData(Table table, List<Sequence>? sequences, bool createConstraints)
    {
        if (table.IsTemporary()) throw DbException.GetUnsupportedException("TEMP TABLE");
        Database db = GetSession().GetDatabase();
        string tempName = db.GetTempTableName(table.GetTableName(), GetSession());
        List<Column> newColumns = [];
        Table newTable = CloneTableStructure(table, table.GetColumns(), db, tempName, newColumns);
        if (sequences is not null)
            foreach (Sequence sequence in sequences) table.AddSequence(sequence);
        // Persistent TableView objects are not yet implemented, so neither table has dependent views.
        string originalName = table.GetTableName();
        Execute(table.GetSql(new StringBuilder("DROP TABLE "), HasSql.DefaultSqlFlags).Append(" IGNORE").ToString());
        db.RenameSchemaObject(GetSession(), newTable, originalName);
        foreach (DbObject child in newTable.GetChildren())
        {
            if (child is Sequence) continue;
            string? name = child.GetName();
            if (name is null || child.GetCreateSql() is null || !name.StartsWith(tempName + "_", System.StringComparison.Ordinal)) continue;
            name = name[(tempName.Length + 1)..];
            SchemaObject obj = (SchemaObject)child;
            if (obj is Constraint && obj.GetSchema().FindConstraint(GetSession(), name) is not null)
                name = obj.GetSchema().GetUniqueConstraintName(GetSession(), newTable);
            else if (obj is Index && obj.GetSchema().FindIndex(GetSession(), name) is not null)
                name = obj.GetSchema().GetUniqueIndexName(GetSession(), newTable, name);
            db.RenameSchemaObject(GetSession(), obj, name);
        }
        if (createConstraints) CreateConstraints();
    }

    private Table CloneTableStructure(Table table, Column[] columns, Database db, string tempName, List<Column> newColumns)
    {
        foreach (Column column in columns) newColumns.Add(column.GetClone());
        if (type == CommandInterface.AlterTableAddColumn)
        {
            int position;
            if (addFirst)
            {
                position = 0;
            }
            else if (addBefore is not null)
            {
                position = table.GetColumn(addBefore).GetColumnId();
            }
            else if (addAfter is not null)
            {
                position = table.GetColumn(addAfter).GetColumnId() + 1;
            }
            else
            {
                position = columns.Length;
            }

            foreach (Column column in columnsToAdd)
            {
                newColumns.Insert(position++, column);
            }
        }
        else
        {
            newColumns[oldColumn!.GetColumnId()] = newColumn!;
        }
        CreateTableData data = new()
        {
            TableName = tempName, Id = db.AllocateObjectId(), Columns = newColumns,
            Temporary = table.IsTemporary(), PersistIndexes = table.IsPersistIndexes(),
            // The existing MVTable API exposes the reference's isPersistData as a property.
            PersistData = ((MvStore.Db.MVTable)table).IsPersistData, Session = GetSession(),
        };
        Table newTable = GetSchema().CreateTable(data);
        newTable.SetComment(table.GetComment());
        string newTableSql = newTable.GetCreateSqlForMeta()!;
        StringBuilder columnNames = new(), columnValues = new();
        foreach (Column column in newColumns)
        {
            if (column.IsGenerated()) continue;
            if (type == CommandInterface.AlterTableAddColumn && columnsToAdd.Contains(column))
            {
                if (usingExpression is not null)
                {
                    usingExpression.GetUnenclosedSql(
                        AddColumn(column, columnNames, columnValues), HasSql.DefaultSqlFlags);
                }

                continue;
            }

            if (ReferenceEquals(column, newColumn) && usingExpression is not null)
                usingExpression.GetUnenclosedSql(AddColumn(column, columnNames, columnValues), HasSql.DefaultSqlFlags);
            else column.GetSql(AddColumn(column, columnNames, columnValues), HasSql.DefaultSqlFlags);
        }
        string newName = newTable.GetTableName();
        Schema newSchema = newTable.GetSchema()!;
        newTable.RemoveChildrenAndResources(GetSession());
        Execute(newTableSql);
        newTable = newSchema.GetTableOrView(GetSession(), newName);
        List<string> children = [];
        bool hasDelegateIndex = false;
        foreach (DbObject child in table.GetChildren())
        {
            if (child is Sequence || child is Index idx && idx.GetIndexType()!.GetBelongsToConstraint()) continue;
            if (child.GetCreateSql() is null) continue;
            string quotedName = ParserUtil.QuoteIdentifier(new StringBuilder(), tempName + "_" + child.GetName(), HasSql.DefaultSqlFlags).ToString();
            string? childSql = null;
            if (child is ConstraintReferential referential && !ReferenceEquals(referential.GetTable(), table))
                childSql = referential.GetCreateSqlForCopy(referential.GetTable(), newTable, quotedName, false);
            childSql ??= child.GetCreateSqlForCopy(newTable, quotedName);
            if (childSql is null) continue;
            if (!hasDelegateIndex)
            {
                Index? index = child is ConstraintUnique unique && unique.GetConstraintType() == Constraint.Type.PRIMARY_KEY
                    ? unique.GetIndex() : child as Index;
                if (index is not null && Table.GetMainIndexColumn(index.GetIndexType()!, index.GetColumns()) != SearchRow.RowidIndex)
                {
                    Execute(childSql);
                    hasDelegateIndex = true;
                    continue;
                }
            }
            children.Add(childSql);
        }
        StringBuilder builder = newTable.GetSql(new StringBuilder("INSERT INTO "), HasSql.DefaultSqlFlags)
            .Append('(').Append(columnNames).Append(") OVERRIDING SYSTEM VALUE SELECT ");
        builder.Append(columnValues.Length == 0 ? "*" : columnValues.ToString());
        table.GetSql(builder.Append(" FROM "), HasSql.DefaultSqlFlags);
        try { Execute(builder.ToString()); }
        catch
        {
            Execute(newTable.GetSql(new StringBuilder("DROP TABLE "), HasSql.DefaultSqlFlags).ToString());
            throw;
        }
        foreach (string childSql in children) Execute(childSql);
        table.SetModified();
        foreach (Column column in newColumns)
        {
            Sequence? sequence = column.GetSequence();
            if (sequence is null) continue;
            table.RemoveSequence(sequence);
            column.SetSequence(null, false);
        }
        return newTable;
    }

    private static StringBuilder AddColumn(Column column, StringBuilder names, StringBuilder values)
    {
        if (names.Length > 0) names.Append(", ");
        column.GetSql(names, HasSql.DefaultSqlFlags);
        if (values.Length > 0) values.Append(", ");
        return values;
    }

    private void Execute(string sql) => GetSession().PrepareCommand(sql).ExecuteUpdate(false);

    private void CheckNullable(Table table)
    {
        if (oldColumn!.IsIdentity()) throw DbException.Get(ErrorCode.ColumnMustNotBeNullable1, oldColumn.GetName());
        foreach (Index index in table.GetIndexes())
            if (index.GetColumnIndex(oldColumn) >= 0 && index.GetIndexType()!.IsPrimaryKey())
                throw DbException.Get(ErrorCode.ColumnMustNotBeNullable1, oldColumn.GetName());
    }

    private void CheckNoNullValues(Table table)
    {
        StringBuilder builder = table.GetSql(new StringBuilder("SELECT COUNT(*) FROM "), HasSql.DefaultSqlFlags).Append(" WHERE ");
        oldColumn!.GetSql(builder, HasSql.DefaultSqlFlags).Append(" IS NULL");
        ResultInterface result = GetSession().PrepareCommand(builder.ToString()).ExecuteQuery(0, 0, false);
        try
        {
            result.Next();
            if (result.CurrentRow()[0]!.GetInt() > 0)
                throw DbException.Get(ErrorCode.ColumnContainsNullValues1, oldColumn.GetSql(HasSql.TraceSqlFlags));
        }
        finally { result.Close(); }
    }

    public override int GetCommandType() => type;
}
