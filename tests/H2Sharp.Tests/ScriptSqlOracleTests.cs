/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The reference's dml/script.sql, statement by statement.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ScriptSqlOracleTests
{
    [JavaOracleFact]
    public void ScriptSqlAnswersTheReference()
    {
        string[] statements =
        [
            "create memory table test(id int primary key, name varchar(255));",
            "INSERT INTO TEST VALUES(2, STRINGDECODE('abcsond\\344rzeich\\344 ') || char(22222) || STRINGDECODE(' \\366\\344\\374\\326\\304\\334\\351\\350\\340\\361!'));",
            "SCRIPT NOPASSWORDS NOSETTINGS NOVERSION;",
            "SCRIPT COLUMNS NOPASSWORDS NOSETTINGS NOVERSION;",
            "DROP TABLE TEST;",
            "CREATE MEMORY TABLE TEST(ID BIGINT GENERATED ALWAYS AS IDENTITY, V INT, G INT GENERATED ALWAYS AS (V + 1));",
            "INSERT INTO TEST(V) VALUES 5;",
            "SCRIPT NOPASSWORDS NOSETTINGS NOVERSION;",
            "DROP TABLE TEST;",
            "CREATE DOMAIN C AS INT;",
            "CREATE DOMAIN B AS C;",
            "CREATE DOMAIN A AS B;",
            "SCRIPT NOPASSWORDS NOSETTINGS NOVERSION;",
            "DROP DOMAIN A;",
            "DROP DOMAIN B;",
            "DROP DOMAIN C;",
            "CREATE DOMAIN A AS INT;",
            "CREATE DOMAIN B AS A;",
            "CREATE DOMAIN X AS INT;",
            "CREATE DOMAIN Y AS X;",
            "CREATE DOMAIN Z AS Y;",
            "SCRIPT NOPASSWORDS NOSETTINGS NOVERSION;",
            "DROP ALL OBJECTS;",
            "CREATE SCHEMA S1;",
            "CREATE SCHEMA S2;",
            "CREATE SCHEMA S3;",
            "CREATE DOMAIN S1.D1 AS INTEGER;",
            "CREATE DOMAIN S2.D2 AS S1.D1;",
            "CREATE DOMAIN S3.D3 AS S2.D2;",
            "DROP SCHEMA S3 CASCADE;",
            "DROP SCHEMA S2 CASCADE;",
            "DROP SCHEMA S1 CASCADE;"
        ];
        RunBoth(statements);
    }

    private static void RunBoth(IReadOnlyList<string> statements)
    {
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        port.RemoveAll(static line => line.StartsWith("=CREATE USER", StringComparison.Ordinal));
        java.RemoveAll(static line => line.StartsWith("=CREATE USER", StringComparison.Ordinal));
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        List<string> lines = [];
        using H2Connection connection = new(
            "Data Source=script_sql_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        foreach (string sql in statements)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            try
            {
                if (sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                    || sql.StartsWith("SCRIPT", StringComparison.OrdinalIgnoreCase)
                    || sql.StartsWith("TABLE", StringComparison.OrdinalIgnoreCase))
                {
                    using DbDataReader rows = command.ExecuteReader();
                    while (rows.Read())
                    {
                        List<string> cells = [];
                        for (int c = 0; c < rows.FieldCount; c++)
                        {
                            cells.Add(rows.IsDBNull(c) ? "-" : rows.GetString(c));
                        }

                        string row = string.Join(":", cells);
                        string[] parts = row.Split('\n');
                        for (int p = 0; p < parts.Length; p++)
                        {
                            lines.Add(p == 0 ? "=" + parts[p].TrimEnd('\r') : parts[p].TrimEnd('\r'));
                        }
                    }
                }
                else
                {
                    lines.Add("=" + command.ExecuteNonQuery());
                }
            }
            catch (Exception e)
            {
                lines.Add(e is H2SharpException h2
                    ? "=" + h2.H2ErrorCode.ToString(CultureInfo.InvariantCulture)
                    : "=" + e.GetType().Name + ":" + e.Message);
            }
        }

        return lines;
    }

    private static List<string> RunJava(IReadOnlyList<string> statements)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:script_sql", "sa", "");
            java.sql.Statement stat = conn.createStatement();
            for (int i = 0; i < inputs.length; i++) {
              try {
                boolean isQuery = stat.execute(inputs[i]);
                if (isQuery) {
                  java.sql.ResultSet rs = stat.getResultSet();
                  int columns = rs.getMetaData().getColumnCount();
                  while (rs.next()) {
                    StringBuilder line = new StringBuilder("=");
                    for (int c = 1; c <= columns; c++) {
                      if (c > 1) {
                        line.append(':');
                      }
                      String s = rs.getString(c);
                      line.append(rs.wasNull() ? "-" : s);
                    }
                    System.out.println(line);
                  }
                } else {
                  System.out.println("=" + stat.getUpdateCount());
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}
