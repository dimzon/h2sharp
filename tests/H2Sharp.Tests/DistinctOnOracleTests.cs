/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class DistinctOnOracleTests
{
    [JavaOracleFact]
    public void DistinctOnMatchesJdbc()
    {
        string[] statements =
        [
            "SELECT DISTINCT ON (X % 2) X, X * 10 FROM SYSTEM_RANGE(1, 5) ORDER BY X",
            "SELECT DISTINCT ON (X % 2) X FROM SYSTEM_RANGE(1, 5) ORDER BY X DESC",
        ];

        using H2Connection connection = new(
            "Data Source=distinct_on_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        List<string> port = [];
        foreach (string sql in statements)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            using DbDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                List<string> values = [];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    values.Add(reader.IsDBNull(i)
                        ? "NULL"
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? "NULL");
                }

                port.Add("=R:" + string.Join('|', values));
            }
        }

        List<string> java = OracleRunner.Run(statements, """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:distinct_on");
            for (String sql : inputs) {
                try (java.sql.Statement s = conn.createStatement();
                     java.sql.ResultSet r = s.executeQuery(sql)) {
                    while (r.next()) {
                        StringBuilder line = new StringBuilder("=R:");
                        for (int i = 1; i <= r.getMetaData().getColumnCount(); i++) {
                            if (i > 1) line.append('|');
                            Object v = r.getObject(i);
                            line.append(v == null ? "NULL" : v.toString());
                        }
                        System.out.println(line);
                    }
                }
            }
            conn.close();
            """);

        Assert.Equal(java, port);
    }
}
