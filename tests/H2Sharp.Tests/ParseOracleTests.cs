/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The parse-only conformance harness.
 *
 * H2 has no unit tests for its parser, so the .sql scripts are the only bulk corpus
 * available. This test drives the reference parser over all of them and pins the result, so
 * that when our own parser lands it has something to be compared against, and so that a
 * change in the reference or in the corpus is loud rather than silent.
 *
 * The baseline is exact rather than a range. Both the reference revision and the splitter
 * are pinned, so any movement means one of those two changed, and both deserve a look
 * instead of being absorbed by a tolerance.
 *
 * Note what the numbers mean: the scripts are sequential and nothing is executed, so a
 * statement that references a table an earlier statement would have created still fails.
 * Such a failure is a *resolution* error and means the statement parsed. That is why the
 * baseline is split into "parsed" and "rejected" rather than pass/fail, and why 42104
 * (TableOrViewNotFoundDatabaseEmpty) is the single largest error group: it is evidence the
 * splitter is no longer mangling statements, not evidence of a broken parser.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using H2Sharp.Api;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ParseOracleTests
{
    /// <summary>One line of the oracle's output.</summary>
    private readonly record struct Result(string Source, bool Parsed, string Type, string Detail);

    /// <summary>
    /// Statements, scripts and how many statements the reference accepts. Pinned exactly:
    /// see the note on the class about why there is no tolerance here.
    /// </summary>
    private const int ExpectedStatements = 9678;

    private const int ExpectedParsed = 5628;

    [JavaOracleFact]
    public void ReferenceParser_AcceptsTheScriptCorpus()
    {
        IReadOnlyList<Result> results = Run();

        Assert.Equal(ExpectedStatements, results.Count);

        int parsed = results.Count(static r => r.Parsed);
        Assert.Equal(ExpectedParsed, parsed);

        // Every line must be well formed. A short line here means the oracle changed its
        // output shape, which would silently weaken every later comparison.
        Assert.DoesNotContain(results, static r => r.Type.Length == 0);
        Assert.DoesNotContain(results, static r => r.Source.Length == 0);

        // The error groups are resolution failures far more often than syntax errors, and
        // that ratio is itself a signal: if the splitter regressed and started feeding
        // directive lines to the parser, the syntax error count would jump.
        int syntaxErrors = results.Count(static r =>
            !r.Parsed && r.Type == ErrorCode.SyntaxError2.ToString(CultureInfo.InvariantCulture));

        Assert.Equal(156, syntaxErrors);
    }

    [JavaOracleFact]
    public void ResolutionFailuresMeanTheStatementParsed()
    {
        IReadOnlyList<Result> results = Run();

        // A statement against a table no script has created fails with 42104 rather than a
        // syntax error. That is the documented behaviour of a parse-only run and the reason
        // "did the parser accept it" cannot be read off "did it succeed".
        IReadOnlyList<Result> unresolved = results
            .Where(static r => !r.Parsed
                && r.Type == ErrorCode.TableOrViewNotFoundDatabaseEmpty1.ToString(
                    CultureInfo.InvariantCulture))
            .ToList();

        Assert.NotEmpty(unresolved);

        // Each of those must carry the reference's own message, not an empty detail.
        Assert.All(unresolved, static r => Assert.NotEqual(string.Empty, r.Detail));
    }

    [JavaOracleFact]
    public void StatementCorpus_CoversRealSyntax()
    {
        IReadOnlyList<Result> results = Run();

        // Guard against the corpus collapsing to something trivial: many distinct scripts,
        // many distinct error codes, and both outcomes represented. Without this, a broken
        // path that returns an empty corpus would still satisfy the count assertions above
        // only by coincidence, and would be obvious only later.
        Assert.True(
            results.Select(static r => r.Source).Distinct().Count() > 250,
            "expected the corpus to span most of the reference scripts");

        Assert.True(
            results.Select(static r => r.Type).Distinct().Count() > 20,
            "expected a variety of parse outcomes, not a single uniform result");

        Assert.Contains(results, static r => r.Parsed);
        Assert.Contains(results, static r => !r.Parsed);
    }

    private static IReadOnlyList<Result> Run()
    {
        if (JavaOracle.ReferenceRoot is null || JavaOracle.H2Jar is null)
        {
            throw new InvalidOperationException("reference H2 is not available");
        }

        // ReferenceRoot is the repository root, which is how the reference checkout is
        // located in the first place, so both the scripts and the oracle source are found
        // relative to it.
        string scripts = System.IO.Path.Combine(
            JavaOracle.ReferenceRoot,
            "reference", "h2", "h2", "src", "test", "org", "h2", "test", "scripts");
        string oracle = System.IO.Path.Combine(JavaOracle.ReferenceRoot, "tools", "ParseOracle.java");

        List<string> lines = JavaOracle.OutputLines(
            JavaOracle.RunWithInputs(
                System.IO.File.ReadAllText(oracle),
                "ParseOracle",
                [],
                [JavaOracle.H2Jar],
                extraJavaArgs: null,
                numbers: null,
                programArgs: [scripts]));

        List<Result> results = new(lines.Count);
        foreach (string line in lines)
        {
            string[] parts = line.Split('\t');
            Assert.Equal(4, parts.Length);

            // The third field is the command class when the statement parsed and the
            // numeric error code when it did not; both identify what the parser did, which
            // is what the port has to reproduce.
            results.Add(new Result(parts[0], parts[1] == "OK", parts[2], parts[3]));
        }

        return results;
    }
}
