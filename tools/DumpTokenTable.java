/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Dumps the `static final int` tables and the map entries of a Java source file, using
 * the JDK's own parser instead of regular expressions.
 *
 * javac ships a complete Java parser as com.sun.source, reachable through JavacTask, so
 * this needs no dependency beyond the JDK that tools/regen.py already requires for the
 * case-folding probe. Using the AST also gets the javadoc: guessing where a comment block
 * starts by walking lines upwards is a heuristic, and a heuristic that fails silently
 * writes a wrong token table.
 *
 * Output is TSV, one record per line:
 *
 *   CONST <name> <expression source> <summary> <token text>
 *   PUT   <token text> <constant name>
 *
 * The expression is passed through rather than evaluated on purpose: the reference numbers
 * its tokens by chaining `PREV + 1`, and keeping that shape in the generated C# leaves the
 * ordering visible and reviewable.
 *
 * Usage:
 *   javac -d tools/out tools/DumpTokenTable.java
 *   java -cp tools/out DumpTokenTable org/h2/util/ParserUtil.java
 */
import java.io.IOException;
import java.io.PrintWriter;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;

import javax.lang.model.element.Modifier;
import javax.tools.JavaCompiler;
import javax.tools.JavaFileObject;
import javax.tools.StandardJavaFileManager;
import javax.tools.ToolProvider;

import com.sun.source.tree.CompilationUnitTree;
import com.sun.source.doctree.DocCommentTree;
import com.sun.source.doctree.DocCommentTable;
import com.sun.source.util.DocTrees;
import com.sun.source.tree.IdentifierTree;
import com.sun.source.tree.LiteralTree;
import com.sun.source.tree.MemberSelectTree;
import com.sun.source.tree.MethodInvocationTree;
import com.sun.source.tree.Tree;
import com.sun.source.tree.VariableTree;
import com.sun.source.util.JavacTask;
import com.sun.source.util.TreePathScanner;
import com.sun.source.util.TreePath;
import com.sun.source.util.Trees;

public class DumpTokenTable {

    private static String oneLine(String text) {
        return text.replaceAll("\\s+", " ").trim();
    }

    /** The javadoc attached to a declaration, or null when it has none. */
    private static DocCommentTree doc(DocTrees docs, TreePath path) {
        DocCommentTable table = docs.getDocCommentTable();
        return table.getDocCommentTree(path);
    }

    /**
     * The token text a javadoc names, e.g. `The token "ALL".` becomes ALL. ParserUtil keys
     * its lookup by that text, so the doc comment and the map have to agree, which is why
     * the text is extracted from the AST rather than scraped.
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

    /** First sentence of the javadoc, or the constant name when it has none. */
    private static String summary(DocTrees docs, TreePath path, String fallback) {
        DocCommentTree comment = doc(docs, path);
        if (comment != null && comment.getFirstSentence() != null) {
            return oneLine(comment.getFirstSentence().toString());
        }

        return fallback;
    }

    public static void main(String[] args) throws IOException {
        if (args.length != 1) {
            System.err.println("usage: DumpTokenTable <file.java>");
            System.exit(2);
        }

        JavaCompiler compiler = ToolProvider.getSystemJavaCompiler();
        if (compiler == null) {
            System.err.println("ERROR: no system java compiler; a JDK is required");
            System.exit(1);
        }

        try (StandardJavaFileManager fileManager =
                compiler.getStandardFileManager(null, null, null);
                PrintWriter out = new PrintWriter(System.out)) {

            List<JavaFileObject> sources = List.copyOf(fileManager.getJavaFileObjects(Path.of(args[0])));

            JavacTask task = (JavacTask) compiler.getTask(
                    null,
                    fileManager,
                    null,
                    List.of("-proc:none"),
                    null,
                    sources);

            CompilationUnitTree unit = task.parse().iterator().next();
            DocTrees docs = DocTrees.instance(task);
            List<String> lines = new ArrayList<>();

            new TreePathScanner<Void, Void>() {
                @Override
                public Void visitVariable(VariableTree node, Void unused) {
                    Set<Modifier> flags = node.getModifiers().getFlags();
                    if (flags.contains(Modifier.STATIC)
                            && flags.contains(Modifier.FINAL)
                            && "int".equals(node.getType().toString())
                            && node.getInitializer() != null) {
                        String name = node.getName().toString();
                        // Whitespace in the source expression is not significant and would
                        // make the TSV awkward to consume.
                        String expr = oneLine(node.getInitializer().toString());
                        lines.add(String.join("\t",
                                "CONST", name, expr, summary(docs, getCurrentPath(), name),
                                tokenText(docs, getCurrentPath())));
                    }

                    return super.visitVariable(node, unused);
                }

                @Override
                public Void visitMethodInvocation(MethodInvocationTree node, Void unused) {
                    Tree select = node.getMethodSelect();
                    String called = select instanceof MemberSelectTree member
                            ? member.getIdentifier().toString()
                            : select.toString();

                    if ("put".equals(called) && node.getArguments().size() == 2) {
                        Tree key = node.getArguments().get(0);
                        Tree value = node.getArguments().get(1);
                        if (key instanceof LiteralTree literal
                                && literal.getKind() == Tree.Kind.STRING_LITERAL
                                && value instanceof IdentifierTree id) {
                            lines.add(String.join("\t",
                                    "PUT", String.valueOf(literal.getValue()), id.getName().toString()));
                        }
                    }

                    return super.visitMethodInvocation(node, unused);
                }
            }.scan(unit, null);

            lines.forEach(out::println);
        }
    }
}