/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 247: reference scripts whose part before the first SET MODE agrees with the port.
 * D-029 fixes the mode at open, so the mode-switching tail is out of scope for a prefix.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(ScriptCatalogCollection.Name)]
public sealed class ScriptConformancePrefixTests
{
    /// <summary>The scripts whose prefix reproduces the reference exactly.</summary>
    public static TheoryData<string> Scripts => new()
    {
        "compatibility/add_months.sql",
        "compatibility/group_by.sql",
        "compatibility/strict_and_legacy.sql",
        "datatypes/boolean.sql",
        "datatypes/char.sql",
        "datatypes/numeric.sql",
        "datatypes/smallint.sql",
        "ddl/alterTableAdd.sql",
        "ddl/alterTableAlterColumn.sql",
        "ddl/dropIndex.sql",
        "ddl/truncateTable.sql",
        "dml/delete.sql",
        "dml/insertIgnore.sql",
        "dml/merge.sql",
        "dml/mergeUsing.sql",
        "dml/replace.sql",
        "dml/with.sql",
        "dual.sql",
        "functions/aggregate/json_objectagg.sql",
        "functions/aggregate/listagg.sql",
        "functions/json/json_object.sql",
        "functions/numeric/log.sql",
        "functions/numeric/random-uuid.sql",
        "functions/string/array-to-string.sql",
        "functions/string/hextoraw.sql",
        "functions/string/length.sql",
        "functions/string/locate.sql",
        "functions/string/rawtohex.sql",
        "functions/string/replace.sql",
        "functions/string/translate.sql",
        "functions/system/array-slice.sql",
        "functions/system/csvread.sql",
        "functions/system/identity.sql",
        "functions/system/ifnull.sql",
        "functions/system/last-insert-id.sql",
        "functions/system/link-schema.sql",
        "functions/system/memory-free.sql",
        "functions/timeanddate/date_trunc.sql",
        "functions/timeanddate/extract.sql",
        "information_schema.sql",
        "other/sequence.sql",
        "predicates/in.sql",
        "queries/distinct.sql",
        "testScript.sql",
    };

    /// <summary>Runs one prefix and compares every answer with the reference expectation.</summary>
    /// <param name="relativePath">the script path in the reference tree</param>
    [Theory]
    [MemberData(nameof(Scripts))]
    public void PrefixMatchesTheReference(string relativePath)
    {
        System.Collections.Generic.List<string> failures = ScriptRunner.RunPrefix(relativePath);
        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }
}
