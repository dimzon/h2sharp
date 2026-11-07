/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * SET forms from the coverage data (uncovered.py): the table type words, the transaction
 * modes through both spellings and every isolation level, the compare modes, the refusals
 * (LOG, an unknown setting name, a boolean that is not one), the connection-string setting
 * that becomes a no-operation, and the expression where an identifier may stand.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class SetFormsOracleTests
{
    [JavaOracleFact]
    public void SetFormsAnswerTheReference()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE T(ID INT PRIMARY KEY, V INT)",
            // The default table type: both words and the numeric form.
            "SET DEFAULT_TABLE_TYPE MEMORY",
            "SET DEFAULT_TABLE_TYPE CACHED",
            "SET DEFAULT_TABLE_TYPE 0",
            // The transaction modes: the bare spelling and the session one, every level.
            "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED",
            "SET TRANSACTION ISOLATION LEVEL READ COMMITTED",
            "SET TRANSACTION ISOLATION LEVEL REPEATABLE READ",
            "SET TRANSACTION ISOLATION LEVEL SNAPSHOT",
            "SET TRANSACTION ISOLATION LEVEL SERIALIZABLE",
            "SET SESSION CHARACTERISTICS AS TRANSACTION ISOLATION LEVEL SERIALIZABLE",
            // The compare modes.
            "SET COMPARE SECONDARY",
            "SET COMPARE TERTIARY",
            "SET COMPARE IDENTICAL",
            // The refusals and the no-operation.
            "SET LOG 1",
            "SET DB_CLOSE_DELAY -1",
            "SET FOOBAR 1",
            "SET IGNORE_CATALOGS 5",
            "SET IGNORE_CATALOGS NOT_A_BOOLEAN",
            // Where an identifier may stand, an expression may too.
            "SET SCHEMA (SELECT 'MAIN' FROM SYSTEM_RANGE(1, 1))",
            "SET DEFAULT_NULL_ORDERING HIGH",
            "DROP TABLE T",
        ]);
    }
}
