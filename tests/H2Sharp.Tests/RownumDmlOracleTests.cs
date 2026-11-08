/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * ROWNUM() inside a data change statement. The reference builds the command before it reads
 * the statement's expressions and numbers every scanned row with the count of rows matched
 * so far plus one, so ROWNUM() in a WHERE, SET or VALUES expression reads the number of the
 * statement that runs. The parse-only layer of the port must keep that binding.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class RownumDmlOracleTests
{
    [JavaOracleFact]
    public void RownumInDeleteAnswersTheReference()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE T(ID INT)",
            "INSERT INTO T VALUES (1), (2), (3)",
            // The reference's dml/delete.sql statement: the EXISTS condition filters and
            // ROWNUM() = 1 stops after the first row that passes it.
            "DELETE FROM T WHERE EXISTS (SELECT X FROM SYSTEM_RANGE(1, 3) WHERE X = ID) AND ROWNUM() = 1",
            "SELECT ID FROM T ORDER BY ID",
            // ROWNUM() = 2 never matches: the number stays 1 until a row has matched.
            "DELETE FROM T WHERE ROWNUM() = 2",
            "SELECT ID FROM T ORDER BY ID",
            "DROP TABLE T",
        ]);
    }

    [JavaOracleFact]
    public void RownumInUpdateAnswersTheReference()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE T(ID INT, V INT)",
            "INSERT INTO T VALUES (1, 0), (2, 0), (3, 0)",
            "UPDATE T SET V = ROWNUM()",
            "SELECT ID, V FROM T ORDER BY ID",
            // A non-matching row does not advance the number: the next row still sees 1.
            "UPDATE T SET V = V + 10 WHERE ID > 1 AND ROWNUM() <= 1",
            "SELECT ID, V FROM T ORDER BY ID",
            "DROP TABLE T",
        ]);
    }

    [JavaOracleFact]
    public void RownumInInsertAndMergeAnswersTheReference()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE T(ID INT, V INT)",
            "INSERT INTO T VALUES (1, ROWNUM()), (2, ROWNUM()), (3, ROWNUM())",
            "SELECT ID, V FROM T ORDER BY ID",
            "MERGE INTO T KEY(ID) VALUES (4, ROWNUM()), (5, ROWNUM())",
            "SELECT ID, V FROM T ORDER BY ID",
            "DROP TABLE T",
        ]);
    }
}
