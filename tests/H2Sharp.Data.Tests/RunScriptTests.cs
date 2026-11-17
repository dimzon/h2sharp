/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 238: RUNSCRIPT reads and executes the statements of a file, including comments and
 * semicolons in literals, and answers the number of statements it ran.
 */
using System;
using System.Data.Common;
using System.IO;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class RunScriptTests : IDisposable
{
    private readonly string file =
        Path.Combine(Path.GetTempPath(), "h2sharp_runscript_" + Guid.NewGuid().ToString("N") + ".sql");

    public void Dispose()
    {
        if (File.Exists(file))
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void RunsEveryStatementOfTheFile()
    {
        File.WriteAllText(file, """
            CREATE TABLE T(ID INT, V VARCHAR(20)); -- a ; in a comment
            INSERT INTO T VALUES (1, 'a;b');
            INSERT INTO T VALUES (2, 'c;d');
            """);

        using H2Connection connection = new("Data Source=runscript;Storage=Memory");
        connection.Open();

        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "RUNSCRIPT FROM '" + file.Replace("'", "''", StringComparison.Ordinal) + "'";
            Assert.Equal(3, command.ExecuteNonQuery());
        }

        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT V FROM T WHERE ID = 1";
            Assert.Equal("a;b", command.ExecuteScalar());
        }

        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM T";
            Assert.Equal(2L, Convert.ToInt64(command.ExecuteScalar()));
        }
    }

    [Fact]
    public void CharsetAndQuirksOptionsAreAccepted()
    {
        File.WriteAllText(file, "CREATE TABLE T(A INT);\n");
        using H2Connection connection = new("Data Source=runscript_opts;Storage=Memory");
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "RUNSCRIPT FROM '" + file.Replace("'", "''", StringComparison.Ordinal)
            + "' CHARSET 'UTF-8' QUIRKS_MODE VARIABLE_BINARY";
        // The reference also counts the trailing newline as a (no-operation) statement.
        Assert.Equal(2, command.ExecuteNonQuery());
    }
}
