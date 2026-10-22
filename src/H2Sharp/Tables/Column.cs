/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.Column.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A column of a table.
 *
 * Three things in here are about *when* a value is decided, and getting them in the wrong order
 * produces rows that look right and are not.
 *
 * The default and the NULL check come before the conversion. A column with no value is NULL, and
 * a column that is NOT NULL refuses it — *then* whatever arrived is converted to the column's
 * type. The other order would report "cannot convert NULL to INT" for a column whose real
 * problem is that it is not nullable, and the message names the wrong thing.
 *
 * An identity column takes its value from the sequence when none was given, and a manual insert
 * into one pushes the sequence forward if the inserted value is beyond it — otherwise the next
 * generated value would collide with a row that is already there.
 *
 * equals is on the *table and the name*, never on the type: a column that was created and later
 * widened is still the same column, and a hash of the type would make the second one a different
 * object from the first. The rowid pseudo column is the exception the reference allows itself —
 * it is not a column at all, and it prints unquoted because its name is `_ROWID_`.
 */
using System;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Tables;

/// <summary>
/// A column of a table.
/// </summary>
public class Column : Typed
{
    /// <summary>The name of the rowid pseudo column.</summary>
    public const string RowId = "_ROWID_";

    /// <summary>This column is not nullable.</summary>
    public const int NotNullable = 0;

    /// <summary>This column is nullable.</summary>
    public const int Nullable = 1;

    /// <summary>It is not known whether this column is nullable.</summary>
    public const int NullableUnknown = 2;

    private TypeInfo type;

    private Table? table;

    private string name;

    private int columnId;

    private bool nullable = true;

    private Expression? defaultExpression;

    private Expression? onUpdateExpression;

    private bool defaultOnNull;

    private bool isGeneratedAlways;

    /// <summary>
    /// The options of the identity column, when the column is one and the sequence of it
    /// has not been created yet.
    /// </summary>
    private Command.Ddl.SequenceOptions? identityOptions;

    /// <summary>
    /// The sequence of the identity column, once it is created.
    /// </summary>
    private Schemas.Sequence? sequence;

    /// <summary>
    /// The resolver the expression of a generated column is mapped through.
    /// </summary>
    private GeneratedColumnResolver? generatedTableFilter;

    private int selectivity;

    private string? comment;

    private bool primaryKey;

    private bool visible = true;

    private bool rowId;

    /// <summary>
    /// Creates a column that is not yet part of a table.
    /// </summary>
    /// <param name="name">the column name</param>
    /// <param name="type">the data type</param>
    public Column(string name, TypeInfo type)
    {
        this.name = name;
        this.type = type;
    }

    /// <summary>
    /// Creates a column of a table.
    /// </summary>
    /// <param name="name">the column name</param>
    /// <param name="type">the data type</param>
    /// <param name="table">the table</param>
    /// <param name="columnId">the position in the table</param>
    public Column(string name, TypeInfo type, Table? table, int columnId)
    {
        this.name = name;
        this.type = type;
        this.table = table;
        this.columnId = columnId;
    }

    /// <summary>
    /// Appends the specified columns to the specified builder.
    /// </summary>
    /// <param name="builder">the builder</param>
    /// <param name="columns">the columns</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the builder</returns>
    public static StringBuilder WriteColumns(StringBuilder builder, Column[] columns, int sqlFlags)
    {
        for (int i = 0; i < columns.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            _ = columns[i].GetSql(builder, sqlFlags);
        }

        return builder;
    }

    /// <summary>
    /// Appends the specified columns with a separator and a suffix after each.
    /// </summary>
    /// <param name="builder">the builder</param>
    /// <param name="columns">the columns</param>
    /// <param name="separator">the separator</param>
    /// <param name="suffix">additional SQL after each column</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the builder</returns>
    public static StringBuilder WriteColumns(
        StringBuilder builder,
        Column[] columns,
        string separator,
        string suffix,
        int sqlFlags)
    {
        for (int i = 0; i < columns.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(separator);
            }

            _ = columns[i].GetSql(builder, sqlFlags).Append(suffix);
        }

        return builder;
    }

    /// <summary>
    /// Returns a copy of this column that belongs to no table.
    /// </summary>
    /// <returns>the copy</returns>
    public Column GetClone()
    {
        Column newColumn = new(name, type);
        newColumn.Copy(this);
        return newColumn;
    }

    private void Copy(Column other)
    {
        nullable = other.nullable;
        defaultExpression = other.defaultExpression;
        onUpdateExpression = other.onUpdateExpression;
        defaultOnNull = other.defaultOnNull;
        isGeneratedAlways = other.isGeneratedAlways;
        selectivity = other.selectivity;
        comment = other.comment;
        primaryKey = other.primaryKey;
        visible = other.visible;
        rowId = other.rowId;
    }

    /// <summary>
    /// Sets the table and column id.
    /// </summary>
    /// <param name="newTable">the table</param>
    /// <param name="columnId">the column index</param>
    public void SetTable(Table? newTable, int columnId)
    {
        table = newTable;
        this.columnId = columnId;
    }

    /// <summary>
    /// Gets the table this column belongs to.
    /// </summary>
    /// <returns>the table</returns>
    public Table? GetTable()
    {
        return table;
    }

    /// <summary>
    /// Gets the position of this column in its table.
    /// </summary>
    /// <returns>the position</returns>
    public int GetColumnId()
    {
        return columnId;
    }

    /// <summary>
    /// Gets the column name.
    /// </summary>
    /// <returns>the name</returns>
    public string GetName()
    {
        return name;
    }

    /// <summary>
    /// Renames this column.
    /// </summary>
    /// <param name="newName">the new name</param>
    public void Rename(StringBuilder newName)
    {
        name = newName.ToString();
    }

    /// <inheritdoc/>
    public virtual string GetSql(int sqlFlags)
    {
        return GetSql(new StringBuilder(), sqlFlags).ToString();
    }

    /// <inheritdoc/>
    public virtual StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        return rowId ? builder.Append(name) : ParserUtil.QuoteIdentifier(builder, name, sqlFlags);
    }

    /// <summary>
    /// Appends the table name and column name to the specified builder.
    /// </summary>
    /// <param name="builder">the string builder</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the string builder</returns>
    public StringBuilder GetSqlWithTable(StringBuilder builder, int sqlFlags)
    {
        ArgumentNullException.ThrowIfNull(table);
        return GetSql(table.GetSql(builder, sqlFlags).Append('.'), sqlFlags);
    }

    /// <summary>
    /// Gets the type of this column.
    /// </summary>
    /// <returns>the type</returns>
    public TypeInfo GetTypeInfo()
    {
        return type;
    }

    /// <summary>
    /// Converts a row's values to the types of the given columns.
    /// </summary>
    /// <remarks>
    /// Not ported. It copies the row and converts each value to its column's type, so that a
    /// compound IN list read from storage compares against values of the right types. Until it
    /// is here, a compound IN over columns of different types would compare unconverted values
    /// and match rows it should not.
    /// </remarks>
    /// <param name="provider">where casts come from</param>
    /// <param name="columns">the columns</param>
    /// <param name="valueRow">the row</param>
    /// <returns>the converted row</returns>
    public static Values.ValueRow Convert(Engine.CastDataProvider? provider, Column[] columns, Values.ValueRow valueRow)
        => throw new TODOException("Column.convert: compound IN conversion is not ported");
    /// <returns>the value type</returns>
    public int GetValueType()
    {
        return type.GetValueType();
    }

    /// <summary>
    /// Gets the type name of this column.
    /// </summary>
    /// <returns>the type name</returns>
    public string GetTypeName()
    {
        return type.GetSql(new StringBuilder(), HasSql.DefaultSqlFlags).ToString();
    }

    /// <summary>
    /// Gets the declared precision of this column.
    /// </summary>
    /// <returns>the precision</returns>
    public long GetPrecision()
    {
        return type.GetDeclaredPrecision();
    }

    /// <summary>
    /// Gets the declared scale of this column.
    /// </summary>
    /// <returns>the scale</returns>
    public int GetScale()
    {
        return type.GetDeclaredScale();
    }

    /// <summary>
    /// Sets the type of this column.
    /// </summary>
    /// <param name="newType">the new type</param>
    public void SetType(TypeInfo newType)
    {
        type = newType;
    }

    /// <summary>
    /// Sets whether this column may be null.
    /// </summary>
    /// <param name="b">the new value</param>
    public void SetNullable(bool b)
    {
        nullable = b;
    }

    /// <summary>
    /// Whether this column may be null, as the three-valued answer of the reference.
    /// </summary>
    /// <returns>whether NULL is allowed</returns>
    public int IsNullable()
    {
        return nullable ? Nullable : NotNullable;
    }

    /// <summary>
    /// Whether this column is visible.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool GetVisible()
    {
        return visible;
    }

    /// <summary>
    /// Sets whether this column is visible.
    /// </summary>
    /// <param name="b">the new value</param>
    public void SetVisible(bool b)
    {
        visible = b;
    }

    /// <summary>
    /// Returns whether this column is a row identity column.
    /// </summary>
    /// <returns>true for _ROWID_ column, false otherwise</returns>
    public bool IsRowId()
    {
        return rowId;
    }

    /// <summary>
    /// Set row identity flag.
    /// </summary>
    /// <param name="rowId">true for _ROWID_ column, false otherwise</param>
    public void SetRowId(bool rowId)
    {
        this.rowId = rowId;
    }

    /// <summary>
    /// Whether this column is part of the primary key.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsPrimaryKey()
    {
        return primaryKey;
    }

    /// <summary>
    /// Sets whether this column is part of the primary key.
    /// </summary>
    /// <param name="b">the new value</param>
    public void SetPrimaryKey(bool b)
    {
        primaryKey = b;
    }

    /// <summary>
    /// Returns whether this column is an identity column.
    /// </summary>
    /// <returns>whether this column is an identity column</returns>
    public bool IsIdentity()
    {
        // The reference's isIdentity: a column is an identity one when it has a sequence or
        // when it is waiting for one.
        return sequence is not null || identityOptions is not null;
    }

    /// <summary>
    /// Whether this column has the options of an identity column.
    /// </summary>
    /// <returns>whether it has</returns>
    public bool HasIdentityOptions() => identityOptions is not null;

    /// <summary>
    /// The options of the identity column, or null when the column is not one.
    /// </summary>
    /// <returns>the options</returns>
    public Command.Ddl.SequenceOptions? GetIdentityOptions() => identityOptions;

    /// <summary>
    /// Sets the options of the identity column.
    /// </summary>
    /// <remarks>
    /// The reference's setIdentityOptions: an identity column has no default and no on-update
    /// expression, and it is never nullable.
    /// </remarks>
    /// <param name="options">the options</param>
    /// <param name="generatedAlways">whether the value is always generated</param>
    public void SetIdentityOptions(Command.Ddl.SequenceOptions options, bool generatedAlways)
    {
        identityOptions = options;
        isGeneratedAlways = generatedAlways;
        RemoveNonIdentityProperties();
    }

    /// <summary>
    /// Removes the properties an identity column may not have.
    /// </summary>
    private void RemoveNonIdentityProperties()
    {
        nullable = false;
        onUpdateExpression = null;
        defaultExpression = null;
    }

    /// <summary>
    /// The sequence of this column, when it is an identity column.
    /// </summary>
    /// <returns>the sequence, or null</returns>
    public Schemas.Sequence? GetSequence() => sequence;

    /// <summary>
    /// Sets the sequence of this column, or takes it away.
    /// </summary>
    /// <param name="newSequence">the sequence, or null when the column has none</param>
    /// <param name="generatedAlways">whether the value is always generated</param>
    public void SetSequence(Schemas.Sequence? newSequence, bool generatedAlways)
    {
        sequence = newSequence;
        isGeneratedAlways = generatedAlways;
        identityOptions = null;
        if (newSequence is not null)
        {
            nullable = false;
        }
    }

    /// <summary>
    /// Creates the sequence of this identity column and puts it in the schema.
    /// </summary>
    /// <remarks>
    /// The reference's initializeSequence. The name of the sequence is a random one, so that
    /// two tables cannot have the same, and the column is not nullable from now on.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="schema">the schema the sequence belongs to</param>
    /// <param name="id">the object id of the sequence</param>
    /// <param name="temporary">whether the sequence is temporary</param>
    public void InitializeSequence(
        Engine.SessionLocal session,
        Schemas.Schema schema,
        int id,
        bool temporary)
    {
        if (identityOptions is null)
        {
            throw Message.DbException.GetInternalError();
        }

        string sequenceName;
        do
        {
            sequenceName = "SYSTEM_SEQUENCE_"
                + Util.StringUtils.ToUpperEnglish(
                    ValueUuid.GetNewRandom(4).GetString().Replace("-", "_"));
        }
        while (schema.FindSequence(sequenceName) is not null);

        identityOptions.SetDataType(type);
        Schemas.Sequence seq = new(session, schema, id, sequenceName, identityOptions, true);
        session.GetDatabase().AddSchemaObject(session, seq);
        SetSequence(seq, isGeneratedAlways);
    }

    /// <summary>
    /// Returns whether this column is a generated column.
    /// </summary>
    /// <returns>whether this column is a generated column</returns>
    public bool IsGenerated()
    {
        return isGeneratedAlways && defaultExpression is not null;
    }

    /// <summary>
    /// Returns whether this column is generated always, which includes an identity column.
    /// </summary>
    /// <returns>whether this column is always generated</returns>
    public bool IsGeneratedAlways()
    {
        return isGeneratedAlways;
    }

    /// <summary>
    /// Set the default value in the form of a generated expression of other columns.
    /// </summary>
    /// <param name="expression">the computed expression</param>
    public void SetGeneratedExpression(Expression expression)
    {
        isGeneratedAlways = true;
        defaultExpression = expression;
    }

    /// <summary>
    /// Sets the default expression, after optimising it and folding it to a value when it is
    /// constant — so that no column name is left in it.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="newDefaultExpression">the default expression</param>
    public void SetDefaultExpression(Engine.SessionLocal session, Expression? newDefaultExpression)
    {
        if (newDefaultExpression is not null)
        {
            newDefaultExpression = newDefaultExpression.Optimize(session);
            if (newDefaultExpression.IsConstant())
            {
                newDefaultExpression = ValueExpression.Get(newDefaultExpression.GetValue(session));
            }
        }

        defaultExpression = newDefaultExpression;
        isGeneratedAlways = false;
    }

    /// <summary>
    /// Sets the expression that is evaluated when the row is updated.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="newOnUpdateExpression">the expression</param>
    public void SetOnUpdateExpression(Engine.SessionLocal session, Expression? newOnUpdateExpression)
    {
        if (newOnUpdateExpression is not null)
        {
            newOnUpdateExpression = newOnUpdateExpression.Optimize(session);
            if (newOnUpdateExpression.IsConstant())
            {
                newOnUpdateExpression = ValueExpression.Get(newOnUpdateExpression.GetValue(session));
            }
        }

        onUpdateExpression = newOnUpdateExpression;
    }

    /// <summary>Gets the default expression, if any.</summary>
    public Expression? GetDefaultExpression()
    {
        return defaultExpression;
    }

    /// <summary>Gets the on-update expression, if any.</summary>
    public Expression? GetOnUpdateExpression()
    {
        return onUpdateExpression;
    }

    /// <summary>Whether the default is used when an explicit NULL is given.</summary>
    public bool IsDefaultOnNull()
    {
        return defaultOnNull;
    }

    /// <summary>Sets whether the default is used when an explicit NULL is given.</summary>
    /// <param name="b">the new value</param>
    public void SetDefaultOnNull(bool b)
    {
        defaultOnNull = b;
    }

    /// <summary>Gets the comment of this column.</summary>
    public string? GetComment()
    {
        return comment;
    }

    /// <summary>Sets the comment of this column.</summary>
    /// <param name="newComment">the new comment</param>
    public void SetComment(string? newComment)
    {
        comment = string.IsNullOrEmpty(newComment) ? null : newComment;
    }

    /// <summary>Gets the selectivity of this column.</summary>
    /// <remarks>
    /// The default when nothing was measured, because a column whose selectivity was never
    /// analyzed is not a column with no distinct values: answering 0 would price every lookup
    /// on it as a full scan, and the planner would never choose the index that holds it.
    /// </remarks>
    /// <returns>the selectivity, or the default when it was never set</returns>
    public int GetSelectivity()
    {
        return selectivity == 0 ? Engine.Constants.SelectivityDefault : selectivity;
    }

    /// <summary>Sets the selectivity of this column.</summary>
    /// <param name="newSelectivity">the new value</param>
    public void SetSelectivity(int newSelectivity)
    {
        selectivity = newSelectivity;
    }

    /// <summary>
    /// Convert a value to this column's type without precision and scale checks.
    /// </summary>
    /// <param name="provider">the cast information provider</param>
    /// <param name="v">the value</param>
    /// <returns>the value</returns>
    public Value Convert(Engine.CastDataProvider? provider, Value v)
    {
        ArgumentNullException.ThrowIfNull(v);
        try
        {
            return v.ConvertToType(type);
        }
        catch (H2SharpException e) when (e.H2ErrorCode == ErrorCode.DataConversionError1)
        {
            throw GetDataConversionError(v, e);
        }
    }

    /// <summary>
    /// Validate the value, convert it if required. If the value is null, the default value
    /// (NULL if no default is set) is returned.
    /// </summary>
    /// <remarks>
    /// The order is the reference's and it matters: the default and the NOT NULL refusal happen
    /// before the conversion, so a missing value in a NOT NULL column is refused as "no value"
    /// rather than as a conversion failure of NULL.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="value">the value or null</param>
    /// <returns>the new or converted value</returns>
    public Value ValidateConvertUpdateSequence(Engine.SessionLocal session, Value? value, Row? row)
    {
        Value v;
        if (value is null)
        {
            if (sequence is not null)
            {
                // An identity column takes the next value of its sequence.
                v = session.GetNextValueFor(sequence, null);
            }
            else
            {
                v = GetDefaultOrGenerated(session, row);
            }
        }
        else
        {
            v = value;
        }

        if (v is ValueNull && !nullable)
        {
            throw DbException.Get(ErrorCode.NullNotAllowed, name);
        }

        try
        {
            // The reference converts for assignment: the session is the provider, so a value
            // that has to be moved between zones knows the zone of the reader, and the name
            // of the column is in the message when the value does not fit.
            return v.ConvertForAssignTo(type, session, name);
        }
        catch (H2SharpException e) when (e.H2ErrorCode == ErrorCode.DataConversionError1)
        {
            throw GetDataConversionError(v, e);
        }
    }

    /// <summary>
    /// Prepares the expressions of this column: the expression of a generated column is mapped
    /// through the resolver of the row that is being written, and then optimized.
    /// </summary>
    /// <remarks>
    /// The reference's prepareExpressions. The mapping comes first: an expression that names
    /// the other columns of the row cannot be optimized before it knows them.
    /// </remarks>
    /// <param name="session">the session</param>
    public void PrepareExpressions(Engine.SessionLocal session)
    {
        if (defaultExpression is not null)
        {
            if (isGeneratedAlways)
            {
                generatedTableFilter = new GeneratedColumnResolver(table!);
                defaultExpression.MapColumns(generatedTableFilter, 0, Expression.MapInitial);
            }

            defaultExpression = defaultExpression.Optimize(session);
        }

        if (onUpdateExpression is not null)
        {
            onUpdateExpression = onUpdateExpression.Optimize(session);
        }
    }

    private Value GetDefaultOrGenerated(Engine.SessionLocal session, Row? row)
    {
        Expression? localDefault = defaultExpression;
        if (localDefault is null)
        {
            return ValueNull.Instance;
        }

        if (isGeneratedAlways)
        {
            // The expression of a generated column reads the row that is being written, so
            // the row is put into the resolver for the time of the evaluation.
            GeneratedColumnResolver? filter = generatedTableFilter;
            if (filter is null)
            {
                return localDefault.GetValue(session);
            }

            filter.Set(row);
            try
            {
                return localDefault.GetValue(session);
            }
            finally
            {
                filter.Set(null);
            }
        }

        return localDefault.GetValue(session);
    }

    private H2SharpException GetDataConversionError(Value value, H2SharpException cause)
    {
        StringBuilder builder = new();
        _ = value.GetSql(new StringBuilder(), HasSql.TraceSqlFlags).Append(" (");
        if (table is not null)
        {
            _ = builder.Append(table.GetTableName()).Append(": ");
        }

        _ = builder.Append(GetCreateSql()).Append(')');
        return DbException.Get(ErrorCode.DataConversionError1, cause, builder.ToString());
    }

    /// <summary>
    /// The CREATE ... SQL of this column, as it appears inside a CREATE TABLE.
    /// </summary>
    /// <returns>the SQL</returns>
    public string GetCreateSql()
    {
        StringBuilder builder = new();
        _ = GetSql(builder, HasSql.DefaultSqlFlags);
        _ = type.GetSql(builder, HasSql.DefaultSqlFlags);
        if (null != defaultExpression)
        {
            _ = builder.Append(" DEFAULT ").Append(defaultExpression.GetSql(HasSql.DefaultSqlFlags));
        }

        if (!nullable)
        {
            _ = builder.Append(" NOT NULL");
        }

        return builder.ToString();
    }

    /// <inheritdoc/>
    public override bool Equals(object? o)
    {
        if (ReferenceEquals(o, this))
        {
            return true;
        }

        if (o is not Column other)
        {
            return false;
        }

        // On the table and the name, never on the type: a column that was created and later
        // widened is still the same column.
        if (table is null || other.table is null || name is null || other.name is null)
        {
            return false;
        }

        return ReferenceEquals(table, other.table) && name == other.name;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (table is null || name is null)
        {
            return 0;
        }

        return table.GetId() ^ name.GetHashCode(StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return name + ":" + columnId + ":" + GetSql(HasSql.DefaultSqlFlags);
    }
}
