/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The documented DDL/DML forms issue #61 listed: IDENTITY as a column type, local and
 * global temporary tables, CREATE TABLE ... AS SELECT / AS (SELECT) / AS VALUES, INSERT
 * ... SELECT, and DROP TABLE with a comma-separated list. All compare with H2 2.5 JDBC.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class DdlFormsOracleTests
{
    [JavaOracleFact]
    public void TheEightDdlFormsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE TI(ID IDENTITY, V INT)",
            "INSERT INTO TI(V) VALUES (1)",
            "SELECT ID, V FROM TI",
            "CREATE LOCAL TEMPORARY TABLE LT(A INT)",
            "INSERT INTO LT VALUES (7)",
            "SELECT * FROM LT",
            "CREATE GLOBAL TEMPORARY TABLE GT(ID INT)",
            "INSERT INTO GT VALUES (8)",
            "SELECT * FROM GT",
            "CREATE TABLE C1 AS SELECT 1 AS A",
            "SELECT * FROM C1",
            "CREATE TABLE C2 AS (SELECT 2 AS B)",
            "SELECT * FROM C2",
            "CREATE TABLE C3(ID INT PRIMARY KEY) AS VALUES (1)",
            "SELECT * FROM C3",
            "INSERT INTO TI(V) SELECT 5",
            "SELECT ID, V FROM TI ORDER BY ID",
            "CREATE TABLE DA(ID INT)",
            "CREATE TABLE DB2(ID INT)",
            "INSERT INTO DA VALUES (1), (2)",
            "INSERT INTO DB2 VALUES (3), (4)",
            "DROP TABLE DA, DB2",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('DA', 'DB2')",
        ]);
    }

    [JavaOracleFact]
    public void CreateTypeAsEnumMatchesJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TYPE Mood AS ENUM ('Happy', 'Sad')",
            "CREATE TABLE T(ID INT PRIMARY KEY, M Mood)",
            "INSERT INTO T VALUES (1, 'Happy'), (2, 'Sad')",
            "SELECT M FROM T ORDER BY ID",
            // Enum labels are case-sensitive and a label outside the list is refused.
            "INSERT INTO T VALUES (3, 'happy')",
            "SELECT ID, M FROM T ORDER BY ID",
        ]);
    }
}
