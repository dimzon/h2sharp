/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 289: LIKE/ILIKE/REGEXP forms compared with the reference, including explicit escape
 * characters and the wildcard shapes that drive the matcher's index and escape paths.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class LikeSurfaceOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void LikeFormsMatchTheReference()
    {
        AssertSameAsReference("like_surface_forms",
            "SELECT 'abc' LIKE 'a%'",
            "SELECT 'abc' LIKE 'a_c'",
            "SELECT 'a_c' LIKE 'a\\_c' ESCAPE '\\'",
            "SELECT 'a%' LIKE 'a\\%%' ESCAPE '\\'",
            "SELECT 'abc' ILIKE 'A%'",
            "SELECT 'abc' NOT LIKE 'z%'",
            "SELECT 'abc' REGEXP '^a.c$'",
            "SELECT 'abc' REGEXP 'b'",
            "SELECT 'ABC' REGEXP '(?i)^abc$'");
    }
}
