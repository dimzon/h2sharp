/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A prepared LIKE with a parameter is planned again at execution, when the value is
 * bound: the reference sets Parser.recompileAlways for every LIKE, so the plan of
 * "NAME LIKE ?" can choose IDXNAME for a prefix pattern and fall back to a table scan
 * for a leading wildcard. This locks both the plan text and that re-planning.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class PreparedLikePlanOracleTests
{
    [JavaOracleFact]
    public void ALikeParameterIsPlannedAgainWithItsValue()
    {
        List<string> port = RunPort();
        List<string> java = OracleRunner.Run([], """
            try (java.sql.Connection c = java.sql.DriverManager.getConnection("jdbc:h2:mem:preparedlike")) {
              java.sql.Statement s = c.createStatement();
              s.execute("CREATE TABLE TEST(ID INT PRIMARY KEY, V INT, NAME VARCHAR(255))");
              s.execute("INSERT INTO TEST VALUES(1, 2, 'Hello')");
              s.execute("INSERT INTO TEST VALUES(2, 4, 'World')");
              s.execute("CREATE INDEX IDXNAME ON TEST(NAME)");
              try (java.sql.PreparedStatement p = c.prepareStatement(
                      "EXPLAIN SELECT * FROM TEST WHERE NAME LIKE ?")) {
                for (String pattern : new String[] { "%orld", "H%" }) {
                  p.setString(1, pattern);
                  try (java.sql.ResultSet r = p.executeQuery()) {
                    while (r.next()) {
                      System.out.println(r.getString(1));
                    }
                  }
                }
              }
            }
            """);

        Assert.Equal(java.Count, port.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == port[i],
                "line " + i + ":\n  jvm:  " + java[i] + "\n  port: " + port[i]);
        }
    }

    private static List<string> RunPort()
    {
        using H2Connection connection = new(
            "Data Source=preparedlike_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        Execute(connection, "CREATE TABLE TEST(ID INT PRIMARY KEY, V INT, NAME VARCHAR(255))");
        Execute(connection, "INSERT INTO TEST VALUES(1, 2, 'Hello')");
        Execute(connection, "INSERT INTO TEST VALUES(2, 4, 'World')");
        Execute(connection, "CREATE INDEX IDXNAME ON TEST(NAME)");

        using DbCommand command = connection.CreateCommand();
        command.CommandText = "EXPLAIN SELECT * FROM TEST WHERE NAME LIKE ?";
        DbParameter pattern = command.CreateParameter();
        command.Parameters.Add(pattern);
        List<string> output = [];
        foreach (string value in new[] { "%orld", "H%" })
        {
            pattern.Value = value;
            using DbDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                output.AddRange(reader.GetString(0).Replace("\r", "", StringComparison.Ordinal).Split('\n'));
            }
        }

        return output;
    }

    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
