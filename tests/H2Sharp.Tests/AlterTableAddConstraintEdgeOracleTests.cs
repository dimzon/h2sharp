/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * ALTER TABLE ADD CONSTRAINT edges from the coverage data (uncovered.py): a second primary
 * key, a foreign key over a generated column with ON DELETE/ON UPDATE actions that would
 * write it, a foreign key over an incomparable column, the bare UNIQUE that takes every
 * visible column, the reuse of an existing unique index, and the foreign key that owns the
 * index it found. Checked against the reference running the same text.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class AlterTableAddConstraintEdgeOracleTests
{
    [JavaOracleFact]
    public void ConstraintEdgesAnswerTheReference()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE P(ID INT PRIMARY KEY, S VARCHAR(10))",
            // A second primary key on a table that has one.
            "CREATE TABLE T(A INT PRIMARY KEY, B INT)",
            "ALTER TABLE T ADD CONSTRAINT PK2 PRIMARY KEY (B)",
            // A generated column cannot be written by a constraint action.
            "CREATE TABLE G1(A INT, G INT GENERATED ALWAYS AS (A + 1))",
            "ALTER TABLE G1 ADD CONSTRAINT FK_G1 FOREIGN KEY (G) REFERENCES P(ID) ON DELETE SET NULL",
            "CREATE TABLE G2(A INT, G INT GENERATED ALWAYS AS (A + 1))",
            "ALTER TABLE G2 ADD CONSTRAINT FK_G2 FOREIGN KEY (G) REFERENCES P(ID) ON UPDATE CASCADE",
            // The child column is not comparable with the referenced one.
            "CREATE TABLE C5(S VARCHAR(10))",
            "ALTER TABLE C5 ADD CONSTRAINT FK5 FOREIGN KEY (S) REFERENCES P(ID)",
            // The bare UNIQUE takes every visible column.
            "CREATE TABLE T2(A INT, B INT)",
            "ALTER TABLE T2 ADD CONSTRAINT U UNIQUE",
            // An existing unique index is reused, not rebuilt.
            "CREATE TABLE T3(A INT)",
            "CREATE UNIQUE INDEX UX ON T3(A)",
            "ALTER TABLE T3 ADD CONSTRAINT U3 UNIQUE(A)",
            // The foreign key owns the index it found.
            "CREATE TABLE C4(ID INT)",
            "CREATE INDEX IDX ON C4(ID)",
            "ALTER TABLE C4 ADD CONSTRAINT FK4 FOREIGN KEY (ID) REFERENCES P(ID) ON DELETE CASCADE",
            "DROP TABLE T",
            "DROP TABLE G1",
            "DROP TABLE G2",
            "DROP TABLE C5",
            "DROP TABLE T2",
            "DROP TABLE T3",
            "DROP TABLE C4",
            "DROP TABLE P",
        ]);
    }
}
