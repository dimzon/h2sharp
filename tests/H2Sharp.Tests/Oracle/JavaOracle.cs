/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new file for H2Sharp).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Executes the same small programs on the reference JVM (H2's own classes where
 * relevant) and captures the normalised output, so that C# behaviour can be diffed
 * against Java behaviour. Used by the UUID and number/temporal formatting tests.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    internal static string Run(string javaSource, string className, params string[] additionalClassPath)
    {
        if (ReferenceRoot is null)
        {
            throw new InvalidOperationException("Reference root is not available.");
        }

        string java = FindJavaExecutable() ?? throw new InvalidOperationException("java not found");
        string javac = FindJavacExecutable() ?? throw new InvalidOperationException("javac not found");

        string work = Path.Combine(Path.GetTempPath(), "h2sharp-oracle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string sourceFile = Path.Combine(work, className + ".java");
            File.WriteAllText(sourceFile, javaSource, new UTF8Encoding(false));

            List<string> javacArgs = new() { "-d", work };
            if (additionalClassPath.Length > 0)
            {
                javacArgs.Add("-cp");
                javacArgs.Add(string.Join(Path.PathSeparator, additionalClassPath));
            }

            javacArgs.Add(sourceFile);
            RunProcess(javac, work, javacArgs.ToArray());

            List<string> javaArgs = new() { "-cp", work };
            if (additionalClassPath.Length > 0)
            {
                javaArgs[1] = string.Join(Path.PathSeparator, Prepend(work, additionalClassPath));
            }

            javaArgs.Add(className);
            return RunProcess(java, work, javaArgs.ToArray());
        }
        finally
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
    }

    private static string Prepend(string first, string[] rest)
    {
        List<string> all = new() { first };
        all.AddRange(rest);
        return string.Join(Path.PathSeparator, all);
    }

    private static string RunProcess(string fileName, string workingDirectory, params string[] arguments)
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

        using Process process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start " + fileName);
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
            string candidate = Path.Combine(dir.FullName, "reference", "h2", "h2", "src", "main");
            if (Directory.Exists(candidate))
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
            if (!Path.GetFileName(file).Contains("sources", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(file).Contains("javadoc", StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }

    private static string? FindJavaExecutable()
        => FindOnPath("java");

    private static string? FindJavacExecutable()
        => FindOnPath("javac");

    private static string? FindOnPath(string name)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
        {
            return null;
        }

        string[] extensions = OperatingSystem.IsWindows()
            ? [".exe", ".cmd", ".bat", ""]
            : [""];

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

    /// <summary>
    /// Normalises line endings so Java and .NET output can be compared.
    /// </summary>
    internal static string Normalize(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .TrimEnd('\n');

    /// <summary>
    /// Runs <paramref name="cases"/> through both implementations and returns the
    /// pairs that disagree.
    /// </summary>
    internal static IReadOnlyList<string> Compare(
        IReadOnlyList<(string Name, string Expected, string Actual)> cases)
    {
        List<string> mismatches = new();
        foreach ((string name, string expected, string actual) in cases)
        {
            if (!string.Equals(Normalize(expected), Normalize(actual), StringComparison.Ordinal))
            {
                mismatches.Add(
                    $"{name}:\n  java: {Normalize(expected)}\n  csharp: {Normalize(actual)}");
            }
        }

        return mismatches;
    }
}