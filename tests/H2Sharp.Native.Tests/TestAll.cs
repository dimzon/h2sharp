// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.TestAll.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: TestAll in the reference is both the configuration the tests read and the
// runner that drives them (a main() method, the test lists, beforeTest/afterTest, the
// output catcher). The runner half is what xUnit does instead, so only the
// configuration survives here: the fields every test reads through
// TestBase.config. The field set and defaults are the reference's own.
namespace H2Sharp.Native.Tests;

/// <summary>
/// The test configuration. The reference class also runs the tests; here the runner is
/// xUnit, and what remains is the configuration.
/// </summary>
public class TestAll
{
    /// <summary>
    /// If the test should run with many rows.
    /// </summary>
    public bool Big;

    /// <summary>
    /// If remote database connections should be used.
    /// </summary>
    public bool Networked;

    /// <summary>
    /// If in-memory databases should be used.
    /// </summary>
    public bool Memory;

    /// <summary>
    /// If code coverage is enabled.
    /// </summary>
    public bool CodeCoverage;

    /// <summary>
    /// If lazy queries should be used.
    /// </summary>
    public bool Lazy;

    /// <summary>
    /// The cipher to use (null for unencrypted).
    /// </summary>
    public string? Cipher;

    /// <summary>
    /// The file trace level value to use.
    /// </summary>
    public int TraceLevelFile;

    /// <summary>
    /// If test trace information should be written (for debugging only).
    /// </summary>
    public bool TraceTest;

    /// <summary>
    /// If testing on Google App Engine.
    /// </summary>
    public bool GoogleAppEngine;

    /// <summary>
    /// If a small cache and a low number for MAX_MEMORY_ROWS should be used.
    /// </summary>
    public bool DiskResult;

    /// <summary>
    /// Test using the recording file system.
    /// </summary>
    public bool Reopen;

    /// <summary>
    /// Test the split file system.
    /// </summary>
    public bool SplitFileSystem;

    /// <summary>
    /// If only fast CI tests should be run.
    /// </summary>
    public bool Ci;

    /// <summary>
    /// the vmlens.com race condition tool
    /// </summary>
    public bool Vmlens;

    /// <summary>
    /// The lock timeout to use.
    /// </summary>
    public int LockTimeout = 50;

    /// <summary>
    /// If the transaction log should be kept small (that is, the log should be
    /// switched early).
    /// </summary>
    public bool SmallLog;

    /// <summary>
    /// If SSL should be used for remote connections.
    /// </summary>
    public bool Ssl;

    /// <summary>
    /// If MAX_MEMORY_UNDO=3 should be used.
    /// </summary>
    public bool DiskUndo;

    /// <summary>
    /// If TRACE_LEVEL_SYSTEM_OUT should be set to 2 (for debugging only).
    /// </summary>
    public bool TraceSystemOut;

    /// <summary>
    /// If the tests should run forever.
    /// </summary>
    public bool Endless;

    /// <summary>
    /// The THROTTLE value to use.
    /// </summary>
    public int Throttle;

    /// <summary>
    /// The THROTTLE value to use by default.
    /// </summary>
    /// <remarks>
    /// The reference reads it from the "throttle" system property; a .NET process is
    /// configured the same way through the H2SHARP_THROTTLE environment variable.
    /// </remarks>
    public int ThrottleDefault = int.TryParse(
        Environment.GetEnvironmentVariable("H2SHARP_THROTTLE"), out int throttle)
        ? throttle : 0;

    /// <summary>
    /// If the test should stop when the first error occurs.
    /// </summary>
    public bool StopOnError;

    /// <summary>
    /// The cache type.
    /// </summary>
    public string? CacheType;

    /// <summary>
    /// If not null the database should be opened with the collation parameter.
    /// </summary>
    public string? Collation;

    /// <summary>
    /// The TCP port the test server listens on.
    /// </summary>
    /// <returns>the port</returns>
    /// <remarks>
    /// The reference answers with the port of the TCP server it started for the networked
    /// configuration; the port has no server yet, so this answers with the default port
    /// the reference answers when no server is running.
    /// </remarks>
    public int GetPort()
    {
        return 9192;
    }
}
