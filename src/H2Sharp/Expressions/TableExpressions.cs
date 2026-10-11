/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.ConditionAndOr, Comparison, ConditionNot,
 * ConditionIsNull, Parameter and ExpressionColumn.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The expressions a table engine needs, evaluated against the row the session is reading.
 *
 * Where the row comes from is the design decision, and it is the reference's: a column
 * expression asks its column resolver for the value, and the session carries the resolver that
 * is current. That is why no expression here holds a row — the same expression object is used by
 * the scan, by the filter and by the projection, and a captured row would be right in exactly one
 * of those places.
 *
 * The comparison types are the reference's numbers, because they are printed in generated SQL and
 * stored in plans.
 */
using System;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>AND or OR over two expressions.</summary>
public sealed class LogicalExpression : Expression
{
    /// <summary>An AND.</summary>
    public const int And = 0;

    /// <summary>An OR.</summary>
    public const int Or = 1;

    private readonly Expression left;

    private readonly int andOr;

    private readonly Expression right;

    /// <summary>
    /// Creates a conjunction.
    /// </summary>
    /// <param name="left">the left operand</param>
    /// <param name="andOr">And or Or</param>
    /// <param name="right">the right operand</param>
    public LogicalExpression(Expression left, int andOr, Expression right)
    {
        this.left = left;
        this.andOr = andOr;
        this.right = right;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Both stop as soon as an operand decides the answer, and a NULL seen on the way is
    /// remembered rather than returned: FALSE OR NULL is FALSE, because the FALSE decided it.
    /// </remarks>
    public override Value GetValue(SessionLocal session)
    {
        bool hasNull = false;
        foreach (Expression operand in new[] { left, right })
        {
            Value v = operand.GetValue(session);
            if (v is ValueNull)
            {
                hasNull = true;
                continue;
            }

            bool value = v.GetBoolean();
            if (andOr == And)
            {
                if (!value)
                {
                    return ValueBoolean.False;
                }
            }
            else if (value)
            {
                return ValueBoolean.True;
            }
        }

        return hasNull ? ValueNull.Instance : ValueBoolean.Get(andOr == Or);
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        return TypeInfo.TypeBoolean;
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        return left.IsEverything(visitor) && right.IsEverything(visitor);
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        return left.GetCost() + 1 + right.GetCost();
    }

    /// <inheritdoc/>
    public override bool NeedParentheses()
    {
        return true;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        _ = left.GetSql(builder, sqlFlags, AutoParentheses)
            .Append(andOr == And ? " AND " : " OR ");
        return right.GetSql(builder, sqlFlags, AutoParentheses);
    }
}

/// <summary>A NOT over one expression.</summary>
public sealed class NotExpression : Expression
{
    private readonly Expression operand;

    /// <summary>
    /// Creates a negation.
    /// </summary>
    /// <param name="operand">the operand</param>
    public NotExpression(Expression operand)
    {
        this.operand = operand;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value v = operand.GetValue(session);
        if (v is ValueNull)
        {
            return v;
        }

        return ValueBoolean.Get(!v.GetBoolean());
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        return TypeInfo.TypeBoolean;
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        return operand.IsEverything(visitor);
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        return operand.GetCost() + 1;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        return operand.GetSql(builder.Append("NOT "), sqlFlags, WithoutParentheses);
    }
}

/// <summary>A comparison of two expressions.</summary>
public sealed class ComparisonExpression : Expression
{
    /// <summary>The comparison type meaning = as in ID=1.</summary>
    public const int Equal = 0;

    /// <summary>The comparison type meaning &lt;&gt; as in ID&lt;&gt;1.</summary>
    public const int NotEqual = 1;

    /// <summary>The comparison type meaning &lt; as in ID&lt;1.</summary>
    public const int Smaller = 2;

    /// <summary>The comparison type meaning &gt; as in ID&gt;1.</summary>
    public const int Bigger = 3;

    /// <summary>The comparison type meaning &lt;= as in ID&lt;=1.</summary>
    public const int SmallerEqual = 4;

    /// <summary>The comparison type meaning &gt;= as in ID&gt;=1.</summary>
    public const int BiggerEqual = 5;

    private static readonly string[] Operators = ["=", "<>", "<", ">", "<=", ">="];

    private readonly Expression left;

    private readonly int compareType;

    private readonly Expression right;

    /// <summary>
    /// Creates a comparison.
    /// </summary>
    /// <param name="left">the left operand</param>
    /// <param name="compareType">one of the constants above</param>
    /// <param name="right">the right operand</param>
    public ComparisonExpression(Expression left, int compareType, Expression right)
    {
        this.left = left;
        this.compareType = compareType;
        this.right = right;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A comparison with NULL is unknown, and the short-circuit is what makes it cheap: once one
    /// side is NULL the other need not be read at all.
    /// </remarks>
    public override Value GetValue(SessionLocal session)
    {
        Value l = left.GetValue(session);
        if (l is ValueNull)
        {
            return ValueNull.Instance;
        }

        Value r = right.GetValue(session);
        if (r is ValueNull)
        {
            return ValueNull.Instance;
        }

        int cmp = l.CompareTo(r, session.CastDataProvider, session.CompareMode);
        bool result = compareType switch
        {
            Equal => cmp == 0,
            NotEqual => cmp != 0,
            Smaller => cmp < 0,
            Bigger => cmp > 0,
            SmallerEqual => cmp <= 0,
            _ => cmp >= 0,
        };
        return ValueBoolean.Get(result);
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        return TypeInfo.TypeBoolean;
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        return left.IsEverything(visitor) && right.IsEverything(visitor);
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        return left.GetCost() + 1 + right.GetCost();
    }

    /// <inheritdoc/>
    public override bool NeedParentheses()
    {
        return true;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        _ = left.GetSql(builder, sqlFlags, AutoParentheses).Append(' ').Append(Operators[compareType]).Append(' ');
        return right.GetSql(builder, sqlFlags, AutoParentheses);
    }
}

/// <summary>IS [NOT] NULL.</summary>
public sealed class IsNullExpression : Expression
{
    private readonly Expression operand;

    private readonly bool not;

    /// <summary>
    /// Creates an IS NULL test.
    /// </summary>
    /// <param name="operand">the operand</param>
    /// <param name="not">whether this is IS NOT NULL</param>
    public IsNullExpression(Expression operand, bool not)
    {
        this.operand = operand;
        this.not = not;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// This is the one predicate that never answers NULL: "x IS NULL" is true or false whatever
    /// x is, which is what separates it from "x = NULL".
    /// </remarks>
    public override Value GetValue(SessionLocal session)
    {
        return ValueBoolean.Get((operand.GetValue(session) is ValueNull) != not);
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        return TypeInfo.TypeBoolean;
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        return operand.IsEverything(visitor);
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        return operand.GetCost() + 1;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        _ = operand.GetSql(builder, sqlFlags, AutoParentheses).Append(not ? " IS NOT NULL" : " IS NULL");
        return builder;
    }
}

/// <summary>
/// A parameter of the statement, by name.
/// </summary>
/// <remarks>
/// The value is not held here: a statement is prepared once and run many times, and a value
/// captured at parse time would be the value of the run that prepared it. It is asked of the
/// session instead, which is the run.
/// </remarks>
public sealed class ParameterExpression : Expression
{
    private readonly string name;

    /// <summary>
    /// Creates a reference to a named parameter.
    /// </summary>
    /// <param name="name">the parameter name, folded like an identifier</param>
    public ParameterExpression(string name)
    {
        this.name = name;
    }

    /// <summary>Gets the parameter name.</summary>
    public string GetName()
    {
        return name;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        return session.GetParameterValue(name);
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        return TypeInfo.TypeUnknown;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A parameter is evaluable only once it has a value, and "has a value" is what the
    /// INDEPENDENT visitor asks. A parameter that is set is independent; one that is not is not,
    /// and an expression that folds it away would fold away the fact that a value is missing.
    /// </remarks>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        return visitor.GetVisitorType() != ExpressionVisitor.Independent || sessionIsSet();
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        return 1;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        return builder.Append('@').Append(name);
    }

    /// <summary>Whether a value was set; filled in by the command when it runs.</summary>
    internal Func<bool>? IsSet { get; set; }

    private bool sessionIsSet()
    {
        return IsSet is null || IsSet();
    }
}

/// <summary>A reference to a column of the row being read.</summary>
public sealed class ColumnExpression : Expression
{
    private readonly string name;

    private Column? column;

    /// <summary>
    /// Creates a reference to a column by name.
    /// </summary>
    /// <param name="name">the column name, folded like an identifier</param>
    public ColumnExpression(string name)
    {
        this.name = name;
    }

    /// <summary>Gets the column name.</summary>
    public string GetName()
    {
        return name;
    }

    /// <summary>
    /// Binds this reference to the column of the given table.
    /// </summary>
    /// <param name="table">the table</param>
    public void MapColumn(Table table)
    {
        column = FindColumn(table.GetColumns());
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Mapping is by name, as it is in the reference's first pass, and by column in its second.
    /// With one table in the FROM there is nothing left for the second pass to disambiguate.
    /// </remarks>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        column = FindColumn(resolver.GetColumns());
    }

    private Column FindColumn(Column[] columns)
    {
        foreach (Column c in columns)
        {
            if (c.GetName().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return c;
            }
        }

        throw DbException.Get(ErrorCode.ColumnNotFound1, name);
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        return column is null ? ValueNull.Instance : session.GetRowValue(column);
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        return column?.GetTypeInfo() ?? TypeInfo.TypeUnknown;
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        return true;
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        return 1;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        return ParserUtil.QuoteIdentifier(builder, name, sqlFlags);
    }
}
