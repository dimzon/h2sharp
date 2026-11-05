/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Inline REFERENCES without a column list (issue #63): the column-level foreign key
 * without an explicit column list takes the referenced table's primary key. The named
 * and explicit forms, the working key, and the orphan refusal are locked against the
 * reference too.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class InlineForeignKeyOracleTests
{
    [JavaOracleFact]
    public void InlineReferencesTakesThePrimaryKey()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE T1(ID1 BIGINT PRIMARY KEY, S INT NOT NULL)",
            "INSERT INTO T1 VALUES (1, 1)",
            // The implicit form: no column list after the table name.
            "CREATE TABLE T2(ID1 BIGINT REFERENCES T1, ID2 BIGINT)",
            "INSERT INTO T2 VALUES (1, 10)",
            // The parent key is not the first column.
            "CREATE TABLE T7(A INT, B BIGINT PRIMARY KEY)",
            "CREATE TABLE T8(X BIGINT REFERENCES T7)",
            "INSERT INTO T8 VALUES (2)",
            // An orphan is refused by the key the constraint took.
            "INSERT INTO T2 VALUES (2, 20)",
            // The explicit single-column form still works.
            "CREATE TABLE T4(Y BIGINT, Z BIGINT, FOREIGN KEY(Y) REFERENCES T1(ID1))",
            "INSERT INTO T4 VALUES (1, 5)",
            // A real count mismatch is still refused.
            "CREATE TABLE T9(A BIGINT, FOREIGN KEY(A) REFERENCES T1(ID1, S))",
            "DROP TABLE T9",
            "DROP TABLE T4",
            "DROP TABLE T8",
            "DROP TABLE T7",
            "DROP TABLE T2",
            "DROP TABLE T1",
        ]);
    }
}
