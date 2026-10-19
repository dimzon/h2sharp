/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.NamedExpression.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Java name: NamedExpression. Renamed with the I prefix, as the port does for every
 * interface (see MIGRATION_STATUS.md).
 */
namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A function-like expression with a name.
/// </summary>
public interface INamedExpression
{
    /// <summary>
    /// Gets the name.
    /// </summary>
    /// <returns>the name in uppercase</returns>
    string GetName();
}
