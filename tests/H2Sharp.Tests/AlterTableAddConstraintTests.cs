/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The ALTER TABLE ... ADD CONSTRAINT paths the ordinary CREATE TABLE tests miss: the
 * UNIQUE(VALUE) column list, the single-column NULLS NOT DISTINCT collapse, the reuse of an
 * existing unique index, and the foreign-key refusals the reference checks before it builds
 * the constraint.
 */
using System;
using H2Sharp.Command;
using H2Sharp.Engine;
using Xunit;

namespace H2Sharp.Tests;

public sealed class AlterTableAddConstraintTests
{
    private readonly SessionLocal session;
    private readonly Database database;

    public AlterTableAddConstraintTests()
    {
        database = new Database(
            ConnectionInfo.Memory("add_constraint_" + Guid.NewGuid().ToString("N")));
        session = new SessionLocal(database, "sa");
    }

    private void Execute(string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        _ = container.ExecuteUpdate(false);
    }

    [Fact]
    public void UniqueValueCoversEveryVisibleColumn()
    {
        Execute("CREATE TABLE V(A INT, B INT)");
        Execute("INSERT INTO V VALUES (1, 2)");
        Execute("ALTER TABLE V ADD CONSTRAINT UV UNIQUE(VALUE)");

        H2SharpException duplicate = Assert.Throws<H2SharpException>(
            () => Execute("INSERT INTO V VALUES (1, 2)"));
        Assert.Equal(Api.ErrorCode.DuplicateKey1, duplicate.H2ErrorCode);
    }

    /// <summary>
    /// The reference collapses a single-column ALL_DISTINCT to DISTINCT (both in the command
    /// and in the index type), so NULLS ALL DISTINCT on one column answers like the default.
    /// </summary>
    [Fact]
    public void SingleColumnNullsAllDistinctCollapsesToDistinct()
    {
        Execute("CREATE TABLE N(A INT)");
        Execute("ALTER TABLE N ADD CONSTRAINT UN UNIQUE NULLS ALL DISTINCT (A)");
        Execute("INSERT INTO N VALUES (NULL)");
        Execute("INSERT INTO N VALUES (NULL)");
    }

    [Fact]
    public void UniqueConstraintReusesAnExistingUniqueIndex()
    {
        Execute("CREATE TABLE R(A INT)");
        Execute("CREATE UNIQUE INDEX UX ON R(A)");
        Execute("ALTER TABLE R ADD CONSTRAINT UR UNIQUE(A)");

        H2SharpException duplicate = Assert.Throws<H2SharpException>(
            () => Execute("INSERT INTO R VALUES (1), (1)"));
        Assert.Equal(Api.ErrorCode.DuplicateKey1, duplicate.H2ErrorCode);
    }

    /// <summary>
    /// A generated-always FK column refuses the updating actions and allows the rest; the
    /// default RESTRICT walks the refuser's switch to its default branch.
    /// </summary>
    [Fact]
    public void ForeignKeyOnGeneratedColumnAllowsRestrict()
    {
        Execute("CREATE TABLE P(ID INT PRIMARY KEY)");
        Execute("CREATE TABLE G(ID INT GENERATED ALWAYS AS IDENTITY)");
        Execute("ALTER TABLE G ADD CONSTRAINT GFK FOREIGN KEY(ID) REFERENCES P(ID)");

        H2SharpException setNull = Assert.Throws<H2SharpException>(
            () => Execute(
                "ALTER TABLE G ADD CONSTRAINT GFK2 FOREIGN KEY(ID) REFERENCES P(ID) ON DELETE SET NULL"));
        Assert.Equal(Api.ErrorCode.GeneratedColumnCannotBeUpdatableByConstraint2, setNull.H2ErrorCode);

        H2SharpException setCascade = Assert.Throws<H2SharpException>(
            () => Execute(
                "ALTER TABLE G ADD CONSTRAINT GFK3 FOREIGN KEY(ID) REFERENCES P(ID) ON UPDATE CASCADE"));
        Assert.Equal(Api.ErrorCode.GeneratedColumnCannotBeUpdatableByConstraint2, setCascade.H2ErrorCode);
    }

    /// <summary>
    /// DATE and TIME are not stably comparable (TIME conversions depend on the current time),
    /// so a foreign key from a DATE column to a TIME key is refused. The column list of the
    /// referenced side defaults to its primary key.
    /// </summary>
    [Fact]
    public void ForeignKeyToAnIncomparableColumnIsRefused()
    {
        Execute("CREATE TABLE PT(T TIME PRIMARY KEY)");
        Execute("CREATE TABLE C(D DATE)");
        H2SharpException e = Assert.Throws<H2SharpException>(
            () => Execute("ALTER TABLE C ADD CONSTRAINT FK FOREIGN KEY(D) REFERENCES PT"));
        Assert.Equal(Api.ErrorCode.UncomparableReferencedColumn2, e.H2ErrorCode);
    }

    [Fact]
    public void ForeignKeyToAMissingTableIsRefused()
    {
        Execute("CREATE TABLE C(A INT)");
        H2SharpException e = Assert.Throws<H2SharpException>(
            () => Execute("ALTER TABLE C ADD CONSTRAINT FK FOREIGN KEY(A) REFERENCES NOPE(ID)"));
        Assert.Equal(Api.ErrorCode.TableOrViewNotFound1, e.H2ErrorCode);
    }

    [Fact]
    public void ForeignKeyWithMismatchedColumnCountIsRefused()
    {
        Execute("CREATE TABLE P(ID INT PRIMARY KEY)");
        Execute("CREATE TABLE C(A INT, B INT)");
        H2SharpException e = Assert.Throws<H2SharpException>(
            () => Execute("ALTER TABLE C ADD CONSTRAINT FK FOREIGN KEY(A, B) REFERENCES P(ID)"));
        Assert.Equal(Api.ErrorCode.ColumnCountDoesNotMatch, e.H2ErrorCode);
    }

    [Fact]
    public void ForeignKeyToAViewIsRefused()
    {
        Execute("CREATE TABLE P(ID INT PRIMARY KEY)");
        Execute("CREATE VIEW PV AS SELECT ID FROM P");
        Execute("CREATE TABLE C(A INT)");
        H2SharpException e = Assert.Throws<H2SharpException>(
            () => Execute("ALTER TABLE C ADD CONSTRAINT FK FOREIGN KEY(A) REFERENCES PV(ID)"));
        Assert.Contains("Reference", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ForeignKeyAcrossTemporalityIsRefused()
    {
        Execute("CREATE TEMPORARY TABLE TR(A INT PRIMARY KEY)");
        Execute("CREATE TABLE C(A INT)");
        H2SharpException e = Assert.Throws<H2SharpException>(
            () => Execute("ALTER TABLE C ADD CONSTRAINT FK FOREIGN KEY(A) REFERENCES TR(A)"));
        Assert.Contains("temporary table", e.Message, StringComparison.Ordinal);
    }
}
