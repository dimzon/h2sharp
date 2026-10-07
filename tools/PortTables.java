/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Generates the C# token tables from H2's Java sources, using the JDK's own parser and
 * writing the C# directly.
 *
 * Why this is Java and not a script over regexes: javac ships a complete Java parser as
 * com.sun.source, reachable through JavacTask, so no dependency is needed beyond the JDK
 * this repository already requires. The AST also supplies the javadoc, and scraping a
 * comment block by walking lines upwards is a heuristic that fails silently. Emitting the
 * C# from here rather than dumping an intermediate format keeps the whole table, its
 * documentation and its naming rules in one place, instead of splitting them across a
 * dumper and a formatter that can disagree.
 *
 * The constant expressions are passed through rather than evaluated on purpose: the
 * reference numbers its tokens by chaining `PREV + 1`, and keeping that shape in the C#
 * leaves the ordering visible and reviewable.
 *
 * Usage:
 *   javac -d tools/out tools/PortTables.java
 *   java -cp tools/out PortTables <reference/h2/h2/src/main> <repo root>
 */
import java.io.IOException;
import java.io.PrintWriter;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.TreeSet;

import javax.lang.model.element.Modifier;
import javax.tools.JavaCompiler;
import javax.tools.JavaFileObject;
import javax.tools.StandardJavaFileManager;
import javax.tools.ToolProvider;

import com.sun.source.doctree.DocCommentTree;
import com.sun.source.tree.CompilationUnitTree;
import com.sun.source.tree.IdentifierTree;
import com.sun.source.tree.LiteralTree;
import com.sun.source.tree.MemberSelectTree;
import com.sun.source.tree.MethodInvocationTree;
import com.sun.source.tree.Tree;
import com.sun.source.tree.VariableTree;
import com.sun.source.util.DocTrees;
import com.sun.source.util.JavacTask;
import com.sun.source.util.TreePath;
import com.sun.source.util.TreePathScanner;

public class PortTables {

    private static final String INDENT = "    ";

    /** One `static final int` declaration, with the javadoc that documents it. */
    private record Constant(String Name, String Expression, String Summary, String TokenText) {}

    /** What was found in one Java source file. */
    private record Parsed(List<Constant> Constants, Map<String, String> MapEntries) {}

    public static void main(String[] args) throws IOException {
        if (args.length != 2) {
            System.err.println("usage: PortTables <h2 src/main> <repo root>");
            System.exit(2);
        }

        Path source = Path.of(args[0]);
        Path repo = Path.of(args[1]);

        generateParserUtil(parse(source.resolve("org/h2/util/ParserUtil.java")),
                repo.resolve("src/H2Sharp/Util/ParserUtil.cs"));
        generateToken(parse(source.resolve("org/h2/command/Token.java")),
                repo.resolve("src/H2Sharp/Command/Token.cs"));
    }

    // ------------------------------------------------------------------ parsing

    private static Parsed parse(Path file) throws IOException {
        JavaCompiler compiler = ToolProvider.getSystemJavaCompiler();
        if (compiler == null) {
            throw new IllegalStateException("no system java compiler; a JDK is required");
        }

        try (StandardJavaFileManager fileManager =
                compiler.getStandardFileManager(null, null, null)) {

            List<JavaFileObject> sources = new ArrayList<>();
            fileManager.getJavaFileObjects(file).forEach(sources::add);

            JavacTask task = (JavacTask) compiler.getTask(
                    null, fileManager, null, List.of("-proc:none"), null, sources);

            CompilationUnitTree unit = task.parse().iterator().next();
            DocTrees docs = DocTrees.instance(task);

            List<Constant> constants = new ArrayList<>();
            // A LinkedHashMap keeps the reference's declaration order for diagnostics while
            // the duplicate check below stays order independent.
            Map<String, String> entries = new LinkedHashMap<>();

            new TreePathScanner<Void, Void>() {
                @Override
                public Void visitVariable(VariableTree node, Void unused) {
                    Set<Modifier> flags = node.getModifiers().getFlags();
                    if (flags.contains(Modifier.STATIC)
                            && flags.contains(Modifier.FINAL)
                            && "int".equals(node.getType().toString())
                            && node.getInitializer() != null) {
                        TreePath path = getCurrentPath();
                        constants.add(new Constant(
                                node.getName().toString(),
                                oneLine(node.getInitializer().toString()),
                                summary(docs, path, node.getName().toString()),
                                tokenText(docs, path)));
                    }

                    return super.visitVariable(node, unused);
                }

                @Override
                public Void visitMethodInvocation(MethodInvocationTree node, Void unused) {
                    Tree select = node.getMethodSelect();
                    String called = select instanceof MemberSelectTree member
                            ? member.getIdentifier().toString()
                            : select.toString();

                    // org.h2.util.ParserUtil builds its lookup with a local map; the call
                    // shape is matched instead of the variable name so a rename in the
                    // reference does not silently produce an empty table.
                    if ("put".equals(called) && node.getArguments().size() == 2) {
                        Tree key = node.getArguments().get(0);
                        Tree value = node.getArguments().get(1);
                        if (key instanceof LiteralTree literal
                                && literal.getKind() == Tree.Kind.STRING_LITERAL
                                && value instanceof IdentifierTree id) {
                            String token = String.valueOf(literal.getValue());
                            String target = id.getName().toString();
                            String previous = entries.put(token, target);
                            if (previous != null && !previous.equals(target)) {
                                throw new IllegalStateException(
                                        "keyword " + token + " mapped to both "
                                                + previous + " and " + target);
                            }
                        }
                    }

                    return super.visitMethodInvocation(node, unused);
                }
            }.scan(unit, null);

            if (constants.isEmpty()) {
                throw new IllegalStateException(
                        "no constants found in " + file + "; the reference layout changed");
            }

            return new Parsed(constants, entries);
        }
    }

    private static String oneLine(String text) {
        return text.replaceAll("\\s+", " ").trim();
    }

    private static DocCommentTree doc(DocTrees docs, TreePath path) {
        return docs.getDocCommentTree(path);
    }

    /** First sentence of the javadoc, or the constant name when it has none. */
    private static String summary(DocTrees docs, TreePath path, String fallback) {
        DocCommentTree comment = doc(docs, path);
        if (comment != null && comment.getFirstSentence() != null) {
            return oneLine(comment.getFirstSentence().toString());
        }

        return fallback;
    }

    /**
     * The token text a javadoc names, e.g. {@code The token "ALL".} becomes ALL.
     * ParserUtil keys its lookup by that text, so the doc comment and the map have to
     * agree, which is why both come from the AST rather than one from the AST and one
     * from a regex.
     */
    private static String tokenText(DocTrees docs, TreePath path) {
        DocCommentTree comment = doc(docs, path);
        if (comment == null) {
            return "";
        }

        String text = comment.toString();
        int open = text.indexOf('"');
        if (open < 0) {
            return "";
        }

        int close = text.indexOf('"', open + 1);
        return close < 0 ? "" : text.substring(open + 1, close);
    }

    // ------------------------------------------------------------- naming rules

    /** SNAKE_CASE -> PascalCase; _ROWID_ keeps its underscores. */
    private static String toPascal(String name) {
        if (name.startsWith("_") && name.endsWith("_")) {
            return name;
        }

        StringBuilder out = new StringBuilder(name.length());
        for (String part : name.split("_")) {
            if (part.isEmpty()) {
                continue;
            }

            out.append(Character.toUpperCase(part.charAt(0)));
            out.append(part.substring(1).toLowerCase(java.util.Locale.ROOT));
        }

        return out.toString();
    }

    /**
     * Rewrites the Java identifiers in a constant expression to their C# names. Identifiers
     * not declared in the same file arrive through a static import, which in C# becomes a
     * qualified reference; {@code owner} supplies the declaring type.
     */
    private static String expression(String expr, Set<String> local, String owner) {
        StringBuilder out = new StringBuilder();
        int i = 0;
        while (i < expr.length()) {
            char c = expr.charAt(i);
            // A Java identifier may start with '_', and _ROWID_ is exactly such a case. A
            // scanner that starts on isLetter(_) misses the leading underscore, splits the
            // name, and then qualifies a name that was local all along.
            if (Character.isJavaIdentifierStart(c)) {
                int start = i;
                while (i < expr.length() && Character.isJavaIdentifierPart(expr.charAt(i))) {
                    i++;
                }

                String word = expr.substring(start, i);
                boolean isConstantName = word.equals(word.toUpperCase(java.util.Locale.ROOT));
                out.append(isConstantName ? qualified(word, local, owner) : word);
                continue;
            }

            out.append(c);
            i++;
        }

        return out.toString();
    }

    private static String qualified(String word, Set<String> local, String owner) {
        return local.contains(word) ? toPascal(word) : owner + "." + toPascal(word);
    }

    // ------------------------------------------------------------- rendering

    /*
     * The rendering below does not try to lay code out. Indentation, wrapping and spacing
     * are `dotnet format`'s job, driven by .editorconfig, so the generator only has to emit
     * readable code: templates with newlines in sensible places, no manual alignment, no
     * column bookkeeping. That is what keeps this file short instead of turning it into a
     * miniature pretty printer.
     */

    private static void generateParserUtil(Parsed parsed, Path destination) {
        Set<String> local = names(parsed);

        StringBuilder out = new StringBuilder();
        out.append("""
                /*
                 * Copyright 2004-2026 H2 Group.
                 * Copyright 2026 H2Sharp contributors.
                 * Ported from org.h2.util.ParserUtil.
                 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
                 * Modified: generated by tools/PortTables.java; do not edit by hand.
                 *
                 * The token values are a contract, not an implementation detail: the parser
                 * switches on them and they are compared against values stored in H2
                 * metadata, so they are transcribed from the reference rather than
                 * renumbered.
                 */
                using System;
                using System.Collections.Frozen;
                using System.Collections.Generic;

                namespace H2Sharp.Util;

                /// <summary>
                /// Token constants and keyword lookup.
                /// </summary>
                public static partial class ParserUtil
                {
                """);

        for (Constant constant : parsed.Constants()) {
            appendConstant(out, constant, expression(constant.Expression(), local, "ParserUtil"));
        }

        out.append("""
                    /// <summary>
                    /// The token text to token type, with <see cref="StringComparer.Ordinal"/>
                    /// equality. A frozen dictionary because it is built once and then only
                    /// read: it keeps the lookup allocation free without a lazy
                    /// initialisation check on every call.
                    /// </summary>
                    private static readonly FrozenDictionary<string, int> Keywords
                        = new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                """);

        for (Map.Entry<String, String> entry : parsed.MapEntries().entrySet()) {
            out.append("[\"").append(entry.getKey()).append("\"] = ")
                    .append(toPascal(entry.getValue())).append(",\n");
        }

        out.append("""
                            }.ToFrozenDictionary(StringComparer.Ordinal);
                        }
                """);

        write(destination, out.toString());
    }

    private static void generateToken(Parsed parsed, Path destination) {
        Set<String> local = names(parsed);

        StringBuilder out = new StringBuilder();
        out.append("""
                /*
                 * Copyright 2004-2026 H2 Group.
                 * Copyright 2026 H2Sharp contributors.
                 * Ported from org.h2.command.Token.
                 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
                 * Modified: generated by tools/PortTables.java; do not edit by hand.
                 *
                 * Constants only. The reference class is an abstract token whose value()
                 * builds org.h2.value.Value instances, and the Value layer is not ported
                 * yet, so the class body is deferred rather than half transliterated. The
                 * dependency order is recorded in MIGRATION_STATUS.md: the Value layer is
                 * the gate for both Tokenizer and Parser.
                 *
                 * The numbering continues Util/ParserUtil.cs and is a contract: the parser
                 * switches on these values, so they are transcribed from the reference
                 * rather than renumbered.
                 */
                using H2Sharp.Util;

                namespace H2Sharp.Command;

                /// <summary>
                /// Token type constants for the parser.
                /// </summary>
                public static partial class Token
                {
                """);

        for (Constant constant : parsed.Constants()) {
            appendConstant(out, constant, expression(constant.Expression(), local, "ParserUtil"));
        }

        out.append("}\n");
        write(destination, out.toString());
    }

    private static Set<String> names(Parsed parsed) {
        Set<String> local = new TreeSet<>();
        parsed.Constants().forEach(c -> local.add(c.Name()));
        return local;
    }

    private static void appendConstant(StringBuilder out, Constant constant, String rendered) {
        out.append("/// <summary>\n")
                .append("/// ").append(constant.Summary()).append("\n")
                .append("/// </summary>\n")
                .append("public const int ").append(toPascal(constant.Name()))
                .append(" = ").append(rendered).append(";\n\n");
    }

    private static void write(Path destination, String content) {
        try {
            Files.createDirectories(destination.getParent());
            Files.writeString(destination, content, StandardCharsets.UTF_8);
            System.out.println("wrote " + destination);
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }
}