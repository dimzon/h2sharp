/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 207: the query and table edges the suite had not reached — index-sorted scans
 * and lazy WITH TIES, DISTINCT ON in view SQL and beside DISTINCT, ORDER BY position and
 * HAVING over an alias, constant join conditions, generated-column assignment, a CHECK
 * violation rolled back inside an UPDATE, temporary-table types, and foreign keys with
 * comments, ON UPDATE actions and between temporary tables.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class SelectTableEdgeOracleTests : ParserCoverageOracleBase
{
    private const string Setup =
        "CREATE TABLE T(A INT, B INT, NAME VARCHAR(20))"
        + ";CREATE INDEX IA ON T(A)"
        + ";CREATE INDEX IAD ON T(B DESC)"
        + ";INSERT INTO T SELECT X, 50 - X, 'n' || X FROM SYSTEM_RANGE(1, 50)";

    [JavaOracleFact]
    public void IndexOrderAndLazyWithTies()
    {
        AssertSameAsReference("ste_index_order",
            Setup,
            "SELECT A FROM T ORDER BY A",
            "SELECT A FROM T ORDER BY A DESC",
            "SELECT B FROM T ORDER BY B",
            "SELECT A FROM T ORDER BY A FETCH FIRST 1 ROWS WITH TIES",
            "SET LAZY_QUERY_EXECUTION 1",
            "SELECT A FROM T ORDER BY A FETCH FIRST 1 ROWS WITH TIES",
            "SELECT A FROM T ORDER BY A DESC FETCH FIRST 1 ROWS WITH TIES",
            "SET LAZY_QUERY_EXECUTION 0",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void DistinctOnFormsAndTheirRefusals()
    {
        AssertSameAsReference("ste_distinct_on",
            Setup,
            "CREATE VIEW V1 AS SELECT DISTINCT ON (A) A FROM T",
            "SELECT REPLACE(VIEW_DEFINITION, CHAR(10), '|') FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME = 'V1'",
            "CREATE VIEW V2 AS SELECT A, COUNT(*) FROM T GROUP BY A",
            "SELECT REPLACE(VIEW_DEFINITION, CHAR(10), '|') FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME = 'V2'",
            "SELECT DISTINCT ON (A) DISTINCT A FROM T",
            "SELECT DISTINCT ON (A) A, DISTINCT B FROM T",
            "DROP VIEW V1, V2",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void OrderByPositionHavingAliasAndConstantJoins()
    {
        AssertSameAsReference("ste_order_having",
            Setup,
            "CREATE TABLE U(ID INT, A INT)",
            "INSERT INTO U SELECT X, X FROM SYSTEM_RANGE(1, 5)",
            "SELECT A FROM T ORDER BY 3",
            "SELECT -1 FROM T ORDER BY 3",
            "SELECT A, COUNT(*) AS C FROM T GROUP BY A HAVING C > 1",
            "SELECT COUNT(*) AS C FROM T HAVING C > 1",
            "SELECT U.ID, T.A FROM U JOIN T ON 1 = 1 AND U.ID = T.A",
            "SELECT U.ID, T.A FROM U JOIN T ON FALSE",
            "SELECT U.ID FROM U LEFT JOIN T ON 1 = 0",
            "DROP TABLE U",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void ComparisonIndexConditions()
    {
        AssertSameAsReference("ste_compare_index",
            Setup,
            "SELECT A FROM T WHERE A < 5",
            "SELECT A FROM T WHERE A > 45",
            "SELECT A FROM T WHERE A <= 5",
            "SELECT A FROM T WHERE A >= 45",
            "SELECT A, B FROM T WHERE (A, B) < (2, 50)",
            "SELECT A, B FROM T WHERE (A, B) > (49, 0)",
            "SELECT A FROM T WHERE A < 5.5",
            "SELECT A FROM T WHERE NAME LIKE 'n1%'",
            "SELECT A FROM T WHERE NAME LIKE 'n1_'",
            "SELECT A FROM T WHERE NAME LIKE 'n\\_1%' ESCAPE '\\'",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void GeneratedColumnAndCheckRollbackInUpdate()
    {
        AssertSameAsReference("ste_table_write",
            "CREATE TABLE C(I INT CHECK (I < 10), G INT GENERATED ALWAYS AS (I + 1))",
            "INSERT INTO C(I) VALUES (1), (2), (3)",
            "UPDATE C SET G = 5",
            "UPDATE C SET I = I + 100",
            "SELECT I, G FROM C",
            "DROP TABLE C");
    }

    [JavaOracleFact]
    public void TemporaryTableTypesAndForeignKeys()
    {
        AssertSameAsReference("ste_temp_fk",
            "CREATE GLOBAL TEMPORARY TABLE GT(ID INT PRIMARY KEY)",
            "CREATE LOCAL TEMPORARY TABLE LT(ID INT PRIMARY KEY, P INT)",
            "SELECT TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES "
                + "WHERE TABLE_NAME IN ('GT', 'LT') ORDER BY TABLE_NAME",
            "ALTER TABLE LT ADD CONSTRAINT FK1 COMMENT 'child' FOREIGN KEY (P) REFERENCES GT(ID)",
            "SELECT SQL FROM INFORMATION_SCHEMA.CONSTRAINTS WHERE CONSTRAINT_NAME = 'FK1'",
            "INSERT INTO GT VALUES (1)",
            "INSERT INTO LT VALUES (10, 1)",
            "UPDATE GT SET ID = 2",
            "SELECT * FROM LT",
            "DROP TABLE LT",
            "DROP TABLE GT");
    }

    [JavaOracleFact]
    public void ForeignKeyOnUpdateActions()
    {
        AssertSameAsReference("ste_fk_actions",
            "CREATE TABLE P(ID INT PRIMARY KEY)",
            "CREATE TABLE C(ID INT PRIMARY KEY, P INT, D INT DEFAULT 7)",
            "ALTER TABLE C ADD CONSTRAINT FKS FOREIGN KEY (P) REFERENCES P(ID) "
                + "ON UPDATE SET NULL ON DELETE SET DEFAULT",
            "INSERT INTO P VALUES (1), (2)",
            "INSERT INTO C VALUES (10, 1, 1), (11, 2, 2)",
            "UPDATE P SET ID = 3 WHERE ID = 1",
            "SELECT * FROM C",
            "DELETE FROM P WHERE ID = 2",
            "SELECT * FROM C",
            "DROP TABLE C",
            "DROP TABLE P");
    }
}
