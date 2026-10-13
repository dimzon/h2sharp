/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.ExpressionList.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A list of expressions, for IN lists and row constructors.
 */
using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>
/// A list of expressions, for IN lists and row constructors.
/// </summary>
/// <remarks>
/// Not ported beyond its shape: nothing builds one yet — there is no IN list or row
/// constructor in the parsed language — so every member refuses with what it needs. The
/// signatures are the reference's, so bodies port without reshaping, and the type checks in
/// condition translation compile.
/// </remarks>
public sealed class ExpressionList : Expression
{
    /// <summary>
    /// Creates a list. Refuses: evaluating lists needs the value layer's row support.
    /// </summary>
    /// <param name="list">the expressions</param>
    /// <param name="isArray">whether this is an array rather than a list</param>
    public ExpressionList(Expression[] list, bool isArray)
    {
        throw new TODOException("ExpressionList: expression lists are not ported");
    }

    /// <inheritdoc/>
    public override int GetSubexpressionCount()
    {
        throw new TODOException("ExpressionList: expression lists are not ported");
    }

    /// <inheritdoc/>
    public override Expression GetSubexpression(int index)
    {
        throw new TODOException("ExpressionList: expression lists are not ported");
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        throw new TODOException("ExpressionList: expression lists are not ported");
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        throw new TODOException("ExpressionList: expression lists are not ported");
    }

    /// <inheritdoc/>
    public override System.Text.StringBuilder GetUnenclosedSql(System.Text.StringBuilder builder, int sqlFlags)
    {
        throw new TODOException("ExpressionList: expression lists are not ported");
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        throw new TODOException("ExpressionList: expression lists are not ported");
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        throw new TODOException("ExpressionList: expression lists are not ported");
    }
}
