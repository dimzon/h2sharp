/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0.
 * The indexed "$N" form the ADO named-parameter rewriter targets, against H2 2.5 JDBC. */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using H2Sharp.Command;
using H2Sharp.Data;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class IndexedParameterOracleTests
{
    [JavaOracleFact]
    public void IndexedParametersRepeatAndOrderLikeTheReference()
    {
        (string Sql, int Count, int[] Values)[] cases =
        [
            ("SELECT $1 + $1", 1, [7]),
            ("SELECT $1, $2", 2, [7, 5]),
            ("SELECT $2", 2, [7, 5]),
            ("SELECT ?1 + $1", 1, [7]),
        ];

        List<string> port = [];
        foreach ((string sql, int count, int[] values) in cases)
        {
            port.Add(sql + " => " + string.Join(";", RunPort(sql, count, values)));
        }

        List<string> java = OracleRunner.Run([], """
            try (java.sql.Connection c = java.sql.DriverManager.getConnection("jdbc:h2:mem:indexed_parameters")) {
                String[] sqls = { "SELECT $1 + $1", "SELECT $1, $2", "SELECT $2", "SELECT ?1 + $1" };
                int[][] values = { {7}, {7, 5}, {7, 5}, {7} };
                for (int i = 0; i < sqls.length; i++) {
                    try (java.sql.PreparedStatement s = c.prepareStatement(sqls[i])) {
                        for (int p = 0; p < values[i].length; p++) s.setInt(p + 1, values[i][p]);
                        StringBuilder out = new StringBuilder(sqls[i]).append(" => ");
                        try (java.sql.ResultSet r = s.executeQuery()) {
                            boolean first = true;
                            while (r.next()) {
                                if (!first) out.append(";");
                                first = false;
                                StringBuilder row = new StringBuilder();
                                for (int column = 1; column <= r.getMetaData().getColumnCount(); column++) {
                                    if (column > 1) row.append("|");
                                    row.append(r.getString(column));
                                }
                                out.append(row);
                            }
                        }
                        System.out.println(out);
                    }
                }
            }
            """);

        Assert.Equal(java, port);
    }

    private static List<string> RunPort(string sql, int count, int[] values)
    {
        using H2Connection connection = new("Data Source=indexed_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        CommandContainer command = Assert.IsType<CommandContainer>(
            connection.Session!.PrepareCommand(sql));
        List<ParameterInterface> parameters = command.GetParameters();
        Assert.Equal(count, parameters.Count);
        for (int i = 0; i < values.Length; i++)
        {
            parameters[i].SetValue(ValueInteger.Get(values[i]), false);
        }

        List<string> lines = [];
        using ResultInterface rows = command.Query(0);
        while (rows.Next())
        {
            Value?[] row = rows.CurrentRow();
            lines.Add(string.Join("|", Enumerable.Range(0, rows.GetVisibleColumnCount())
                .Select(column => row[column]!.GetString() ?? "null")));
        }

        return lines;
    }
}
