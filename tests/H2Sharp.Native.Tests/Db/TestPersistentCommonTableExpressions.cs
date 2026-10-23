// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestPersistentCommonTableExpressions.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the reference's main() entry is the runner; xUnit runs Test() directly.
namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test persistent common table expressions queries using WITH.
/// </summary>
public sealed class TestPersistentCommonTableExpressions
    : AbstractBaseForCommonTableExpressions
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        // persistent cte tests - also tests reconnects and database reloading...
        TestRecursiveTable();
        TestPersistentNonRecursiveTableInCreateView();
        TestPersistentRecursiveTableInCreateView();
        TestPersistentNonRecursiveTableInCreateViewDropAllObjects();
        TestPersistentRecursiveTableInCreateViewDropAllObjects();
    }

    private void TestRecursiveTable()
    {
        string?[] expectedRowData = ["|meat|null", "|fruit|3", "|veg|2"];
        string[] expectedColumnTypes = ["CHARACTER VARYING", "NUMERIC"];
        string[] expectedColumnNames = ["VAL",
            "SUM((SELECT\n"
            + "    X\n"
            + "FROM BB BB\n"
            + "WHERE BB.A IS NOT DISTINCT FROM A.VAL))"];

        string setupSQL =
            "DROP TABLE IF EXISTS A;                           "
            + "DROP TABLE IF EXISTS B;                           "
            + "DROP TABLE IF EXISTS C;                           "
            + "CREATE TABLE A(VAL VARCHAR(255));                 "
            + "CREATE TABLE B(A VARCHAR(255), VAL VARCHAR(255)); "
            + "CREATE TABLE C(B VARCHAR(255), VAL VARCHAR(255)); "
            + "                                                  "
            + "INSERT INTO A VALUES('fruit');                    "
            + "INSERT INTO B VALUES('fruit','apple');            "
            + "INSERT INTO B VALUES('fruit','banana');           "
            + "INSERT INTO C VALUES('apple', 'golden delicious');"
            + "INSERT INTO C VALUES('apple', 'granny smith');    "
            + "INSERT INTO C VALUES('apple', 'pippin');          "
            + "INSERT INTO A VALUES('veg');                      "
            + "INSERT INTO B VALUES('veg', 'carrot');            "
            + "INSERT INTO C VALUES('carrot', 'nantes');         "
            + "INSERT INTO C VALUES('carrot', 'imperator');      "
            + "INSERT INTO C VALUES(null, 'banapple');           "
            + "INSERT INTO A VALUES('meat');                     ";

        string withQuery = "WITH BB as (SELECT                        \n"
            + "sum(1) as X,                             \n"
            + "a                                        \n"
            + "FROM B                                   \n"
            + "JOIN C ON B.val=C.b                      \n"
            + "GROUP BY a)                              \n"
            + "SELECT                                   \n"
            + "A.val,                                   \n"
            + "sum((SELECT X FROM BB WHERE BB.a IS NOT DISTINCT FROM A.val))\n"
            + "FROM A                                   \n GROUP BY A.val";

        int maxRetries = 3;
        int expectedNumberOfRows = expectedRowData.Length;

        TestRepeatedQueryWithSetup(maxRetries, expectedRowData, expectedColumnNames, expectedNumberOfRows,
            setupSQL, withQuery, maxRetries - 1, expectedColumnTypes, true);
    }

    private void TestPersistentRecursiveTableInCreateView()
    {
        string setupSQL = "--SET TRACE_LEVEL_SYSTEM_OUT 3;\n"
            + "DROP TABLE IF EXISTS my_tree;                                                                \n"
            + "DROP VIEW IF EXISTS v_my_tree;                                                               \n"
            + "CREATE TABLE my_tree (                                                                       \n"
            + " id INTEGER,                                                                                 \n"
            + " parent_fk INTEGER                                                                           \n"
            + ");                                                                                           \n"
            + "                                                                                             \n"
            + "INSERT INTO my_tree ( id, parent_fk) VALUES ( 1, NULL );                                     \n"
            + "INSERT INTO my_tree ( id, parent_fk) VALUES ( 11, 1 );                                       \n"
            + "INSERT INTO my_tree ( id, parent_fk) VALUES ( 111, 11 );                                     \n"
            + "INSERT INTO my_tree ( id, parent_fk) VALUES ( 12, 1 );                                       \n"
            + "INSERT INTO my_tree ( id, parent_fk) VALUES ( 121, 12 );                                     \n"
            + "                                                                                             \n"
            + "CREATE OR REPLACE VIEW v_my_tree AS                                                          \n"
            + "WITH RECURSIVE tree_cte (sub_tree_root_id, tree_level, parent_fk, child_fk) AS (             \n"
            + "    SELECT mt.ID AS sub_tree_root_id, CAST(0 AS INT) AS tree_level, mt.parent_fk, mt.id      \n"
            + "      FROM my_tree mt                                                                        \n"
            + " UNION ALL                                                                                   \n"
            + "    SELECT sub_tree_root_id, mtc.tree_level + 1 AS tree_level, mtc.parent_fk, mt.id          \n"
            + "      FROM my_tree mt                                                                        \n"
            + "INNER JOIN tree_cte mtc ON mtc.child_fk = mt.parent_fk                                       \n"
            + "),                                                                                           \n"
            + "unused_cte(unUsedColumn) AS ( SELECT 1 AS unUsedColumn )                                     \n"
            + "SELECT sub_tree_root_id, tree_level, parent_fk, child_fk FROM tree_cte;                      \n";

        string withQuery = "SELECT * FROM v_my_tree";
        int maxRetries = 4;
        string?[] expectedRowData =
        [
            "|1|0|null|1",
            "|11|0|1|11",
            "|111|0|11|111",
            "|12|0|1|12",
            "|121|0|12|121",
            "|1|1|null|11",
            "|11|1|1|111",
            "|1|1|null|12",
            "|12|1|1|121",
            "|1|2|null|111",
            "|1|2|null|121",
        ];
        string[] expectedColumnNames = ["SUB_TREE_ROOT_ID", "TREE_LEVEL", "PARENT_FK", "CHILD_FK"];
        string[] expectedColumnTypes = ["INTEGER", "INTEGER", "INTEGER", "INTEGER"];
        int expectedNumberOfRows = 11;
        TestRepeatedQueryWithSetup(maxRetries, expectedRowData, expectedColumnNames, expectedNumberOfRows,
            setupSQL, withQuery, maxRetries - 1, expectedColumnTypes, false);
    }

    private void TestPersistentNonRecursiveTableInCreateView()
    {
        string setupSQL = ""
            + "DROP VIEW IF EXISTS v_my_nr_tree;                                                            \n"
            + "DROP TABLE IF EXISTS my_table;                                                               \n"
            + "CREATE TABLE my_table (                                                                      \n"
            + " id INTEGER,                                                                                 \n"
            + " parent_fk INTEGER                                                                           \n"
            + ");                                                                                           \n"
            + "                                                                                             \n"
            + "INSERT INTO my_table ( id, parent_fk) VALUES ( 1, NULL );                                    \n"
            + "INSERT INTO my_table ( id, parent_fk) VALUES ( 11, 1 );                                      \n"
            + "INSERT INTO my_table ( id, parent_fk) VALUES ( 111, 11 );                                    \n"
            + "INSERT INTO my_table ( id, parent_fk) VALUES ( 12, 1 );                                      \n"
            + "INSERT INTO my_table ( id, parent_fk) VALUES ( 121, 12 );                                    \n"
            + "                                                                                             \n"
            + "CREATE OR REPLACE VIEW v_my_nr_tree AS                                                       \n"
            + "WITH tree_cte_nr (sub_tree_root_id, tree_level, parent_fk, child_fk) AS (                    \n"
            + "    SELECT mt.ID AS sub_tree_root_id, CAST(0 AS INT) AS tree_level, mt.parent_fk, mt.id      \n"
            + "      FROM my_table mt                                                                       \n"
            + "),                                                                                            \n"
            + "unused_cte AS ( SELECT 1 AS unUsedColumn )                                                   \n"
            + "SELECT sub_tree_root_id, tree_level, parent_fk, child_fk FROM tree_cte_nr;                   \n";

        string withQuery = "SELECT * FROM v_my_nr_tree";
        int maxRetries = 6;
        string?[] expectedRowData =
        [
            "|1|0|null|1",
            "|11|0|1|11",
            "|111|0|11|111",
            "|12|0|1|12",
            "|121|0|12|121",
        ];
        string[] expectedColumnNames = ["SUB_TREE_ROOT_ID", "TREE_LEVEL", "PARENT_FK", "CHILD_FK"];
        string[] expectedColumnTypes = ["INTEGER", "INTEGER", "INTEGER", "INTEGER"];
        int expectedNumberOfRows = 5;
        TestRepeatedQueryWithSetup(maxRetries, expectedRowData, expectedColumnNames, expectedNumberOfRows,
            setupSQL, withQuery, maxRetries - 1, expectedColumnTypes, false);
    }

    private void TestPersistentNonRecursiveTableInCreateViewDropAllObjects()
    {
        string setupSQL = ""
            + "DROP ALL OBJECTS;                                                                            \n"
            + "CREATE TABLE my_table (                                                                      \n"
            + " id INTEGER,                                                                                 \n"
            + " parent_fk INTEGER                                                                           \n"
            + ");                                                                                           \n"
            + "                                                                                             \n"
            + "INSERT INTO my_table ( id, parent_fk) VALUES ( 1, NULL );                                    \n"
            + "INSERT INTO my_table ( id, parent_fk) VALUES ( 11, 1 );                                      \n"
            + "INSERT INTO my_table ( id, parent_fk) VALUES ( 111, 11 );                                    \n"
            + "INSERT INTO my_table ( id, parent_fk) VALUES ( 12, 1 );                                      \n"
            + "INSERT INTO my_table ( id, parent_fk) VALUES ( 121, 12 );                                    \n"
            + "                                                                                             \n"
            + "CREATE OR REPLACE VIEW v_my_nr_tree AS                                                       \n"
            + "WITH tree_cte_nr (sub_tree_root_id, tree_level, parent_fk, child_fk) AS (                    \n"
            + "    SELECT mt.ID AS sub_tree_root_id, CAST(0 AS INT) AS tree_level, mt.parent_fk, mt.id      \n"
            + "      FROM my_table mt                                                                       \n"
            + "),                                                                                            \n"
            + "unused_cte AS ( SELECT 1 AS unUsedColumn )                                                   \n"
            + "SELECT sub_tree_root_id, tree_level, parent_fk, child_fk FROM tree_cte_nr;                   \n";

        string withQuery = "SELECT * FROM v_my_nr_tree";
        int maxRetries = 6;
        string?[] expectedRowData =
        [
            "|1|0|null|1",
            "|11|0|1|11",
            "|111|0|11|111",
            "|12|0|1|12",
            "|121|0|12|121",
        ];
        string[] expectedColumnNames = ["SUB_TREE_ROOT_ID", "TREE_LEVEL", "PARENT_FK", "CHILD_FK"];
        string[] expectedColumnTypes = ["INTEGER", "INTEGER", "INTEGER", "INTEGER"];
        int expectedNumberOfRows = 5;
        TestRepeatedQueryWithSetup(maxRetries, expectedRowData, expectedColumnNames, expectedNumberOfRows,
            setupSQL, withQuery, maxRetries - 1, expectedColumnTypes, false);
    }

    private void TestPersistentRecursiveTableInCreateViewDropAllObjects()
    {
        string setupSQL = "--SET TRACE_LEVEL_SYSTEM_OUT 3;\n"
            + "DROP ALL OBJECTS;                                                                            \n"
            + "CREATE TABLE my_tree (                                                                       \n"
            + " id INTEGER,                                                                                 \n"
            + " parent_fk INTEGER                                                                           \n"
            + ");                                                                                           \n"
            + "                                                                                             \n"
            + "INSERT INTO my_tree ( id, parent_fk) VALUES ( 1, NULL );                                     \n"
            + "INSERT INTO my_tree ( id, parent_fk) VALUES ( 11, 1 );                                       \n"
            + "INSERT INTO my_tree ( id, parent_fk) VALUES ( 111, 11 );                                     \n"
            + "INSERT INTO my_tree ( id, parent_fk) VALUES ( 12, 1 );                                       \n"
            + "INSERT INTO my_tree ( id, parent_fk) VALUES ( 121, 12 );                                     \n"
            + "                                                                                             \n"
            + "CREATE OR REPLACE VIEW v_my_tree AS                                                          \n"
            + "WITH RECURSIVE tree_cte (sub_tree_root_id, tree_level, parent_fk, child_fk) AS (             \n"
            + "    SELECT mt.ID AS sub_tree_root_id, CAST(0 AS INT) AS tree_level, mt.parent_fk, mt.id      \n"
            + "      FROM my_tree mt                                                                        \n"
            + " UNION ALL                                                                                   \n"
            + "    SELECT sub_tree_root_id, mtc.tree_level + 1 AS tree_level, mtc.parent_fk, mt.id          \n"
            + "      FROM my_tree mt                                                                        \n"
            + "INNER JOIN tree_cte mtc ON mtc.child_fk = mt.parent_fk                                       \n"
            + "),                                                                                           \n"
            + "unused_cte(unUsedColumn) AS ( SELECT 1 AS unUsedColumn )                                     \n"
            + "SELECT sub_tree_root_id, tree_level, parent_fk, child_fk FROM tree_cte;                      \n";

        string withQuery = "SELECT * FROM v_my_tree";
        int maxRetries = 4;
        string?[] expectedRowData =
        [
            "|1|0|null|1",
            "|11|0|1|11",
            "|111|0|11|111",
            "|12|0|1|12",
            "|121|0|12|121",
            "|1|1|null|11",
            "|11|1|1|111",
            "|1|1|null|12",
            "|12|1|1|121",
            "|1|2|null|111",
            "|1|2|null|121",
        ];
        string[] expectedColumnNames = ["SUB_TREE_ROOT_ID", "TREE_LEVEL", "PARENT_FK", "CHILD_FK"];
        string[] expectedColumnTypes = ["INTEGER", "INTEGER", "INTEGER", "INTEGER"];
        int expectedNumberOfRows = 11;
        TestRepeatedQueryWithSetup(maxRetries, expectedRowData, expectedColumnNames, expectedNumberOfRows,
            setupSQL, withQuery, maxRetries - 1, expectedColumnTypes, false);
    }
}
