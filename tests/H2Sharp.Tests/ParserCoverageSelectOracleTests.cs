/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Parser coverage: SELECT shapes — run through the ADO.NET layer and compared against the
 * reference jar.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ParserCoverageSelectOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void Joins()
    {
        AssertSameAsReference("parser_sel1",
            "CREATE TABLE P(ID INT PRIMARY KEY, GRP VARCHAR(10), VAL INT)",
            "CREATE TABLE Q(ID INT, PID INT, TAG VARCHAR(10))",
            "INSERT INTO P VALUES(1,'a',10),(2,'a',20),(3,'b',30),(4,'b',40)",
            "INSERT INTO Q VALUES(101,1,'t1'),(102,1,'t2'),(103,3,'t3')",
            "SELECT P.ID, Q.TAG FROM P JOIN Q ON P.ID = Q.PID ORDER BY P.ID",
            "SELECT P.ID, Q.TAG FROM P LEFT JOIN Q ON P.ID = Q.PID ORDER BY P.ID",
            "SELECT ID, TAG FROM P RIGHT JOIN Q ON P.ID = Q.PID ORDER BY ID, TAG",
            "SELECT P.ID, Q.TAG FROM P FULL OUTER JOIN Q ON P.ID = Q.PID ORDER BY P.ID, Q.TAG",
            "SELECT P.ID FROM P CROSS JOIN Q ORDER BY P.ID LIMIT 5",
            "SELECT P.ID, Q.TAG FROM P JOIN Q ON P.ID = Q.PID WHERE P.GRP = 'a' ORDER BY Q.TAG",
            "SELECT A.ID, B.ID FROM P A JOIN P B ON A.ID < B.ID ORDER BY A.ID, B.ID LIMIT 3");
    }

    [JavaOracleFact]
    public void GroupingAndOrdering()
    {
        AssertSameAsReference("parser_sel2",
            "CREATE TABLE P(ID INT PRIMARY KEY, GRP VARCHAR(10), VAL INT)",
            "INSERT INTO P VALUES(1,'a',10),(2,'a',20),(3,'b',30),(4,'b',40)",
            "SELECT GRP, COUNT(*), SUM(VAL) FROM P GROUP BY GRP ORDER BY GRP",
            "SELECT GRP, SUM(VAL) FROM P GROUP BY GRP HAVING SUM(VAL) > 40 ORDER BY GRP",
            "SELECT ID, VAL FROM P ORDER BY VAL DESC LIMIT 2",
            "SELECT ID FROM P ORDER BY ID OFFSET 1 ROWS FETCH NEXT 2 ROWS ONLY",
            "SELECT DISTINCT GRP FROM P ORDER BY GRP",
            "SELECT COUNT(DISTINCT GRP) FROM P",
            "SELECT ID, VAL FROM P ORDER BY VAL, ID");
    }

    [JavaOracleFact]
    public void SubqueriesAndSetOps()
    {
        AssertSameAsReference("parser_sel3",
            "CREATE TABLE P(ID INT PRIMARY KEY, GRP VARCHAR(10), VAL INT)",
            "CREATE TABLE Q(ID INT, PID INT, TAG VARCHAR(10))",
            "INSERT INTO P VALUES(1,'a',10),(2,'a',20),(3,'b',30),(4,'b',40)",
            "INSERT INTO Q VALUES(101,1,'t1'),(102,1,'t2'),(103,3,'t3')",
            "SELECT ID FROM P WHERE VAL IN (10,30) ORDER BY ID",
            "SELECT ID FROM P WHERE ID > ANY (SELECT PID FROM Q) ORDER BY ID",
            "SELECT ID FROM P WHERE EXISTS (SELECT 1 FROM Q WHERE Q.PID = P.ID) ORDER BY ID",
            "SELECT (SELECT MAX(VAL) FROM P) - MIN(VAL) FROM P",
            "SELECT ID FROM P WHERE GRP = 'a' UNION SELECT ID FROM P WHERE GRP = 'b' ORDER BY ID",
            "SELECT ID FROM P WHERE GRP = 'a' INTERSECT SELECT ID FROM P WHERE GRP = 'a' ORDER BY ID",
            "SELECT ID FROM P WHERE VAL > 15 EXCEPT SELECT ID FROM P WHERE VAL > 35 ORDER BY ID",
            "WITH C AS (SELECT ID FROM P WHERE VAL > 15) SELECT COUNT(*) FROM C",
            "WITH C AS (SELECT ID, VAL FROM P) SELECT SUM(VAL) FROM C");
    }

    [JavaOracleFact]
    public void WindowAndExpressions()
    {
        AssertSameAsReference("parser_sel4",
            "CREATE TABLE P(ID INT PRIMARY KEY, GRP VARCHAR(10), VAL INT)",
            "INSERT INTO P VALUES(1,'a',10),(2,'a',20),(3,'b',30),(4,'b',40)",
            "SELECT ID, ROW_NUMBER() OVER (ORDER BY VAL) AS RN FROM P ORDER BY ID",
            "SELECT GRP, ID, SUM(VAL) OVER (PARTITION BY GRP) AS TOTAL FROM P ORDER BY ID",
            "SELECT ID, CASE WHEN VAL > 25 THEN 'big' ELSE 'small' END FROM P ORDER BY ID",
            "SELECT ID, COALESCE(NULL, 'x') FROM P ORDER BY ID LIMIT 1",
            "SELECT ID, VAL * 2 AS DOUBLE_VAL FROM P ORDER BY ID LIMIT 2",
            "SELECT ID FROM P WHERE ID BETWEEN 2 AND 3 ORDER BY ID",
            "SELECT ID FROM P WHERE GRP LIKE 'a%' ORDER BY ID",
            "SELECT ID FROM P WHERE NAME IS NULL ORDER BY ID",
            "SELECT CONCAT(CAST(ID AS VARCHAR), '-', GRP) FROM P ORDER BY ID LIMIT 2");
    }
}
