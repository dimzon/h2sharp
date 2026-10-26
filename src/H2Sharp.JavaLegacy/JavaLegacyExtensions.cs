/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using H2Sharp.Data;

namespace H2Sharp.JavaLegacy;

/// <summary>Registers the Java-compatible parts of H2's behavior.</summary>
public static class JavaLegacyExtensions
{
    /// <summary>
    /// Uses the JDK random generator, so RAND(seed) follows the same sequence as H2 on the JVM.
    /// </summary>
    /// <param name="extensions">the closed connection's extension surface</param>
    public static void UseJavaLegacy(this IH2Extensions extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        extensions.RegisterRandomGenerator(seed => new JavaRandom(seed ?? Environment.TickCount64));
        if (extensions is not IH2RawExtensions raw)
        {
            throw new InvalidOperationException(
                "JavaLegacy requires the H2Sharp connection extension implementation");
        }

        OraHashFunction.Register(raw);
    }
}
