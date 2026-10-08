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
import java.util.HashMap;
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
import com.sun.source.tree.NewArrayTree;
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

    /**
     * What was found in one Java source file.
     *
     * <p>The token texts are read out of the reference's own table rather than out of the javadoc
     * beside each number: the table is what the reference looks words up in, and the two are not
     * the same thing. A parameter prints as a question mark, and its javadoc says only "Token
     * with parameter", so a table written from the comments has no question mark in it.
     */
    private record Parsed(List<Constant> Constants, Map<String, String> MapEntries,
            List<String> TokenTexts) {}

    public static void main(String[] args) throws IOException {
        if (args.length != 2) {
            System.err.println("usage: PortTables <h2 src/main> <repo root>");
            System.exit(2);
        }

        Path source = Path.of(args[0]);
        Path repo = Path.of(args[1]);

        generateParserUtil(parse(source.resolve("org/h2/util/ParserUtil.java")),
                repo.resolve("src/H2Sharp/Util/ParserUtil.cs"));
        generateToken(
                parse(source.resolve("org/h2/util/ParserUtil.java")),
                parse(source.resolve("org/h2/command/Token.java")),
                repo.resolve("src/H2Sharp/Command/Token.cs"));
        generateValueTypes(parse(source.resolve("org/h2/value/Value.java")),
                repo.resolve("src/H2Sharp/Value/Value.cs"));
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
            List<String> tokenTexts = new ArrayList<>();

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

                    // Token.TOKENS is the table of texts, one entry per token number.
                    if (flags.contains(Modifier.STATIC) && flags.contains(Modifier.FINAL)
                            && "String[]".equals(node.getType().toString())
                            && node.getInitializer() instanceof NewArrayTree array) {
                        for (Tree element : array.getInitializers()) {
                            if (element instanceof LiteralTree literal
                                    && literal.getKind() == Tree.Kind.NULL_LITERAL) {
                                tokenTexts.add(null);
                            } else if (element instanceof LiteralTree literal
                                    && literal.getKind() == Tree.Kind.STRING_LITERAL) {
                                tokenTexts.add(String.valueOf(literal.getValue()));
                            } else {
                                throw new IllegalStateException(
                                        "TOKENS holds something that is not a string literal: "
                                                + element);
                            }
                        }
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

            return new Parsed(constants, entries, tokenTexts);
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

    private static void generateToken(Parsed keywords, Parsed parsed, Path destination) {
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
                 * The constants and the token text table only. The class body is hand written in
                 * Token.Body.cs, so this part stays the numbers and the names, which are what the
                 * parser switches on and what a table cannot be trusted to be reshaped by hand.
                 *
                 * The numbering continues Util/ParserUtil.cs and is a contract: the parser
                 * switches on these values, so they are transcribed from the reference
                 * rather than renumbered.
                 */
                using H2Sharp.Util;

                namespace H2Sharp.Command;

                /// <summary>
                /// A token of a parsed statement: the constants of the kinds of token, the text
                /// each kind is written as, and the body in Token.Body.cs.
                /// </summary>
                public abstract partial class Token
                {
                """);

        for (Constant constant : parsed.Constants()) {
            appendConstant(out, constant, expression(constant.Expression(), local, "ParserUtil"));
        }

        appendTokenTexts(out, keywords, parsed);

        out.append("}\n");
        write(destination, out.toString());
    }

    /**
     * Writes the text of each kind of token, in the same order as the numbers.
     *
     * <p>The reference keeps a parallel array of strings and its own javadoc says what every one of
     * them is, so the table comes from the comments rather than from a second pass over the source:
     * one list of tokens, and a change to a keyword cannot leave the text behind.
     */
    private static void appendTokenTexts(StringBuilder out, Parsed keywords, Parsed parsed) {
        // The table is indexed by token number, so the numbers decide the order and not the order
        // of the declarations. That matters because a number can be declared twice: FIRST_KEYWORD
        // and LAST_KEYWORD are the first and the last keyword under another name, and writing a
        // row for each name would push every later row one place along and print "=" as "&".
        // The numbering also runs on from ParserUtil's keywords into Token's own constants, so both
        // files are resolved into one map before a single row is written.
        Map<String, Integer> numbers = new HashMap<>();
        Map<Integer, List<String>> names = new HashMap<>();
        int last = -1;
        for (Constant constant : keywords.Constants()) {
            last = record(constant, numbers, names, last);
        }
        for (Constant constant : parsed.Constants()) {
            last = record(constant, numbers, names, last);
        }

        // Two independent things have to agree for the table to mean anything: the numbers the
        // constants resolve to, and the length of the reference's table. A table one entry short
        // would otherwise be written out and every token after the gap would print as the wrong
        // thing, silently, which is the one failure this whole exercise is about.
        List<String> texts = parsed.TokenTexts();
        if (texts.size() != last + 1) {
            throw new IllegalStateException("the reference's table holds " + texts.size()
                    + " texts but its numbers run to " + last);
        }

        out.append("""
                    /// <summary>
                    /// The text of every kind of token, in the order of the numbers above.
                    /// </summary>
                    /// <remarks>
                    /// A kind that is not written as itself, UNUSED, KEYWORD, IDENTIFIER, a
                    /// parameter and the end of input, has no text; the reference's table says so
                    /// with nulls and so does this one, because a token that is not a token has
                    /// nothing to print.
                    /// </remarks>
                    internal static readonly string?[] Tokens = [
                """);

        for (int number = 0; number <= last; number++) {
            List<String> declared = names.get(number);
            out.append("    ").append(quote(texts.get(number)))
                    .append(", // ")
                    .append(declared == null ? "Unused" : String.join(", ", declared))
                    .append("\n");
        }

        System.out.println("token texts: " + (last + 1) + " kinds");
        out.append("];\n");
    }

    /**
     * Resolves one declaration to its number, and remembers it as the owner of that number.
     *
     * @return the number of the declaration
     */
    private static int record(Constant constant, Map<String, Integer> numbers,
            Map<Integer, List<String>> names, int last) {
        String expression = constant.Expression().trim();
        Integer number;
        if (expression.endsWith("+ 1")) {
            number = resolve(expression.substring(0, expression.length() - 3).trim(), numbers) + 1;
        } else {
            number = resolve(expression, numbers);
        }
        if (number == null) {
            throw new IllegalStateException(
                    "cannot resolve " + constant.Name() + " = " + expression + " to a number");
        }

        numbers.put(constant.Name(), number.intValue());
        names.computeIfAbsent(number, key -> new ArrayList<>()).add(constant.Name());
        return Math.max(last, number.intValue());
    }

    /** A declaration's expression is a number or the name of one; the first three are numbers. */
    private static Integer resolve(String expression, Map<String, Integer> numbers) {
        Integer number = numbers.get(expression);
        if (number != null) {
            return number;
        }
        try {
            return Integer.valueOf(expression);
        } catch (NumberFormatException e) {
            throw new IllegalStateException("cannot resolve " + expression + " to a number", e);
        }
    }

    /**
     * One entry of the table as C# source: a quoted string, or null for a kind that has none.
     *
     * <p>A C# string literal and a Java one are not the same for a few characters, and a
     * keyword is printed in the middle of a statement, so an operator written with an angle
     * bracket or an ampersand has to be escaped rather than pasted.
     */
    private static String quote(String text) {
        if (text == null) {
            return "null";
        }
        return "\"" + text
                .replace("\\", "\\\\")
                .replace("\"", "\\\"")
                + "\"";
    }

    /**
     * The value type numbers, and nothing else.
     *
     * These are the entry point to the whole value layer: TypeInfo switches on them, the
     * tokenizer recognises literals by them, and DataType is indexed by them. Porting them
     * first is not a leaf, it is what makes the next two files portable at all.
     *
     * Only the constants are generated. org.h2.value.Value is 2693 lines whose behaviour
     * needs the type system, so the class body is deferred and this file is marked
     * partial, in the same spirit as Command/Token.cs.
     */
    private static void generateValueTypes(Parsed parsed, Path destination) {
        Set<String> local = names(parsed);

        StringBuilder out = new StringBuilder();
        out.append("""
                /*
                 * Copyright 2004-2026 H2 Group.
                 * Copyright 2026 H2Sharp contributors.
                 * Ported from org.h2.value.Value (the type number constants).
                 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
                 * Modified: generated by tools/PortTables.java; do not edit by hand.
                 *
                 * Constants only, for the reason given in the class remark. These numbers
                 * are a contract in the strongest sense in this repository: TypeInfo
                 * switches on them, DataType is indexed by them, the tokenizer recognises
                 * literals by them, and they travel in stored rows.
                 *
                 * The namespace is H2Sharp.Values, not the literal H2Sharp.Value, because a
                 * namespace with the same name as a type in it makes `Value.Unknown` bind
                 * to the namespace and fail to compile. Every value type in this layer is
                 * referenced by name, so the collision would be everywhere rather than
                 * somewhere. See docs/PORTING.md, language-level differences.
                 */
                namespace H2Sharp.Values;

                /// <summary>
                /// The value abstraction, and the numbers naming its types.
                /// </summary>
                /// <remarks>
                /// Abstract, because the reference class is abstract and holds instances, and
                /// partial because the constants here are generated while the surface is
                /// ported by hand into the Value.Core, Value.Arithmetic, Value.Compare,
                /// Value.Conversion and Value.Lobs files.
                /// </remarks>
                public abstract partial class Value
                {
                """);

        for (Constant constant : parsed.Constants()) {
            appendConstant(out, constant, expression(constant.Expression(), local, "Value"));
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