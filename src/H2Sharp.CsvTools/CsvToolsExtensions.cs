/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The optional CSV functions. They are not default functions: the connection registers them
 * before Open, like every other host-provided UDF.
 */
using System;
using H2Sharp.Data;

namespace H2Sharp.CsvTools;

/// <summary>Registers the CSVREAD and CSVWRITE functions on a connection.</summary>
public static class CsvToolsExtensions
{
    /// <summary>
    /// Registers CSVREAD (1–7 arguments) and CSVWRITE (2–8 arguments). The functions are
    /// non-deterministic, so a prepared statement that uses one is recompiled, and CSVWRITE
    /// runs its query in the transaction of the statement that called it.
    /// </summary>
    /// <param name="extensions">the extension surface of a closed connection</param>
    public static void UseCsvTools(this IH2Extensions extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        CsvReadFunction.Register(extensions);
        CsvWriteFunction.Register(extensions);
    }
}
