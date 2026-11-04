/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The frames of a window aggregate: the bounded frames both sides of the row, the partitions,
 * the EXCLUDE variants, RANGE and GROUPS frames, an unbounded partition, and a frame bound
 * that is a bound parameter rather than a constant.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class WindowFrameOracleTests
{
    [JavaOracleFact]
    public void TheFramesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE W(ID INT, G INT, V INT)",
            "INSERT INTO W VALUES (1, 1, 10), (2, 1, 20), (3, 1, 30), (4, 2, 40), (5, 2, 50)",
            "SELECT ID, SUM(V) OVER (ORDER BY ID ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING) FROM W ORDER BY ID",
            "SELECT ID, COUNT(*) OVER (ORDER BY ID ROWS BETWEEN 2 PRECEDING AND CURRENT ROW) FROM W ORDER BY ID",
            "SELECT ID, SUM(V) OVER (ORDER BY ID ROWS BETWEEN CURRENT ROW AND 2 FOLLOWING) FROM W ORDER BY ID",
            "SELECT ID, SUM(V) OVER (PARTITION BY G ORDER BY ID ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING) FROM W ORDER BY ID",
            "SELECT ID, MIN(V) OVER (ORDER BY ID ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING "
                + "EXCLUDE CURRENT ROW) FROM W ORDER BY ID",
            "SELECT ID, SUM(V) OVER (ORDER BY ID ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING "
                + "EXCLUDE GROUP) FROM W ORDER BY ID",
            "SELECT ID, SUM(V) OVER (ORDER BY ID ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING "
                + "EXCLUDE TIES) FROM W ORDER BY ID",
            "SELECT ID, SUM(V) OVER (ORDER BY ID ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) "
                + "FROM W ORDER BY ID",
            "SELECT ID, SUM(V) OVER (ORDER BY ID ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) "
                + "FROM W ORDER BY ID",
            "SELECT ID, SUM(V) OVER (ORDER BY ID ROWS BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING) "
                + "FROM W ORDER BY ID",
            "SELECT ID, SUM(V) OVER (ORDER BY V RANGE BETWEEN 10 PRECEDING AND 10 FOLLOWING) FROM W ORDER BY ID",
            "SELECT ID, SUM(V) OVER (ORDER BY V RANGE BETWEEN 20 PRECEDING AND CURRENT ROW) FROM W ORDER BY ID",
            "SELECT ID, SUM(V) OVER (ORDER BY V RANGE BETWEEN CURRENT ROW AND 20 FOLLOWING) FROM W ORDER BY ID",
            "SELECT ID, COUNT(*) OVER (ORDER BY G GROUPS BETWEEN 1 PRECEDING AND 1 FOLLOWING) "
                + "FROM W ORDER BY ID",
            "SELECT ID, COUNT(DISTINCT V) OVER (ORDER BY ID ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING) "
                + "FROM W ORDER BY ID",
            // A peer group repeated over several rows: one value per group.
            "CREATE TABLE P(ID INT, G INT, V INT)",
            "INSERT INTO P VALUES (1, 1, 10), (2, 1, 20), (3, 1, 20), (4, 2, 30)",
            "SELECT ID, SUM(SUM(V)) OVER (PARTITION BY G) FROM P GROUP BY ID, G, V ORDER BY ID",
            "SELECT ID, SUM(V) OVER (ORDER BY G GROUPS BETWEEN 1 PRECEDING AND CURRENT ROW) FROM P ORDER BY ID",
            "SELECT ID, SUM(V) OVER (ORDER BY G RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) FROM P ORDER BY ID",
        ]);
    }

    [JavaOracleFact]
    public void VariableFrameBoundsMatchJdbc()
    {
        string[] inputs = ["1", "2"];
        using H2Connection connection = new(
            "Data Source=window_frame_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        using (DbCommand setup = connection.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE W(ID INT, V INT)";
            setup.ExecuteNonQuery();
            setup.CommandText = "INSERT INTO W VALUES (1, 10), (2, 20), (3, 30), (4, 40), (5, 50)";
            setup.ExecuteNonQuery();
        }

        using DbCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT ID, SUM(V) OVER (ORDER BY ID ROWS BETWEEN ? PRECEDING AND ? FOLLOWING) "
            + "FROM W ORDER BY ID";
        DbParameter preceding = command.CreateParameter();
        preceding.Value = 1;
        command.Parameters.Add(preceding);
        DbParameter following = command.CreateParameter();
        following.Value = 1;
        command.Parameters.Add(following);
        command.Prepare();

        List<string> port = [];
        for (int i = 0; i < inputs.Length; i++)
        {
            int n = int.Parse(inputs[i], CultureInfo.InvariantCulture);
            preceding.Value = n;
            following.Value = n;
            using DbDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                port.Add("=" + i + ":"
                    + Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) + "|"
                    + Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture));
            }

            port.Add("=" + i + ":Q");
        }

        List<string> java = OracleRunner.Run(inputs,
            """
            try (java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:window_frame_params")) {
                java.sql.Statement setup = conn.createStatement();
                setup.execute("CREATE TABLE W(ID INT, V INT)");
                setup.execute("INSERT INTO W VALUES (1, 10), (2, 20), (3, 30), (4, 40), (5, 50)");
                try (java.sql.PreparedStatement s = conn.prepareStatement(
                        "SELECT ID, SUM(V) OVER (ORDER BY ID ROWS BETWEEN ? PRECEDING AND ? FOLLOWING) FROM W ORDER BY ID")) {
                    for (int i = 0; i < inputs.length; i++) {
                        int n = Integer.parseInt(inputs[i]);
                        s.setInt(1, n);
                        s.setInt(2, n);
                        try (java.sql.ResultSet r = s.executeQuery()) {
                            while (r.next()) {
                                System.out.println("=" + i + ":" + r.getInt(1) + "|" + r.getString(2));
                            }
                            System.out.println("=" + i + ":Q");
                        }
                    }
                }
            }
            """);
        Assert.Equal(java, port);
    }
}
