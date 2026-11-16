/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * CSVWRITE against the reference: the reference and the port write different files, and
 * both the answer and the file contents are compared. The port runs its query on the
 * calling session through H2Database.CurrentSessionConnection.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using H2Sharp.CsvTools;
using H2Sharp.Data;
using H2Sharp.Data.Extensions;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class CsvWriteOracleTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "h2sharp_csvwrite_oracle_" + Guid.NewGuid().ToString("N"));

    public CsvWriteOracleTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [JavaOracleFact]
    public void TheBasicWriteMatches()
    {
        AssertSameFile("basic",
            "CREATE TABLE T(ID INT, NAME VARCHAR(10))",
            "INSERT INTO T VALUES (1, 'one'), (2, NULL)",
            "SELECT CSVWRITE('{file}', 'SELECT ID, NAME AS ALIAS FROM T ORDER BY ID')");
    }

    [JavaOracleFact]
    public void TheOptionStringMatches()
    {
        AssertSameFile("options",
            "CREATE TABLE T(ID INT, NAME VARCHAR(10))",
            "INSERT INTO T VALUES (1, 'one'), (2, NULL)",
            "SELECT CSVWRITE('{file}', 'SELECT * FROM T ORDER BY ID', "
            + "'fieldSeparator=; null=NULL')");
    }

    [JavaOracleFact]
    public void ThePositionalArgumentsMatch()
    {
        AssertSameFile("positional",
            "CREATE TABLE T(ID INT, NAME VARCHAR(10))",
            "INSERT INTO T VALUES (1, 'one'), (2, NULL)",
            "SELECT CSVWRITE('{file}', 'SELECT * FROM T ORDER BY ID', "
            + "NULL, ',', '\"', '\"', 'NULL')");
    }

    private void AssertSameFile(string name, params string[] statements)
    {
        string portFile = Path.Combine(directory, name + "-port.csv");
        string javaFile = Path.Combine(directory, name + "-java.csv");
        List<string> port = RunPort(statements, portFile);
        List<string> java = RunJava(statements, javaFile);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
        Assert.Equal(Normalize(javaFile), Normalize(portFile));
    }

    private static List<string> RunPort(string[] statements, string file)
    {
        using H2Connection connection = new(
            $"Data Source=csvwrite_oracle_{Guid.NewGuid():N};Storage=Memory");
        ((IH2Extensions)connection).UseCsvTools();
        connection.Open();
        List<string> lines = [];
        foreach (string template in statements)
        {
            string sql = template.Replace("{file}", Sql(file), StringComparison.Ordinal);
            try
            {
                using DbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                using DbDataReader reader = command.ExecuteReader();
                int columns = reader.FieldCount;
                while (reader.Read())
                {
                    List<string> cells = [];
                    for (int i = 0; i < columns; i++)
                    {
                        object? value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        cells.Add(value switch
                        {
                            null => "-",
                            bool b => b ? "TRUE" : "FALSE",
                            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "-",
                        });
                    }

                    lines.Add("=" + string.Join(":", cells));
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

    private static List<string> RunJava(string[] statements, string file)
    {
        string dbName = "csvwrite_oracle_java_" + Guid.NewGuid().ToString("N");
        string[] inputs = new string[statements.Length];
        for (int i = 0; i < statements.Length; i++)
        {
            inputs[i] = statements[i].Replace("{file}", Sql(file), StringComparison.Ordinal);
        }

        return Oracle.OracleRunner.Run(
            inputs,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:DBNAME");
            java.sql.Statement stat = conn.createStatement();
            for (String s : inputs) {
              try {
                boolean isQuery = stat.execute(s);
                if (isQuery) {
                  java.sql.ResultSet rs = stat.getResultSet();
                  int n = rs.getMetaData().getColumnCount();
                  while (rs.next()) {
                    StringBuilder line = new StringBuilder("=");
                    for (int c = 1; c <= n; c++) {
                      if (c > 1) {
                        line.append(':');
                      }
                      String v = rs.getString(c);
                      line.append(rs.wasNull() ? "-" : v);
                    }
                    System.out.println(line);
                  }
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            conn.close();
            """.Replace("DBNAME", dbName, StringComparison.Ordinal),
            extraJavaArgs: ["-Duser.language=en"]);
    }

    private static string Normalize(string file)
        => File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Sql(string path)
        => path.Replace('\\', '/').Replace("'", "''", StringComparison.Ordinal);
}
