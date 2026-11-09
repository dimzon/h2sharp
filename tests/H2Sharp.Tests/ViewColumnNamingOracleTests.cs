/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Regression for the column names a query gets when it becomes a table or a view:
 * the alias of the select item names the column, not the column under it. The modes
 * decide what a missing alias means — MySQL derives a name from the expression text,
 * Oracle and HyperSQL refuse it.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ViewColumnNamingOracleTests
{
    [JavaOracleFact]
    public void CreateTableAsSelectUsesTheSelectAliases()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE P1 AS SELECT X AS ID, 'M'||X AS NAME FROM SYSTEM_RANGE(1, 2)",
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS "
                + "WHERE TABLE_NAME = 'P1' ORDER BY ORDINAL_POSITION",
            "CREATE TABLE P2 AS SELECT 1 AS A, 'x' AS B",
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS "
                + "WHERE TABLE_NAME = 'P2' ORDER BY ORDINAL_POSITION",
            "CREATE TABLE T(A INT)",
            "CREATE VIEW V AS SELECT A AS B FROM T",
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS "
                + "WHERE TABLE_NAME = 'V' ORDER BY ORDINAL_POSITION",
            "DROP VIEW V",
            "DROP TABLE T",
            "DROP TABLE P2",
            "DROP TABLE P1",
        ]);
    }

    /// <summary>
    /// MySQL names an unaliased column after its expression text; the port fixes the mode
    /// when the database opens (D-029).
    /// </summary>
    [JavaOracleFact]
    public void MySqlNamesAnUnaliasedColumnAfterItsExpression()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE P AS SELECT X + 1 FROM SYSTEM_RANGE(1, 1)",
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'P'",
            "DROP TABLE P",
        ], "MODE=MySQL");
    }

    /// <summary>
    /// Oracle refuses a view column without an alias, with the column-alias error.
    /// </summary>
    [JavaOracleFact]
    public void OracleRefusesAViewColumnWithoutAlias()
    {
        ExplainOracleTests.Check([
            "CREATE VIEW V AS SELECT 1 FROM DUAL",
        ], "MODE=Oracle");
    }

    /// <summary>
    /// HyperSQL refuses a common table expression whose column has no name.
    /// </summary>
    [JavaOracleFact]
    public void HyperSqlRefusesACteColumnWithoutAlias()
    {
        ExplainOracleTests.Check([
            "WITH C AS (SELECT 1 FROM SYSTEM_RANGE(1, 1)) SELECT * FROM C",
        ], "MODE=HSQLDB");
    }
}
