/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * .NET side of the cross-language benchmark suite: the H2 reference (reference/h2,
 * v2.5.x) vs the .NET port. This is the mirror of tools/perf/java-bench/CrossBench.java —
 * the same scenarios, the same SQL text, the same protocol (setup outside timing, 3
 * warmups, 10 iterations, mean ns/op). The scenario bodies are transliterations of the
 * existing BenchmarkDotNet benches (BulkInsertBench, SelectRowsBench, LikeBench,
 * ParsePrepareBench, BlobInsertBench); BenchmarkDotNet itself is deliberately not used
 * here so that both sides measure with the identical protocol.
 *
 * Run with:  dotnet run -c Release --project tools/perf/H2Sharp.CrossBench
 *
 * Each scenario prints one JSON line to stdout:
 *   {"benchmark":"select_scan","meanNs":123.4,"opsPerSec":8103.7}
 */
using System;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using H2Sharp.Data;

namespace H2Sharp.CrossBench;

internal static class Program
{
    private const int Warmup = 3;
    private const int Iterations = 10;

    private static int Main(string[] args)
    {
        string? only = null;
        int repeat = 1;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scenario":
                    only = args[++i];
                    break;
                case "--repeat":
                    repeat = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                default:
                    Console.Error.WriteLine("usage: H2Sharp.CrossBench [--scenario <name> [--repeat N]]");
                    return 2;
            }
        }

        (string Name, IScenario Scenario)[] scenarios =
        [
            ("bulk_insert", new BulkInsert()),
            ("select_scan", new SelectScan()),
            ("like_filter", new LikeFilter()),
            ("parse_prepare", new ParsePrepare()),
            ("select_order_by", new SelectOrderBy()),
            ("update_rows", new UpdateRows()),
            ("inner_join", new InnerJoin()),
            ("window_sum", new WindowSum()),
            ("blob_insert", new BlobInsert()),
        ];

        foreach ((string name, IScenario scenario) in scenarios)
        {
            if (only is not null && name != only)
            {
                continue;
            }

            for (int r = 0; r < repeat; r++)
            {
                RunScenario(name, scenario);
            }
        }

        return 0;
    }

    private interface IScenario
    {
        void Setup(H2Connection connection);

        /// <summary>Runs the measured operation once; the return value is a checksum kept alive.</summary>
        long RunOnce(H2Connection connection);
    }

    private static void RunScenario(string name, IScenario scenario)
    {
        using H2Connection connection = new("Data Source=bench_" + name + ";Storage=Memory");
        connection.Open();
        scenario.Setup(connection);

        for (int i = 0; i < Warmup; i++)
        {
            Keep(scenario.RunOnce(connection));
        }

        long totalNs = 0;
        long totalAlloc = 0;
        for (int i = 0; i < Iterations; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            long sink = scenario.RunOnce(connection);
            long allocAfter = GC.GetAllocatedBytesForCurrentThread();
            long t1 = Stopwatch.GetTimestamp();
            totalNs += (t1 - t0) * 1_000_000_000L / Stopwatch.Frequency;
            totalAlloc += Math.Max(0, allocAfter - allocBefore);
            Keep(sink);
        }

        double meanNs = (double)totalNs / Iterations;
        double meanAlloc = (double)totalAlloc / Iterations;
        // InvariantCulture: decimal point, not a locale-dependent comma, so the line stays JSON.
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{{\"benchmark\":\"{0}\",\"meanNs\":{1:F1},\"opsPerSec\":{2:F1},\"allocBytesPerOp\":{3:F0}}}",
            name, meanNs, 1e9 / meanNs, meanAlloc));
    }

    /// <summary>Keeps the scenario result observable so the JIT cannot elide the work.</summary>
    private static void Keep(long value)
    {
        if (value == long.MinValue)
        {
            throw new InvalidOperationException("unexpected sink value");
        }
    }

    /// <summary>Builds one multi-row INSERT; identical text to PerfDb.FillTestTable.</summary>
    private static string InsertStatement(string table, string columns, bool withName, int batch, int count)
    {
        System.Text.StringBuilder sb = new("INSERT INTO " + table + "(" + columns + ") VALUES ");
        int end = Math.Min(batch + 500, count);
        for (int i = batch; i < end; i++)
        {
            if (i > batch)
            {
                sb.Append(',');
            }

            sb.Append('(').Append(i).Append(',').Append(i % 100);
            if (withName)
            {
                sb.Append(",'name").Append(i).Append("'");
            }

            sb.Append(')');
        }

        return sb.ToString();
    }

    /// <summary>Fills a table with count rows in batches of 500; used as setup, outside timing.</summary>
    private static void Fill(H2Connection connection, string table, string columns, bool withName, int count)
    {
        for (int batch = 0; batch < count; batch += 500)
        {
            ExecuteNonQuery(connection, InsertStatement(table, columns, withName, batch, count));
        }
    }

    private static void ExecuteNonQuery(H2Connection connection, string sql)
    {
        using H2Command command = new(sql, connection);
        command.ExecuteNonQuery();
    }

    /// <summary>Bulk insert of 1000 rows in batches of 500 (mirror of BulkInsertBench).</summary>
    private sealed class BulkInsert : IScenario
    {
        public void Setup(H2Connection connection)
        {
            ExecuteNonQuery(connection, "CREATE TABLE TEST(A INT, B INT, C VARCHAR(50))");
        }

        public long RunOnce(H2Connection connection)
        {
            for (int batch = 0; batch < 1000; batch += 500)
            {
                ExecuteNonQuery(connection, InsertStatement("TEST", "A, B, C", true, batch, 1000));
            }

            return 1000;
        }
    }

    /// <summary>Full scan SELECT A,B,C FROM TEST over 10 000 rows (mirror of SelectRowsBench).</summary>
    private sealed class SelectScan : IScenario
    {
        public void Setup(H2Connection connection)
        {
            ExecuteNonQuery(connection, "CREATE TABLE TEST(A INT, B INT, C VARCHAR(50))");
            Fill(connection, "TEST", "A, B, C", true, 10_000);
        }

        public long RunOnce(H2Connection connection)
        {
            using H2Command command = new("SELECT A, B, C FROM TEST", connection);
            using DbDataReader reader = command.ExecuteReader();
            long sum = 0;
            while (reader.Read())
            {
                sum += reader.GetInt32(0);
            }

            return sum;
        }
    }

    /// <summary>LIKE predicate scan over 10 000 rows (mirror of LikeBench).</summary>
    private sealed class LikeFilter : IScenario
    {
        public void Setup(H2Connection connection)
        {
            ExecuteNonQuery(connection, "CREATE TABLE TEST(A INT, B INT, C VARCHAR(50))");
            Fill(connection, "TEST", "A, B, C", true, 10_000);
        }

        public long RunOnce(H2Connection connection)
        {
            using H2Command command = new("SELECT A FROM TEST WHERE C LIKE 'name1%'", connection);
            using DbDataReader reader = command.ExecuteReader();
            long sum = 0;
            while (reader.Read())
            {
                sum += reader.GetInt32(0);
            }

            return sum;
        }
    }

    /// <summary>Parse+plan of 16 rotating statements matching zero rows (mirror of ParsePrepareBench).</summary>
    private sealed class ParsePrepare : IScenario
    {
        private static readonly string[] Statements = BuildStatements();

        private static string[] BuildStatements()
        {
            string[] statements = new string[16];
            for (int i = 0; i < statements.Length; i++)
            {
                statements[i] = "SELECT A, B, C FROM TEST WHERE A > " + i
                    + " AND B LIKE 'x" + i + "%' ORDER BY C";
            }

            return statements;
        }

        private int index;

        public void Setup(H2Connection connection)
        {
            ExecuteNonQuery(connection, "CREATE TABLE TEST(A INT, B INT, C VARCHAR(50))");
        }

        public long RunOnce(H2Connection connection)
        {
            string statement = Statements[index];
            index = (index + 1) & 15;
            using H2Command command = new(statement, connection);
            using DbDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                // zero rows: never entered
            }

            return 1;
        }
    }

    /// <summary>SELECT ... ORDER BY over 10 000 rows: LocalResult with a sort (mirror of SelectRowsBench).</summary>
    private sealed class SelectOrderBy : IScenario
    {
        public void Setup(H2Connection connection)
        {
            ExecuteNonQuery(connection, "CREATE TABLE TEST(A INT, B INT, C VARCHAR(50))");
            Fill(connection, "TEST", "A, B, C", true, 10_000);
        }

        public long RunOnce(H2Connection connection)
        {
            using H2Command command = new("SELECT A, B, C FROM TEST ORDER BY C", connection);
            using DbDataReader reader = command.ExecuteReader();
            long sum = 0;
            while (reader.Read())
            {
                sum += reader.GetInt32(0);
            }

            return sum;
        }
    }

    /// <summary>UPDATE of all 10 000 rows: full rewrite with undo log.</summary>
    private sealed class UpdateRows : IScenario
    {
        public void Setup(H2Connection connection)
        {
            ExecuteNonQuery(connection, "CREATE TABLE TEST(A INT, B INT, C VARCHAR(50))");
            Fill(connection, "TEST", "A, B, C", true, 10_000);
        }

        public long RunOnce(H2Connection connection)
        {
            using H2Command command = new("UPDATE TEST SET B = B + 1", connection);
            return command.ExecuteNonQuery();
        }
    }

    /// <summary>Join of two 10 000-row tables on a matching key, half the rows filtered out.</summary>
    private sealed class InnerJoin : IScenario
    {
        public void Setup(H2Connection connection)
        {
            ExecuteNonQuery(connection, "CREATE TABLE J1(A INT, B INT, C VARCHAR(50))");
            ExecuteNonQuery(connection, "CREATE TABLE J2(A INT, B INT, C VARCHAR(50))");
            Fill(connection, "J1", "A, B, C", true, 10_000);
            Fill(connection, "J2", "A, B, C", true, 10_000);
        }

        public long RunOnce(H2Connection connection)
        {
            using H2Command command = new(
                "SELECT J1.A FROM J1 JOIN J2 ON J1.A = J2.A WHERE J2.B > 50", connection);
            using DbDataReader reader = command.ExecuteReader();
            long sum = 0;
            while (reader.Read())
            {
                sum += reader.GetInt32(0);
            }

            return sum;
        }
    }

    /// <summary>Window SUM over 100 000 rows (the OFFHEAP audit's missing scenario).</summary>
    private sealed class WindowSum : IScenario
    {
        public void Setup(H2Connection connection)
        {
            ExecuteNonQuery(connection, "CREATE TABLE W(ID INT, VAL INT)");
            Fill(connection, "W", "ID, VAL", false, 100_000);
        }

        public long RunOnce(H2Connection connection)
        {
            using H2Command command = new(
                "SELECT ID, SUM(VAL) OVER (ORDER BY ID ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) FROM W",
                connection);
            using DbDataReader reader = command.ExecuteReader();
            long sum = 0;
            while (reader.Read())
            {
                sum += reader.GetInt32(0);
            }

            return sum;
        }
    }

    /// <summary>LOB insert of 1 MiB each (mirror of BlobInsertBench).</summary>
    private sealed class BlobInsert : IScenario
    {
        private static readonly byte[] Blob = new byte[1024 * 1024];

        public void Setup(H2Connection connection)
        {
            ExecuteNonQuery(connection, "CREATE TABLE LOB_T(B BLOB)");
        }

        public long RunOnce(H2Connection connection)
        {
            using H2Command command = new("INSERT INTO LOB_T(B) VALUES (?)", connection);
            DbParameter parameter = command.CreateParameter();
            parameter.Value = Blob;
            _ = command.Parameters.Add(parameter);
            command.ExecuteNonQuery();
            return Blob.Length;
        }
    }
}
