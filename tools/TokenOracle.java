/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The tokenizer oracle: the reference's own lexical layer, asked what it makes of a statement.
 *
 * The file is in package org.h2.command on purpose. Tokenizer's constructor and its tokenize method
 * are package-private, because nothing outside the package has a reason to read statements; so a
 * helper that wants to drive them has to live in the same package, and the jar on the class path
 * makes that legal without touching the reference checkout.
 *
 * A session is opened rather than a database being constructed, because the tokenizer asks the
 * session for the compatibility mode, and the mode is the only part of the database the lexical
 * layer can see. In-memory, so nothing is executed and nothing is written.
 *
 * Every token is written out with everything the port has to agree about: its kind, where it began,
 * the name it carries, whether it is quoted, and how it prints. That last one is the value for a
 * literal, so a NUMERIC and a DECFLOAT are compared by their digits rather than by the fact that
 * both of them parsed.
 *
 * Input arrives the way every other oracle in this repository takes it: one statement per line of
 * the file, with the characters that would break a line written as \n, \r and \t.
 */
package org.h2.command;

import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.sql.Connection;
import java.sql.DriverManager;
import java.util.ArrayList;
import java.util.BitSet;
import java.util.List;

import org.h2.engine.Session;
import org.h2.jdbc.JdbcConnection;
import org.h2.message.DbException;

/** Reads statements with the reference tokenizer and prints what it read. */
/**
 * Not public, and that is deliberate: the harness writes the source to a file whose name is the
 * class name it is asked to run, and here that name has the package in it, which javac will not
 * accept for a public class. A package-private class may live in any file, and the reference has
 * no rule against a helper that is not meant to be called from outside.
 */
class TokenOracle {

    /** In-memory only: nothing is executed and nothing is written to disk. */
    private static final String URL = "jdbc:h2:mem:tokenoracle";

    /**
     * One token, as one line of output.
     *
     * <p>The fields are separated by a tab, and the text of the token is last because it may
     * contain anything at all, including a tab, which is written as \t.
     */
    private static String describe(Token token) {
        StringBuilder b = new StringBuilder();
        b.append(token.tokenType()).append('\t');
        b.append(token.start()).append('\t');
        b.append(token.asIdentifier() == null ? "-" : escape(token.asIdentifier())).append('\t');
        b.append(token.isQuoted()).append('\t');
        // The end of the statement prints as itself being an object, because it is one that never
        // overrides toString, so there is nothing to compare: "-" says "nothing", on both sides.
        b.append(token instanceof Token.EndOfInputToken ? "-" : escape(token.toString()));
        return b.toString();
    }

    /** Writes a string so that a tab, a newline or a backslash cannot look like a separator. */
    private static String escape(String s) {
        StringBuilder b = new StringBuilder(s.length());
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            switch (c) {
            case '\\':
                b.append("\\\\");
                break;
            case '\t':
                b.append("\\t");
                break;
            case '\r':
                b.append("\\r");
                break;
            case '\n':
                b.append("\\n");
                break;
            default:
                b.append(c);
                break;
            }
        }
        return b.toString();
    }

    /** Reads back what {@link #escape} wrote. */
    private static String unescape(String s) {
        if (s.indexOf('\\') < 0) {
            return s;
        }
        StringBuilder b = new StringBuilder(s.length());
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            if (c != '\\' || i == s.length() - 1) {
                b.append(c);
                continue;
            }
            char next = s.charAt(++i);
            switch (next) {
            case 't':
                b.append('\t');
                break;
            case 'r':
                b.append('\r');
                break;
            case 'n':
                b.append('\n');
                break;
            default:
                b.append(next);
                break;
            }
        }
        return b.toString();
    }

    /**
     * Reads one statement and prints one line: the number of parameters it names, then one line per
     * token, then a blank line. The blank line is what makes a statement that produced no tokens
     * at all still produce something.
     */
    private static String tokenize(Session session, String sql, boolean identifiersToUpper,
            boolean identifiersToLower, BitSet nonKeywords) {
        Tokenizer tokenizer = new Tokenizer(session, identifiersToUpper, identifiersToLower, nonKeywords);
        BitSet parameters = new BitSet();
        StringBuilder b = new StringBuilder();
        try {
            ArrayList<Token> tokens = tokenizer.tokenize(sql, false, parameters);
            b.append("OK ").append(parameters.cardinality());
            for (Token token : tokens) {
                b.append('\n').append(describe(token));
            }
        } catch (DbException e) {
            // The code and the whole message, because the message is where the place in the
            // statement is: "Syntax error in SQL statement "SELECT ~;" [22000-207]" says more than
            // the code alone, and the port builds the same message from the same two arguments.
            b.append("ERR ").append(e.getErrorCode()).append(' ').append(stripTrace(e.getMessage()));
        } catch (RuntimeException e) {
            b.append("THROWS ").append(e.getClass().getSimpleName());
        }
        return b.toString();
    }

    /**
     * Takes off the "[42000-259]" the JDBC layer adds to a message.
     *
     * <p>That suffix is added when the reference's message becomes a SQLException, so it is a
     * property of the JDBC layer rather than of the tokenizer, and the port does not build it. It
     * is taken off here so that the comparison is about the message the tokenizer's error carries.
     */
    private static String stripTrace(String message) {
        if (message.endsWith("]")) {
            int open = message.lastIndexOf(" [");
            if (open > 0) {
                String tail = message.substring(open + 2, message.length() - 1);
                int dash = tail.indexOf('-');
                if (dash > 0 && isDigits(tail, 0, dash) && isDigits(tail, dash + 1, tail.length())) {
                    return message.substring(0, open);
                }
            }
        }
        return message;
    }

    private static boolean isDigits(String s, int start, int end) {
        if (start >= end) {
            return false;
        }
        for (int i = start; i < end; i++) {
            if (s.charAt(i) < '0' || s.charAt(i) > '9') {
                return false;
            }
        }
        return true;
    }

    public static void main(String[] args) throws Exception {
        if (args.length != 1) {
            System.err.println("usage: TokenOracle <statements file>");
            System.exit(2);
        }

        List<String> statements = new ArrayList<>();
        for (String line : Files.readAllLines(Path.of(args[0]), StandardCharsets.UTF_8)) {
            statements.add(unescape(line));
        }

        try (Connection connection = DriverManager.getConnection(URL);
                java.io.PrintWriter out = new java.io.PrintWriter(new java.io.OutputStreamWriter(System.out, StandardCharsets.UTF_8))) {
            Session session = ((JdbcConnection) connection).getSession();

            for (String statement : statements) {
                // Three combinations of folding, because a name is folded or it is not and the
                // tokenizer must not depend on which: the words below are all names in one of
                // these settings and keywords in another.
                //
                // One line per answer, and the line breaks inside an answer are written out as \n:
                // a statement may produce any number of tokens and this way a failure names the
                // statement it is about instead of the token it happened to be on.
                out.println(escape(tokenize(session, statement, false, false, null)));
                out.println(escape(tokenize(session, statement, true, false, null)));
                out.println(escape(tokenize(session, statement, false, true, null)));
            }
        }
    }
}