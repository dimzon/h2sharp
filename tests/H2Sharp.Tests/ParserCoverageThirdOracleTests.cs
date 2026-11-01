/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Parser coverage (third wave): typed literals, variables, natural/using joins, override
 * identity inserts, materialized views, alter view, comment on, foreign key actions,
 * limit edge forms — run through the ADO.NET layer and compared with the reference jar.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ParserCoverageThirdOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void TypedLiteralsAndVariables()
    {
        AssertSameAsReference("parser_third1",
            "SELECT CAST(TIMESTAMP WITH TIME ZONE '2020-01-02 03:04:05+02:00' AS VARCHAR)",
            "SELECT CAST(TIME WITH TIME ZONE '03:04:05+02:00' AS VARCHAR)",
            "SET @X = 5",
            "SELECT @X",
            "SET @Y = 'abc'",
            "SELECT @X + 1, @Y",
            "SELECT CAST(DATE '2020-01-02' + 1 AS VARCHAR)",
            "SELECT CAST(TIMESTAMP '2020-01-02 00:00:00' + INTERVAL '1' HOUR AS VARCHAR)");
    }

    [JavaOracleFact]
    public void NaturalAndUsingJoins()
    {
        AssertSameAsReference("parser_third2",
            "CREATE TABLE A(ID INT, X INT)",
            "CREATE TABLE B(ID INT, Y INT)",
            "INSERT INTO A VALUES(1,10),(2,20)",
            "INSERT INTO B VALUES(1,100),(2,200)",
            "SELECT ID, X, Y FROM A NATURAL JOIN B ORDER BY ID",
            "SELECT ID, X, Y FROM A JOIN B USING(ID) ORDER BY ID",
            "SELECT A.X + B.Y FROM A JOIN B ON A.ID = B.ID ORDER BY A.X",
            "DROP TABLE A, B");
    }

    [JavaOracleFact]
    public void OverrideIdentityAndDefaults()
    {
        AssertSameAsReference("parser_third3",
            "CREATE TABLE H(ID BIGINT GENERATED ALWAYS AS IDENTITY, V VARCHAR(10))",
            "INSERT INTO H(V) VALUES('a')",
            "INSERT INTO H(ID, V) OVERRIDING SYSTEM VALUE VALUES(5, 'b')",
            "SELECT ID, V FROM H ORDER BY ID",
            "CREATE TABLE D(X INT DEFAULT 7, Y INT)",
            "INSERT INTO D DEFAULT VALUES",
            "INSERT INTO D(Y) VALUES(1)",
            "SELECT X, Y FROM D ORDER BY Y",
            "DROP TABLE H, D");
    }

    [JavaOracleFact]
    public void MaterializedViewsAndAlterView()
    {
        AssertSameAsReference("parser_third4",
            "CREATE TABLE S(ID INT, VAL INT)",
            "INSERT INTO S VALUES(1,10),(2,20)",
            "CREATE MATERIALIZED VIEW MV AS SELECT SUM(VAL) AS TOTAL FROM S",
            "SELECT TOTAL FROM MV",
            "REFRESH MATERIALIZED VIEW MV",
            "DROP MATERIALIZED VIEW MV",
            "CREATE VIEW V1 AS SELECT ID FROM S",
            "ALTER VIEW V1 AS SELECT ID, VAL FROM S",
            "SELECT ID, VAL FROM V1 ORDER BY ID",
            "DROP VIEW V1",
            "DROP TABLE S");
    }

    [JavaOracleFact]
    public void CommentAndForeignKeyActions()
    {
        AssertSameAsReference("parser_third5",
            "CREATE TABLE P(ID INT PRIMARY KEY)",
            "CREATE TABLE C(ID INT PRIMARY KEY, PID INT REFERENCES P(ID) ON DELETE CASCADE ON UPDATE RESTRICT)",
            "INSERT INTO P VALUES(1)",
            "INSERT INTO C VALUES(1,1)",
            "DELETE FROM P WHERE ID = 1",
            "SELECT COUNT(*) FROM C",
            "DROP TABLE C, P",
            "CREATE TABLE T(ID INT, V VARCHAR(10))",
            "COMMENT ON TABLE T IS 'my table'",
            "COMMENT ON COLUMN T.ID IS 'the id'",
            "SELECT REMARKS FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='T'",
            "SELECT REMARKS FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='T' AND COLUMN_NAME='ID'",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void ForUpdateAndLimitEdges()
    {
        AssertSameAsReference("parser_third6",
            "CREATE TABLE T(ID INT)",
            "INSERT INTO T VALUES(1),(2)",
            "SELECT ID FROM T ORDER BY ID FOR UPDATE",
            "SELECT ID FROM T ORDER BY ID FOR UPDATE NOWAIT",
            "SELECT ID FROM T ORDER BY ID LIMIT 0",
            "SELECT ID FROM T ORDER BY ID LIMIT 1 OFFSET 5",
            "DROP TABLE T");
    }
}
