/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Parser coverage (advanced expressions): operators, postfixes, filtered/ordered
 * aggregates, window frames, advanced INSERT/MERGE — run through the ADO.NET layer and
 * compared with the reference jar.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ParserCoverageExpressionsAdvancedOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void OperatorsAndPostfixes()
    {
        AssertSameAsReference("parser_expr1",
            "SELECT 'a' || 'b'",
            "SELECT CASE WHEN 'abc' LIKE 'a%' THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 'abc' LIKE 'a_%' ESCAPE '\\' THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 'abc' REGEXP '^a' THEN 1 ELSE 0 END",
            "SELECT CASE WHEN NULL IS NULL THEN 1 ELSE 0 END",
            "SELECT CASE WHEN TRUE IS TRUE THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 2 BETWEEN 1 AND 3 THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 2 BETWEEN SYMMETRIC 3 AND 1 THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 2 IN (1,2,3) THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 5 NOT IN (1,2,3) THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 3 IN (SELECT 1 UNION SELECT 3) THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 2 < ALL (SELECT 3) THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 2 <= ANY (SELECT 2,3) THEN 1 ELSE 0 END",
            "SELECT CAST(1.5 AS INT) + CAST(2.5 AS INT)",
            "SELECT 5 % 2",
            "SELECT 2 * 3 + 4",
            "SELECT - (2 + 3)",
            "SELECT CASE WHEN 'abc' = 'abc' COLLATE ENGLISH THEN 1 ELSE 0 END",
            "SELECT X FROM (SELECT 1 AS X) T WHERE X = 1");
    }

    [JavaOracleFact]
    public void AdvancedAggregates()
    {
        AssertSameAsReference("parser_expr2",
            "CREATE TABLE P(ID INT, GRP VARCHAR(10), VAL INT)",
            "INSERT INTO P VALUES(1,'a',10),(2,'a',20),(3,'b',30),(4,'b',40)",
            "SELECT SUM(VAL) FILTER (WHERE VAL > 15) FROM P",
            "SELECT GRP, COUNT(*) FILTER (WHERE VAL > 15) FROM P GROUP BY GRP ORDER BY GRP",
            "SELECT GRP, STRING_AGG(CAST(VAL AS VARCHAR), ',' ORDER BY VAL) FROM P GROUP BY GRP ORDER BY GRP",
            "SELECT LISTAGG(CAST(VAL AS VARCHAR), ',') WITHIN GROUP (ORDER BY VAL) FROM P",
            "SELECT CARDINALITY(ARRAY_AGG(VAL ORDER BY VAL)) FROM P",
            "SELECT ID, SUM(VAL) OVER (PARTITION BY GRP ORDER BY ID ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)"
            + " FROM P ORDER BY ID",
            "SELECT ID, SUM(VAL) OVER (ORDER BY ID ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING) FROM P ORDER BY ID",
            "SELECT ID, COUNT(*) OVER (PARTITION BY GRP) FROM P ORDER BY ID",
            "SELECT ID, RANK() OVER (ORDER BY VAL DESC), DENSE_RANK() OVER (ORDER BY VAL DESC) FROM P ORDER BY ID",
            "SELECT ID, NTILE(2) OVER (ORDER BY VAL) FROM P ORDER BY ID",
            "SELECT ID, LAG(VAL) OVER (ORDER BY ID), LEAD(VAL) OVER (ORDER BY ID) FROM P ORDER BY ID",
            "SELECT ID, FIRST_VALUE(VAL) OVER (PARTITION BY GRP ORDER BY ID) FROM P ORDER BY ID");
    }

    [JavaOracleFact]
    public void AdvancedInsertMerge()
    {
        AssertSameAsReference("parser_expr3",
            "CREATE TABLE A(ID INT PRIMARY KEY, NAME VARCHAR(20), VAL INT DEFAULT 0)",
            "INSERT INTO A(ID, NAME) VALUES(1, 'a')",
            "SELECT ID, VAL FROM A",
            "INSERT INTO A VALUES(2, 'b', DEFAULT)",
            "INSERT INTO A(ID, NAME, VAL) VALUES(3, 'c', 30), (4, 'd', DEFAULT)",
            "SELECT ID, VAL FROM A ORDER BY ID",
            "INSERT INTO A SELECT ID + 10, NAME, VAL FROM A WHERE ID < 3",
            "SELECT COUNT(*) FROM A",
            "MERGE INTO A USING (SELECT 50 AS ID, 'x' AS NAME) S ON A.ID = S.ID"
            + " WHEN MATCHED THEN UPDATE SET NAME = S.NAME"
            + " WHEN NOT MATCHED THEN INSERT(ID, NAME) VALUES(S.ID, S.NAME)",
            "SELECT COUNT(*) FROM A",
            "DELETE FROM A WHERE ID > 50",
            "SELECT COUNT(*) FROM A");
    }

    [JavaOracleFact]
    public void MySqlOnDuplicateKey()
    {
        AssertSameAsReferenceInMode("parser_expr4", "MySQL",
            "CREATE TABLE A(ID INT PRIMARY KEY, NAME VARCHAR(20))",
            "INSERT INTO A(ID, NAME) VALUES(60, 'm') ON DUPLICATE KEY UPDATE NAME = 'M'",
            "INSERT INTO A(ID, NAME) VALUES(60, 'm2') ON DUPLICATE KEY UPDATE NAME = 'M2'",
            "SELECT NAME FROM A WHERE ID = 60");
    }
}
