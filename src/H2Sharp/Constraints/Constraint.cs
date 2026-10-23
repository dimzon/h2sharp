/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.constraint.Constraint.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The base class for constraint checking.
 *
 * A constraint is an object of its own in the schema, not a flag on the index it is backed by,
 * and that is what makes its name a database name: a second table cannot take CONSTRAINT_5
 * while the first one has it, and DROP TABLE releases it for the next table to use. The kind is
 * answered by the subclass rather than stored, because the kind is the subclass — an enum field
 * beside the type would be a second answer that can disagree with the first.
 *
 * Only the members a unique constraint needs are carried over concretely. CHECK, FOREIGN KEY
 * and domain constraints refuse in their own parts of the layer; the members they would use are
 * declared here because a caller that walks every constraint — the duplicate-key message, the
 * table's cleanup — must be able to ask all of them the same questions.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using Index = H2Sharp.Indexes.Index;

namespace H2Sharp.Constraints;

/// <summary>
/// The base class for constraint checking.
/// </summary>
public abstract class Constraint : SchemaObject, IComparable<Constraint>
{
    /// <summary>
    /// The kind of a constraint.
    /// </summary>
    /// <remarks>
    /// The member spelling is the reference's, as in <see cref="ConstraintActionType"/>: the
    /// names are printed and compared, and an enum that renames its members renames the SQL.
    /// </remarks>
    public enum Type
    {
        /// <summary>The constraint type for check constraints.</summary>
        CHECK,

        /// <summary>The constraint type for primary key constraints.</summary>
        PRIMARY_KEY,

        /// <summary>The constraint type for unique constraints.</summary>
        UNIQUE,

        /// <summary>The constraint type for referential constraints.</summary>
        REFERENTIAL,

        /// <summary>The constraint type for domain constraints.</summary>
        DOMAIN,
    }

    /// <summary>
    /// The table for which this constraint is defined.
    /// </summary>
    protected Table table;

    /// <summary>
    /// Creates the constraint.
    /// </summary>
    /// <param name="schema">the schema</param>
    /// <param name="id">the object id</param>
    /// <param name="name">the name</param>
    /// <param name="table">the table the constraint belongs to</param>
    protected Constraint(Schema schema, int id, string name, Table table)
        : base(schema, id, name, Trace.Constraint)
    {
        this.table = table;
        if (table is not null)
        {
            SetTemporary(table.IsTemporary());
        }
    }

    /// <summary>
    /// The constraint type name.
    /// </summary>
    /// <returns>the name</returns>
    public abstract Type GetConstraintType();

    /// <summary>
    /// Check if this row fulfils the constraint. This method throws an exception if not.
    /// </summary>
    /// <remarks>
    /// The rows are null where there is no row: the old one on an insert, the new one on a
    /// delete. Java expresses that with a nullable reference and C# with a nullable one; the
    /// signature is the reference's, with the question marks the language requires.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="t">the table</param>
    /// <param name="oldRow">the old row</param>
    /// <param name="newRow">the new row</param>
    public abstract void CheckRow(SessionLocal session, Table t, Row? oldRow, Row? newRow);

    /// <summary>
    /// Check if this constraint needs the specified index.
    /// </summary>
    /// <param name="index">the index</param>
    /// <returns>true if the index is used</returns>
    public abstract bool UsesIndex(Index index);

    /// <summary>
    /// This index is now the owner of the specified index.
    /// </summary>
    /// <param name="index">the index</param>
    public abstract void SetIndexOwner(Index index);

    /// <summary>
    /// Get all referenced columns.
    /// </summary>
    /// <param name="table">the table</param>
    /// <returns>the set of referenced columns</returns>
    public abstract HashSet<Column> GetReferencedColumns(Table table);

    /// <summary>
    /// Returns the CHECK expression or null.
    /// </summary>
    /// <returns>the CHECK expression or null</returns>
    public virtual Expression? GetExpression()
    {
        return null;
    }

    /// <summary>
    /// Get the SQL statement to create this constraint.
    /// </summary>
    /// <returns>the SQL statement</returns>
    public abstract string GetCreateSqlWithoutIndexes();

    /// <summary>
    /// Check if this constraint needs to be checked before updating the data.
    /// </summary>
    /// <returns>true if it must be checked before updating</returns>
    public abstract bool IsBefore();

    /// <summary>
    /// Check the existing data. This method is called if the constraint is added after data has
    /// been inserted into the table.
    /// </summary>
    /// <param name="session">the session</param>
    public abstract void CheckExistingData(SessionLocal session);

    /// <summary>
    /// This method is called after a related table has changed (the table was renamed, or
    /// columns have been renamed).
    /// </summary>
    public abstract void Rebuild();

    /// <summary>
    /// Get the index of this constraint in the source table, or null if no index is used.
    /// </summary>
    /// <returns>the index</returns>
    public virtual Index? GetIndex()
    {
        return null;
    }

    /// <summary>
    /// Returns the referenced unique constraint, or null.
    /// </summary>
    /// <returns>the referenced unique constraint, or null</returns>
    public virtual ConstraintUnique? GetReferencedConstraint()
    {
        return null;
    }

    /// <inheritdoc/>
    public override int GetTypeId()
    {
        return DbObject.Constraint;
    }

    /// <summary>
    /// Gets the table this constraint belongs to.
    /// </summary>
    /// <returns>the table</returns>
    public Table GetTable()
    {
        return table;
    }

    /// <summary>
    /// Gets the table this constraint refers to, which is the table it belongs to unless it is
    /// a referential constraint.
    /// </summary>
    /// <returns>the table</returns>
    public virtual Table GetRefTable()
    {
        return table;
    }

    /// <inheritdoc/>
    public int CompareTo(Constraint? other)
    {
        if (ReferenceEquals(this, other))
        {
            return 0;
        }

        if (other is null)
        {
            return 1;
        }

        return ((int)GetConstraintType()).CompareTo((int)other.GetConstraintType());
    }

    /// <summary>
    /// Visit all elements in the constraint.
    /// </summary>
    /// <param name="visitor">the visitor</param>
    /// <returns>true if every visited expression returned true, or if there are no expressions</returns>
    public virtual bool IsEverything(ExpressionVisitor visitor)
    {
        _ = visitor;
        return true;
    }
}

/// <summary>
/// The rules of <see cref="Constraint.Type"/>, which an enum cannot carry.
/// </summary>
/// <remarks>
/// The same arrangement as <see cref="ConstraintActionTypeRules"/>, and for the same reason:
/// C# does not allow methods on an enum, so the members that need the enum's value live here.
/// </remarks>
public static class ConstraintTypeRules
{
    /// <summary>
    /// Get standard SQL type name.
    /// </summary>
    /// <param name="type">the constraint type</param>
    /// <returns>standard SQL type name</returns>
    public static string GetSqlName(Constraint.Type type)
    {
        if (type == Constraint.Type.PRIMARY_KEY)
        {
            return "PRIMARY KEY";
        }

        if (type == Constraint.Type.REFERENTIAL)
        {
            return "FOREIGN KEY";
        }

        return type.ToString();
    }

    /// <summary>
    /// Tests whether the given type is a check or domain type or not.
    /// </summary>
    /// <param name="type">the constraint type</param>
    /// <returns>true if this type is a check or a domain type, false otherwise</returns>
    public static bool IsCheck(Constraint.Type type)
    {
        return type is Constraint.Type.CHECK or Constraint.Type.DOMAIN;
    }

    /// <summary>
    /// Tests whether the given type is a primary key or unique type or not.
    /// </summary>
    /// <param name="type">the constraint type</param>
    /// <returns>true if this type is a primary key or unique type, false otherwise</returns>
    public static bool IsUnique(Constraint.Type type)
    {
        return type is Constraint.Type.PRIMARY_KEY or Constraint.Type.UNIQUE;
    }
}
