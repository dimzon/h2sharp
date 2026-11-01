/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Parser coverage (eighth wave): LOB values, IN/ANY subqueries and operator trees,
 * IS JSON predicates — run through the ADO.NET layer and compared with the reference jar.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ParserCoverageEighthOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void LobValues()
    {
        AssertSameAsReference("parser_eighth1",
            "CREATE TABLE L(ID INT, B BLOB, C CLOB)",
            "INSERT INTO L VALUES(1, X'00FF', 'small')",
            "SELECT LENGTH(C), OCTET_LENGTH(B) FROM L",
            "SELECT SUBSTRING(C, 1, 3) FROM L",
            "UPDATE L SET C = C || '-more' WHERE ID = 1",
            "SELECT LENGTH(C) FROM L",
            "CREATE TABLE BIG(ID INT, C CLOB)",
            "INSERT INTO BIG VALUES(1, SPACE(2000))",
            "SELECT LENGTH(C) FROM BIG",
            "SELECT SUBSTRING(C, 1000, 5) FROM BIG",
            "DROP TABLE BIG, L");
    }

    [JavaOracleFact]
    public void InAndAnySubqueries()
    {
        AssertSameAsReference("parser_eighth2",
            "CREATE TABLE P(ID INT, VAL INT)",
            "CREATE TABLE Q(ID INT, PID INT)",
            "INSERT INTO P VALUES(1,10),(2,20),(3,30)",
            "INSERT INTO Q VALUES(1,1),(2,1),(3,3)",
            "SELECT ID FROM P WHERE ID IN (SELECT PID FROM Q) ORDER BY ID",
            "SELECT ID FROM P WHERE ID NOT IN (SELECT PID FROM Q) ORDER BY ID",
            "SELECT ID FROM P WHERE VAL > ALL (SELECT VAL FROM P WHERE ID = 1) ORDER BY ID",
            "SELECT ID FROM P WHERE VAL >= ANY (SELECT VAL FROM P) ORDER BY ID",
            "SELECT ID FROM P WHERE ID = ANY (SELECT PID FROM Q) ORDER BY ID",
            "SELECT ID FROM P WHERE EXISTS (SELECT 1 FROM Q WHERE Q.PID = P.ID) ORDER BY ID",
            "DROP TABLE Q, P");
    }

    [JavaOracleFact]
    public void OperatorTrees()
    {
        AssertSameAsReference("parser_eighth3",
            "SELECT 1 + 2 * 3 - 4 / 2",
            "SELECT (1 + 2) * (3 - 4) + 5",
            "SELECT (2 + 3) * (4 - 1) / (1 + 1)",
            "SELECT CASE WHEN NOT (1 = 0) AND (2 > 1) THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 1 + 2 = 3 THEN 1 ELSE 0 END",
            "SELECT 'a' || 'b' || 'c'",
            "SELECT 3 * 3 * 3",
            "SELECT ((1)) + ((2))");
    }

    [JavaOracleFact]
    public void IsJson()
    {
        AssertSameAsReference("parser_eighth4",
            "CREATE TABLE J(ID INT, D JSON)",
            "INSERT INTO J VALUES(1, '{\"a\":1}')",
            "INSERT INTO J VALUES(2, 'not json')",
            "SELECT ID FROM J WHERE D IS JSON ORDER BY ID",
            "SELECT ID FROM J WHERE D IS NOT JSON ORDER BY ID",
            "SELECT CASE WHEN '{}' IS JSON THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 'x' IS NOT JSON THEN 1 ELSE 0 END",
            "SELECT ID FROM J WHERE D IS JSON WITH UNIQUE KEYS ORDER BY ID",
            "DROP TABLE J");
    }
}
