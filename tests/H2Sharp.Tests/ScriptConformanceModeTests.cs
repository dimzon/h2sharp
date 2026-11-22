/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 240: reference scripts that use a compatibility mode but otherwise agree with the
 * port. D-029 fixes the mode at open, so ScriptRunner.RunInMode drops the SET MODE lines and
 * selects the mode through the connection info instead.
 */
using System;
using Xunit;

namespace H2Sharp.Tests;

[Collection(ScriptCatalogCollection.Name)]
public sealed class ScriptConformanceModeTests
{
    /// <summary>The scripts that reproduce the reference exactly with the mode chosen at open.</summary>
    public static TheoryData<string, string> Scripts => new()
    {
        { "compatibility/add_months.sql", "Oracle" },
        { "datatypes/boolean.sql", "MySQL" },
        { "dml/with.sql", "MySQL" },
        { "dual.sql", "DB2" },
        { "functions/aggregate/json_objectagg.sql", "MySQL" },
        { "functions/aggregate/listagg.sql", "Regular" },
        { "functions/json/json_object.sql", "MySQL" },
        { "functions/string/array-to-string.sql", "PostgreSQL" },
        { "functions/string/replace.sql", "Oracle" },
        { "functions/system/last-insert-id.sql", "MySQL" },
        { "functions/timeanddate/extract.sql", "PostgreSQL" },
    };

    /// <summary>Runs one script and compares every answer with the reference's expectation.</summary>
    /// <param name="relativePath">the script path in the reference tree</param>
    /// <param name="mode">the mode to open the database in</param>
    [ReferenceScriptsTheory]
    [MemberData(nameof(Scripts))]
    public void ScriptMatchesTheReference(string relativePath, string mode)
    {
        System.Collections.Generic.List<string> failures = ScriptRunner.RunInMode(relativePath, mode);
        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }
}
