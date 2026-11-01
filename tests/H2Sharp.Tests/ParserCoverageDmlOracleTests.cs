/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Parser coverage: DML statements — run through the ADO.NET layer and compared against
 * the reference jar.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ParserCoverageDmlOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void InsertUpdateDelete()
    {
        AssertSameAsReference("parser_dml1",
            "CREATE TABLE A(ID INT PRIMARY KEY, NAME VARCHAR(20), VAL INT)",
            "INSERT INTO A VALUES(1,'one',10)",
            "INSERT INTO A VALUES(2,'two',20),(3,'three',30)",
            "SELECT ID, NAME, VAL FROM A ORDER BY ID",
            "INSERT INTO A SELECT 4, UPPER('four'), 40",
            "SELECT COUNT(*) FROM A",
            "UPDATE A SET VAL = VAL + 1 WHERE ID = 2",
            "SELECT VAL FROM A WHERE ID = 2",
            "UPDATE A SET NAME = (SELECT 'x') WHERE ID = 1",
            "SELECT NAME FROM A WHERE ID = 1",
            "DELETE FROM A WHERE ID = 3",
            "SELECT COUNT(*) FROM A",
            "TRUNCATE TABLE A",
            "SELECT COUNT(*) FROM A");
    }

    [JavaOracleFact]
    public void Merge()
    {
        AssertSameAsReference("parser_dml2",
            "CREATE TABLE A(ID INT PRIMARY KEY, NAME VARCHAR(20), VAL INT)",
            "CREATE TABLE SRC(SID INT, SNAME VARCHAR(20))",
            "INSERT INTO SRC VALUES(10,'ten')",
            "MERGE INTO A KEY(ID) VALUES(5,'five',50)",
            "SELECT ID, NAME FROM A",
            "MERGE INTO A KEY(ID) VALUES(5,'FIVE',55)",
            "SELECT ID, NAME, VAL FROM A",
            "MERGE INTO A USING SRC ON A.ID = SRC.SID"
            + " WHEN MATCHED THEN UPDATE SET NAME = SRC.SNAME"
            + " WHEN NOT MATCHED THEN INSERT(ID, NAME, VAL) VALUES(SRC.SID, SRC.SNAME, 0)",
            "SELECT COUNT(*) FROM A");
    }
}
