/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mode.OnDuplicateKeyValues.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * VALUES(column) in INSERT ... ON DUPLICATE KEY UPDATE.
 *
 * The expression reads the value the INSERT offered for a column, not the value the target
 * row has now. The INSERT keeps those values while it handles a duplicate, so the expression
 * does not need a generated UPDATE statement to read them.
 */
using System.Text;
using H2Sharp.Command.Dml;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Modes;

/// <summary>
/// The <c>VALUES(column)</c> expression of an ON DUPLICATE KEY UPDATE clause.
/// </summary>
public sealed class OnDuplicateKeyValues : Expression
{
    private readonly Column column;

    private readonly Insert insert;

    /// <summary>
    /// Creates the expression.
    /// </summary>
    /// <param name="column">the column whose inserted value is read</param>
    /// <param name="insert">the INSERT that keeps the offered values</param>
    public OnDuplicateKeyValues(Column column, Insert insert)
    {
        this.column = column;
        this.insert = insert;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value? value = insert.GetOnDuplicateKeyValue(column.GetColumnId());
        if (value is null)
        {
            throw DbException.GetUnsupportedException(
                GetUnenclosedSql(new StringBuilder(), HasSql.TraceSqlFlags).ToString());
        }

        return value;
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => column.GetTypeInfo();

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
        => column.GetSql(builder.Append("VALUES("), sqlFlags).Append(')');

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
        => visitor.GetVisitorType() != ExpressionVisitor.Deterministic;

    /// <inheritdoc/>
    public override int GetCost() => 1;
}
