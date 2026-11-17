/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 269: the adapted catalog rerun with different session settings. Every statement must
 * answer or fail with a database exception; output differences are covered by the conformance
 * suites and are not the subject of this robustness sweep.
 */
using System.Collections.Generic;
using Xunit;

namespace H2Sharp.Tests;

[Collection(ScriptCatalogCollection.Name)]
public sealed class ScriptSettingsRobustnessTests
{
    private static readonly Dictionary<string, string[]> Settings = new()
    {
        ["quirks"] = ["SET QUIRKS_MODE TRUE"],
        ["ignorecase"] = ["SET IGNORECASE TRUE"],
        ["nulls-first"] = ["SET DEFAULT_NULL_ORDERING FIRST"],
        ["nulls-last"] = ["SET DEFAULT_NULL_ORDERING LAST"],
        ["truncate"] = ["SET TRUNCATE_LARGE_LENGTH TRUE"],
        ["collation-off"] = ["SET COLLATION OFF"],
    };

    public static TheoryData<string, string> Combos()
    {
        TheoryData<string, string> data = new();
        foreach (string script in ScriptConformanceAdaptedTests.Skips.Keys)
        {
            foreach (string setting in Settings.Keys)
            {
                data.Add(script, setting);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Combos))]
    public void EveryStatementEitherAnswersOrFailsWithADatabaseException(string script, string setting)
    {
        _ = ScriptRunner.RunAdaptedWithSettings(
            script, ScriptConformanceAdaptedTests.Skips[script], Settings[setting]);
    }
}
