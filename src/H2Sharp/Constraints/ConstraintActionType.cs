/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.constraint.ConstraintActionType.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What a foreign key does when a row it points at is changed or removed.
 *
 * Two of the five have a SQL name that is not their own: NO_ACTION is written "NO ACTION" and
 * SET_NULL is written "SET NULL". That is the whole reason this is an enum with a name beside it
 * rather than four names and a default — the statement has to print the action back the way SQL
 * spells it, and an enum member called NO_ACTION printed as NO_ACTION would be a syntax error
 * in the middle of a DDL statement.
 */
namespace H2Sharp.Constraints;

/// <summary>
/// The action of a constraint.
/// </summary>
public enum ConstraintActionType
{
    /// <summary>No action (default).</summary>
    NO_ACTION,

    /// <summary>The action is to restrict the operation.</summary>
    RESTRICT,

    /// <summary>The action is to cascade the operation.</summary>
    CASCADE,

    /// <summary>The action is to set the value to the default value.</summary>
    SET_DEFAULT,

    /// <summary>The action is to set the value to NULL.</summary>
    SET_NULL,
}

/// <summary>
/// The rules of <see cref="ConstraintActionType"/>, which an enum cannot carry.
/// </summary>
/// <remarks>
/// C# does not allow methods on an enum, so the two members that need the enum's value live
/// here. That is the same arrangement as <c>DefaultNullOrderingRules</c>, and for the same
/// reason: the members are per value and an enum cannot carry them.
/// </remarks>
public static class ConstraintActionTypeRules
{
    /// <summary>
    /// Tests if the action is a <see cref="ConstraintActionType.NO_ACTION"/> or
    /// <see cref="ConstraintActionType.RESTRICT"/>.
    /// </summary>
    /// <param name="action">the action</param>
    /// <returns>true if it is</returns>
    public static bool IsNoActionOrRestrict(ConstraintActionType action)
        => action is ConstraintActionType.NO_ACTION or ConstraintActionType.RESTRICT;

    /// <summary>
    /// Gets the standard SQL name of an action.
    /// </summary>
    /// <param name="action">the action</param>
    /// <returns>the SQL name</returns>
    public static string GetSqlName(ConstraintActionType action) => action switch
    {
        ConstraintActionType.NO_ACTION => "NO ACTION",
        ConstraintActionType.SET_DEFAULT => "SET DEFAULT",
        ConstraintActionType.SET_NULL => "SET NULL",
        _ => action.ToString(),
    };
}
