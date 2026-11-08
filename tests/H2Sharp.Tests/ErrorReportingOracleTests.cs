/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The reference's dml/error_reporting.sql: malformed literals and predicates, checked
 * against the reference running the same text. The pointer the error carries is a H2
 * feature of the error message; here only the code is compared, the way the rest of the
 * suite compares refusal codes.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ErrorReportingOracleTests
{
    [JavaOracleFact]
    public void ErrorReportingFormsAnswerTheReference()
    {
        FreshIssuesOracleTests.Compare([
            "SELECT 0x",
            "SELECT 0xZ",
            "SELECT 0xAAZ",
            "SELECT 0x1LZ",
            "SELECT 0x1234567890abZ",
            "SELECT 0x1234567890abLZ",
            "CREATE TABLE test (id INT NOT NULL, name VARCHAR)",
            "select * from test where id = ARRAY [1, 2]",
            "insert into test values (1, 't')",
            "select * from test where id = (1, 2)",
            "drop table test",
        ]);
    }
}
