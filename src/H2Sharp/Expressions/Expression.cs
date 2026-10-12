/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.Expression.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * An expression is an operation, a value, or a function in a query.
 *
 * Most of this class is small answers with consequences, and three of them decide what SQL a
 * statement prints as — so they are worth reading before changing anything.
 *
 * Parentheses are not a property of an expression, they are a decision made by whoever is
 * *using* it: getSQL takes a parentheses mode, and AUTO_PARENTHESES asks the expression whether
 * it needs them. That is why needParentheses is a method and not a field — the same expression
 * is bare inside "a + b" and wrapped as "(a > b)" inside a NOT, and the decision belongs to the
 * parent. An expression that decided for itself would print parentheses that the reference does
 * not print, and the difference shows up in INFORMATION_SCHEMA and in every test that compares
 * generated SQL.
 *
 * getAlias is the same kind of answer: the column name of a select item is usually its SQL, but
 * the mode may say "C1", "1", "" or "?column?", and there is a length limit on the first form —
 * past it the reference switches to "C1", because a column name is an identifier and identifiers
 * are not arbitrarily long. Which answer is right depends on the database mode, so it is asked
 * of the session rather than decided here.
 *
 * The row is not passed to getValue. In this version of the reference the current row is
 * reached through the session, which is why the signature is getValue(session) and not
 * getValue(session, row): the expressions that need a row say so by being column expressions,
 * and the rest — literals, parameters — answer the same for every row.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>
/// An expression is an operation, a value, or a function in a query.
/// </summary>
public abstract class Expression
{
    /// <summary>
    /// Initial state for <see cref="MapColumns(ColumnResolver, int, int)"/>.
    /// </summary>
    public const int MapInitial = 0;

    /// <summary>
    /// State for expressions inside a window function for
    /// <see cref="MapColumns(ColumnResolver, int, int)"/>.
    /// </summary>
    public const int MapInWindow = 1;

    /// <summary>
    /// State for expressions inside an aggregate for
    /// <see cref="MapColumns(ColumnResolver, int, int)"/>.
    /// </summary>
    public const int MapInAggregate = 2;

    /// <summary>
    /// Wrap expression in parentheses only if it can't be safely included into
    /// other expressions without them.
    /// </summary>
    public const int AutoParentheses = 0;

    /// <summary>
    /// Wrap expression in parentheses unconditionally.
    /// </summary>
    public const int WithParentheses = 1;

    /// <summary>
    /// Do not wrap expression in parentheses.
    /// </summary>
    public const int WithoutParentheses = 2;

    private bool addedToFilter;

    /// <summary>
    /// Get the SQL snippet for a list of expressions.
    /// </summary>
    /// <param name="builder">the builder to append the SQL to</param>
    /// <param name="expressions">the list of expressions</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the specified string builder</returns>
    public static StringBuilder WriteExpressions(
        StringBuilder builder,
        IReadOnlyList<Expression> expressions,
        int sqlFlags)
    {
        for (int i = 0; i < expressions.Count; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            _ = expressions[i].GetUnenclosedSql(builder, sqlFlags);
        }

        return builder;
    }

    /// <summary>
    /// Get the SQL snippet for an array of expressions, where a null element is a DEFAULT.
    /// </summary>
    /// <param name="builder">the builder to append the SQL to</param>
    /// <param name="expressions">the list of expressions</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the specified string builder</returns>
    public static StringBuilder WriteExpressions(StringBuilder builder, Expression?[] expressions, int sqlFlags)
    {
        for (int i = 0; i < expressions.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            Expression? e = expressions[i];
            if (e is null)
            {
                _ = builder.Append("DEFAULT");
            }
            else
            {
                _ = e.GetUnenclosedSql(builder, sqlFlags);
            }
        }

        return builder;
    }

    /// <summary>
    /// Return the resulting value for the current row.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the result</returns>
    public abstract Value GetValue(SessionLocal session);

    /// <summary>
    /// Returns the data type. The data type may be unknown before the
    /// optimization phase.
    /// </summary>
    /// <remarks>
    /// Named GetTypeInfo, not GetType: the reference's getType() returns a H2 TypeInfo and there
    /// is already an object.GetType() to collide with, and a class that hides the method every
    /// .NET caller expects is a trap rather than a convenience.
    /// </remarks>
    /// <returns>the data type</returns>
    public abstract TypeInfo GetTypeInfo();

    /// <summary>
    /// Map the columns of the resolver to expression columns.
    /// </summary>
    /// <remarks>
    /// Abstract in the reference, where every expression forwards this to its operands. Here it
    /// is virtual with a no-op default, because a column expression is the only thing with
    /// something to map and the forwarding is what the derived classes would otherwise repeat.
    /// </remarks>
    /// <param name="resolver">the column resolver</param>
    /// <param name="level">the subquery nesting level</param>
    /// <param name="state">current state for nesting checks, initial value is <see cref="MapInitial"/></param>
    public virtual void MapColumns(ColumnResolver resolver, int level, int state)
    {
    }

    /// <summary>
    /// Try to optimize the expression.
    /// </summary>
    /// <remarks>
    /// The reference folds constants and reorders conjunctions here. That optimiser is not
    /// ported, and every expression written so far is already in the shape it would produce, so
    /// the answer is "itself" until something folds.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <returns>the optimized expression</returns>
    public virtual Expression Optimize(SessionLocal session) => this;

    /// <summary>
    /// Try to optimize or remove the condition.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the optimized condition, or null</returns>
    public Expression? OptimizeCondition(SessionLocal session)
    {
        Expression e = Optimize(session);
        if (e.IsConstant())
        {
            return e.GetBooleanValue(session) ? null : ValueExpression.False;
        }

        return e;
    }

    /// <summary>
    /// Tell the expression columns whether the table filter can return values
    /// now. This is used when optimizing the query.
    /// </summary>
    /// <remarks>
    /// A no-op while the optimiser is not ported: the flag exists for the optimiser, and there
    /// is nothing yet that reads it.
    /// </remarks>
    /// <param name="tableFilter">the table filter</param>
    /// <param name="value">true if the table filter can return value</param>
    public virtual void SetEvaluatable(TableFilter tableFilter, bool value)
    {
    }

    /// <summary>
    /// Get the SQL statement of this expression with automatic parentheses.
    /// </summary>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the SQL statement</returns>
    public string GetSql(int sqlFlags)
    {
        return GetSql(new StringBuilder(), sqlFlags, AutoParentheses).ToString();
    }

    /// <summary>
    /// Get the SQL statement of this expression with automatic parentheses.
    /// </summary>
    /// <param name="builder">the builder to append to</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the specified string builder</returns>
    public StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        return GetSql(builder, sqlFlags, AutoParentheses);
    }

    /// <summary>
    /// Get the SQL statement of this expression. This may not always be the
    /// original SQL statement, especially after optimization.
    /// </summary>
    /// <param name="sqlFlags">formatting flags</param>
    /// <param name="parentheses">parentheses mode</param>
    /// <returns>the SQL statement</returns>
    public string GetSql(int sqlFlags, int parentheses)
    {
        return GetSql(new StringBuilder(), sqlFlags, parentheses).ToString();
    }

    /// <summary>
    /// Get the SQL statement of this expression. This may not always be the
    /// original SQL statement, especially after optimization.
    /// </summary>
    /// <param name="builder">string builder</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <param name="parentheses">parentheses mode</param>
    /// <returns>the specified string builder</returns>
    public StringBuilder GetSql(StringBuilder builder, int sqlFlags, int parentheses)
    {
        return parentheses == WithParentheses
            || parentheses != WithoutParentheses && NeedParentheses()
            ? GetUnenclosedSql(builder.Append('('), sqlFlags).Append(')')
            : GetUnenclosedSql(builder, sqlFlags);
    }

    /// <summary>
    /// Returns whether this expression needs to be wrapped in parentheses when
    /// it is used as an argument of other expressions.
    /// </summary>
    /// <returns>true if it is</returns>
    public virtual bool NeedParentheses()
    {
        return false;
    }

    /// <summary>
    /// Get the SQL statement of this expression, always enclosed in parentheses.
    /// </summary>
    /// <param name="builder">string builder</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the specified string builder</returns>
    public StringBuilder GetEnclosedSql(StringBuilder builder, int sqlFlags)
    {
        return GetUnenclosedSql(builder.Append('('), sqlFlags).Append(')');
    }

    /// <summary>
    /// Get the SQL statement of this expression, never enclosed in parentheses.
    /// </summary>
    /// <param name="builder">string builder</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the specified string builder</returns>
    public abstract StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags);

    /// <summary>
    /// Update an aggregate value. This method is called at statement execution
    /// time. It is usually called once for each row, but if the expression is
    /// used multiple times (for example in the column list, and as part of the
    /// HAVING expression) it is called multiple times - the row counter needs to
    /// be used to make sure the internal state is only updated once.
    /// </summary>
    /// <remarks>
    /// Only an aggregate answers this; every other expression has nothing to accumulate, and
    /// saying so by doing nothing is what the reference's non-aggregates do.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="stage">select stage</param>
    public virtual void UpdateAggregate(SessionLocal session, int stage)
    {
    }

    /// <summary>
    /// Check if this expression and all sub-expressions can fulfill a criteria.
    /// If any part returns false, the result is false.
    /// </summary>
    /// <param name="visitor">the visitor</param>
    /// <returns>if the criteria can be fulfilled</returns>
    public abstract bool IsEverything(ExpressionVisitor visitor);

    /// <summary>
    /// Estimate the cost to process the expression.
    /// Used when optimizing the query, to calculate the query plan
    /// with the lowest estimated cost.
    /// </summary>
    /// <returns>the estimated cost</returns>
    public abstract int GetCost();

    /// <summary>
    /// If it is possible, return the negated expression. This is used
    /// to optimize NOT expressions: NOT ID&gt;10 can be converted to
    /// ID&lt;=10. Returns null if negating is not possible.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the negated expression, or null</returns>
    public virtual Expression? GetNotIfPossible(SessionLocal session)
    {
        // by default it is not possible
        return null;
    }

    /// <summary>
    /// Returns data type of this expression if it is statically known.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>data type or null</returns>
    public virtual TypeInfo? GetTypeIfStaticallyKnown(SessionLocal session)
    {
        return null;
    }

    /// <summary>
    /// Check if this expression will always return the same value.
    /// </summary>
    /// <returns>if the expression is constant</returns>
    public virtual bool IsConstant()
    {
        return false;
    }

    /// <summary>
    /// Check if this expression will always return the NULL value.
    /// </summary>
    /// <returns>if the expression is constant NULL value</returns>
    public virtual bool IsNullConstant()
    {
        return false;
    }

    /// <summary>
    /// Is the value of a parameter set.
    /// </summary>
    /// <returns>true if set</returns>
    public virtual bool IsValueSet()
    {
        return false;
    }

    /// <summary>
    /// Check if this is an identity column.
    /// </summary>
    /// <returns>true if it is an identity column</returns>
    public virtual bool IsIdentity()
    {
        return false;
    }

    /// <summary>
    /// Get the value in form of a boolean expression.
    /// Returns true or false.
    /// In this database, everything can be a condition.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the result</returns>
    public virtual bool GetBooleanValue(SessionLocal session)
    {
        return GetValue(session).IsTrue();
    }

    /// <summary>
    /// Create index conditions if possible and attach them to the table filter.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="filter">the table filter</param>
    public virtual void CreateIndexConditions(SessionLocal session, TableFilter filter)
    {
        // default is do nothing
    }

    /// <summary>
    /// Get the column name or alias name of this expression.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="columnIndex">0-based column index</param>
    /// <returns>the column name</returns>
    public virtual string GetColumnName(SessionLocal session, int columnIndex)
    {
        return GetAlias(session, columnIndex);
    }

    /// <summary>
    /// Get the schema name, or null.
    /// </summary>
    /// <returns>the schema name</returns>
    public virtual string? GetSchemaName()
    {
        return null;
    }

    /// <summary>
    /// Get the table name, or null.
    /// </summary>
    /// <returns>the table name</returns>
    public virtual string? GetTableName()
    {
        return null;
    }

    /// <summary>
    /// Check whether this expression is a column and can store NULL.
    /// </summary>
    /// <returns>whether NULL is allowed</returns>
    public virtual int GetNullable()
    {
        return Column.NullableUnknown;
    }

    /// <summary>
    /// Get the table alias name or null
    /// if this expression does not represent a column.
    /// </summary>
    /// <returns>the table alias name</returns>
    public virtual string? GetTableAlias()
    {
        return null;
    }

    /// <summary>
    /// Get the alias name of a column or SQL expression
    /// if it is not an aliased expression.
    /// </summary>
    /// <remarks>
    /// The mode decides the shape of the name and this method only enforces the one rule that
    /// does not depend on it: an identifier has a length limit, so a long expression falls back
    /// to "C1" rather than becoming an identifier no database should accept.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="columnIndex">0-based column index</param>
    /// <returns>the alias name</returns>
    public virtual string GetAlias(SessionLocal session, int columnIndex)
    {
        string sql = GetSql(HasSql.QuoteOnlyWhenRequired | HasSql.NoCasts, WithoutParentheses);
        if (sql.Length <= Constants.MaxIdentifierLength)
        {
            return sql;
        }

        return "C" + (columnIndex + 1);
    }

    /// <summary>
    /// Returns the main expression, skipping aliases.
    /// </summary>
    /// <returns>the expression</returns>
    public virtual Expression GetNonAliasExpression()
    {
        return this;
    }

    /// <summary>
    /// Add conditions to a table filter if they can be evaluated.
    /// </summary>
    /// <param name="filter">the table filter</param>
    public virtual void AddFilterConditions(TableFilter filter)
    {
        if (!addedToFilter && IsEverything(ExpressionVisitor.EvaluableVisitor))
        {
            filter.AddFilterCondition(this, false);
            addedToFilter = true;
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return GetTraceSql();
    }

    /// <summary>
    /// The SQL of this expression as it is written into a trace, where it is never quoted and
    /// never cast: a trace shows what was asked for, not what would be sent to a server.
    /// </summary>
    /// <returns>the trace SQL</returns>
    public string GetTraceSql()
    {
        return GetSql(0, WithParentheses);
    }

    /// <summary>
    /// Returns count of subexpressions.
    /// </summary>
    /// <returns>count of subexpressions</returns>
    public virtual int GetSubexpressionCount()
    {
        return 0;
    }

    /// <summary>
    /// Returns subexpression with specified index.
    /// </summary>
    /// <param name="index">0-based index</param>
    /// <returns>subexpression with specified index, may be null</returns>
    public virtual Expression? GetSubexpression(int index)
    {
        throw new IndexOutOfRangeException();
    }

    /// <summary>
    /// Return the resulting value of this when operand for the current row.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="left">value on the left side</param>
    /// <returns>the result</returns>
    public virtual bool GetWhenValue(SessionLocal session, Value left)
    {
        return session.CompareWithNull(left, GetValue(session), true) == 0;
    }

    /// <summary>
    /// Appends the SQL statement of this when operand to the specified builder.
    /// </summary>
    /// <param name="builder">string builder</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the specified string builder</returns>
    public virtual StringBuilder GetWhenSql(StringBuilder builder, int sqlFlags)
    {
        return GetUnenclosedSql(builder.Append(' '), sqlFlags);
    }

    /// <summary>
    /// Returns whether this expression is a right side of condition in a when
    /// operand.
    /// </summary>
    /// <returns>true if it is, false otherwise</returns>
    public virtual bool IsWhenConditionOperand()
    {
        return false;
    }
}
