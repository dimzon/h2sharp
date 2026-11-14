/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * SCRIPT SCHEMA/TABLE filters and CHARSET, checked against the reference running the same
 * statements. Only the lines that name the created objects are compared: the port does not
 * emit settings and users (D-004 / P-008), which the reference does.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ScriptOptionsOracleTests
{
    [JavaOracleFact]
    public void TheSchemaAndTableFiltersSelectTheSameTablesAsTheReference()
    {
        string[] schemaScript =
        [
            "CREATE SCHEMA S1",
            "CREATE SCHEMA S2",
            "CREATE TABLE S1.T1(ID INT)",
            "CREATE TABLE S2.T2(ID INT)",
            "CREATE TABLE P(ID INT)",
            "SCRIPT NODATA SCHEMA S1",
        ];
        AssertSameTables(schemaScript);

        string[] tableScript =
        [
            "CREATE SCHEMA S1",
            "CREATE SCHEMA S2",
            "CREATE TABLE S1.T1(ID INT)",
            "CREATE TABLE S2.T2(ID INT)",
            "CREATE TABLE P(ID INT)",
            "SCRIPT NODATA TABLE S2.T2",
        ];
        AssertSameTables(tableScript);
    }

    private static void AssertSameTables(string[] statements)
    {
        List<string> port = Relevant(RunPort(statements));
        List<string> java = Relevant(RunJava(statements));
        Assert.True(java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    [JavaOracleFact]
    public void TheCharsetEncodesTheFileAsTheReferenceDoes()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "script_charset_oracle_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string portFile = Path.Combine(directory, "port.sql");
        string javaFile = Path.Combine(directory, "java.sql");
        string[] common = ["CREATE TABLE T1(ID INT)"];
        try
        {
            _ = RunPort([.. common, "SCRIPT NODATA TO '" + portFile.Replace("\\", "/") + "' CHARSET 'UTF-16LE'"]);
            _ = RunJava([.. common, "SCRIPT NODATA TO '" + javaFile.Replace("\\", "/") + "' CHARSET 'UTF-16LE'"]);

            UnicodeEncoding utf16Le = new(bigEndian: false, byteOrderMark: false);
            List<string> port = Relevant(File.ReadAllLines(portFile, utf16Le));
            List<string> java = Relevant(File.ReadAllLines(javaFile, utf16Le));
            Assert.Equal(java, port);
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    /// <summary>
    /// The lines that create the tables; the schema lines differ by the fixed "sa" principal
    /// (D-004), so the selected tables are what is compared.
    /// </summary>
    /// <param name="lines">the script lines</param>
    /// <returns>the relevant lines</returns>
    private static List<string> Relevant(IEnumerable<string> lines)
        => lines.Where(line => line.Contains("CREATE MEMORY TABLE", StringComparison.Ordinal))
            .ToList();

    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("script_options_oracle")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        List<string> lines = [];
        foreach (string sql in statements)
        {
            try
            {
                CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
                if (container.IsQuery())
                {
                    ResultInterface result = container.Query(-1);
                    while (result.Next())
                    {
                        // The reference's output is read line by line; a multi-line statement
                        // comes back as several lines there, so the row is split the same way.
                        string? value = result.CurrentRow()![0]?.GetString();
                        if (value is null)
                        {
                            lines.Add("-");
                        }
                        else
                        {
                            lines.AddRange(value.Split('\n').Select(l => l.TrimEnd('\r')));
                        }
                    }
                }
                else
                {
                    _ = container.ExecuteUpdate(false);
                }
            }
            catch (Exception e)
            {
                lines.Add(e is H2SharpException h2
                    ? "=" + h2.H2ErrorCode.ToString(CultureInfo.InvariantCulture)
                    : "=" + e.GetType().Name);
            }
        }

        return lines;
    }

    private static List<string> RunJava(IReadOnlyList<string> statements)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:script_options_oracle");
            java.sql.Statement stat = conn.createStatement();
            for (int i = 0; i < inputs.length; i++) {
              try {
                boolean isQuery = stat.execute(inputs[i]);
                if (isQuery) {
                  java.sql.ResultSet rs = stat.getResultSet();
                  while (rs.next()) {
                    String s = rs.getString(1);
                    System.out.println(rs.wasNull() ? "-" : s);
                  }
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}
