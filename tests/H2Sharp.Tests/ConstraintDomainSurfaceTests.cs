/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 256: a domain constraint's own API — rendering with and without a comment, checking
 * values against the parsed VALUE expression, the two CHECK SQL spellings and the refusals of
 * row-only constraint operations.
 */
using System;
using H2Sharp.Api;
using H2Sharp.Command;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Schemas;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ConstraintDomainSurfaceTests
{
    [Fact]
    public void DomainConstraintChecksValuesAndRendersItsSql()
    {
        Database database = new(ConnectionInfo.Memory("domain_constraint_surface"));
        SessionLocal session = new(database, "sa");
        try
        {
            Schema schema = database.GetSchema("PUBLIC");
            Domain domain = new(schema, database.AllocateObjectId(), "D");
            domain.SetDataType(TypeInfo.TypeInteger);
            ConstraintDomain constraint = new(schema, database.AllocateObjectId(), "C", domain);
            Expression expression = new Parser(session).ParseDomainConstraintExpression("VALUE > 0");
            constraint.SetExpression(session, expression);

            Assert.Equal(Constraint.Type.DOMAIN, constraint.GetConstraintType());
            Assert.Same(domain, constraint.GetDomain());
            Assert.Same(expression, constraint.GetExpression());
            Assert.True(constraint.IsBefore());
            Assert.False(constraint.UsesIndex(null!));

            Assert.Contains("CHECK", constraint.GetCreateSql(), StringComparison.Ordinal);
            Assert.Equal(constraint.GetCreateSql(), constraint.GetCreateSqlWithoutIndexes());
            constraint.SetComment("a check");
            Assert.Contains("COMMENT 'a check'", constraint.GetCreateSql(), StringComparison.Ordinal);

            constraint.Check(session, ValueInteger.Get(1));
            Assert.ThrowsAny<H2SharpException>(() => constraint.Check(session, ValueInteger.Get(0)));

            Assert.NotNull(constraint.GetCheckConstraint(session, "COLUMN"));
            Assert.NotNull(constraint.GetCheckConstraint(session, null));

            Assert.ThrowsAny<Exception>(() => constraint.CheckRow(session, null!, null, null));
            Assert.ThrowsAny<Exception>(() => constraint.SetIndexOwner(null!));
            constraint.Rebuild();
            constraint.CheckExistingData(session);
        }
        finally
        {
            database.Close();
        }
    }
}
