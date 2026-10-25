/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.schema.Domain.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Schemas;

/// <summary>Represents a domain.</summary>
public sealed class Domain : SchemaObject
{
    private TypeInfo? type;

    private Domain? domain;

    private Expression? defaultExpression;

    private Expression? onUpdateExpression;

    private List<ConstraintDomain>? constraints;

    /// <summary>Creates a domain.</summary>
    public Domain(Schema schema, int id, string name) : base(schema, id, name, Trace.Schema)
    {
    }

    /// <inheritdoc/>
    public override string? GetDropSql()
        => GetSql(new StringBuilder("DROP DOMAIN IF EXISTS "), HasSql.DefaultSqlFlags).ToString();

    /// <inheritdoc/>
    public override string? GetCreateSql()
    {
        StringBuilder builder = GetSql(new StringBuilder("CREATE DOMAIN "), HasSql.DefaultSqlFlags).Append(" AS ");
        if (domain is not null)
        {
            _ = domain.GetSql(builder, HasSql.DefaultSqlFlags);
        }
        else
        {
            _ = type!.GetSql(builder, HasSql.DefaultSqlFlags);
        }

        if (defaultExpression is not null)
        {
            _ = defaultExpression.GetUnenclosedSql(builder.Append(" DEFAULT "), HasSql.DefaultSqlFlags);
        }

        if (onUpdateExpression is not null)
        {
            _ = onUpdateExpression.GetUnenclosedSql(builder.Append(" ON UPDATE "), HasSql.DefaultSqlFlags);
        }

        return builder.ToString();
    }

    /// <inheritdoc/>
    public override int GetTypeId() => DbObject.Domain;

    /// <summary>Sets the data type.</summary>
    public void SetDataType(TypeInfo type) => this.type = type;

    /// <summary>Gets the data type.</summary>
    public TypeInfo GetDataType() => type!;

    /// <summary>Sets the parent domain.</summary>
    public void SetDomain(Domain? domain) => this.domain = domain;

    /// <summary>Gets the parent domain.</summary>
    public Domain? GetDomain() => domain;

    /// <summary>Sets the default expression.</summary>
    public void SetDefaultExpression(SessionLocal session, Expression? expression)
    {
        if (expression is not null)
        {
            expression = expression.Optimize(session);
            if (expression.IsConstant())
            {
                expression = ValueExpression.Get(expression.GetValue(session));
            }
        }

        defaultExpression = expression;
    }

    /// <summary>Gets the default expression.</summary>
    public Expression? GetDefaultExpression() => defaultExpression;

    /// <summary>Gets the effective default expression.</summary>
    public Expression? GetEffectiveDefaultExpression()
        => defaultExpression ?? domain?.GetEffectiveDefaultExpression();

    /// <summary>Gets the default SQL, or null.</summary>
    public string? GetDefaultSql()
        => defaultExpression?.GetUnenclosedSql(new StringBuilder(), HasSql.DefaultSqlFlags).ToString();

    /// <summary>Sets the on-update expression.</summary>
    public void SetOnUpdateExpression(SessionLocal session, Expression? expression)
    {
        if (expression is not null)
        {
            expression = expression.Optimize(session);
            if (expression.IsConstant())
            {
                expression = ValueExpression.Get(expression.GetValue(session));
            }
        }

        onUpdateExpression = expression;
    }

    /// <summary>Gets the on-update expression.</summary>
    public Expression? GetOnUpdateExpression() => onUpdateExpression;

    /// <summary>Gets the effective on-update expression.</summary>
    public Expression? GetEffectiveOnUpdateExpression()
        => onUpdateExpression ?? domain?.GetEffectiveOnUpdateExpression();

    /// <summary>Gets the on-update SQL, or null.</summary>
    public string? GetOnUpdateSql()
        => onUpdateExpression?.GetUnenclosedSql(new StringBuilder(), HasSql.DefaultSqlFlags).ToString();

    /// <summary>Prepares the expressions of this domain.</summary>
    public void PrepareExpressions(SessionLocal session)
    {
        if (defaultExpression is not null)
        {
            defaultExpression = defaultExpression.Optimize(session);
        }

        if (onUpdateExpression is not null)
        {
            onUpdateExpression = onUpdateExpression.Optimize(session);
        }

        domain?.PrepareExpressions(session);
    }

    /// <summary>Adds a constraint to this domain.</summary>
    public void AddConstraint(ConstraintDomain constraint)
    {
        constraints ??= [];
        if (!constraints.Contains(constraint))
        {
            constraints.Add(constraint);
        }
    }

    /// <summary>Gets the constraints of this domain.</summary>
    public List<ConstraintDomain>? GetConstraints() => constraints;

    /// <summary>Removes the given constraint from the list.</summary>
    public void RemoveConstraint(Constraint constraint) => constraints?.Remove((ConstraintDomain)constraint);

    /// <summary>Checks the specified value against the domain constraints.</summary>
    public void CheckConstraints(SessionLocal session, Value value)
    {
        if (constraints is not null)
        {
            foreach (ConstraintDomain constraint in constraints)
            {
                constraint.Check(session, value);
            }
        }

        domain?.CheckConstraints(session, value);
    }

    /// <inheritdoc/>
    public override void RemoveChildrenAndResources(SessionLocal session)
    {
        if (constraints is not null && constraints.Count > 0)
        {
            foreach (ConstraintDomain constraint in constraints.ToArray())
            {
                GetSchema().GetDatabase().RemoveSchemaObject(session, constraint);
            }

            constraints = null;
        }

        GetSchema().GetDatabase().RemoveMeta(session, GetId());
    }
}
