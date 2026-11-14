/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The base of the parser-coverage oracle tests: a batch of SQL statements is run through
 * the ADO.NET layer on the port and through the reference jar with the same text, and the
 * two answers are compared line by line. This is what puts the parser under coverage with
 * the reference's behavior as the oracle.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Runs a batch of statements on both sides and asserts equal answers.
/// </summary>
public abstract class ParserCoverageOracleBase
{
    /// <summary>
    /// Runs the statements on the port through the ADO.NET layer and on the reference, and
    /// asserts the answers match line for line.
    /// </summary>
    /// <param name="dbName">the unique in-memory database name</param>
    /// <param name="statements">the SQL statements</param>
    protected static void AssertSameAsReference(string dbName, params string[] statements)
        => Compare(dbName, null, statements);

    /// <summary>
    /// Runs the statements on the port through the ADO.NET layer and on the reference, and
    /// asserts the answers match line for line, with the given compatibility mode fixed at
    /// open (the port does not support SET MODE after open).
    /// </summary>
    /// <param name="dbName">the unique in-memory database name</param>
    /// <param name="mode">the compatibility mode</param>
    /// <param name="statements">the SQL statements</param>
    protected static void AssertSameAsReferenceInMode(string dbName, string mode, params string[] statements)
        => Compare(dbName, mode, statements);

    private static void Compare(string dbName, string? mode, IReadOnlyList<string> statements)
    {
        List<string> port = RunPort(dbName, mode, statements);
        List<string> java = RunJava(dbName, mode, statements);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    private static List<string> RunPort(string dbName, string? mode, IReadOnlyList<string> statements)
    {
        string options = mode is null ? string.Empty : $";MODE={mode}";
        using H2Connection connection = new(
            $"Data Source={dbName};IGNORE_UNKNOWN_SETTINGS=TRUE;Storage=Memory{options}");
        connection.Open();
        List<string> lines = [];
        foreach (string sql in statements)
        {
            try
            {
                // ExecuteReader serves both shapes: a query yields its rows, an update
                // yields a reader without rows, and a syntax error yields an exception.
                using DbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                using DbDataReader reader = command.ExecuteReader();
                int columns = reader.FieldCount;
                while (reader.Read())
                {
                    List<string> cells = [];
                    for (int i = 0; i < columns; i++)
                    {
                        // A boolean is spelled TRUE/FALSE as the reference's getString writes
                        // it, not the CLR False/True.
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

    private static List<string> RunJava(string dbName, string? mode, IReadOnlyList<string> statements)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:DBNAMEMODE");
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
            """
                .Replace("DBNAME", dbName)
                .Replace("MODE", mode is null ? string.Empty : ";MODE=" + mode),
            extraJavaArgs: ["-Duser.language=en"]);
}
