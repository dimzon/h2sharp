/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 238: the reference's ScriptReader splits a script the way the SQL tokenizer does — a
 * semicolon inside a string, identifier, comment or dollar-quoted literal is not a separator.
 */
using System;
using System.IO;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ScriptReaderTests
{
    private static string[] Split(string script, bool skipRemarks = false)
    {
        using StringReader reader = new(script);
        using ScriptReader scriptReader = new(reader);
        scriptReader.SetSkipRemarks(skipRemarks);
        List<string> statements = [];
        while (scriptReader.ReadStatement() is string statement)
        {
            statements.Add(statement.Trim());
        }

        return [.. statements];
    }

    [Fact]
    public void SplitsOnSemicolonsOutsideQuotesAndComments()
    {
        string[] statements = Split("""
            CREATE TABLE T(A INT); -- a ; in a comment
            INSERT INTO T VALUES ('a;b');
            INSERT INTO T VALUES ("a;b"); /* nested /* ; */ comment */
            SELECT $$a;b$$;
            """);
        Assert.Equal(4, statements.Length);
        Assert.StartsWith("CREATE TABLE T(A INT)", statements[0]);
        Assert.Contains("'a;b'", statements[1]);
        Assert.Contains("\"a;b\"", statements[2]);
        Assert.Contains("$$a;b$$", statements[3]);
    }

    [Fact]
    public void KeepsTheFinalStatementWithoutATrailingSemicolon()
    {
        Assert.Equal(["SELECT 1", "SELECT 2"], Split("SELECT 1;\nSELECT 2"));
    }

    [Fact]
    public void SkippedRemarksBecomeSpaces()
    {
        string[] statements = Split("SELECT /* remark */ 1;", skipRemarks: true);
        Assert.Single(statements);
        Assert.DoesNotContain("remark", statements[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnfinishedBlockRemarkIsReported()
    {
        using StringReader reader = new("SELECT 1 /* unfinished");
        using ScriptReader scriptReader = new(reader);
        _ = scriptReader.ReadStatement();
        Assert.True(scriptReader.IsInsideRemark());
        Assert.True(scriptReader.IsBlockRemark());
    }

    [Fact]
    public void ASlashSlashCommentEndsAtTheLineBreak()
    {
        string[] statements = Split("SELECT 1 // ; comment\n+ 2;");
        Assert.Single(statements);
        Assert.Contains("+ 2", statements[0], StringComparison.Ordinal);
    }
}
