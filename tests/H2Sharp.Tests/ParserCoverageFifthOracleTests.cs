/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Parser coverage (fifth wave): the statement forms the DTO path used to carry — UPDATE
 * and DELETE with aliases, row-value arrays, order/fetch limits, indexes, total index
 * rename, data change delta tables — run through the ADO.NET layer and compared with the
 * reference jar.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ParserCoverageFifthOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void UpdateForms()
    {
        AssertSameAsReference("parser_fifth1",
            "CREATE TABLE T(ID INT, NAME VARCHAR(20), VAL INT, NUMBERS INT ARRAY)",
            "INSERT INTO T VALUES(1,'a',10, ARRAY[1,2,3])",
            "INSERT INTO T VALUES(2,'b',20, ARRAY[4])",
            "UPDATE T SET VAL = 99 WHERE ID = 1",
            "SELECT ID, VAL FROM T ORDER BY ID",
            "UPDATE T t SET NAME = 'y' WHERE t.ID = 2",
            "SELECT NAME FROM T WHERE ID = 2",
            "UPDATE T AS t SET VAL = 7 WHERE t.ID = 1",
            "SELECT VAL FROM T WHERE ID = 1",
            "UPDATE T SET (NAME, VAL) = ('x', 5) WHERE ID = 1",
            "SELECT NAME, VAL FROM T WHERE ID = 1",
            "UPDATE T SET VAL = DEFAULT WHERE ID = 1",
            "SELECT VAL FROM T WHERE ID = 1",
            "UPDATE T SET NUMBERS[1] = 9 WHERE ID = 1",
            "SELECT NUMBERS[1] FROM T WHERE ID = 1",
            "UPDATE T SET NAME = UPPER(NAME) WHERE ID = 2",
            "SELECT NAME FROM T WHERE ID = 2",
            "UPDATE T SET VAL = VAL + 1 WHERE ID > 0 ORDER BY ID",
            "SELECT VAL FROM T ORDER BY ID",
            "UPDATE T SET VAL = 1 ORDER BY ID FETCH FIRST 1 ROW ONLY",
            "SELECT VAL, NAME FROM T ORDER BY ID",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void DeleteForms()
    {
        AssertSameAsReference("parser_fifth2",
            "CREATE TABLE T(ID INT, VAL INT)",
            "INSERT INTO T VALUES(1,10),(2,20),(3,30)",
            "DELETE FROM T WHERE ID = 1",
            "SELECT COUNT(*) FROM T",
            "DELETE FROM T t WHERE t.ID = 2",
            "SELECT COUNT(*) FROM T",
            "DELETE FROM T AS t WHERE t.ID = 3",
            "SELECT COUNT(*) FROM T",
            "DELETE FROM T ORDER BY ID FETCH FIRST 1 ROW ONLY",
            "SELECT COUNT(*) FROM T",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void IndexForms()
    {
        AssertSameAsReference("parser_fifth3",
            "CREATE TABLE T(ID INT, A INT, B INT)",
            "CREATE INDEX IX1 ON T(A)",
            "CREATE INDEX IX2 ON T(A DESC, B ASC)",
            "CREATE HASH INDEX IX3 ON T(A)",
            "SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME='T' ORDER BY INDEX_NAME",
            "DROP INDEX IX1",
            "DROP INDEX IF EXISTS IX1",
            "ALTER INDEX IX2 RENAME TO IX2B",
            "SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME='T' ORDER BY INDEX_NAME",
            "DROP INDEX IX2B, IX3",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void DataChangeDelta()
    {
        AssertSameAsReference("parser_fifth4",
            "CREATE TABLE T(ID INT, VAL INT)",
            "SELECT ID FROM FINAL TABLE (INSERT INTO T VALUES(1, 10))",
            "SELECT ID, VAL FROM NEW TABLE (INSERT INTO T VALUES(2, 20))",
            "SELECT ID FROM OLD TABLE (DELETE FROM T WHERE ID = 1)",
            "SELECT ID, VAL FROM FINAL TABLE (UPDATE T SET VAL = 25 WHERE ID = 2)",
            "SELECT * FROM T ORDER BY ID",
            "DROP TABLE T");
    }
}
