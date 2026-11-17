/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 238: RUNSCRIPT against a file written by the test itself, compared with the JVM.
 */
using System;
using System.IO;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class RunScriptOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void RunsTheScriptAndKeepsItsRows()
    {
        string file = Path.Combine(
            Path.GetTempPath(), "h2sharp_runscript_oracle_" + Guid.NewGuid().ToString("N") + ".sql");
        try
        {
            File.WriteAllText(file,
                "CREATE TABLE T(ID INT, V VARCHAR(20)); -- a ; in a comment\n"
                + "INSERT INTO T VALUES (1, 'a;b');\n");
            string path = file.Replace("'", "''", StringComparison.Ordinal);
            AssertSameAsReference("runscript_oracle",
                "RUNSCRIPT FROM '" + path + "'",
                "SELECT V FROM T WHERE ID = 1",
                "SELECT COUNT(*) FROM T");
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
