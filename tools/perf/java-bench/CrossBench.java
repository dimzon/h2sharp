/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Java side of the cross-language benchmark suite: the H2 reference (reference/h2,
 * v2.5.x) vs the .NET port. This is the mirror of tools/perf/H2Sharp.CrossBench — the
 * same scenarios, the same SQL text, the same protocol (setup outside timing, 3 warmups,
 * 10 iterations, mean ns/op). The scenario bodies are transliterations of the existing
 * BenchmarkDotNet benches (BulkInsertBench, SelectRowsBench, LikeBench, ParsePrepareBench,
 * BlobInsertBench).
 *
 * Build and run:
 *   javac -encoding UTF-8 -cp <h2jar> -d <out> CrossBench.java
 *   java -cp <out>;<h2jar> CrossBench
 *
 * Each scenario prints one JSON line to stdout:
 *   {"benchmark":"select_scan","meanNs":123.4,"opsPerSec":8103.7}
 */
import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.Statement;
import java.util.Locale;

public class CrossBench {

    /** Warmup iterations per scenario, same as the .NET side. */
    private static final int WARMUP = 3;

    /** Measured iterations per scenario, same as the .NET side. */
    private static final int ITERATIONS = 10;

    public static void main(String[] args) throws Exception {
        Class.forName("org.h2.Driver");
        runScenario("bulk_insert", new BulkInsert());
        runScenario("select_scan", new SelectScan());
        runScenario("like_filter", new LikeFilter());
        runScenario("parse_prepare", new ParsePrepare());
        runScenario("select_order_by", new SelectOrderBy());
        runScenario("update_rows", new UpdateRows());
        runScenario("inner_join", new InnerJoin());
        runScenario("window_sum", new WindowSum());
        runScenario("blob_insert", new BlobInsert());
    }

    interface Scenario {
        void setup(Connection conn) throws Exception;

        /** Runs the measured operation once; the return value is a checksum kept alive. */
        long runOnce(Connection conn) throws Exception;
    }

    private static void runScenario(String name, Scenario scenario) throws Exception {
        try (Connection conn = DriverManager.getConnection("jdbc:h2:mem:bench_" + name)) {
            scenario.setup(conn);

            for (int i = 0; i < WARMUP; i++) {
                keep(scenario.runOnce(conn));
            }

            long totalNs = 0;
            for (int i = 0; i < ITERATIONS; i++) {
                long t0 = System.nanoTime();
                long sink = scenario.runOnce(conn);
                long t1 = System.nanoTime();
                totalNs += t1 - t0;
                keep(sink);
            }

            double meanNs = (double) totalNs / ITERATIONS;
            // Locale.ROOT: decimal point, not a locale-dependent comma, so the line stays JSON.
            System.out.println(String.format(Locale.ROOT,
                    "{\"benchmark\":\"%s\",\"meanNs\":%.1f,\"opsPerSec\":%.1f}",
                    name, meanNs, 1e9 / meanNs));
        }
    }

    /** Keeps the scenario result observable so the JIT cannot elide the work. */
    private static void keep(long value) {
        if (value == Long.MIN_VALUE) {
            throw new AssertionError("unexpected sink value");
        }
    }

    /** Builds one multi-row INSERT; identical text to PerfDb.FillTestTable. */
    private static String insertStatement(String table, String columns, boolean withName,
            int batch, int count) {
        StringBuilder sb = new StringBuilder("INSERT INTO ").append(table)
                .append('(').append(columns).append(") VALUES ");
        int end = Math.min(batch + 500, count);
        for (int i = batch; i < end; i++) {
            if (i > batch) {
                sb.append(',');
            }
            sb.append('(').append(i).append(',').append(i % 100);
            if (withName) {
                sb.append(",'name").append(i).append("'");
            }
            sb.append(')');
        }
        return sb.toString();
    }

    /** Fills a table with count rows in batches of 500; used as setup, outside timing. */
    private static void fill(Connection conn, String table, String columns, boolean withName,
            int count) throws Exception {
        for (int batch = 0; batch < count; batch += 500) {
            try (Statement s = conn.createStatement()) {
                s.executeUpdate(insertStatement(table, columns, withName, batch, count));
            }
        }
    }

    /** Bulk insert of 1000 rows in batches of 500 (mirror of BulkInsertBench). */
    static final class BulkInsert implements Scenario {
        @Override
        public void setup(Connection conn) throws Exception {
            try (Statement s = conn.createStatement()) {
                s.execute("CREATE TABLE TEST(A INT, B INT, C VARCHAR(50))");
            }
        }

        @Override
        public long runOnce(Connection conn) throws Exception {
            for (int batch = 0; batch < 1000; batch += 500) {
                try (Statement s = conn.createStatement()) {
                    s.executeUpdate(insertStatement("TEST", "A, B, C", true, batch, 1000));
                }
            }
            return 1000L;
        }
    }

    /** Full scan SELECT A,B,C FROM TEST over 10 000 rows (mirror of SelectRowsBench). */
    static final class SelectScan implements Scenario {
        @Override
        public void setup(Connection conn) throws Exception {
            try (Statement s = conn.createStatement()) {
                s.execute("CREATE TABLE TEST(A INT, B INT, C VARCHAR(50))");
            }
            fill(conn, "TEST", "A, B, C", true, 10_000);
        }

        @Override
        public long runOnce(Connection conn) throws Exception {
            long sum = 0;
            try (Statement s = conn.createStatement();
                 ResultSet rs = s.executeQuery("SELECT A, B, C FROM TEST")) {
                while (rs.next()) {
                    sum += rs.getInt(1);
                }
            }
            return sum;
        }
    }

    /** LIKE predicate scan over 10 000 rows (mirror of LikeBench). */
    static final class LikeFilter implements Scenario {
        @Override
        public void setup(Connection conn) throws Exception {
            try (Statement s = conn.createStatement()) {
                s.execute("CREATE TABLE TEST(A INT, B INT, C VARCHAR(50))");
            }
            fill(conn, "TEST", "A, B, C", true, 10_000);
        }

        @Override
        public long runOnce(Connection conn) throws Exception {
            long sum = 0;
            try (Statement s = conn.createStatement();
                 ResultSet rs = s.executeQuery("SELECT A FROM TEST WHERE C LIKE 'name1%'")) {
                while (rs.next()) {
                    sum += rs.getInt(1);
                }
            }
            return sum;
        }
    }

    /** Parse+plan of 16 rotating statements matching zero rows (mirror of ParsePrepareBench). */
    static final class ParsePrepare implements Scenario {
        private static final String[] STATEMENTS = buildStatements();

        private static String[] buildStatements() {
            String[] statements = new String[16];
            for (int i = 0; i < statements.length; i++) {
                statements[i] = "SELECT A, B, C FROM TEST WHERE A > " + i
                        + " AND B LIKE 'x" + i + "%' ORDER BY C";
            }
            return statements;
        }

        private int index;

        @Override
        public void setup(Connection conn) throws Exception {
            try (Statement s = conn.createStatement()) {
                s.execute("CREATE TABLE TEST(A INT, B INT, C VARCHAR(50))");
            }
        }

        @Override
        public long runOnce(Connection conn) throws Exception {
            String statement = STATEMENTS[index];
            index = (index + 1) & 15;
            try (Statement s = conn.createStatement();
                 ResultSet rs = s.executeQuery(statement)) {
                while (rs.next()) {
                    // zero rows: never entered
                }
            }
            return 1L;
        }
    }

    /** SELECT ... ORDER BY over 10 000 rows: LocalResult with a sort (mirror of SelectRowsBench). */
    static final class SelectOrderBy implements Scenario {
        @Override
        public void setup(Connection conn) throws Exception {
            try (Statement s = conn.createStatement()) {
                s.execute("CREATE TABLE TEST(A INT, B INT, C VARCHAR(50))");
            }
            fill(conn, "TEST", "A, B, C", true, 10_000);
        }

        @Override
        public long runOnce(Connection conn) throws Exception {
            long sum = 0;
            try (Statement s = conn.createStatement();
                 ResultSet rs = s.executeQuery("SELECT A, B, C FROM TEST ORDER BY C")) {
                while (rs.next()) {
                    sum += rs.getInt(1);
                }
            }
            return sum;
        }
    }

    /** UPDATE of all 10 000 rows: full rewrite with undo log. */
    static final class UpdateRows implements Scenario {
        @Override
        public void setup(Connection conn) throws Exception {
            try (Statement s = conn.createStatement()) {
                s.execute("CREATE TABLE TEST(A INT, B INT, C VARCHAR(50))");
            }
            fill(conn, "TEST", "A, B, C", true, 10_000);
        }

        @Override
        public long runOnce(Connection conn) throws Exception {
            try (Statement s = conn.createStatement()) {
                return s.executeUpdate("UPDATE TEST SET B = B + 1");
            }
        }
    }

    /** Join of two 10 000-row tables on a matching key, half the rows filtered out. */
    static final class InnerJoin implements Scenario {
        @Override
        public void setup(Connection conn) throws Exception {
            try (Statement s = conn.createStatement()) {
                s.execute("CREATE TABLE J1(A INT, B INT, C VARCHAR(50))");
                s.execute("CREATE TABLE J2(A INT, B INT, C VARCHAR(50))");
            }
            fill(conn, "J1", "A, B, C", true, 10_000);
            fill(conn, "J2", "A, B, C", true, 10_000);
        }

        @Override
        public long runOnce(Connection conn) throws Exception {
            long sum = 0;
            try (Statement s = conn.createStatement();
                 ResultSet rs = s.executeQuery(
                         "SELECT J1.A FROM J1 JOIN J2 ON J1.A = J2.A WHERE J2.B > 50")) {
                while (rs.next()) {
                    sum += rs.getInt(1);
                }
            }
            return sum;
        }
    }

    /** Window SUM over 100 000 rows (the OFFHEAP audit's missing scenario). */
    static final class WindowSum implements Scenario {
        @Override
        public void setup(Connection conn) throws Exception {
            try (Statement s = conn.createStatement()) {
                s.execute("CREATE TABLE W(ID INT, VAL INT)");
            }
            fill(conn, "W", "ID, VAL", false, 100_000);
        }

        @Override
        public long runOnce(Connection conn) throws Exception {
            long sum = 0;
            try (Statement s = conn.createStatement();
                 ResultSet rs = s.executeQuery(
                         "SELECT ID, SUM(VAL) OVER (ORDER BY ID ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) FROM W")) {
                while (rs.next()) {
                    sum += rs.getInt(1);
                }
            }
            return sum;
        }
    }

    /** LOB insert of 1 MiB each (mirror of BlobInsertBench). */
    static final class BlobInsert implements Scenario {
        private static final byte[] BLOB = new byte[1024 * 1024];

        @Override
        public void setup(Connection conn) throws Exception {
            try (Statement s = conn.createStatement()) {
                s.execute("CREATE TABLE LOB_T(B BLOB)");
            }
        }

        @Override
        public long runOnce(Connection conn) throws Exception {
            try (PreparedStatement ps = conn.prepareStatement("INSERT INTO LOB_T(B) VALUES (?)")) {
                ps.setBytes(1, BLOB);
                ps.executeUpdate();
            }
            return BLOB.length;
        }
    }
}
