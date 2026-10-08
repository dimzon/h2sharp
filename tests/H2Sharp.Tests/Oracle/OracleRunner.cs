/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Shared scaffolding for the differential tests: wraps a snippet body into a Java
 * program and runs it against the reference H2 jar.
 *
 * Extracted from StringUtilsOracleTests because the second differential suite needed the
 * same 60 lines of Java preamble, and a copy of it would be a copy that drifts.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace H2Sharp.Tests.Oracle;

/// <summary>
/// Runs a Java snippet against the reference H2 and returns its stdout lines.
/// </summary>
internal static class OracleRunner
{
    /// <summary>
    /// The class name every generated program uses. The Java sidecar is compiled into a
    /// per-class working directory, so the name has to be stable.
    /// </summary>
    private const string ClassName = "Oracle";

    /// <summary>
    /// Runs a snippet and returns its output lines.
    /// </summary>
    /// <param name="inputs">strings passed as UTF-16 code units in hex; null cannot be
    /// transported, use <see cref="NullMarker"/></param>
    /// <param name="body">Java statements placed inside main</param>
    /// <param name="numbers">ints passed as numbers.txt, for snippets that cannot use
    /// collection initialisers</param>
    /// <param name="extraJavaArgs">extra JVM arguments, e.g. -Duser.language=en</param>
    /// <returns>the output lines, each prefixed with '=' by the Java helper so that an
    /// empty result survives</returns>
    internal static List<string> Run(
        IReadOnlyList<string> inputs,
        string body,
        IReadOnlyList<int>? numbers = null,
        string[]? extraJavaArgs = null)
        => Run(inputs, body, numbers, extraJavaArgs, null);

    /// <summary>
    /// Runs a snippet and returns its output lines.
    /// </summary>
    /// <remarks>
    /// The package exists for the few snippets that must reach a member the reference keeps
    /// package-private — the JSON value classes, whose constructors are not public, are the
    /// reason it is here at all. Everything else uses the overload without it and stays in the
    /// default package, where nothing of the reference is reachable by accident.
    /// </remarks>
    /// <param name="inputs">strings passed as UTF-16 code units in hex; null cannot be
    /// transported, use <see cref="NullMarker"/></param>
    /// <param name="body">Java statements placed inside main</param>
    /// <param name="numbers">ints passed as numbers.txt, for snippets that cannot use
    /// collection initialisers</param>
    /// <param name="extraJavaArgs">extra JVM arguments, e.g. -Duser.language=en</param>
    /// <param name="packageName">the package to generate the class in, or null for none</param>
    /// <returns>the output lines, each prefixed with '=' by the Java helper so that an
    /// empty result survives</returns>
    internal static List<string> Run(
        IReadOnlyList<string> inputs,
        string body,
        IReadOnlyList<int>? numbers,
        string[]? extraJavaArgs,
        string? packageName)
    {
        if (JavaOracle.H2Jar is null)
        {
            throw new InvalidOperationException(
                "H2 jar not found. Build it with: "
                + "(cd reference/h2/h2 && mvn -B -q -DskipTests package)");
        }

        return JavaOracle.OutputLines(
            JavaOracle.RunWithInputs(
                Program(body, packageName),
                ClassName,
                inputs,
                [JavaOracle.H2Jar],
                extraJavaArgs,
                numbers,
                programArgs: null,
                packageName));
    }

    /// <summary>
    /// The marker that stands in for a null argument, which cannot travel through the hex
    /// transport. The Java side converts it back to null via <c>nullable()</c>.
    /// </summary>
    internal const string NullMarker = "\u0000<NULL>";

    /// <summary>
    /// UTF-16 code units as hex, the same shape the Java <c>hex()</c> helper produces, so
    /// the two sides can be compared directly and lone surrogates survive the round trip.
    /// </summary>
    internal static string Hex(string? value)
    {
        if (value is null)
        {
            return "=<null>";
        }

        StringBuilder builder = new(value.Length * 4 + 1);
        builder.Append('=');
        foreach (char c in value)
        {
            builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Builds a complete Java program. The body goes inside main, wrapped in a
    /// try/catch so a Java-side exception is reported as "THROWS" instead of aborting the
    /// run.
    ///
    /// The snippet can read:
    ///   inputs - the strings from inputs.hex
    ///   nums   - the ints from numbers.txt
    /// and can call:
    ///   nullable(String) - turns <see cref="NullMarker"/> back into null
    ///   hex(String)      - UTF-16 code units as hex, '=' prefixed
    /// </summary>
    private static string Program(string body, string? packageName)
    {
        string declaration = packageName is null ? string.Empty : $"package {packageName};{Environment.NewLine}{Environment.NewLine}";
        return declaration
            + """
        import java.nio.file.Files;
        import java.nio.file.Path;

        public class Oracle {
          static final String NULL_MARKER = "\u0000<NULL>";
          static String[] inputs;
          static int[] nums;

          public static void main(String[] args) throws Exception {
            // The default encoding of the console is whatever the machine says, and a snippet
            // that prints non-ASCII text would be compared against a mangled answer rather
            // than against ours. Writing UTF-8 explicitly is what makes a printed string
            // comparable at all.
            System.setOut(new java.io.PrintStream(
                new java.io.FileOutputStream(java.io.FileDescriptor.out), true, "UTF-8"));
            inputs = readInputs();
            nums = readNumbers();
            try {
        BODY
            } catch (RuntimeException e) {
              System.out.println("THROWS");
            }
          }

          static String nullable(String s) {
            return s.equals(NULL_MARKER) ? null : s;
          }

          static String hex(String s) {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < s.length(); i++) {
              b.append(String.format("%04x", (int) s.charAt(i)));
            }
            // The "=" keeps an empty result visible: the harness drops blank lines.
            return "=" + b.toString();
          }

          static String[] readInputs() throws Exception {
            java.util.List<String> lines = Files.readAllLines(Path.of("inputs.hex"));
            String[] out = new String[lines.size()];
            for (int i = 0; i < lines.size(); i++) {
              StringBuilder b = new StringBuilder();
              String line = lines.get(i);
              for (int j = 0; j + 3 < line.length(); j += 4) {
                b.append((char) Integer.parseInt(line.substring(j, j + 4), 16));
              }
              out[i] = b.toString();
            }
            return out;
          }

          static int[] readNumbers() throws Exception {
            if (!Files.exists(Path.of("numbers.txt"))) {
              return new int[0];
            }
            java.util.List<String> lines = Files.readAllLines(Path.of("numbers.txt"));
            int[] out = new int[lines.size()];
            for (int i = 0; i < lines.size(); i++) {
              out[i] = Integer.parseInt(lines.get(i).trim());
            }
            return out;
          }
        }
        """
            .Replace("BODY", body);
    }
}
