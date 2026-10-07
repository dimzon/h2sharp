/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Parse-only oracle for the reference H2.
 *
 * H2 has no unit tests for its parser, so the .sql scripts are the only bulk corpus
 * available (see MIGRATION_STATUS.md). This tool walks them, splits them into statements
 * and reports what the reference parser makes of each statement, without executing
 * anything: Session.prepareCommand compiles the command and stops there.
 *
 * The splitter lives in the test tooling and not in the port. Its only job is to define the
 * corpus, and the corpus is regenerated on every run rather than committed, so a splitter
 * bug limits coverage instead of causing false failures.
 *
 * Output is one TSV record per statement:
 *
 *   <source tag> <TAB> OK <TAB> <command class> <TAB> <round-trip SQL as UTF-16 hex>
 *   <source tag> <TAB> ERR <TAB> <error code> <TAB> <message as UTF-16 hex>
 *
 * The round-trip SQL matters more than the status: it is the observable output of parsing,
 * and it is what the port has to reproduce. The command class is included because a
 * statement can be valid SQL yet land in a different command type.
 *
 * Usage:
 *   java -cp "tools/out;<h2.jar>" ParseOracle <scripts dir>
 */
import java.io.IOException;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.sql.Connection;
import java.sql.DriverManager;
import java.util.ArrayList;
import java.util.List;
import java.util.stream.Stream;

import org.h2.command.Prepared;
import org.h2.engine.Session;
import org.h2.jdbc.JdbcConnection;
import org.h2.message.DbException;

public class ParseOracle {

    /** In-memory only: nothing is executed and nothing is written to disk. */
    private static final String URL = "jdbc:h2:mem:parseoracle";

    /**
     * Splits a script into statements.
     *
     * Three things end a statement, and getting any of them wrong silently produces SQL
     * that the parser rejects for the wrong reason:
     *
     * <ul>
     * <li>a semicolon, except inside a string literal or a quoted identifier;
     * <li>a line comment, or a (possibly nested) block comment;
     * <li>a directive line, which is not SQL at all.
     * </ul>
     *
     * The directive lines are the part that is easy to miss. H2's script format interleaves
     * the expected output with the statements: a statement, then its result as
     * <pre>
     * &gt; ok 1
     * &gt; rows: 2
     * </pre>
     * and harness control as <pre>
     * &#64;autocommit off
     * &#64;reconnect
     * </pre>
     * Those lines are not SQL, and treating them as such produces syntax errors on
     * statements the reference parses happily. They are recognised only at the start of a
     * line, and never inside a string literal, which may legitimately contain a line
     * beginning with a greater-than sign.
     */
    static List<String> split(String script) {
        List<String> statements = new ArrayList<>();
        StringBuilder current = new StringBuilder();
        int i = 0;
        int length = script.length();
        boolean atLineStart = true;

        while (i < length) {
            char c = script.charAt(i);

            if (atLineStart && (c == '>' || c == '@')) {
                while (i < length && script.charAt(i) != '\n') {
                    i++;
                }

                continue;
            }

            if (c == '-' && i + 1 < length && script.charAt(i + 1) == '-') {
                while (i < length && script.charAt(i) != '\n') {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < length && script.charAt(i + 1) == '*') {
                int depth = 1;
                i += 2;
                while (i < length && depth > 0) {
                    if (i + 1 < length && script.charAt(i) == '/' && script.charAt(i + 1) == '*') {
                        depth++;
                        i += 2;
                    } else if (i + 1 < length && script.charAt(i) == '*'
                            && script.charAt(i + 1) == '/') {
                        depth--;
                        i += 2;
                    } else {
                        i++;
                    }
                }

                atLineStart = false;
                continue;
            }

            if (c == '\'' || c == '"') {
                char quote = c;
                current.append(c);
                i++;
                while (i < length) {
                    char q = script.charAt(i);
                    current.append(q);
                    i++;
                    if (q == quote) {
                        if (i < length && script.charAt(i) == quote) {
                            current.append(quote);
                            i++;
                        } else {
                            break;
                        }
                    }
                }

                atLineStart = false;
                continue;
            }

            if (c == ';') {
                add(statements, current);
                i++;
                atLineStart = false;
                continue;
            }

            current.append(c);
            atLineStart = c == '\n';
            i++;
        }

        add(statements, current);
        return statements;
    }

    /** Adds a statement, collapsing the leading whitespace that separates statements. */
    private static void add(List<String> statements, StringBuilder buffer) {
        String statement = buffer.toString().strip();
        buffer.setLength(0);
        if (!statement.isEmpty()) {
            statements.add(statement);
        }
    }

    /** UTF-16 code units as hex, the transport the differential tests already use. */
    static String hex(String value) {
        StringBuilder out = new StringBuilder();
        for (int i = 0; i < value.length(); i++) {
            out.append(String.format("%04x", (int) value.charAt(i)));
        }

        return out.toString();
    }

    private static String report(Session session, String statement) {
        try {
            var command = session.prepareCommand(statement);
            StringBuilder roundTrip = new StringBuilder();
            if (command instanceof Prepared prepared) {
                roundTrip.append(hex(prepared.getSQL()));
            } else {
                roundTrip.append(hex(String.valueOf(command)));
            }

            return "OK\t" + command.getClass().getSimpleName() + "\t" + roundTrip;
        } catch (DbException e) {
            int code = e.getErrorCode();
            return "ERR\t" + code + "\t" + hex(e.getMessage());
        } catch (RuntimeException e) {
            // Anything that is not a DbException is still a parse failure, and is worth
            // seeing verbatim: it means the reference threw something unexpected.
            return "ERR\t-\t" + hex(e.toString());
        }
    }

    public static void main(String[] args) throws Exception {
        if (args.length != 1) {
            System.err.println("usage: ParseOracle <scripts dir>");
            System.exit(2);
        }

        Path scripts = Path.of(args[0]);

        List<Path> files;
        try (Stream<Path> found = Files.walk(scripts)) {
            files = found.filter(p -> p.toString().endsWith(".sql")).sorted().toList();
        }

        int statements = 0;
        int ok = 0;

        try (Connection connection = DriverManager.getConnection(URL);
                PrintWriter out = new PrintWriter(System.out)) {

            Session session = ((JdbcConnection) connection).getSession();

            for (Path file : files) {
                String script = Files.readString(file, StandardCharsets.UTF_8);
                String tag = scripts.relativize(file).toString().replace('\\', '/');

                for (String statement : split(script)) {
                    statements++;
                    String result = report(session, statement);
                    if (result.startsWith("OK")) {
                        ok++;
                    }

                    out.println(tag + "\t" + result);
                }
            }
        }

        System.err.println("ParseOracle: " + files.size() + " scripts, " + statements
                + " statements, " + ok + " parsed");
    }
}