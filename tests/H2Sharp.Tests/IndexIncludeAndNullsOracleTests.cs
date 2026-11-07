/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Index forms from the coverage data (uncovered.py): INCLUDE columns of a unique index
 * (the index owns the key columns only, the script prints the INCLUDE list), and the
 * NULLS DISTINCT / NULLS NOT DISTINCT spellings whose inserts ask the unique index whether
 * the row must be checked at all — NULL keys skip it, non-NULL keys run it.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using H2Sharp;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class IndexIncludeAndNullsOracleTests
{
    [JavaOracleFact]
    public void IncludeAndNullsDistinctFormsAnswerTheReference()
    {
        List<string> statements =
        [
            "CREATE TABLE U(A INT, B INT, C INT)",
            // NULLS NOT DISTINCT: two NULL keys may live together.
            "CREATE UNIQUE NULLS NOT DISTINCT INDEX UX ON U(A)",
            "INSERT INTO U VALUES (NULL, 1, 1), (NULL, 2, 2)",
            // NULLS DISTINCT: the same, on another column.
            "CREATE UNIQUE NULLS DISTINCT INDEX UY ON U(B)",
            "INSERT INTO U VALUES (1, NULL, 3), (2, NULL, 4)",
            // INCLUDE: the index keeps the key column and carries the rest.
            "CREATE UNIQUE INDEX UZ ON U(C) INCLUDE (A, B)",
            "SCRIPT NOPASSWORDS NOSETTINGS NOVERSION",
            "SELECT A, B, C FROM U WHERE C = 1 ORDER BY C",
            "DROP TABLE U",
        ];
        RunBoth(statements);
    }

    /// <summary>
    /// Runs the statements on both sides and asserts the same answers: the rows, or the code
    /// of the error.
    /// </summary>
    /// <param name="statements">the statements</param>
    private static void RunBoth(IReadOnlyList<string> statements)
    {
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        // The reference answers one row per statement, multi-line ones included: flatten
        // the rows into physical lines, the way the port already answered them.
        List<string> flatJava = [];
        foreach (string line in java)
        {
            string[] parts = line.Split('\n');
            for (int p = 0; p < parts.Length; p++)
            {
                flatJava.Add(parts[p].TrimEnd('\r'));
            }
        }

        // The reference prints a CREATE USER line for its built-in user and the port has no
        // users (D-004), so those lines are dropped before the comparison.
        port.RemoveAll(static line => line.StartsWith("=CREATE USER", StringComparison.Ordinal));
        flatJava.RemoveAll(static line => line.StartsWith("=CREATE USER", StringComparison.Ordinal));
        java = flatJava;
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        List<string> lines = [];
        using H2Connection connection = new(
            "Data Source=index_include_nulls_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        foreach (string sql in statements)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            try
            {
                if (sql.StartsWith("SELECT", StringComparison.Ordinal)
                    || sql.StartsWith("SCRIPT", StringComparison.Ordinal))
                {
                    using DbDataReader rows = command.ExecuteReader();
                    while (rows.Read())
                    {
                        List<string> cells = [];
                        for (int c = 0; c < rows.FieldCount; c++)
                        {
                            cells.Add(rows.IsDBNull(c) ? "-" : rows.GetString(c));
                        }

                        // The port answers one row per physical line, the reference one row
                        // per statement: both are flattened the same way, as the script
                        // comparison of ScriptCommandOracleTests does.
                        string row = string.Join(":", cells);
                        string[] parts = row.Split('\n');
                        for (int p = 0; p < parts.Length; p++)
                        {
                            // The first physical line carries the answer mark, the rest do
                            // not, exactly as the reference's one-row print does.
                            string part = parts[p].TrimEnd('\r');
                            lines.Add(p == 0 ? "=" + part : part);
                        }
                    }
                }
                else
                {
                    lines.Add("=" + command.ExecuteNonQuery());
                }
            }
            catch (H2SharpException e)
            {
                lines.Add("=" + e.H2ErrorCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return lines;
    }

    /// <summary>
    /// Runs the statements on the reference and writes what the question answered.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunJava(IReadOnlyList<string> statements)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:index_include_nulls", "sa", "");
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
