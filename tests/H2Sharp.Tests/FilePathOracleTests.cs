/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for path resolution.
 *
 * A file system cannot be compared against the reference — a disk is not a function of its input —
 * but which file system a path resolves to, and what that file system's name is, is exactly that.
 * This is the part the store depends on when it reads a database URL out of a connection string,
 * and the part where a mistake is silent: a path that opens the wrong file works perfectly.
 *
 * The corpus covers the shapes a connection string actually carries, including the ones that are
 * schemes and the one that is not: "C:" is a drive letter, so the text before its colon is a single
 * character and there is no scheme there at all.
 *
 * Three of the reference's file systems are not ported — the split file, the compressing file and
 * the encrypted ones — and a path naming one of them falls back to the disk here, where it would
 * not there. Those prefixes are left out of the corpus, and the gap is in MIGRATION_STATUS.md.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using H2Sharp.Store.FileSystem;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class FilePathOracleTests
{
    private static readonly string[] Paths =
    [
        "x.h2.db",
        "./x.h2.db",
        "../db/x.h2.db",
        "/var/lib/db/x.h2.db",
        "file:/tmp/x.h2.db",
        "file:x.h2.db",
        "nio:/tmp/x.h2.db",
        "nio:x.h2.db",
        "memFS:x.h2.db",
        "memFS:",
        "memFS:/named/with/slashes.h2.db",
        "memFS:some\\where\\x.h2.db",
        "mem:x.h2.db",
        "mem:",
        "mem:/named/with/slashes.h2.db",
        "nosuchscheme:/tmp/x.h2.db",
        "split:/tmp/x.h2.db",
        "split:26:/tmp/x.h2.db",
        "split:30:/tmp/x.h2.db",
        "C:/data/x.h2.db",
        "c:/data/x.h2.db",
        "C:data/x.h2.db",
        "some\\where\\x.h2.db",
        "file:some\\where\\x.h2.db",
        "~/x.h2.db",
        "~",
        "~user/x.h2.db",
        "a~/x.h2.db",
        ":leading",
        ":",
        "::",
        "a:b",
        "a:b:c",
        "ab:cd:ef",
        "file:",
        "file::x",
    ];

    [JavaOracleFact]
    public void PathsResolveToTheSameFileSystemAndTheSameName()
    {
        string body = """
                      for (int i = 0; i < paths.length; i++) {
                        System.out.println(hexText.apply(describe.apply(paths[i])));
                      }
            """;

        List<string> java = OracleRunner.Run(
                [],
                Setup + Declare + body,
                extraJavaArgs: ["-Duser.language=en", "-Duser.country=", "-Duser.variant="])
            .Select(Decode)
            .Select(line => TraceSuffix.Replace(line, string.Empty))
            .ToList();

        List<string> csharp = Paths.Select(Describe).ToList();

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static string Describe(string path)
    {
        try
        {
            FilePath p = FilePath.Get(path);
            FilePath? parent = p.GetParent();
            FieldList line = new();
            _ = line
                .Text(p.GetScheme())
                .Text(p.Name)
                .Text(p.GetName())
                .Text(p.ToString())
                .Mark(p.IsAbsolute())
                .Mark(p.IsDirectory())
                .Mark(p.IsRegularFile())
                .Mark(p.Exists())
                .Text(parent is null ? null : parent.Name)

                // Only an absolute path can be resolved to a real path in a way both sides would
                // agree on: a relative one becomes absolute against the working directory of the
                // process, and the two processes do not have the same one. So the field is left out
                // rather than filled with an answer that is about the environment and not the port.
                .Text(p.IsAbsolute() ? p.ToRealPath().Name : null);
            return line.Build();
        }
        catch (Exception e) when (e is not H2SharpException)
        {
            return "EX";
        }
    }

    private static readonly System.Text.RegularExpressions.Regex TraceSuffix =
        new(@"\s\[\d+-\d+\]", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private const string Declare = """
                      String[] paths = new String[] {
                          "x.h2.db", "./x.h2.db", "../db/x.h2.db", "/var/lib/db/x.h2.db",
                          "file:/tmp/x.h2.db", "file:x.h2.db", "nio:/tmp/x.h2.db", "nio:x.h2.db",
                          "memFS:x.h2.db", "memFS:", "memFS:/named/with/slashes.h2.db",
                          "memFS:some\\where\\x.h2.db",
                          "mem:x.h2.db", "mem:", "mem:/named/with/slashes.h2.db",
                          "nosuchscheme:/tmp/x.h2.db",
                          "split:/tmp/x.h2.db", "split:26:/tmp/x.h2.db", "split:30:/tmp/x.h2.db",
                          "C:/data/x.h2.db", "c:/data/x.h2.db", "C:data/x.h2.db",
                          "some\\where\\x.h2.db", "file:some\\where\\x.h2.db",
                          "~/x.h2.db", "~", "~user/x.h2.db", "a~/x.h2.db",
                          ":leading", ":", "::", "a:b", "a:b:c", "ab:cd:ef", "file:", "file::x"
                      };

            """;

    private const string Setup = """
                      java.util.function.Function<String, String> hexText = s -> {
                        StringBuilder b = new StringBuilder();
                        for (int i = 0; i < s.length(); i++) {
                          b.append(String.format("%04x", (int) s.charAt(i)));
                        }
                        return b.length() == 0 ? "-" : b.toString();
                      };
                      java.util.function.Function<String, String> describe = s -> {
                        org.h2.store.fs.FilePath p = org.h2.store.fs.FilePath.get(s);
                        try {
                          org.h2.store.fs.FilePath parent = p.getParent();
                          return p.getScheme() + "|"
                              + p.name + "|"
                              + p.getName() + "|"
                              + p.toString() + "|"
                              + p.isAbsolute() + "|"
                              + p.isDirectory() + "|"
                              + p.isRegularFile() + "|"
                              + p.exists() + "|"
                              + (parent == null ? "-" : parent.name) + "|"
                              + (p.isAbsolute() ? p.toRealPath().name : "-");
                        } catch (Throwable e) {
                          // Every failure is an answer: a path the system will not take is a thing
                          // the port has to answer the same way, and the loop must not stop at it.
                          // Only the fact of the failure is compared — the reference's text is the
                          // JDK's, and reproducing another runtime's message word for word would be
                          // a translation, not a port.
                          return "EX";
                        }
                      };

            """;

    /// <summary>
    /// Builds the pipe-separated line both sides build: the answers as they are, separated, with
    /// "-" where a path has no parent, because a field that is missing would shift every field
    /// after it and the comparison would be of the wrong things.
    /// </summary>
    private sealed class FieldList
    {
        private readonly System.Text.StringBuilder parts = new();

        public FieldList Text(string? text)
        {
            _ = parts.Append(text ?? "-").Append('|');
            return this;
        }

        public FieldList Mark(bool value)
        {
            _ = parts.Append(value ? "true" : "false").Append('|');
            return this;
        }

        public string Build()
        {
            string s = parts.ToString();
            return s.EndsWith('|') ? s[..^1] : s;
        }
    }

    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        if (!only.All(static c => char.IsAsciiHexDigit(c) || c == '-'))
        {
            throw new InvalidOperationException($"Not hex from the oracle: '{hex}'");
        }

        if (only == "-")
        {
            return string.Empty;
        }

        // Four hexadecimal digits per character, in the order the character is, which is what
        // String.format("%04x", (int) c) prints — not two bytes to read back in any order.
        System.Text.StringBuilder sb = new();
        for (int i = 0; i + 3 < only.Length; i += 4)
        {
            _ = sb.Append((char)Convert.ToInt32(only.Substring(i, 4), 16));
        }

        return sb.ToString();
    }
}
