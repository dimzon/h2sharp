/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * MERGE as the reference dml/merge.sql and dml/mergeUsing.sql define it: the VALUES and
 * SELECT sources with the implicit and explicit keys, DEFAULT and generated columns, the
 * index-matching refusals, the MySQL boolean conversion, the USING forms with their WHEN
 * clauses, conditions, DELETE, FINAL TABLE, identity overriding and the plans.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class MergeOracleTests
{
    [JavaOracleFact]
    public void MergeValuesAndSelectMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(255))",
            "MERGE INTO TEST VALUES(1, 'Hello')",
            "MERGE INTO TEST VALUES(1, 'Hi')",
            "MERGE INTO TEST VALUES(2, 'World')",
            "MERGE INTO TEST VALUES(2, 'World!')",
            "MERGE INTO TEST(ID, NAME) VALUES(3, 'How are you')",
            "MERGE INTO TEST(ID, NAME) KEY(ID) VALUES(3, 'How do you do')",
            "MERGE INTO TEST(ID, NAME) KEY(NAME) VALUES(3, 'Fine')",
            "MERGE INTO TEST(ID, NAME) KEY(NAME) VALUES(4, 'Fine!')",
            "MERGE INTO TEST(ID, NAME) KEY(NAME) VALUES(4, 'Fine! And you')",
            "MERGE INTO TEST(ID, NAME) KEY(NAME, ID) VALUES(5, 'I''m ok')",
            "MERGE INTO TEST(ID, NAME) KEY(NAME, ID) VALUES(5, 'Oh, fine')",
            "MERGE INTO TEST(ID, NAME) VALUES(6, 'Oh, fine.')",
            "SELECT * FROM TEST",
            "MERGE INTO TEST SELECT ID+4, NAME FROM TEST",
            "SELECT * FROM TEST",
            "DROP TABLE TEST",
            // The index matching: a two-row source that collides on the second unique key.
            "CREATE TABLE TEST(ID INT PRIMARY KEY, VALUE1 INT, VALUE2 INT, UNIQUE(VALUE1, VALUE2))",
            "MERGE INTO TEST KEY (ID) VALUES (1, 2, 3), (2, 2, 3)",
            "DROP TABLE TEST",
            // DEFAULT in the source row reaches the column default on update and insert.
            "CREATE TABLE TEST(A INT, B INT DEFAULT 5)",
            "MERGE INTO TEST KEY(A) VALUES (1, DEFAULT)",
            "TABLE TEST",
            "UPDATE TEST SET B = 1 WHERE A = 1",
            "SELECT B FROM TEST WHERE A = 1",
            "MERGE INTO TEST KEY(A) VALUES (1, DEFAULT)",
            "SELECT B FROM TEST WHERE A = 1",
            "DROP TABLE TEST",
            // A generated column may not be assigned, but DEFAULT is allowed.
            "CREATE TABLE TEST(A INT, B INT GENERATED ALWAYS AS (A + 1))",
            "MERGE INTO TEST KEY(A) VALUES (1, 1)",
            "MERGE INTO TEST KEY(A) VALUES (1, DEFAULT)",
            "MERGE INTO TEST KEY(A) VALUES (1, 1)",
            "MERGE INTO TEST KEY(A) VALUES (1, DEFAULT)",
            "DROP TABLE TEST",
            "CREATE TABLE TEST(ID INT, G INT GENERATED ALWAYS AS (ID + 1))",
            "MERGE INTO TEST(G) KEY(ID) VALUES (1)",
            "DROP TABLE TEST",
            // The boolean primary key is not comparable to INTEGER in Regular mode, but is
            // in MySQL mode.
            "CREATE TABLE T(ID BOOLEAN PRIMARY KEY)",
            "INSERT INTO T(ID) VALUES (TRUE)",
            "MERGE INTO T(ID) VALUES 2",
            "SET MODE MySQL",
            "MERGE INTO T(ID) VALUES 2",
            "SET MODE Regular",
            "DROP TABLE T",
        ]);
    }

    [JavaOracleFact]
    public void MergeUsingMatchesJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE PARENT(ID INT, NAME VARCHAR, PRIMARY KEY(ID))",
            // The qualified SET column is Oracle syntax.
            "MERGE INTO PARENT AS P USING (SELECT X AS ID, 'Coco'||X AS NAME FROM SYSTEM_RANGE(1,2)) AS S "
                + "ON (P.ID = S.ID AND 1=1 AND S.ID = P.ID) "
                + "WHEN MATCHED THEN UPDATE SET P.NAME = S.NAME WHERE 2 = 2",
            "SET MODE Oracle",
            "MERGE INTO PARENT AS P USING (SELECT X AS ID, 'Coco'||X AS NAME FROM SYSTEM_RANGE(1,2)) AS S "
                + "ON (P.ID = S.ID AND 1=1 AND S.ID = P.ID) "
                + "WHEN MATCHED THEN UPDATE SET P.NAME = S.NAME WHERE 2 = 2 "
                + "WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (S.ID, S.NAME)",
            "SELECT * FROM PARENT",
            "SET MODE Regular",
            "DROP TABLE PARENT",
            // Schema-qualified source and target.
            "CREATE SCHEMA SOURCESCHEMA",
            "CREATE TABLE SOURCESCHEMA.SOURCE(ID INT PRIMARY KEY, \"VALUE\" INT)",
            "INSERT INTO SOURCESCHEMA.SOURCE VALUES (1, 10), (3, 30), (5, 50)",
            "CREATE SCHEMA DESTSCHEMA",
            "CREATE TABLE DESTSCHEMA.DESTINATION(ID INT PRIMARY KEY, \"VALUE\" INT)",
            "INSERT INTO DESTSCHEMA.DESTINATION VALUES (3, 300), (6, 600)",
            "MERGE INTO DESTSCHEMA.DESTINATION USING SOURCESCHEMA.SOURCE "
                + "ON (DESTSCHEMA.DESTINATION.ID = SOURCESCHEMA.SOURCE.ID) "
                + "WHEN MATCHED THEN UPDATE SET \"VALUE\" = SOURCESCHEMA.SOURCE.\"VALUE\" "
                + "WHEN NOT MATCHED THEN INSERT (ID, \"VALUE\") "
                + "VALUES (SOURCESCHEMA.SOURCE.ID, SOURCESCHEMA.SOURCE.\"VALUE\")",
            "SELECT * FROM DESTSCHEMA.DESTINATION",
            "DROP SCHEMA SOURCESCHEMA CASCADE",
            "DROP SCHEMA DESTSCHEMA CASCADE",
            // The update and insert mix, with the source aliased.
            "CREATE TABLE SOURCE_TABLE(ID BIGINT PRIMARY KEY, C1 INT NOT NULL)",
            "INSERT INTO SOURCE_TABLE VALUES (1, 10), (2, 20), (3, 30)",
            "CREATE TABLE DEST_TABLE(ID BIGINT PRIMARY KEY, C1 INT NOT NULL, C2 INT NOT NULL)",
            "INSERT INTO DEST_TABLE VALUES (2, 200, 2000), (4, 400, 4000)",
            "MERGE INTO DEST_TABLE USING SOURCE_TABLE ON (DEST_TABLE.ID = SOURCE_TABLE.ID) "
                + "WHEN MATCHED THEN UPDATE SET DEST_TABLE.C1 = SOURCE_TABLE.C1, DEST_TABLE.C2 = 100",
            "SELECT * FROM DEST_TABLE",
            "MERGE INTO DEST_TABLE D USING SOURCE_TABLE S ON (D.ID = S.ID) "
                + "WHEN MATCHED THEN UPDATE SET D.C1 = S.C1, D.C2 = 100 "
                + "WHEN NOT MATCHED THEN INSERT (ID, C1, C2) VALUES (S.ID, S.C1, 1000)",
            "SELECT * FROM DEST_TABLE",
            "DROP TABLE SOURCE_TABLE",
            "DROP TABLE DEST_TABLE",
            // DUAL as the source, and the condition deciding between insert and update.
            "CREATE TABLE TEST(C1 INT, C2 INT, C3 INT)",
            "MERGE INTO TEST USING DUAL ON C1 = 11 AND C2 = 21 "
                + "WHEN NOT MATCHED THEN INSERT (C1, C2, C3) VALUES (11, 21, 31) "
                + "WHEN MATCHED THEN UPDATE SET C3 = 31",
            "MERGE INTO TEST USING DUAL ON (C1 = 11 AND C2 = 22) "
                + "WHEN NOT MATCHED THEN INSERT (C1, C2, C3) VALUES (11, 22, 32) "
                + "WHEN MATCHED THEN UPDATE SET C3 = 32",
            "SELECT * FROM TEST ORDER BY C1, C2",
            "MERGE INTO TEST USING DUAL ON C1 = 11 AND C2 = 21 "
                + "WHEN NOT MATCHED THEN INSERT (C1, C2, C3) VALUES (11, 21, 33) "
                + "WHEN MATCHED THEN UPDATE SET C3 = 33",
            "SELECT * FROM TEST ORDER BY C1, C2",
            "MERGE INTO TEST USING (SELECT 1 FROM DUAL) ON (C1 = 11 AND C2 = 21) "
                + "WHEN NOT MATCHED THEN INSERT (C1, C2, C3) VALUES (11, 21, 33) "
                + "WHEN MATCHED THEN UPDATE SET C3 = 34",
            "SELECT * FROM TEST ORDER BY C1, C2",
            "DROP TABLE TEST",
        ]);
    }

    [JavaOracleFact]
    public void MergeUsingClausesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            // Incomplete WHEN clauses are syntax errors.
            "CREATE TABLE TEST (ID INT, \"VALUE\" INT)",
            "MERGE INTO TEST USING DUAL ON (ID = 1) WHEN MATCHED THEN UPDATE SET \"VALUE\" = 1 WHEN",
            "MERGE INTO TEST USING DUAL ON (ID = 1) WHEN MATCHED THEN UPDATE SET \"VALUE\" = 1 "
                + "WHEN NOT MATCHED THEN",
            "MERGE INTO TEST USING DUAL ON (ID = 1) "
                + "WHEN NOT MATCHED THEN INSERT (ID, \"VALUE\") VALUES (1, 1) WHEN",
            "MERGE INTO TEST USING DUAL ON (ID = 1) "
                + "WHEN NOT MATCHED THEN INSERT (ID, \"VALUE\") VALUES (1, 1) WHEN MATCHED THEN",
            "DROP TABLE TEST",
            // The ON condition: false inserts, true matches every target row.
            "CREATE TABLE TEST(ID INT PRIMARY KEY, \"VALUE\" INT)",
            "INSERT INTO TEST VALUES (1, 10), (2, 20)",
            "MERGE INTO TEST USING (SELECT 1) ON (ID < 0) "
                + "WHEN MATCHED THEN UPDATE SET \"VALUE\" = 30 "
                + "WHEN NOT MATCHED THEN INSERT VALUES (3, 30)",
            "SELECT * FROM TEST",
            "MERGE INTO TEST USING (SELECT 1) ON (ID = ID) "
                + "WHEN MATCHED THEN UPDATE SET \"VALUE\" = 40 "
                + "WHEN NOT MATCHED THEN INSERT VALUES (4, 40)",
            "SELECT * FROM TEST",
            "MERGE INTO TEST USING (SELECT 1) ON (1 = 1) "
                + "WHEN MATCHED THEN UPDATE SET \"VALUE\" = 50 "
                + "WHEN NOT MATCHED THEN INSERT VALUES (5, 50)",
            "SELECT * FROM TEST",
            "MERGE INTO TEST USING (SELECT 1) ON 1 = 1 "
                + "WHEN MATCHED THEN UPDATE SET \"VALUE\" = 60 WHERE ID = 3 DELETE WHERE ID = 2",
            "MERGE INTO TEST USING (SELECT 1 A) ON 1 = 1 "
                + "WHEN MATCHED THEN DELETE WHERE ID = 2",
            "SET MODE Oracle",
            "MERGE INTO TEST USING (SELECT 1 A) ON 1 = 1 "
                + "WHEN MATCHED THEN DELETE WHERE ID = 2",
            "SET MODE Regular",
            "SELECT * FROM TEST",
            "DROP TABLE TEST",
            // WHEN MATCHED AND / WHEN NOT MATCHED AND.
            "CREATE TABLE T(ID INT, F BOOLEAN, \"VALUE\" INT)",
            "INSERT INTO T VALUES (1, FALSE, 10), (2, TRUE, 20)",
            "CREATE TABLE S(S_ID INT, S_F BOOLEAN, S_VALUE INT)",
            "INSERT INTO S VALUES (1, FALSE, 100), (2, TRUE, 200), (3, FALSE, 300), (4, TRUE, 400)",
            "MERGE INTO T USING S ON ID = S_ID "
                + "WHEN MATCHED AND F THEN UPDATE SET \"VALUE\" = S_VALUE "
                + "WHEN MATCHED AND NOT F THEN DELETE "
                + "WHEN NOT MATCHED AND S_F THEN INSERT VALUES (S_ID, S_F, S_VALUE)",
            "SELECT * FROM T",
            "DROP TABLE T, S",
        ]);
    }

    [JavaOracleFact]
    public void MergeSourceShapesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            // A source that matches several target rows is a duplicate-key error.
            "CREATE TABLE T(ID INT, A INT, B INT) AS VALUES (1, 1, 1), (2, 1, 2)",
            "CREATE TABLE S(ID INT, A INT, B INT) AS VALUES (1, 1, 3), (2, 1, 4)",
            "MERGE INTO T USING S ON T.A = S.A WHEN MATCHED THEN UPDATE SET B = S.B",
            "CREATE TABLE S2(ID INT, A INT, B INT) AS VALUES (3, 3, 3)",
            "MERGE INTO T USING (SELECT * FROM S UNION SELECT * FROM S2) S ON T.ID = S.ID "
                + "WHEN MATCHED THEN UPDATE SET A = S.A, B = S.B "
                + "WHEN NOT MATCHED THEN INSERT VALUES (S.ID, S.A, S.B)",
            "TABLE T",
            // A table source without a subquery.
            "MERGE INTO T USING (S) ON T.ID = S.ID WHEN MATCHED THEN UPDATE SET B = S.B + 1",
            "TABLE T",
            "DROP TABLE T, S, S2 CASCADE",
            "CREATE TABLE TEST(ID INT, V INT)",
            "MERGE INTO TEST USING VALUES (1, 2) S ON TEST.ID = S.C1 "
                + "WHEN NOT MATCHED THEN INSERT VALUES (1, 2), (3, 4)",
            "DROP TABLE TEST",
            // An unknown column in the WHEN condition.
            "CREATE TABLE T(A INT)",
            "MERGE INTO T USING (SELECT 1 A) S ON (TRUE) "
                + "WHEN NOT MATCHED AND S.X THEN INSERT VALUES (1)",
            "DROP TABLE T",
            // Column lists on the source, and a VALUES source with them.
            "CREATE TABLE TARGET(ID INT, V INT)",
            "MERGE INTO TARGET T USING (VALUES (1, 2)) S(ID, V) ON T.ID = S.ID "
                + "WHEN NOT MATCHED THEN INSERT VALUES (S.ID, S.V)",
            "CREATE TABLE SOURCE(ID INT, V INT) AS VALUES (3, 4)",
            "MERGE INTO TARGET T USING SOURCE S(ID, V) ON T.ID = S.ID "
                + "WHEN NOT MATCHED THEN INSERT VALUES (S.ID, S.V)",
            "TABLE TARGET",
            "DROP TABLE SOURCE, TARGET",
        ]);
    }

    [JavaOracleFact]
    public void MergeWhenOrderAndFinalTableMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            // The first matching WHEN clause wins.
            "CREATE TABLE T(ID INT, V INT) AS VALUES (1, 1), (2, 2)",
            "MERGE INTO T USING (SELECT 1) ON (TRUE) "
                + "WHEN MATCHED THEN UPDATE SET V = 2 "
                + "WHEN MATCHED AND ID = 2 THEN UPDATE SET V = 3",
            "TABLE T",
            "TRUNCATE TABLE T",
            "INSERT INTO T VALUES (1, 1)",
            "MERGE INTO T USING (SELECT 1) ON (ID = 1) "
                + "WHEN MATCHED THEN UPDATE SET V = 2 "
                + "WHEN MATCHED THEN UPDATE SET V = 3",
            "TABLE T",
            "SELECT * FROM FINAL TABLE (MERGE INTO T USING (SELECT 1) ON (ID = 1) "
                + "WHEN MATCHED THEN UPDATE SET V = 4 "
                + "WHEN MATCHED THEN UPDATE SET V = 5)",
            "DROP TABLE T",
            // Identity columns and OVERRIDING.
            "CREATE TABLE TEST(ID BIGINT GENERATED BY DEFAULT AS IDENTITY, V INT)",
            "MERGE INTO TEST USING (VALUES (10, 20)) SOURCE(ID, V) ON TEST.ID = SOURCE.ID "
                + "WHEN NOT MATCHED THEN INSERT VALUES(SOURCE.ID, SOURCE.V)",
            "MERGE INTO TEST USING (VALUES (20, 30)) SOURCE(ID, V) ON TEST.ID = SOURCE.ID "
                + "WHEN NOT MATCHED THEN INSERT OVERRIDING USER VALUE VALUES(SOURCE.ID, SOURCE.V)",
            "MERGE INTO TEST USING (VALUES (30, 40)) SOURCE(ID, V) ON TEST.ID = SOURCE.ID "
                + "WHEN NOT MATCHED THEN INSERT OVERRIDING SYSTEM VALUE VALUES(SOURCE.ID, SOURCE.V)",
            "TABLE TEST",
            "DROP TABLE TEST",
            "CREATE TABLE TEST(ID BIGINT GENERATED ALWAYS AS IDENTITY, V INT)",
            "MERGE INTO TEST USING (VALUES (10, 20)) SOURCE(ID, V) ON TEST.ID = SOURCE.ID "
                + "WHEN NOT MATCHED THEN INSERT VALUES(SOURCE.ID, SOURCE.V)",
            "MERGE INTO TEST USING (VALUES (20, 30)) SOURCE(ID, V) ON TEST.ID = SOURCE.ID "
                + "WHEN NOT MATCHED THEN INSERT OVERRIDING USER VALUE VALUES(SOURCE.ID, SOURCE.V)",
            "MERGE INTO TEST USING (VALUES (30, 40)) SOURCE(ID, V) ON TEST.ID = SOURCE.ID "
                + "WHEN NOT MATCHED THEN INSERT OVERRIDING SYSTEM VALUE VALUES(SOURCE.ID, SOURCE.V)",
            "TABLE TEST",
            "DROP TABLE TEST",
        ]);
    }

    [JavaOracleFact]
    public void TheMergePlansMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(255))",
            "EXPLAIN MERGE INTO TEST VALUES(1, 'Hello')",
            "EXPLAIN MERGE INTO TEST(ID, NAME) VALUES(3, 'How are you')",
            "EXPLAIN MERGE INTO TEST(ID, NAME) KEY(NAME) VALUES(3, 'Fine')",
            "CREATE TABLE PARENT(ID INT, NAME VARCHAR, PRIMARY KEY(ID))",
            "SET MODE Oracle",
            "EXPLAIN PLAN MERGE INTO PARENT AS P "
                + "USING (SELECT X AS ID, 'Coco'||X AS NAME FROM SYSTEM_RANGE(1,2)) AS S "
                + "ON (P.ID = S.ID AND 1=1 AND S.ID = P.ID) "
                + "WHEN MATCHED THEN UPDATE SET P.NAME = S.NAME WHERE 2 = 2 "
                + "WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (S.ID, S.NAME)",
            "SET MODE Regular",
            "CREATE TABLE T(ID INT, V INT)",
            "EXPLAIN MERGE INTO T USING (VALUES (1, 2)) S(ID, V) ON T.ID = S.ID "
                + "WHEN NOT MATCHED AND T.ID = 1 THEN INSERT VALUES (S.ID, S.V) "
                + "WHEN NOT MATCHED THEN INSERT VALUES (S.ID, S.V + 1) "
                + "WHEN MATCHED AND T.ID = 2 THEN UPDATE SET V = S.ID + 2 "
                + "WHEN MATCHED THEN UPDATE SET V = S.ID + 3",
            "EXPLAIN MERGE INTO T USING (VALUES (1, 2)) S(ID, V) ON T.ID = S.ID "
                + "WHEN MATCHED AND T.ID = 1 THEN DELETE "
                + "WHEN MATCHED THEN DELETE",
            "DROP TABLE T",
        ]);
    }
}
