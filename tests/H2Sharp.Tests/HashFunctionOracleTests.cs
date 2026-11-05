/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * HASH against the reference JVM: the digest bytes for the SHA-2/MD5/SHA-1 names the
 * core factory registers, string and binary inputs, iteration counts, and the refusals
 * for a non-positive iteration count and an unknown algorithm.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class HashFunctionOracleTests
{
    [JavaOracleFact]
    public void HashDigestsAndRefusalsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "SELECT RAWTOHEX(HASH('SHA256', STRINGTOUTF8('Password'), 1000))",
            "SELECT RAWTOHEX(HASH('SHA256', STRINGTOUTF8('One'), 5))",
            "SELECT RAWTOHEX(HASH('MD5', STRINGTOUTF8('Password'), 1))",
            "SELECT RAWTOHEX(HASH('SHA-1', 'abc', 1))",
            "SELECT RAWTOHEX(HASH('SHA-512', X'0102', 2))",
            // A NULL input stays NULL, as in the reference.
            "SELECT HASH('SHA-256', NULL, 1)",
            // Refusals.
            "SELECT HASH('SHA-256', STRINGTOUTF8('x'), 0)",
            "SELECT HASH('SHA-256', STRINGTOUTF8('x'), -1)",
            "SELECT HASH('NOT-AN-ALGORITHM', STRINGTOUTF8('x'), 1)",
        ]);
    }
}
