/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 125: a runner for the reference's own script tests. The scripts under
 * reference/h2/.../test/scripts carry the reference's expected outputs: `> ok` for an update,
 * `> update count: N`, `> exception CODE`, `>> value` for a single value, and a result-set
 * block (`> header`, `> dashes`, rows, `> rows[ (ordered)]: N`). The runner parses that
 * format, runs the statements on a fresh in-memory database, and compares the answer with
 * the reference's expectation — so a script that passes is a line-by-line oracle check.
 *
 * Statements with `?` placeholders carry a `{ ... }` parameter block; binding them is not
 * ported in the runner yet, so those statements are counted as skipped rather than silently
 * ignored.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using H2Sharp.Api;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ScriptConformanceTests
{
    /// <summary>
    /// The reference scripts the port reproduces exactly today. Each one is a line-by-line
    /// oracle check: the expected outputs come from the reference's own test suite. A script
    /// leaves this list only by being fixed; a script that starts failing fails the test.
    /// </summary>
    public static TheoryData<string> Scripts => new()
    {
        "datatypes/bigint.sql",
        "datatypes/binary.sql",
        "datatypes/blob.sql",
        "datatypes/clob.sql",
        "datatypes/date.sql",
        "datatypes/identity.sql",
        "datatypes/int.sql",
        "datatypes/java_object.sql",
        "datatypes/time-with-time-zone.sql",
        "datatypes/time.sql",
        "datatypes/timestamp-with-time-zone.sql",
        "datatypes/timestamp.sql",
        "datatypes/tinyint.sql",
        "datatypes/uuid.sql",
        "datatypes/varchar.sql",
        "ddl/alterDomain.sql",
        "ddl/alterTableDropConstraint.sql",
        "ddl/alterTableRenameConstraint.sql",
        "ddl/analyze.sql",
        "ddl/createIndex.sql",
        "ddl/createSequence.sql",
        "ddl/createSynonym.sql",
        "ddl/dropTable.sql",
        "dml/insert.sql",
        "dml/update.sql",
        "functions/aggregate/any_value.sql",
        "functions/aggregate/avg.sql",
        "functions/aggregate/bit_and_agg.sql",
        "functions/aggregate/bit_or_agg.sql",
        "functions/aggregate/corr.sql",
        "functions/aggregate/covar_pop.sql",
        "functions/aggregate/covar_samp.sql",
        "functions/aggregate/every.sql",
        "functions/aggregate/gcd_agg.sql",
        "functions/aggregate/histogram.sql",
        "functions/aggregate/json_arrayagg.sql",
        "functions/aggregate/mode.sql",
        "functions/aggregate/rank.sql",
        "functions/aggregate/regr_avgx.sql",
        "functions/aggregate/regr_avgy.sql",
        "functions/aggregate/regr_count.sql",
        "functions/aggregate/regr_intercept.sql",
        "functions/aggregate/regr_r2.sql",
        "functions/aggregate/regr_slope.sql",
        "functions/aggregate/regr_sxx.sql",
        "functions/aggregate/regr_sxy.sql",
        "functions/aggregate/regr_syy.sql",
        "functions/aggregate/sum.sql",
        "functions/json/json_array.sql",
        "functions/numeric/abs.sql",
        "functions/numeric/acos.sql",
        "functions/numeric/asin.sql",
        "functions/numeric/atan.sql",
        "functions/numeric/atan2.sql",
        "functions/numeric/bitand.sql",
        "functions/numeric/bitcount.sql",
        "functions/numeric/bitget.sql",
        "functions/numeric/bitnot.sql",
        "functions/numeric/bitor.sql",
        "functions/numeric/bitxor.sql",
        "functions/numeric/ceil.sql",
        "functions/numeric/compress.sql",
        "functions/numeric/cos.sql",
        "functions/numeric/cot.sql",
        "functions/numeric/degrees.sql",
        "functions/numeric/exp.sql",
        "functions/numeric/expand.sql",
        "functions/numeric/floor.sql",
        "functions/numeric/gcd.sql",
        "functions/numeric/length.sql",
        "functions/numeric/lshift.sql",
        "functions/numeric/mod.sql",
        "functions/numeric/pi.sql",
        "functions/numeric/power.sql",
        "functions/numeric/radians.sql",
        "functions/numeric/rotate.sql",
        "functions/numeric/round.sql",
        "functions/numeric/roundmagic.sql",
        "functions/numeric/rshift.sql",
        "functions/numeric/secure-rand.sql",
        "functions/numeric/sign.sql",
        "functions/numeric/sin.sql",
        "functions/numeric/sinh.sql",
        "functions/numeric/sqrt.sql",
        "functions/numeric/tan.sql",
        "functions/numeric/tanh.sql",
        "functions/numeric/truncate.sql",
        "functions/numeric/zero.sql",
        "functions/string/ascii.sql",
        "functions/string/bit-length.sql",
        "functions/string/btrim.sql",
        "functions/string/char.sql",
        "functions/string/concat-ws.sql",
        "functions/string/concat.sql",
        "functions/string/difference.sql",
        "functions/string/insert.sql",
        "functions/string/left.sql",
        "functions/string/lower.sql",
        "functions/string/lpad.sql",
        "functions/string/ltrim.sql",
        "functions/string/octet-length.sql",
        "functions/string/quote_ident.sql",
        "functions/string/regexp-like.sql",
        "functions/string/repeat.sql",
        "functions/string/right.sql",
        "functions/string/rpad.sql",
        "functions/string/rtrim.sql",
        "functions/string/soundex.sql",
        "functions/string/space.sql",
        "functions/string/stringdecode.sql",
        "functions/string/stringencode.sql",
        "functions/string/stringtoutf8.sql",
        "functions/string/trim.sql",
        "functions/string/upper.sql",
        "functions/string/utf8tostring.sql",
        "functions/string/xmlattr.sql",
        "functions/string/xmlstartdoc.sql",
        "functions/system/array-cat.sql",
        "functions/system/array-contains.sql",
        "functions/system/array-get.sql",
        "functions/system/autocommit.sql",
        "functions/system/casewhen.sql",
        "functions/system/coalesce.sql",
        "functions/system/convert.sql",
        "functions/system/decode.sql",
        "functions/system/greatest.sql",
        "functions/system/h2version.sql",
        "functions/system/least.sql",
        "functions/system/nullif.sql",
        "functions/system/readonly.sql",
        "functions/system/trim_array.sql",
        "functions/system/truncate-value.sql",
        "functions/timeanddate/current-time.sql",
        "functions/timeanddate/current_date.sql",
        "functions/timeanddate/dateadd.sql",
        "functions/timeanddate/datediff.sql",
        "functions/timeanddate/day-of-month.sql",
        "functions/timeanddate/day-of-week.sql",
        "functions/timeanddate/day-of-year.sql",
        "functions/timeanddate/dayname.sql",
        "functions/timeanddate/hour.sql",
        "functions/timeanddate/last_day.sql",
        "functions/timeanddate/minute.sql",
        "functions/timeanddate/month.sql",
        "functions/timeanddate/monthname.sql",
        "functions/timeanddate/quarter.sql",
        "functions/timeanddate/second.sql",
        "functions/timeanddate/truncate.sql",
        "functions/timeanddate/week.sql",
        "functions/timeanddate/year.sql",
        "functions/window/lead.sql",
        "functions/window/nth_value.sql",
        "functions/window/ntile.sql",
        "functions/window/ratio_to_report.sql",
        "functions/window/row_number.sql",
        "other/boolean-test.sql",
        "other/concatenation.sql",
        "other/field-reference.sql",
        "other/unique_include.sql",
        "parser/comments.sql",
        "predicates/between.sql",
        "predicates/distinct.sql",
        "predicates/like.sql",
        "predicates/quantified-comparison-with-array.sql",
        "predicates/type.sql",
        "predicates/unique.sql",
        "queries/derived-column-names.sql",
        "queries/duplicate-order-by.sql",
        "queries/query-optimisations.sql",
        "queries/values.sql",
        "queries/window.sql",
    };

    [Theory]
    [MemberData(nameof(Scripts))]
    public void ReferenceScriptRunsAndMatches(string relativePath)
    {
        List<string> failures = ScriptRunner.Run(relativePath);
        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }
}

/// <summary>Runs one reference script against a fresh in-memory database and compares the answers.</summary>
internal static class ScriptRunner
{
    private static readonly Lazy<string> DirectoryValue = new(FindScriptsDirectory);

    internal static string ScriptsDirectory => DirectoryValue.Value;

    private static string FindScriptsDirectory()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            string candidate = Path.Combine(directory,
                "reference", "h2", "h2", "src", "test", "org", "h2", "test", "scripts");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        throw new DirectoryNotFoundException("the reference script directory was not found");
    }

    internal static List<string> Run(string relativePath)
    {
        try
        {
            return RunCore(relativePath);
        }
        catch (Exception e)
        {
            return ["script could not run: " + e.GetType().Name + ": " + e.Message];
        }
    }

    /// <summary>
    /// Runs script text that is not a file of the reference, for the runner's own tests: the
    /// format is the same, and a failure means the runner and the engine disagree with it.
    /// </summary>
    internal static List<string> RunLines(string[] lines)
    {
        try
        {
            return RunItems(ScriptParser.Parse(lines));
        }
        catch (Exception e)
        {
            return ["script could not run: " + e.GetType().Name + ": " + e.Message];
        }
    }

    private static List<string> RunCore(string relativePath)
    {
        string path = Path.Combine(ScriptsDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return RunItems(ScriptParser.Parse(File.ReadAllLines(path, Encoding.UTF8)));
    }

    private static List<string> RunItems(List<ScriptItem> items)
    {
        List<string> failures = [];
        Database database = new(ConnectionInfo.Memory("script_" + Guid.NewGuid().ToString("N")));
        SessionLocal session = new(database, "sa");
        try
        {
            foreach (ScriptItem item in items)
            {
                string? failure = Execute(session, item);
                if (failure is not null)
                {
                    failures.Add(failure);
                }
            }
        }
        finally
        {
            database.Close();
        }

        return failures;
    }

    private static string? Execute(SessionLocal session, ScriptItem item)
    {
        if (item.ParameterRuns is not null)
        {
            return ExecuteParameterized(session, item);
        }

        string got;
        try
        {
            CommandInterface command = session.PrepareCommand(item.Sql);
            if (command.IsQuery())
            {
                using ResultInterface result = command.ExecuteQuery(-1, 0, false);
                return CompareResult(item.Line, item.Sql, item.Answer, result);
            }

            long count = command.ExecuteUpdate(false).GetUpdateCount();
            got = count < 1 ? "ok" : "update count: " + count.ToString(CultureInfo.InvariantCulture);
        }
        catch (H2SharpException e)
        {
            got = "exception " + ErrorCodes.NameOf(e.H2ErrorCode);
        }

        return CompareSimple(item.Line, item.Sql, item.Answer, got);
    }

    private static string? CompareSimple(int line, string sql, ExpectedAnswer answer, string got)
        => got == answer.Expectation
            ? null
            : $"line {line}: {sql}\n  expected: {answer.Expectation}\n  got:      {got}";

    /// <summary>
    /// Runs a statement once per line of its parameter block, as the reference's
    /// <c>process</c>/<c>processPrepared</c> do: the statement is prepared once, the values
    /// are bound as strings, queries compare their own answer block, and the update counts of
    /// non-queries are summed and compared with the line after the closing brace.
    /// </summary>
    private static string? ExecuteParameterized(SessionLocal session, ScriptItem item)
    {
        CommandInterface command;
        try
        {
            command = session.PrepareCommand(item.Sql);
        }
        catch (H2SharpException e)
        {
            return CompareSimple(item.Line, item.Sql, item.Answer,
                "exception " + ErrorCodes.NameOf(e.H2ErrorCode));
        }

        if (command is not CommandContainer container)
        {
            return $"line {item.Line}: {item.Sql}\n  the statement is not a runnable command";
        }

        long total = 0;
        foreach (ScriptParameterRun run in item.ParameterRuns!)
        {
            List<string?> values = ParseParameterValues(run.Values);
            List<ParameterInterface> parameters = container.GetParameters();
            if (values.Count > parameters.Count)
            {
                return $"line {item.Line}: {item.Sql}\n  expected {values.Count} parameter value(s), got {parameters.Count}";
            }

            for (int i = 0; i < values.Count; i++)
            {
                parameters[i].SetValue(
                    values[i] is null ? ValueNull.Instance : ValueVarchar.Get(values[i]!, session),
                    closeOld: true);
            }

            try
            {
                if (command.IsQuery())
                {
                    using ResultInterface result = command.ExecuteQuery(-1, 0, false);
                    string? failure = CompareResult(item.Line, item.Sql, run.Answer!, result);
                    if (failure is not null)
                    {
                        return failure;
                    }
                }
                else
                {
                    total += command.ExecuteUpdate(false).GetUpdateCount();
                }
            }
            catch (H2SharpException e)
            {
                string got = "exception " + ErrorCodes.NameOf(e.H2ErrorCode);
                if (run.Answer is null)
                {
                    return $"line {item.Line}: {item.Sql}\n  expected: ok\n  got:      {got}";
                }

                string? failure = CompareSimple(item.Line, item.Sql, run.Answer, got);
                if (failure is not null)
                {
                    return failure;
                }
            }
        }

        // The reference writes the sum of the counts unconditionally here — unlike a plain
        // statement, where zero prints as `ok` — so the script's `> update count: 0` matches.
        string totalGot = "update count: " + total.ToString(CultureInfo.InvariantCulture);
        return CompareSimple(item.Line, item.Sql, item.Answer, totalGot);
    }

    /// <summary>
    /// The values of one parameter line, as the reference's <c>processPrepared</c> reads them:
    /// comma separated, a quoted part keeps its spaces and commas, unquoted whitespace is
    /// dropped, and the word <c>null</c> binds NULL.
    /// </summary>
    private static List<string?> ParseParameterValues(string raw)
    {
        List<string?> values = [];
        StringBuilder buff = new();
        bool quoted = false;
        foreach (char c in raw)
        {
            if (quoted)
            {
                if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    buff.Append(c);
                }
            }
            else if (c == ',')
            {
                values.Add(ToParameterValue(buff.ToString()));
                buff.Clear();
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c > ' ')
            {
                buff.Append(c);
            }
        }

        if (buff.Length > 0)
        {
            values.Add(ToParameterValue(buff.ToString()));
        }

        return values;
    }

    private static string? ToParameterValue(string text)
        => text.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : text;

    private static string? CompareResult(
        int line, string sql, ExpectedAnswer answer, ResultInterface result)
    {
        List<Value?[]> rows = [];
        while (result.Next())
        {
            rows.Add(result.CurrentRow());
        }

        int columns = result.GetVisibleColumnCount();
        if (answer.ExpectedValue is not null)
        {
            string got = rows.Count switch
            {
                0 => "<no result>",
                1 when columns == 1 => FormatValue(rows[0][0]).Trim(),
                1 => "<row with " + columns.ToString(CultureInfo.InvariantCulture) + " values>",
                _ => "<" + rows.Count.ToString(CultureInfo.InvariantCulture) + " rows>",
            };
            return got == answer.ExpectedValue
                ? null
                : $"line {line}: {sql}\n  expected: >> {answer.ExpectedValue}\n  got:      >> {got}";
        }

        string[] header = new string[columns];
        for (int i = 0; i < columns; i++)
        {
            header[i] = CollapseText(result.GetAlias(i));
        }

        List<string[]> formatted = rows
            .Select(row => row.Take(columns).Select(FormatValue).ToArray()).ToList();
        if (answer.ExpectedRows is null)
        {
            // A statement expected to fail answered with rows instead. Say so rather than
            // dereferencing the absent block: the mismatch is the finding.
            return $"line {line}: {sql}\n  expected: {answer.Expectation}\n  got: a result of {rows.Count} row(s)";
        }

        List<string> expected = answer.ExpectedRows;
        if (!answer.Ordered)
        {
            formatted.Sort(CompareRows);
        }

        // The reference compares whole trimmed lines: it writes the formatted row after the
        // `> ` prefix and trims it, so a last cell that ends in a space loses it.
        List<string> actual = FormatRows(header, formatted)
            .Select(static line => line.Trim()).ToList();
        for (int i = 0; i < Math.Min(expected.Count, actual.Count); i++)
        {
            if (expected[i] != actual[i])
            {
                return $"line {line}: {sql}\n  expected row: {expected[i]}\n  got row:      {actual[i]}";
            }
        }

        return expected.Count == actual.Count
            ? null
            : $"line {line}: {sql}\n  expected {expected.Count} lines, got {actual.Count}";
    }

    private static int CompareRows(string[] a, string[] b)
    {
        int length = Math.Min(a.Length, b.Length);
        for (int i = 0; i < length; i++)
        {
            int compare = string.CompareOrdinal(a[i], b[i]);
            if (compare != 0)
            {
                return compare;
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    private static string FormatValue(Value? value)
    {
        if (value is null or ValueNull)
        {
            return "null";
        }

        if (value.GetValueType() is Value.Binary or Value.Varbinary or Value.Blob)
        {
            byte[]? bytes = value.GetBytes();
            if (bytes is not null)
            {
                return "X'" + Convert.ToHexString(bytes).ToLowerInvariant() + "'";
            }
        }

        string? text = value.GetString();
        if (text is null)
        {
            return "null";
        }

        text = CollapseText(text);

        return text;
    }

    /// <summary>
    /// The reference's <c>formatString</c>: line breaks become spaces and a run of spaces
    /// becomes one. The scripts were written through it, so a value with a double space is
    /// expected with a single one.
    /// </summary>
    private static string CollapseText(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\n', ' ');
        while (text.Contains("  ", StringComparison.Ordinal))
        {
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        }

        return text;
    }

    private static List<string> FormatRows(string[] header, List<string[]> rows)
    {
        int[] widths = new int[header.Length];
        for (int i = 0; i < header.Length; i++)
        {
            widths[i] = header[i].Length;
        }

        foreach (string[] row in rows)
        {
            for (int i = 0; i < Math.Min(row.Length, widths.Length); i++)
            {
                widths[i] = Math.Max(widths[i], row[i].Length);
            }
        }

        List<string> lines = [FormatLine(header, widths)];
        lines.Add(FormatLine(widths.Select(width => new string('-', width)).ToArray(), widths));
        foreach (string[] row in rows)
        {
            lines.Add(FormatLine(row, widths));
        }

        return lines;
    }

    private static string FormatLine(string[] cells, int[] widths)
    {
        StringBuilder builder = new();
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(cells[i]);
            if (i < cells.Length - 1)
            {
                builder.Append(' ', widths[i] - cells[i].Length);
            }
        }

        return builder.ToString();
    }
}

/// <summary>One statement of a reference script and the answer it expects.</summary>
internal sealed class ScriptItem
{
    internal required string Sql { get; init; }

    internal required ExpectedAnswer Answer { get; init; }

    internal required int Line { get; init; }

    /// <summary>
    /// The executions of a parameterized statement: one per line of its <c>{ ... }</c> block.
    /// Null when the statement has no parameters.
    /// </summary>
    internal List<ScriptParameterRun>? ParameterRuns { get; init; }
}

/// <summary>One expected answer: a value, an exception, an update count, or a row block.</summary>
internal sealed class ExpectedAnswer
{
    internal required string Expectation { get; init; }

    internal string? ExpectedValue { get; init; }

    internal List<string>? ExpectedRows { get; init; }

    internal bool Ordered { get; init; }
}

/// <summary>One line of a parameter block and the answer the reference records for it.</summary>
internal sealed class ScriptParameterRun
{
    internal required string Values { get; init; }

    /// <summary>
    /// The answer block for this execution. Non-queries have none: the reference only compares
    /// their summed update count against the line after the block.
    /// </summary>
    internal ExpectedAnswer? Answer { get; init; }
}

/// <summary>The reference script format: statements end with <c>;</c>, expectations start with <c>&gt;</c>.</summary>
internal static class ScriptParser
{
    internal static List<ScriptItem> Parse(string[] lines)
    {
        List<ScriptItem> items = [];
        StringBuilder statement = new();
        int index = 0;
        while (index < lines.Length)
        {
            string line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith("--", StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            if (line.StartsWith("/*", StringComparison.Ordinal))
            {
                // A block comment: skip to its end; the scripts put them on their own lines.
                while (index < lines.Length && !lines[index].Contains("*/", StringComparison.Ordinal))
                {
                    index++;
                }

                index++;
                continue;
            }

            if (line.StartsWith("@", StringComparison.Ordinal))
            {
                // Connection management (@reconnect, @autocommit) and script-internal
                // markers; a fresh in-memory database ignores them.
                index++;
                continue;
            }

            if (line.StartsWith(">", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("expectation without a statement at line " + (index + 1));
            }

            statement.Clear();
            int sqlLine = index + 1;
            while (true)
            {
                statement.Append(line);
                index++;
                if (line.EndsWith(";", StringComparison.Ordinal))
                {
                    break;
                }

                if (index >= lines.Length)
                {
                    throw new InvalidOperationException("unterminated statement at line " + sqlLine);
                }

                statement.Append('\n');
                line = lines[index].Trim();
            }

            string sql = statement.ToString();
            sql = sql[..^1].Trim();
            if (HasParameters(sql))
            {
                // The parameter block follows the statement: `{`, then one line of values per
                // execution. A query's expectation lines follow its own value line (the
                // reference's processPrepared consumes them); updates have none — their counts
                // are summed and compared with the line after `}`.
                if (index >= lines.Length || lines[index].Trim() != "{")
                {
                    throw new InvalidOperationException(
                        "expected a parameter block at line " + sqlLine + ": " + sql);
                }

                index++;
                List<ScriptParameterRun> runs = [];
                while (index < lines.Length
                       && !lines[index].TrimStart().StartsWith("}", StringComparison.Ordinal))
                {
                    string values = lines[index].Trim();
                    index++;
                    List<string> runExpectations = [];
                    while (index < lines.Length
                           && lines[index].TrimStart().StartsWith(">", StringComparison.Ordinal))
                    {
                        runExpectations.Add(lines[index].Trim());
                        index++;
                    }

                    runs.Add(new ScriptParameterRun
                    {
                        Values = values,
                        Answer = runExpectations.Count == 0
                            ? null
                            : BuildAnswer(sql, sqlLine, runExpectations),
                    });
                }

                if (index < lines.Length)
                {
                    index++;
                }

                List<string> finalExpectations = [];
                while (index < lines.Length
                       && lines[index].TrimStart().StartsWith(">", StringComparison.Ordinal))
                {
                    finalExpectations.Add(lines[index].Trim());
                    index++;
                }

                items.Add(new ScriptItem
                {
                    Sql = sql,
                    Line = sqlLine,
                    Answer = BuildAnswer(sql, sqlLine, finalExpectations),
                    ParameterRuns = runs,
                });
                continue;
            }

            List<string> expectations = [];
            while (index < lines.Length && lines[index].TrimStart().StartsWith(">", StringComparison.Ordinal))
            {
                expectations.Add(lines[index].Trim());
                index++;
            }

            items.Add(new ScriptItem
            {
                Sql = sql,
                Line = sqlLine,
                Answer = BuildAnswer(sql, sqlLine, expectations),
            });
        }

        return items;
    }

    private static ExpectedAnswer BuildAnswer(string sql, int line, List<string> expectations)
    {
        if (expectations.Count == 0)
        {
            throw new InvalidOperationException("statement without an expectation at line " + line + ": " + sql);
        }

        if (expectations.Count == 1)
        {
            string only = expectations[0];
            if (only.StartsWith(">> ", StringComparison.Ordinal))
            {
                return new ExpectedAnswer
                {
                    Expectation = only,
                    ExpectedValue = only[3..],
                };
            }

            return new ExpectedAnswer { Expectation = WithoutPrefix(only) };
        }

        string last = expectations[^1];
        bool ordered;
        if (last.StartsWith("> rows (ordered): ", StringComparison.Ordinal))
        {
            ordered = true;
        }
        else if (last.StartsWith("> rows: ", StringComparison.Ordinal)
                 || last.StartsWith("> rows (partially ordered): ", StringComparison.Ordinal))
        {
            ordered = false;
        }
        else
        {
            throw new InvalidOperationException("unrecognized expectation block at line " + line + ": " + last);
        }

        List<string> rows = [];
        for (int i = 0; i < expectations.Count - 1; i++)
        {
            rows.Add(WithoutPrefix(expectations[i]));
        }

        return new ExpectedAnswer
        {
            Expectation = WithoutPrefix(last),
            ExpectedRows = rows,
            Ordered = ordered,
        };
    }

    /// <summary>
    /// The text of an expectation line after its <c>&gt;</c> marker. A line that is only
    /// <c>&gt;</c> is an empty header cell — the reference trims the line and compares it
    /// whole, so <c>&gt;</c> against <c>&gt; </c> is a match — and has no second character
    /// to cut from.
    /// </summary>
    private static string WithoutPrefix(string line)
        => line.Length > 1 ? line[1..].TrimStart() : string.Empty;

    /// <summary>
    /// Whether a statement has a bind parameter, as the reference's <c>hasParameters</c>:
    /// <c>??</c> is the script escape for the array brackets <c>[</c> and <c>]</c>, not two
    /// parameters, so a statement that contains only those has no parameter block.
    /// </summary>
    private static bool HasParameters(string sql)
    {
        for (int i = 0; i < sql.Length; i++)
        {
            if (sql[i] != '?')
            {
                continue;
            }

            if (i + 1 < sql.Length && sql[i + 1] == '?')
            {
                i++;
                continue;
            }

            return true;
        }

        return false;
    }
}

/// <summary>
/// The symbolic error-code names the reference's scripts write. They are the field names of
/// the reference's <c>ErrorCode</c> class, so the map is read from its source rather than
/// guessed from the port's C# names.
/// </summary>
internal static class ErrorCodes
{
    private static readonly Lazy<Dictionary<int, string>> Names = new(Load);

    internal static string NameOf(int code)
        => Names.Value.TryGetValue(code, out string? name)
            ? name
            : code.ToString(CultureInfo.InvariantCulture);

    private static Dictionary<int, string> Load()
    {
        Dictionary<int, string> names = [];
        string path = Path.GetFullPath(Path.Combine(ScriptRunner.ScriptsDirectory,
            "..", "..", "..", "..", "..", "main", "org", "h2", "api", "ErrorCode.java"));
        if (!File.Exists(path))
        {
            return names;
        }

        foreach (string line in File.ReadLines(path))
        {
            Match match = Regex.Match(line,
                @"public\s+static\s+final\s+int\s+(\w+)\s*=\s*(\d+)\s*;");
            if (match.Success)
            {
                names[int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)] = match.Groups[1].Value;
            }
        }

        return names;
    }
}
