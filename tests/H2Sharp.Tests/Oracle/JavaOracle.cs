/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new file for H2Sharp).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Executes the same small programs on the reference JVM (H2's own classes where
 * relevant) and captures the normalised output, so that C# behaviour can be diffed
 * against Java behaviour. This is the primary oracle for the port: it tests
 * behaviour rather than transliteration.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace H2Sharp.Tests.Oracle;

internal static class JavaOracle
{
    /// <summary>
    /// Absolute path to the reference H2 checkout, or null when it is absent.
    /// </summary>
    internal static string? ReferenceRoot { get; } = LocateReferenceRoot();

    /// <summary>
    /// Path to the built H2 jar in the reference checkout, if present.
    /// </summary>
    internal static string? H2Jar { get; } = LocateH2Jar();

    /// <summary>
    /// True when the reference JVM is usable. The built jar is part of the requirement:
    /// every differential test compares against <c>org.h2</c> classes, so a checkout
    /// without a jar is not a usable oracle and must not be reported as "skipping".
    /// </summary>
    internal static bool IsAvailable =>
        ReferenceRoot is not null && FindJavaExecutable() is not null && H2Jar is not null;

    /// <summary>
    /// When this environment variable is set to a non-empty value, an unavailable oracle
    /// is a hard failure instead of a skip. CI sets it for the conformance job so that a
    /// broken checkout, a missing JDK or a jar that failed to build can never be mistaken
    /// for a green run: a silent skip means zero coverage, and zero coverage looks exactly
    /// like success.
    /// </summary>
    internal static bool Required { get; } =
        Environment.GetEnvironmentVariable("H2SHARP_REQUIRE_ORACLE") is { Length: > 0 } value;

    internal static string? SkipReason =>
        IsAvailable ? null
        : ReferenceRoot is null
            ? "reference/h2 is not checked out"
            : FindJavaExecutable() is null
                ? "no java executable on PATH"
                : "reference H2 jar is not built (mvn -B -q -DskipTests package in reference/h2/h2)";

    /// <summary>
    /// The reason an unavailable oracle is a hard failure, or null when it is available.
    /// </summary>
    internal static string? RequiredFailureReason =>
        IsAvailable || !Required ? null
        : "H2SHARP_REQUIRE_ORACLE is set but the reference JVM is unusable: " + SkipReason;

    /// <summary>
    /// Compiles and runs a Java source snippet, returning its stdout.
    /// Throws when the reference JVM is unavailable; callers must check
    /// <see cref="IsAvailable"/> first.
    /// </summary>
    /// <param name="javaSource">the Java source; the public class must be
    /// <paramref name="className"/></param>
    /// <param name="className">the public class name in <paramref name="javaSource"/></param>
    /// <param name="additionalClassPath">extra classpath entries, e.g. the H2 jar</param>
    internal static string Run(
        string javaSource, string className, params string[] additionalClassPath)
    {
        if (ReferenceRoot is null)
        {
            throw new InvalidOperationException("Reference root is not available.");
        }

        string work = PrepareWorkDirectory(className, javaSource, inputs: null, numbers: null);
        try
        {
            string? javac = FindJavacExecutable();
            if (javac is null)
            {
                throw new InvalidOperationException("javac not found");
            }

            RunProcess(javac, work, CompileArguments(work, className, additionalClassPath));

            string java = FindJavaExecutable() ?? throw new InvalidOperationException("java not found");
            return RunProcess(java, work, RunArguments(
                work, className, additionalClassPath, extraJavaArgs: null, programArgs: null, packageName: null));
        }
        finally
        {
            DeleteWorkDirectory(work);
        }
    }

    /// <summary>
    /// Runs a Java snippet that reads its inputs from a sibling <c>inputs.hex</c> file
    /// and writes one output line per input.
    ///
    /// Inputs are transported as the UTF-16 code units in lowercase hex, four digits
    /// per unit, one input per line. Neither UTF-8 nor UTF-16 bytes work here: both
    /// .NET and Java replace lone surrogates when encoding, so a lone surrogate in a
    /// test input would silently become U+FFFD and the two implementations would be
    /// compared on different data. Code units are the only lossless transport.
    ///
    /// For the same reason, snippets are expected to hex-encode their output as
    /// UTF-16LE bytes, which H2's <c>StringUtils.convertBytesToHex</c> does.
    /// </summary>
    /// <param name="javaSource">the Java source</param>
    /// <param name="className">the public class name</param>
    /// <param name="inputs">the inputs, in order</param>
    /// <param name="additionalClassPath">extra classpath entries, e.g. the H2 jar</param>
    /// <param name="extraJavaArgs">extra JVM arguments, e.g. -Duser.language=en</param>
    /// <returns>the output lines joined by newlines, in order</returns>
    internal static string RunWithInputs(
        string javaSource,
        string className,
        IReadOnlyList<string> inputs,
        string[]? additionalClassPath = null,
        string[]? extraJavaArgs = null,
        IReadOnlyList<int>? numbers = null,
        string[]? programArgs = null,
        string? packageName = null)
    {
        if (ReferenceRoot is null)
        {
            throw new InvalidOperationException("Reference root is not available.");
        }

        string work = PrepareWorkDirectory(className, javaSource, inputs, numbers);
        try
        {
            string javac = FindJavacExecutable() ?? throw new InvalidOperationException("javac not found");
            RunProcess(javac, work, CompileArguments(work, className, additionalClassPath));

            string java = FindJavaExecutable() ?? throw new InvalidOperationException("java not found");
            return RunProcess(
                java,
                work,
                RunArguments(work, className, additionalClassPath, extraJavaArgs, programArgs, packageName));
        }
        finally
        {
            DeleteWorkDirectory(work);
        }
    }

    /// <summary>
    /// Splits oracle output into lines, dropping only genuinely empty ones.
    /// </summary>
    /// <remarks>
    /// Lines are <em>not</em> trimmed: leading and trailing whitespace is significant in
    /// the values being compared. Snippets must therefore make every line non-empty on
    /// its own (a "=" prefix is the convention used here) so that a blank line always
    /// signals a real difference.
    /// </remarks>
    internal static List<string> OutputLines(string output)
    {
        List<string> result = new();
        foreach (string line in Normalize(output).Split('\n'))
        {
            if (line.Length > 0)
            {
                result.Add(line);
            }
        }

        return result;
    }

    /// <summary>
    /// Encodes a string as its UTF-16 code units in lowercase hex, four digits each.
    /// </summary>
    internal static string ToCodeUnitsHex(string s)
    {
        StringBuilder builder = new(s.Length * 4);
        foreach (char c in s)
        {
            builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Encodes a string as UTF-16LE bytes in hex, the format the oracle snippets
    /// produce. Lossless because it is a direct byte encoding of each code unit.
    /// </summary>
    internal static string ToUtf16LeBytesHex(string s)
    {
        StringBuilder builder = new(s.Length * 4);
        foreach (char c in s)
        {
            builder.Append(((int)(c & 0xFF)).ToString("x2", CultureInfo.InvariantCulture));
            builder.Append(((int)(c >> 8)).ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Normalises line endings so Java and .NET output can be compared.
    /// </summary>
    internal static string Normalize(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .TrimEnd('\n');

    private static string PrepareWorkDirectory(
        string className, string javaSource, IReadOnlyList<string>? inputs, IReadOnlyList<int>? numbers)
    {
        string work = Path.Combine(
            Path.GetTempPath(), "h2sharp-oracle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        File.WriteAllText(
            Path.Combine(work, className + ".java"), javaSource, new UTF8Encoding(false));

        if (inputs is not null)
        {
            StringBuilder payload = new();
            foreach (string input in inputs)
            {
                payload.Append(ToCodeUnitsHex(input)).Append('\n');
            }

            File.WriteAllText(
                Path.Combine(work, "inputs.hex"), payload.ToString(), new UTF8Encoding(false));

            // Numeric parameters live in their own file so a snippet can read them as
            // int[] without routing them through the string transport.
            StringBuilder numeric = new();
            foreach (int value in numbers ?? [])
            {
                numeric.Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }

            File.WriteAllText(
                Path.Combine(work, "numbers.txt"), numeric.ToString(), new UTF8Encoding(false));
        }

        return work;
    }

    private static string[] CompileArguments(
        string work, string className, string[]? additionalClassPath)
    {
        List<string> args = new() { "-d", work, "-encoding", "UTF-8" };
        if (additionalClassPath is { Length: > 0 })
        {
            args.Add("-cp");
            args.Add(string.Join(Path.PathSeparator, additionalClassPath));
        }

        args.Add(Path.Combine(work, className + ".java"));
        return args.ToArray();
    }

    private static string[] RunArguments(
            string work,
            string className,
            string[]? additionalClassPath,
            string[]? extraJavaArgs,
            string[]? programArgs,
            string? packageName)
    {
        List<string> args = new();
        if (extraJavaArgs is { Length: > 0 })
        {
            args.AddRange(extraJavaArgs);
        }

        args.Add("-cp");
        List<string> classPath = new() { work };
        if (additionalClassPath is { Length: > 0 })
        {
            classPath.AddRange(additionalClassPath);
        }

        args.Add(string.Join(Path.PathSeparator, classPath));

        // A generated class in a package is run by its qualified name; the compiled class file
        // lands under the package directory, which is what makes this necessary.
        args.Add(packageName is null ? className : packageName + "." + className);

        // Arguments for main(), which come after the class name. An oracle that reads a
        // file or a directory needs these; JVM options go in extraJavaArgs instead.
        if (programArgs is { Length: > 0 })
        {
            args.AddRange(programArgs);
        }

        return args.ToArray();
    }

    private static void DeleteWorkDirectory(string work)
    {
        try
        {
            Directory.Delete(work, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    private static string RunProcess(string fileName, string workingDirectory, string[] arguments)
    {
        // A JVM that is started while the machine is busy occasionally fails to get off the
        // ground, and it did so in about one run in five on a loaded machine, which is a test
        // infrastructure failure and not a difference between the two implementations. So a process
        // that exits non-zero is started again once and the second failure is the one reported. A
        // snippet that does not compile is retried too, which costs one extra launch to no purpose,
        // but a compiled snippet whose output differs from the port's is never retried: it is
        // compared, not run again.
        InvalidOperationException? last = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return RunOnce(fileName, workingDirectory, arguments);
            }
            catch (InvalidOperationException e) when (e.Message.Contains("exited with", StringComparison.Ordinal))
            {
                last = e;
            }
        }

        throw last!;
    }

    private static string RunOnce(string fileName, string workingDirectory, string[] arguments)
    {
        ProcessStartInfo psi = new(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,

            // The JVM is told to write UTF-8, and it is read as UTF-8. Without this the output is
            // read in whatever encoding the console of the machine happens to use, which on a
            // Russian machine turns every non-ASCII character of the reference's answer into two
            // characters of nonsense: a mojibake that looks like a difference in the port and is
            // only a difference in the pipe. The snippets that transport text as hex are ASCII and
            // are not affected either way.
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using Process? process = Process.Start(psi);
        if (process is null)
        {
            throw new InvalidOperationException("Could not start " + fileName);
        }

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} exited with {process.ExitCode}.\nstdout:\n{stdout}\nstderr:\n{stderr}");
        }

        return stdout;
    }

    private static string? LocateReferenceRoot()
    {
        // An explicit override, so a CI job or a developer with the checkout somewhere
        // else does not depend on the test binaries sitting under the repository root,
        // and so the "oracle unusable" path can be exercised deliberately.
        string? overridden = Environment.GetEnvironmentVariable("H2SHARP_REFERENCE_ROOT");
        if (!string.IsNullOrEmpty(overridden))
        {
            return Directory.Exists(Path.Combine(overridden, "h2", "src", "main"))
                ? overridden
                : null;
        }

        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(
                    Path.Combine(dir.FullName, "reference", "h2", "h2", "src", "main")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static string? LocateH2Jar()
    {
        if (ReferenceRoot is null)
        {
            return null;
        }

        string targetDir = Path.Combine(ReferenceRoot, "reference", "h2", "h2", "target");
        if (!Directory.Exists(targetDir))
        {
            return null;
        }

        foreach (string file in Directory.EnumerateFiles(targetDir, "h2-*.jar"))
        {
            string name = Path.GetFileName(file);
            if (!name.Contains("sources", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("javadoc", StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }

    private static string? FindJavaExecutable() => FindOnPath("java");

    private static string? FindJavacExecutable() => FindOnPath("javac");

    private static string? FindOnPath(string name)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
        {
            return null;
        }

        string[] extensions = OperatingSystem.IsWindows() ? [".exe", ".cmd", ".bat", ""] : [""];
        foreach (string dir in path.Split(Path.PathSeparator))
        {
            foreach (string extension in extensions)
            {
                string candidate = Path.Combine(dir, name + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
