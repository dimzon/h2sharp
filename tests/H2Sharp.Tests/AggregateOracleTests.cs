/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Data.Extensions;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class AggregateOracleTests
{
    [JavaOracleFact]
    public void UserAggregateMatchesJavaAggregateSemantics()
    {
        string[] statements =
        [
            "CREATE TABLE T(V INT, G INT)",
            "INSERT INTO T VALUES (1, 1), (1, 1), (1, 1), (2, 1), (3, 2)",
            "SELECT M(V) FROM T",
            "SELECT M(DISTINCT V) FROM T",
            "SELECT M(V) FILTER (WHERE V > 2) FROM T",
            "SELECT M(V) FROM T WHERE FALSE",
            "SELECT M(V) OVER (ORDER BY V) FROM T ORDER BY V",
            "SELECT M(V) OVER (PARTITION BY G) FROM T ORDER BY G, V",
        ];

        using H2Connection connection = new(
            "Data Source=agg_oracle_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        ((IH2Extensions)connection).RegisterAggregate("M", () => new Median());
        connection.Open();

        List<string> port = [];
        foreach (string sql in statements)
        {
            Record(connection, sql, port);
        }

        List<string> java = OracleRunner.RunWithMembers(statements, """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:agg_oracle");
            try (java.sql.Statement s = conn.createStatement()) {
                s.execute("CREATE AGGREGATE M FOR 'Oracle$Median'");
            }
            for (String sql : inputs) {
                try (java.sql.Statement s = conn.createStatement()) {
                    if (s.execute(sql)) {
                        try (java.sql.ResultSet r = s.getResultSet()) {
                            while (r.next()) {
                                Object value = r.getObject(1);
                                System.out.println("=R:" + (value == null ? "null" : value.toString()));
                            }
                        }
                    } else {
                        System.out.println("=U:" + s.getUpdateCount());
                    }
                } catch (java.sql.SQLException e) {
                    System.out.println("=E:" + e.getErrorCode());
                }
            }
            conn.close();
            """, members: JavaMedian);

        Assert.True(port.SequenceEqual(java),
            "JVM:\n" + string.Join('\n', java) + "\nPort:\n" + string.Join('\n', port));
    }

    private const string JavaMedian = """
        public static class Median implements org.h2.api.Aggregate {
            private final java.util.ArrayList<Integer> values = new java.util.ArrayList<>();
            public int getInternalType(int[] inputTypes) {
                return org.h2.value.Value.INTEGER;
            }
            public void add(Object value) {
                values.add((Integer) value);
            }
            public Object getResult() {
                if (values.isEmpty()) return null;
                java.util.Collections.sort(values);
                return values.get(values.size() / 2);
            }
        }
        """;

    private static void Record(H2Connection connection, string sql, List<string> lines)
    {
        try
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            if (!sql.StartsWith("SELECT", StringComparison.Ordinal))
            {
                lines.Add("=U:" + command.ExecuteNonQuery().ToString(CultureInfo.InvariantCulture));
                return;
            }

            using DbDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                lines.Add("=R:" + (reader.IsDBNull(0)
                    ? "null"
                    : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)));
            }
        }
        catch (H2SharpException error)
        {
            lines.Add("=E:" + error.H2ErrorCode.ToString(CultureInfo.InvariantCulture));
        }
    }

    private sealed class Median : CollectingAggregateBase
    {
        public Median()
            : base(1)
        {
        }

        public override TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes)
            => TypeInfo.TypeInteger;

        protected override object? Compute(IReadOnlyList<object?> values, int argumentCount)
        {
            if (values.Count == 0)
            {
                return null;
            }

            int[] sorted = new int[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                sorted[i] = Convert.ToInt32(values[i], CultureInfo.InvariantCulture);
            }

            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }
    }
}
