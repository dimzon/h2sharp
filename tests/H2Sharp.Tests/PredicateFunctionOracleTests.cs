/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * BETWEEN in both orders and with symmetric bounds, IS NULL over scalars and ROW values, the
 * JSON item type predicates, and the DB_OBJECT_* functions over tables, constraints, indexes,
 * sequences, schemas and settings.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class PredicateFunctionOracleTests
{
    [JavaOracleFact]
    public void BetweenMatchesJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE BT(ID INT, V INT)",
            "INSERT INTO BT VALUES (1, 10), (2, 20), (3, NULL)",
            "CREATE INDEX BT_V ON BT(V)",
            "SELECT ID FROM BT WHERE V BETWEEN 10 AND 20 ORDER BY ID",
            "SELECT ID FROM BT WHERE V NOT BETWEEN 10 AND 20 ORDER BY ID",
            "SELECT ID FROM BT WHERE V BETWEEN 20 AND 10 ORDER BY ID",
            "SELECT ID FROM BT WHERE V BETWEEN SYMMETRIC 20 AND 10 ORDER BY ID",
            "SELECT ID FROM BT WHERE V BETWEEN SYMMETRIC 10 AND 20 ORDER BY ID",
            "SELECT ID FROM BT WHERE V BETWEEN NULL AND 10 ORDER BY ID",
            "SELECT ID FROM BT WHERE V BETWEEN 10 AND NULL ORDER BY ID",
            "SELECT ID FROM BT WHERE V BETWEEN NULL AND NULL ORDER BY ID",
            "SELECT ID FROM BT WHERE V BETWEEN SYMMETRIC NULL AND 10 ORDER BY ID",
            "SELECT ID FROM BT WHERE NOT (V BETWEEN 10 AND 20) ORDER BY ID",
            "SELECT 5 BETWEEN 10 AND 20, 15 BETWEEN 10 AND 20, 15 NOT BETWEEN 10 AND 20",
            "SELECT NULL BETWEEN 10 AND 20",
            "SELECT 15 BETWEEN NULL AND 20",
            "SELECT 15 BETWEEN SYMMETRIC NULL AND 20",
            "SELECT 15 BETWEEN 10 AND NULL",
            "SELECT 10 BETWEEN 10 AND 10, 10 BETWEEN 10 AND 9, 10 NOT BETWEEN 10 AND 10",
            "SELECT 15 BETWEEN SYMMETRIC 20 AND 10",
            "SELECT CASE WHEN V BETWEEN 10 AND 20 THEN 1 ELSE 0 END FROM BT ORDER BY ID",
        ]);
    }

    [JavaOracleFact]
    public void IsNullOverScalarsAndRowsMatchesJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE NT(ID INT, A INT, B INT)",
            "INSERT INTO NT VALUES (1, 1, 2), (2, NULL, 2), (3, NULL, NULL), (4, 1, NULL)",
            "CREATE INDEX NT_A ON NT(A)",
            "SELECT ID FROM NT WHERE A IS NULL ORDER BY ID",
            "SELECT ID FROM NT WHERE A IS NOT NULL ORDER BY ID",
            "SELECT ID FROM NT WHERE NOT (A IS NULL) ORDER BY ID",
            "SELECT ROW(1, NULL) IS NULL, ROW(1, 2) IS NULL, ROW(1, NULL) IS NOT NULL",
            "SELECT ROW(NULL, NULL) IS NULL, ROW(NULL, NULL) IS NOT NULL",
            "SELECT ROW(1, 2) IS NOT NULL, ROW(1, NULL) IS NOT NULL",
            "SELECT (1, NULL) IS NULL, (1, 2) IS NULL",
            "SELECT (A, B) IS NULL, (A, B) IS NOT NULL FROM NT ORDER BY ID",
            "SELECT ID FROM NT WHERE (A, B) IS NULL ORDER BY ID",
            "SELECT ID FROM NT WHERE (A, B) IS NOT NULL ORDER BY ID",
            "SELECT NULL IS NULL, NULL IS NOT NULL, 1 IS NULL, 1 IS NOT NULL",
            "SELECT (SELECT NULL) IS NULL",
            "SELECT CASE WHEN A IS NULL THEN 1 ELSE 0 END FROM NT ORDER BY ID",
        ]);
    }

    [JavaOracleFact]
    public void IsJsonMatchesJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "SELECT JSON '{\"a\": 1}' IS JSON, JSON '[1, 2]' IS JSON ARRAY, "
                + "JSON '{\"a\": 1}' IS JSON OBJECT, JSON '1' IS JSON SCALAR",
            "SELECT 'not json' IS JSON, NULL IS JSON, 1 IS JSON, 1 IS NOT JSON",
            "SELECT JSON '{\"a\": 1, \"a\": 2}' IS JSON WITH UNIQUE KEYS, "
                + "JSON '{\"a\": 1}' IS JSON WITH UNIQUE KEYS",
            "SELECT JSON '[1]' IS JSON WITH UNIQUE, JSON '1' IS JSON WITH UNIQUE",
            "SELECT JSON '{\"a\": 1}' IS NOT JSON, JSON '{\"a\": 1}' IS NOT JSON OBJECT",
            "SELECT NOT (JSON '{}' IS NOT JSON)",
            "CREATE TABLE JT(ID INT, S VARCHAR, J JSON)",
            "INSERT INTO JT VALUES (1, '{\"a\": 1}', JSON '{\"a\": 1}'), "
                + "(2, '[1, 2]', JSON '[1, 2]'), (3, 'x', JSON '1'), (4, NULL, NULL), "
                + "(5, '{}', JSON '{}')",
            "SELECT ID, S IS JSON, S IS JSON ARRAY, S IS JSON OBJECT, S IS JSON SCALAR "
                + "FROM JT ORDER BY ID",
            "SELECT ID, J IS JSON, J IS JSON ARRAY, J IS JSON OBJECT, J IS JSON SCALAR "
                + "FROM JT ORDER BY ID",
            "SELECT ID, S IS JSON WITH UNIQUE KEYS, J IS JSON WITH UNIQUE KEYS FROM JT ORDER BY ID",
            "SELECT ID, S IS NOT JSON, S IS NOT JSON ARRAY FROM JT ORDER BY ID",
            "SELECT ID, CASE WHEN S IS JSON THEN 1 ELSE 0 END FROM JT ORDER BY ID",
        ]);
    }

    [JavaOracleFact]
    public void ThePredicatePlansMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE BT(ID INT, V INT)",
            "CREATE INDEX BT_V ON BT(V)",
            "INSERT INTO BT VALUES (1, 10), (2, 20), (3, NULL)",
            "EXPLAIN SELECT ID FROM BT WHERE V BETWEEN 10 AND 20",
            "EXPLAIN SELECT ID FROM BT WHERE V NOT BETWEEN 10 AND 20",
            "EXPLAIN SELECT ID FROM BT WHERE V BETWEEN SYMMETRIC 10 AND 20",
            "CREATE TABLE NT(ID INT, A INT, B INT)",
            "CREATE INDEX NT_A ON NT(A)",
            "EXPLAIN SELECT ID FROM NT WHERE A IS NULL",
            "EXPLAIN SELECT ID FROM NT WHERE A IS NOT NULL",
            "EXPLAIN SELECT ID FROM NT WHERE (A, B) IS NULL",
            "CREATE TABLE JT(ID INT, S VARCHAR, J JSON)",
            "EXPLAIN SELECT ID FROM JT WHERE S IS JSON AND J IS JSON",
            "EXPLAIN SELECT ID FROM JT WHERE S IS NOT JSON ARRAY",
        ]);
    }

    [JavaOracleFact]
    public void DbObjectFunctionsMatchJdbc()
    {
        // The CREATE SQL of a table spans lines, so the comparison escapes them.
        ExplainOracleTests.Check([
            "CREATE TABLE DBT(ID INT, V INT, CONSTRAINT UQ_DBT UNIQUE(V))",
            "CREATE INDEX DBT_V ON DBT(V)",
            "CREATE SEQUENCE DBT_SEQ",
            // The numeric ids are not compared: the reference's first connection allocates a
            // user id, and users are not ported (D-004), so its sequence is offset by one.
            "SELECT CASE WHEN DB_OBJECT_ID('TABLE', 'PUBLIC', 'DBT') IS NOT NULL THEN 1 ELSE 0 END, "
                + "DB_OBJECT_SQL('TABLE', 'PUBLIC', 'DBT')",
            "SELECT CASE WHEN DB_OBJECT_ID('CONSTRAINT', 'PUBLIC', 'UQ_DBT') IS NOT NULL THEN 1 ELSE 0 END, "
                + "DB_OBJECT_SQL('CONSTRAINT', 'PUBLIC', 'UQ_DBT')",
            "SELECT CASE WHEN DB_OBJECT_ID('INDEX', 'PUBLIC', 'DBT_V') IS NOT NULL THEN 1 ELSE 0 END, "
                + "DB_OBJECT_SQL('INDEX', 'PUBLIC', 'DBT_V')",
            "SELECT CASE WHEN DB_OBJECT_ID('SEQUENCE', 'PUBLIC', 'DBT_SEQ') IS NOT NULL THEN 1 ELSE 0 END, "
                + "DB_OBJECT_SQL('SEQUENCE', 'PUBLIC', 'DBT_SEQ')",
            "SELECT CASE WHEN DB_OBJECT_ID('SCHEMA', 'PUBLIC') IS NOT NULL THEN 1 ELSE 0 END, "
                + "CASE WHEN DB_OBJECT_SQL('SCHEMA', 'PUBLIC') IS NOT NULL THEN 1 ELSE 0 END",
            "SELECT CASE WHEN DB_OBJECT_ID('SETTING', 'CREATE_BUILD') IS NOT NULL THEN 1 ELSE 0 END, "
                + "CASE WHEN DB_OBJECT_SQL('SETTING', 'CREATE_BUILD') IS NOT NULL THEN 1 ELSE 0 END",
            "SELECT DB_OBJECT_ID('TABLE', 'PUBLIC', 'NOSUCH'), DB_OBJECT_SQL('TABLE', 'NOSUCH', 'DBT')",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SETTINGS WHERE SETTING_NAME = 'CREATE_BUILD'",
            "SELECT DB_OBJECT_ID('NOSUCH', 'PUBLIC', 'DBT')",
            "SELECT CASE WHEN DB_OBJECT_SIZE('TABLE', 'PUBLIC', 'DBT') >= 0 THEN 1 ELSE 0 END, "
                + "CASE WHEN DB_OBJECT_TOTAL_SIZE('TABLE', 'PUBLIC', 'DBT') >= 0 THEN 1 ELSE 0 END",
            "SELECT CASE WHEN DB_OBJECT_APPROXIMATE_SIZE('TABLE', 'PUBLIC', 'DBT') >= 0 THEN 1 ELSE 0 END, "
                + "CASE WHEN DB_OBJECT_APPROXIMATE_TOTAL_SIZE('TABLE', 'PUBLIC', 'DBT') >= 0 THEN 1 ELSE 0 END",
            "SELECT CASE WHEN DB_OBJECT_SIZE('INDEX', 'PUBLIC', 'DBT_V') >= 0 THEN 1 ELSE 0 END",
            "SELECT CASE WHEN DB_OBJECT_SIZE('SEQUENCE', 'PUBLIC', 'DBT_SEQ') >= 0 THEN 1 ELSE 0 END",
        ]);
    }
}
