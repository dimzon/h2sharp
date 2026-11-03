/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * AT TIME ZONE / AT LOCAL over every datetime type, checked against the reference running the
 * same text: zone names, fixed offsets as intervals, LOCAL, NULL, and the refusals (an unknown
 * zone, a named zone over a TIME, an interval outside ±18 hours).
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class TimeZoneOperationOracleTests
{
    /// <summary>
    /// The statements, the same on both sides. The session's time zone is fixed to UTC so both
    /// sides answer AT LOCAL and the TIME conversions identically.
    /// </summary>
    [JavaOracleFact]
    public void TheTimeZoneOperationsAnswerTheSameAsTheReference()
    {
        string[] statements =
        [
            "SET TIME ZONE 'UTC'",
            // Timestamp without a zone gains the session zone, then changes it.
            "SELECT TIMESTAMP '2024-01-01 12:00:00' AT TIME ZONE 'UTC'",
            "SELECT TIMESTAMP '2024-01-01 12:00:00' AT TIME ZONE 'Europe/Berlin'",
            "SELECT TIMESTAMP '2024-01-01 12:00:00' AT TIME ZONE INTERVAL '+2:00' HOUR TO SECOND",
            "SELECT TIMESTAMP '2024-01-01 12:00:00' AT TIME ZONE INTERVAL '-14:30' HOUR TO SECOND",
            "SELECT TIMESTAMP '2024-01-01 12:00:00' AT LOCAL",
            // A timestamp with a zone is re-based to the target zone.
            "SELECT TIMESTAMP WITH TIME ZONE '2024-01-01 12:00:00+02:00' AT TIME ZONE 'UTC'",
            "SELECT TIMESTAMP WITH TIME ZONE '2024-01-01 12:00:00+02:00' AT TIME ZONE 'America/New_York'",
            "SELECT TIMESTAMP WITH TIME ZONE '2024-01-01 12:00:00+02:00' AT TIME ZONE "
                + "INTERVAL '-5:00' HOUR TO SECOND",
            "SELECT TIMESTAMP WITH TIME ZONE '2024-01-01 12:00:00+02:00' AT LOCAL",
            // The same zone: the value is returned unchanged.
            "SELECT TIMESTAMP WITH TIME ZONE '2024-01-01 12:00:00+02:00' AT TIME ZONE 'Europe/Berlin'",
            // TIME with a zone: only fixed offsets are allowed, and the named zone is refused.
            "SELECT TIME WITH TIME ZONE '12:00:00+02:00' AT TIME ZONE 'UTC'",
            "SELECT TIME WITH TIME ZONE '12:00:00+02:00' AT TIME ZONE INTERVAL '-3:15' HOUR TO SECOND",
            "SELECT TIME WITH TIME ZONE '12:00:00+02:00' AT TIME ZONE 'Europe/Berlin'",
            "SELECT TIME WITH TIME ZONE '12:00:00+02:00' AT LOCAL",
            // Plain TIME keeps its type over AT LOCAL.
            "SELECT TIME '12:00:00' AT LOCAL",
            // The summer-time boundary through a named zone.
            "SELECT TIMESTAMP WITH TIME ZONE '2024-07-01 12:00:00+00:00' AT TIME ZONE 'Europe/Berlin'",
            "SELECT TIMESTAMP WITH TIME ZONE '2024-01-01 12:00:00+00:00' AT TIME ZONE 'Europe/Berlin'",
            // NULL everywhere.
            "SELECT CAST(NULL AS TIMESTAMP WITH TIME ZONE) AT TIME ZONE 'UTC'",
            "SELECT TIMESTAMP WITH TIME ZONE '2024-01-01 12:00:00+00:00' AT TIME ZONE CAST(NULL AS VARCHAR)",
            "SELECT CAST(NULL AS TIME WITH TIME ZONE) AT TIME ZONE INTERVAL '+1:00' HOUR TO SECOND",
            // The refusals: an unknown zone, a named zone over TIME, an interval beyond 18 hours.
            "SELECT TIMESTAMP '2024-01-01 12:00:00' AT TIME ZONE 'MARS'",
            "SELECT TIMESTAMP '2024-01-01 12:00:00' AT TIME ZONE INTERVAL '19' HOUR TO SECOND",
            "SELECT TIMESTAMP '2024-01-01 12:00:00' AT TIME ZONE INTERVAL '17:59:59.5' HOUR TO SECOND",
            // AT TIME ZONE over a non-datetime type is a syntax error.
            "SELECT 1 AT TIME ZONE 'UTC'",
            "SELECT 'x' AT LOCAL",
            // The plan prints the operation with its zone when the operand is a column and
            // the expression cannot fold.
            "CREATE TABLE P(ID INT PRIMARY KEY, TS TIMESTAMP, TZ TIMESTAMP WITH TIME ZONE, TT TIME WITH TIME ZONE)",
            "INSERT INTO P VALUES (1, TIMESTAMP '2024-01-01 12:00:00', "
                + "TIMESTAMP WITH TIME ZONE '2024-01-01 12:00:00+00:00', TIME WITH TIME ZONE '12:00:00+02:00')",
            "EXPLAIN SELECT TZ AT TIME ZONE 'UTC' FROM P",
            "EXPLAIN SELECT TZ AT LOCAL FROM P",
            "EXPLAIN SELECT TS AT LOCAL FROM P",
            // The deterministic walk over the operation as a comparison operand.
            "SELECT ID FROM P WHERE TZ AT TIME ZONE 'UTC' = TIMESTAMP WITH TIME ZONE '2024-01-01 12:00:00+00:00' "
                + "AND TZ AT TIME ZONE 'UTC' IN (TIMESTAMP WITH TIME ZONE '2024-01-01 12:00:00+00:00')",
            "SELECT ID FROM P WHERE TS AT LOCAL = TIMESTAMP '2024-01-01 12:00:00' "
                + "AND TS AT LOCAL IN (TIMESTAMP '2024-01-01 12:00:00')",
            "SELECT ID FROM P WHERE TT AT LOCAL = TIME '12:00:00' "
                + "AND TT AT LOCAL IN (TIME '12:00:00')",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        // The EXPLAIN answer is one layout question: the reference indents its plan body
        // differently, so the whitespace-folded text as a whole is what is compared.
        string javaText = Regex.Replace(string.Join(" ", java), "\\s+", " ").Trim();
        string portText = Regex.Replace(string.Join(" ", port), "\\s+", " ").Trim();
        Assert.True(javaText == portText, "java:\n" + javaText + "\nport:\n" + portText);
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered: the rows, or the code of
    /// the error.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("tz_operation_oracle")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        List<string> lines = [];
        foreach (string sql in statements)
        {
            try
            {
                CommandContainer container =
                    Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
                if (sql.StartsWith("SELECT", StringComparison.Ordinal)
                    || sql.StartsWith("EXPLAIN", StringComparison.Ordinal))
                {
                    ResultInterface result = container.Query(-1);
                    int visible = result.GetVisibleColumnCount();
                    while (result.Next())
                    {
                        Value?[] row = result.CurrentRow();
                        List<string> cells = [];
                        for (int i = 0; i < visible; i++)
                        {
                            Value? value = row[i];
                            cells.Add(value is null or ValueNull ? "-" : value.GetString() ?? "-");
                        }

                        lines.Add("=" + string.Join(":", cells));
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
                    : "=" + e.GetType().Name + ":" + e.Message);
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
            java.sql.Connection conn = java.sql.DriverManager.getConnection(
                "jdbc:h2:mem:tz_operation_oracle;TIME ZONE=UTC");
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
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.timezone=UTC", "-Duser.language=en"]);
}
