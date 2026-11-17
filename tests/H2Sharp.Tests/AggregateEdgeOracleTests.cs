/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 250: aggregate edges that the reference scripts do not reach — MODE() ordered with
 * ties in both directions, and JSON_OBJECTAGG with a NULL key, which the reference refuses.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class AggregateEdgeOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void ModeWithTiesMatchesTheReference()
    {
        AssertSameAsReference("aggregate_mode_ties",
            "SELECT MODE() WITHIN GROUP (ORDER BY V) FROM (VALUES (1), (1), (2), (2)) T(V)",
            "SELECT MODE() WITHIN GROUP (ORDER BY V DESC) FROM (VALUES (1), (1), (2), (2)) T(V)",
            "SELECT MODE() WITHIN GROUP (ORDER BY V) FROM (VALUES (1), (2), (2), (3), (3)) T(V)",
            "SELECT MODE() WITHIN GROUP (ORDER BY V DESC) FROM (VALUES (1), (1), (2), (3), (3)) T(V)",
            "SELECT MODE() WITHIN GROUP (ORDER BY V) FROM (VALUES (1), (2), (3)) T(V)",
            "SELECT MODE() WITHIN GROUP (ORDER BY V) FROM (VALUES (NULL), (NULL), (1)) T(V)");
    }

    [JavaOracleFact]
    public void JsonObjectAggRefusesANullKey()
    {
        AssertSameAsReference("aggregate_json_null_key",
            "SELECT JSON_OBJECTAGG(K: V) FROM (VALUES (NULL, 1)) T(K, V)",
            "SELECT CAST(JSON_OBJECTAGG(K: V) AS VARCHAR) FROM (VALUES ('a', 1), ('b', 2)) T(K, V)",
            "SELECT CAST(JSON_OBJECTAGG(K: V ABSENT ON NULL) AS VARCHAR) "
            + "FROM (VALUES ('a', NULL)) T(K, V)");
    }
}
