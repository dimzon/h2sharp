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
    /// True if the reference JVM is usable from this test run.
    /// </summary>
    internal static bool IsAvailable => ReferenceRoot is not null && FindJavaExecutable() is not null;

    /// <summary>
    /// Path to the built H2 jar in the reference checkout, if present.
    /// </summary>
    internal static string? H2Jar { get; } = LocateH2Jar();

    internal static string? SkipReason =>
        IsAvailable ? null
        : ReferenceRoot is null
            ? "reference/h2 is not checked out"
            : "no java executable on PATH";

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

        string work = PrepareWorkDirectory(className, javaSource, inputs: null);
        try
        {
            string? javac = FindJavacExecutable();
            if (javac is null)
            {
                throw new InvalidOperationException("javac not found");
            }

            RunProcess(javac, work, CompileArguments(work, className, additionalClassPath));

            string java = FindJavaExecutable() ?? throw new InvalidOperationException("java not found");
            return RunProcess(java, work, RunArguments(work, className, additionalClassPath, extraJavaArgs: null));
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
        string[]? extraJavaArgs = null)
    {
        if (ReferenceRoot is null)
        {
            throw new InvalidOperationException("Reference root is not available.");
        }

        string work = PrepareWorkDirectory(className, javaSource, inputs);
        try
        {
            string javac = FindJavacExecutable() ?? throw new InvalidOperationException("javac not found");
            RunProcess(javac, work, CompileArguments(work, className, additionalClassPath));

            string java = FindJavaExecutable() ?? throw new InvalidOperationException("java not found");
            return RunProcess(
                java, work, RunArguments(work, className, additionalClassPath, extraJavaArgs));
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
        string className, string javaSource, IReadOnlyList<string>? inputs)
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
        string work, string className, string[]? additionalClassPath, string[]? extraJavaArgs)
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
        args.Add(className);
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
        ProcessStartInfo psi = new(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
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