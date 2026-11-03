/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A derived query that has its own parameters, filtered from outside by a condition that
 * becomes a parameter of the derived table's index. The original parameter values must be
 * copied into the pushed query on every run; the answers are compared with the reference
 * running a JDBC prepared statement over the same text.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class DerivedTableParameterOracleTests
{
    [JavaOracleFact]
    public void AderivedQueryKeepsItsParametersWhenAConditionIsPushed()
    {
        List<int[]> bindings =
        [
            [5, 20],
            [5, 30],
            [15, 20],
            [0, 0],
        ];
        List<string> port = RunPort(bindings);
        List<string> java = RunJava(bindings);
        Assert.Equal(java, port);
    }

    private static List<string> RunPort(List<int[]> bindings)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("derived_parameter_oracle")), "sa")
        {
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        _ = session.PrepareCommand("CREATE TABLE C(ID INT PRIMARY KEY, V INT)").ExecuteUpdate(false);
        _ = session.PrepareCommand("INSERT INTO C VALUES (10, 100), (20, 200), (30, 300)").ExecuteUpdate(false);

        List<string> lines = [];
        string[] queries =
        [
            // An equality outside; the inner parameter is only in the derived query.
            "SELECT ID, V FROM (SELECT ID, V FROM C WHERE V > ?1) X WHERE X.ID = ?2 ORDER BY ID",
            // A range outside, so the pushed parameter is the scan start.
            "SELECT ID, V FROM (SELECT ID, V FROM C WHERE V > ?1) X WHERE X.V >= ?2 ORDER BY ID",
        ];
        for (int q = 0; q < queries.Length; q++)
        {
            CommandContainer command = Assert.IsType<CommandContainer>(session.PrepareCommand(queries[q]));
            List<ParameterInterface> parameters = command.GetParameters();
            foreach (int[] binding in bindings)
            {
                for (int i = 0; i < parameters.Count && i < binding.Length; i++)
                {
                    parameters[i].SetValue(ValueInteger.Get(binding[i]), false);
                }

                string key = q.ToString(CultureInfo.InvariantCulture)
                    + ":" + string.Join(",", binding);
                try
                {
                    ResultInterface result = command.Query(-1);
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

                        lines.Add("=" + key + ":R:" + string.Join("|", cells));
                    }

                    lines.Add("=" + key + ":Q");
                }
                catch (Exception e)
                {
                    lines.Add("=" + key + ":E:" + (e is H2SharpException h2
                        ? h2.H2ErrorCode.ToString(CultureInfo.InvariantCulture)
                        : e.GetType().Name));
                }
            }
        }

        return lines;
    }

    private static List<string> RunJava(List<int[]> bindings)
    {
        List<string> statements = [];
        foreach (int[] binding in bindings)
        {
            statements.Add(string.Join(",", binding));
        }

        return OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:derived_parameter_oracle");
            java.sql.Statement stat = conn.createStatement();
            stat.execute("CREATE TABLE C(ID INT PRIMARY KEY, V INT)");
            stat.execute("INSERT INTO C VALUES (10, 100), (20, 200), (30, 300)");
            String[] queries = {
                "SELECT ID, V FROM (SELECT ID, V FROM C WHERE V > ?) X WHERE X.ID = ? ORDER BY ID",
                "SELECT ID, V FROM (SELECT ID, V FROM C WHERE V > ?) X WHERE X.V >= ? ORDER BY ID"
            };
            for (int q = 0; q < queries.length; q++) {
              try {
                java.sql.PreparedStatement ps = conn.prepareStatement(queries[q]);
                for (int b = 0; b < inputs.length; b++) {
                  String[] parts = inputs[b].split(",");
                  for (int i = 0; i < parts.length; i++) {
                    ps.setInt(i + 1, Integer.parseInt(parts[i]));
                  }
                  try {
                    java.sql.ResultSet rs = ps.executeQuery();
                    int columns = rs.getMetaData().getColumnCount();
                    while (rs.next()) {
                      StringBuilder row = new StringBuilder("=").append(q).append(":").append(inputs[b]).append(":R:");
                      for (int c = 1; c <= columns; c++) {
                        if (c > 1) {
                          row.append('|');
                        }
                        String s = rs.getString(c);
                        row.append(rs.wasNull() ? "-" : s);
                      }
                      System.out.println(row);
                    }
                    System.out.println("=" + q + ":" + inputs[b] + ":Q");
                    rs.close();
                  } catch (java.sql.SQLException e) {
                    System.out.println("=" + q + ":" + inputs[b] + ":E:" + e.getErrorCode());
                  }
                }
                ps.close();
              } catch (java.sql.SQLException e) {
                System.out.println("=" + q + ":E:" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en", "-Duser.timezone=UTC"]);
    }
}
