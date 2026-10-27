// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.TestBase.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (test framework): the reference throws java.lang.AssertionError from fail();
// here that is H2Sharp.Native.Tests.AssertionError, because .NET has no such runtime type.
// The runner methods (runTest, testFromMain) are what xUnit does instead and are not
// ported; the temporary directory System.setProperty("java.io.tmpdir") is likewise
// runner work, not test work.
//
// Modified (API): the reference helpers take java.sql.Statement and java.sql.ResultSet;
// the port's provider surface is ADO.NET, so they take System.Data.Common.DbCommand and
// System.Data.Common.DbDataReader. JDBC columns are 1-based, ADO.NET ordinals 0-based;
// the helpers absorb the shift. SQLException.getErrorCode() is H2SharpException.H2ErrorCode.
// java.sql.Types passed to assertResultSetMeta becomes System.Type (GetFieldType), because
// the port has no java.sql.Types constants.
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using H2Sharp;
using H2Sharp.Store.FileSystem;
using H2Sharp.Util;

namespace H2Sharp.Native.Tests;

/// <summary>
/// The base class for all tests.
/// </summary>
public abstract class TestBase
{
    /// <summary>
    /// The base directory.
    /// </summary>
    public const string BaseTestDir = "./data";

    /// <summary>
    /// An id used to create unique file names.
    /// </summary>
    protected static int uniqueId;

    /// <summary>
    /// The base directory to write test databases.
    /// </summary>
    private static string baseDir = GetTestDir("");

    /// <summary>
    /// The test configuration.
    /// </summary>
    public TestAll Config = new();

    /// <summary>
    /// The time when the test was started.
    /// </summary>
    protected long start;

    /// <summary>
    /// Get the test directory for this test.
    /// </summary>
    /// <param name="name">the directory name suffix</param>
    /// <returns>the test directory</returns>
    public static string GetTestDir(string name)
    {
        return BaseTestDir + "/test" + name;
    }

    /// <summary>
    /// Initialize the test configuration using the default settings.
    /// </summary>
    /// <returns>itself</returns>
    public TestBase Init()
    {
        return Init(new TestAll());
    }

    /// <summary>
    /// Initialize the test configuration.
    /// </summary>
    /// <param name="conf">the configuration</param>
    /// <returns>itself</returns>
    public TestBase Init(TestAll conf)
    {
        baseDir = GetTestDir("");
        FileUtils.CreateDirectories(baseDir);
        Config = conf;
        return this;
    }

    /// <summary>
    /// Get the password to use to login for the given user password. The file
    /// password is added if required.
    /// </summary>
    /// <param name="userPassword">the password of this user</param>
    /// <returns>the login password</returns>
    protected string GetPassword(string userPassword)
    {
        return Config is null || Config.Cipher is null
            ? userPassword : GetFilePassword() + " " + userPassword;
    }

    /// <summary>
    /// Get the file password (only required if file encryption is used).
    /// </summary>
    /// <returns>the file password</returns>
    protected string GetFilePassword()
    {
        return "filePassword";
    }

    /// <summary>
    /// Get the login password. This is usually the user password. If file
    /// encryption is used it is combined with the file password.
    /// </summary>
    /// <returns>the login password</returns>
    protected string GetPassword()
    {
        return GetPassword("123");
    }

    /// <summary>
    /// Get the base directory for tests.
    /// If a special file system is used, the prefix is prepended.
    /// </summary>
    /// <returns>the directory, possibly including file system prefix</returns>
    public string GetBaseDir()
    {
        string dir = baseDir;
        if (Config is not null)
        {
            if (Config.Reopen)
            {
                dir = "rec:memFS:" + dir;
            }

            if (Config.SplitFileSystem)
            {
                dir = "split:16:" + dir;
            }
        }

        return dir;
    }

    /// <summary>
    /// Get the small or the big value depending on the configuration.
    /// </summary>
    /// <param name="small">the value to return if the current test mode is 'small'</param>
    /// <param name="big">the value to return if the current test mode is 'big'</param>
    /// <returns>small or big, depending on the configuration</returns>
    protected int GetSize(int small, int big)
    {
        return Config.Endless ? int.MaxValue : Config.Big ? big : small;
    }

    protected string GetUser()
    {
        return "sa";
    }

    /// <summary>
    /// Write a message to system out if trace is enabled.
    /// </summary>
    /// <param name="x">the value to write</param>
    protected void Trace(int x)
    {
        Trace("" + x);
    }

    /// <summary>
    /// Write a message to system out if trace is enabled.
    /// </summary>
    /// <param name="s">the message to write</param>
    public void Trace(string s)
    {
        if (Config.TraceTest)
        {
            Println(s);
        }
    }

    /// <summary>
    /// Print how much memory is currently used.
    /// </summary>
    protected void TraceMemory()
    {
        if (Config.TraceTest)
        {
            Trace("mem=" + GetMemoryUsed());
        }
    }

    /// <summary>
    /// Print the currently used memory, the message and the given time in
    /// milliseconds.
    /// </summary>
    /// <param name="s">the message</param>
    /// <param name="time">the time in millis</param>
    public void PrintTimeMemory(string s, long time)
    {
        if (Config.Big)
        {
            long memNow = GC.GetTotalMemory(forceFullCollection: false);
            Println(memNow / 1024 / 1024 + " MB: " + s + " ms: " + time);
        }
    }

    /// <summary>
    /// Get the number of megabytes heap memory in use.
    /// </summary>
    /// <returns>the used megabytes</returns>
    /// <remarks>
    /// The reference walks Runtime.gc() until the number stops falling; the managed heap
    /// answers the same question through GC.GetTotalMemory with a full collection.
    /// </remarks>
    public static int GetMemoryUsed()
    {
        return (int)(GetMemoryUsedBytes() / 1024 / 1024);
    }

    /// <summary>
    /// Get the number of bytes heap memory in use.
    /// </summary>
    /// <returns>the used bytes</returns>
    public static long GetMemoryUsedBytes()
    {
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    /// <summary>
    /// Called if the test reached a point that was not expected.
    /// </summary>
    public void Fail()
    {
        Fail("Failure");
    }

    /// <summary>
    /// Called if the test reached a point that was not expected.
    /// </summary>
    /// <param name="s">the error message</param>
    /// <remarks>
    /// The reference marks the message with a null while scrubbing it and rebuilds the
    /// string only when something was scrubbed; the flag here carries the same "something
    /// was scrubbed" answer without a nullable walk.
    /// </remarks>
    protected void Fail(string s)
    {
        if (s.Length > 100)
        {
            // avoid long strings with special characters, because they are slow
            // to display in Eclipse
            char[] data = s.ToCharArray();
            bool scrubbed = false;
            for (int i = 0; i < data.Length; i++)
            {
                char c = data[i];
                if (c >= 128 || c < 32)
                {
                    data[i] = (char)('a' + (c & 15));
                    scrubbed = true;
                }
            }

            if (scrubbed)
            {
                s = new string(data);
            }
        }

        Println(s);
        throw new AssertionError(s);
    }

    /// <summary>
    /// Log an error message.
    /// </summary>
    /// <param name="s">the message</param>
    public static void LogErrorMessage(string s)
    {
        Console.Out.Flush();
        Console.Error.WriteLine("ERROR: " + s + "------------------------------");
        LogThrowable(s, null);
    }

    /// <summary>
    /// Log an error message.
    /// </summary>
    /// <param name="s">the message</param>
    /// <param name="e">the exception</param>
    public static void LogError(string s, Exception? e)
    {
        Console.Out.Flush();
        Console.Error.WriteLine("ERROR: " + s + " " + e
            + " ------------------------------");
        Console.Error.WriteLine(e);
        LogThrowable(null, e);
    }

    private static void LogThrowable(string? s, Exception? e)
    {
        // The reference serializes the error to error.txt under a cross-process file
        // lock, because several JVMs write it at once. The tests of one process run
        // under one lock already; appending is the same record without the lock.
        lock (typeof(TestBase))
        {
            try
            {
                using StreamWriter writer = File.AppendText("error.txt");
                if (s is not null)
                {
                    writer.Write(s);
                }

                if (e is not null)
                {
                    writer.Write(e.ToString());
                    writer.WriteLine();
                }
            }
            catch (Exception)
            {
                // the reference swallows the failure the same way
            }
        }

        Console.Error.Flush();
    }

    /// <summary>
    /// Print a message to system out.
    /// </summary>
    /// <param name="s">the message</param>
    public void Println(string s)
    {
        long now = Stopwatch.GetTimestamp();
        long time = (now - start) * 1000 / Stopwatch.Frequency;
        PrintlnWithTime(time, GetType().Name + ' ' + s);
    }

    /// <summary>
    /// Print a message, prepended with the specified time in milliseconds.
    /// </summary>
    /// <param name="millis">the time in milliseconds</param>
    /// <param name="s">the message</param>
    private static void PrintlnWithTime(long millis, string s)
    {
        StringBuilder builder = new(s.Length + 19);
        builder.Append(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        Console.WriteLine(FormatTime(builder.Append(' '), millis).Append(' ').Append(s).ToString());
    }

    /// <summary>
    /// Print the current time and a message to system out.
    /// </summary>
    /// <param name="s">the message</param>
    protected void PrintTime(string s)
    {
        StringBuilder builder = new(s.Length + 9);
        builder.Append(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        Println(builder.Append(' ').Append(s).ToString());
    }

    /// <summary>
    /// Format the time in the format mm:ss.123 or hh:mm:ss.123 where 123 is
    /// milliseconds.
    /// </summary>
    /// <param name="builder">the string builder to append to</param>
    /// <param name="millis">the time in milliseconds, non-negative</param>
    /// <returns>the specified string builder</returns>
    internal static StringBuilder FormatTime(StringBuilder builder, long millis)
    {
        int s = (int)(millis / 1_000);
        int m = s / 60;
        s %= 60;
        int h = m / 60;
        if (h != 0)
        {
            builder.Append(h).Append(':');
            m %= 60;
        }

        _ = StringUtils.AppendTwoDigits(builder, m).Append(':');
        _ = StringUtils.AppendTwoDigits(builder, s).Append('.');
        _ = StringUtils.AppendZeroPadded(builder, 3, (int)(millis % 1_000));
        return builder;
    }

    /// <summary>
    /// Whether this test is enabled in the current configuration.
    /// </summary>
    /// <returns><c>true</c> when the test runs</returns>
    public virtual bool IsEnabled()
    {
        return true;
    }

    /// <summary>
    /// This method will be called by the test framework.
    /// </summary>
    public abstract void Test();

    /// <summary>
    /// Check if two values are equal, and if not throw an exception.
    /// </summary>
    /// <param name="message">the message to print in case of error</param>
    /// <param name="expected">the expected value</param>
    /// <param name="actual">the actual value</param>
    public void AssertEquals(string message, int expected, int actual)
    {
        if (expected != actual)
        {
            Fail("Expected: " + expected + " actual: " + actual + " message: " + message);
        }
    }

    /// <summary>
    /// Check if two values are equal, and if not throw an exception.
    /// </summary>
    /// <param name="expected">the expected value</param>
    /// <param name="actual">the actual value</param>
    public void AssertEquals(int expected, int actual)
    {
        if (expected != actual)
        {
            Fail("Expected: " + expected + " actual: " + actual);
        }
    }

    /// <summary>
    /// Check if two values are equal, and if not throw an exception.
    /// </summary>
    /// <param name="expected">the expected value</param>
    /// <param name="actual">the actual value</param>
    public void AssertEquals(byte[]? expected, byte[]? actual)
    {
        if (expected is null || actual is null)
        {
            AssertTrue(expected == actual);
            return;
        }

        AssertEquals(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i])
            {
                Fail("[" + i + "]: expected: " + expected[i]
                    + " actual: " + actual[i]);
            }
        }
    }

    /// <summary>
    /// Check if two values are equal, and if not throw an exception.
    /// </summary>
    /// <param name="expected">the expected value</param>
    /// <param name="actual">the actual value</param>
    /// <remarks>
    /// The reference takes java.util.Date; the provider hands out DateTime.
    /// </remarks>
    public void AssertEquals(DateTime? expected, DateTime? actual)
    {
        if (!EqualityComparer<DateTime?>.Default.Equals(expected, actual))
        {
            Fail("Expected: "
                + (expected.HasValue ? FormatDate(expected.Value) : "null")
                + " actual: "
                + (actual.HasValue ? FormatDate(actual.Value) : "null"));
        }
    }

    private static string FormatDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fff",
            CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Check if two arrays are equal, and if not throw an exception.
    /// If some of the elements in the arrays are themselves arrays this
    /// check is called recursively.
    /// </summary>
    /// <param name="expected">the expected value</param>
    /// <param name="actual">the actual value</param>
    public void AssertEquals(object?[]? expected, object?[]? actual)
    {
        if (expected is null || actual is null)
        {
            AssertTrue(expected == actual);
            return;
        }

        AssertEquals(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] is null || actual[i] is null)
            {
                if (expected[i] != actual[i])
                {
                    Fail("[" + i + "]: expected: " + expected[i] + " actual: " + actual[i]);
                }
            }
            else if (expected[i] is object?[] expectedArray
                && actual[i] is object?[] actualArray)
            {
                AssertEquals(expectedArray, actualArray);
            }
            else if (!Equals(expected[i], actual[i]))
            {
                Fail("[" + i + "]: expected: " + expected[i] + " actual: " + actual[i]);
            }
        }
    }

    /// <summary>
    /// Check if two values are equal, and if not throw an exception.
    /// </summary>
    /// <param name="expected">the expected value</param>
    /// <param name="actual">the actual value</param>
    public void AssertEquals(object? expected, object? actual)
    {
        if (!Equals(expected, actual))
        {
            Fail(" expected: " + expected + " actual: " + actual);
        }
    }

    /// <summary>
    /// Check if two readers are equal, and if not throw an exception.
    /// </summary>
    /// <param name="expected">the expected value</param>
    /// <param name="actual">the actual value</param>
    /// <param name="len">the maximum length, or -1</param>
    protected void AssertEqualReaders(TextReader expected, TextReader actual, int len)
    {
        for (int i = 0; len < 0 || i < len; i++)
        {
            int ce = expected.Read();
            int ca = actual.Read();
            AssertEquals("pos:" + i, ce, ca);
            if (ce == -1)
            {
                break;
            }
        }

        expected.Close();
        actual.Close();
    }

    /// <summary>
    /// Check if two streams are equal, and if not throw an exception.
    /// </summary>
    /// <param name="expected">the expected value</param>
    /// <param name="actual">the actual value</param>
    /// <param name="len">the maximum length, or -1</param>
    protected void AssertEqualStreams(Stream expected, Stream actual, int len)
    {
        // this doesn't actually read anything - just tests reading 0 bytes
        _ = actual.Read(new byte[0], 0, 0);
        _ = expected.Read(new byte[0], 0, 0);
        _ = actual.Read(new byte[10], 3, 0);
        _ = expected.Read(new byte[10], 0, 0);

        for (int i = 0; len < 0 || i < len; i++)
        {
            int ca = actual.ReadByte();
            _ = actual.Read(new byte[0], 0, 0);
            int ce = expected.ReadByte();
            if (ca != ce)
            {
                AssertEquals("Error at index " + i, ce, ca);
            }

            if (ca == -1)
            {
                break;
            }
        }

        _ = actual.Read(new byte[10], 3, 0);
        _ = expected.Read(new byte[10], 0, 0);
        _ = actual.Read(new byte[0], 0, 0);
        _ = expected.Read(new byte[0], 0, 0);
        actual.Close();
        expected.Close();
    }

    /// <summary>
    /// Check if two values are equal, and if not throw an exception.
    /// </summary>
    /// <param name="message">the message to use if the check fails</param>
    /// <param name="expected">the expected value</param>
    /// <param name="actual">the actual value</param>
    protected void AssertEquals(string message, string? expected, string? actual)
    {
        if (expected is null && actual is null)
        {
            return;
        }
        else if (expected is null || actual is null)
        {
            Fail("Expected: " + expected + " Actual: " + actual + " " + message);
        }
        else if (!expected.Equals(actual, StringComparison.Ordinal))
        {
            int al = expected.Length;
            int bl = actual.Length;
            for (int i = 0; i < expected.Length; i++)
            {
                string s = expected.Substring(0, i);
                if (!actual.StartsWith(s, StringComparison.Ordinal))
                {
                    expected = expected.Substring(0, i) + "<*>" + expected.Substring(i);
                    if (al > 20)
                    {
                        expected = "@" + i + " " + expected;
                    }

                    break;
                }
            }

            if (al > 4000)
            {
                expected = expected.Substring(0, 4000);
            }

            if (bl > 4000)
            {
                actual = actual.Substring(0, 4000);
            }

            Fail("Expected: " + expected + " (" + al + ") actual: " + actual
                + " (" + bl + ") " + message);
        }
    }

    /// <summary>
    /// Check if two values are equal, and if not throw an exception.
    /// </summary>
    /// <param name="expected">the expected value</param>
    /// <param name="actual">the actual value</param>
    protected void AssertEquals(string? expected, string? actual)
    {
        AssertEquals("", expected, actual);
    }

    /// <summary>
    /// Check if two result sets are equal, and if not throw an exception.
    /// </summary>
    /// <param name="message">the message to use if the check fails</param>
    /// <param name="rs0">the first result set</param>
    /// <param name="rs1">the second result set</param>
    protected void AssertEquals(string message, DbDataReader rs0, DbDataReader rs1)
    {
        int columns = rs0.FieldCount;
        AssertEquals(columns, rs1.FieldCount);
        while (rs0.Read())
        {
            AssertTrue(message, rs1.Read());
            for (int i = 0; i < columns; i++)
            {
                AssertEquals(message, rs0.GetString(i), rs1.GetString(i));
            }
        }

        AssertFalse(message, rs0.Read());
        AssertFalse(message, rs1.Read());
    }

    /// <summary>
    /// Check if two objects are the same, and if not throw an exception.
    /// </summary>
    /// <param name="expected">the expected value</param>
    /// <param name="actual">the actual value</param>
    public void AssertSame(object? expected, object? actual)
    {
        if (!ReferenceEquals(expected, actual))
        {
            Fail(" expected: " + expected + " != actual: " + actual);
        }
    }

    /// <summary>
    /// Check if the first value is larger or equal than the second value, and if
    /// not throw an exception.
    /// </summary>
    /// <param name="a">the first value</param>
    /// <param name="b">the second value (must be smaller than the first value)</param>
    protected void AssertSmaller(long a, long b)
    {
        if (a >= b)
        {
            Fail("a: " + a + " is not smaller than b: " + b);
        }
    }

    /// <summary>
    /// Check that a result contains the given substring.
    /// </summary>
    /// <param name="result">the result value</param>
    /// <param name="contains">the term that should appear in the result</param>
    protected void AssertContains(string result, string contains)
    {
        if (!result.Contains(contains, StringComparison.Ordinal))
        {
            Fail(result + " does not contain: " + contains);
        }
    }

    /// <summary>
    /// Check that a result does not contain the given substring.
    /// </summary>
    /// <param name="result">the result value</param>
    /// <param name="shallNotContain">the term that must not appear in the result</param>
    protected void AssertNotContaining(string result, string shallNotContain)
    {
        if (result.Contains(shallNotContain, StringComparison.Ordinal))
        {
            Fail(result + " still contains: " + shallNotContain);
        }
    }

    /// <summary>
    /// Check that a text starts with the expected characters..
    /// </summary>
    /// <param name="text">the text</param>
    /// <param name="expectedStart">the expected prefix</param>
    protected void AssertStartsWith(string text, string expectedStart)
    {
        if (!text.StartsWith(expectedStart, StringComparison.Ordinal))
        {
            Fail("[" + text + "] does not start with: [" + expectedStart + "]");
        }
    }

    /// <summary>
    /// Check if two values are equal, and if not throw an exception.
    /// </summary>
    /// <param name="expected">the expected value</param>
    /// <param name="actual">the actual value</param>
    protected void AssertEquals(long expected, long actual)
    {
        if (expected != actual)
        {
            Fail("Expected: " + expected + " actual: " + actual);
        }
    }

    /// <summary>
    /// Check if two values are equal, and if not throw an exception.
    /// </summary>
    /// <param name="expected">the expected value</param>
    /// <param name="actual">the actual value</param>
    protected void AssertEquals(double expected, double actual)
    {
        if (expected != actual)
        {
            if (double.IsNaN(expected) && double.IsNaN(actual))
            {
                // if both a NaN, then there is no error
            }
            else
            {
                Fail("Expected: " + expected + " actual: " + actual);
            }
        }
    }

    /// <summary>
    /// Check if two values are equal, and if not throw an exception.
    /// </summary>
    /// <param name="expected">the expected value</param>
    /// <param name="actual">the actual value</param>
    protected void AssertEquals(float expected, float actual)
    {
        if (expected != actual)
        {
            if (float.IsNaN(expected) && float.IsNaN(actual))
            {
                // if both a NaN, then there is no error
            }
            else
            {
                Fail("Expected: " + expected + " actual: " + actual);
            }
        }
    }

    /// <summary>
    /// Check if two values are equal, and if not throw an exception.
    /// </summary>
    /// <param name="expected">the expected value</param>
    /// <param name="actual">the actual value</param>
    protected void AssertEquals(bool expected, bool actual)
    {
        if (expected != actual)
        {
            Fail("Boolean expected: " + expected + " actual: " + actual);
        }
    }

    /// <summary>
    /// Check that the passed boolean is true.
    /// </summary>
    /// <param name="condition">the condition</param>
    public void AssertTrue(bool condition)
    {
        if (!condition)
        {
            Fail("Expected: true got: false");
        }
    }

    /// <summary>
    /// Check that the passed object is null.
    /// </summary>
    /// <param name="obj">the object</param>
    public void AssertNull(object? obj)
    {
        if (obj is not null)
        {
            Fail("Expected: null got: " + obj);
        }
    }

    /// <summary>
    /// Check that the passed String is empty.
    /// </summary>
    /// <param name="s">the object</param>
    public void AssertEmpty(string? s)
    {
        if (s is not null && s.Length != 0)
        {
            Fail("Expected: empty String but got: " + s);
        }
    }

    /// <summary>
    /// Check that the passed object is not null.
    /// </summary>
    /// <param name="obj">the object</param>
    public void AssertNotNull(object? obj)
    {
        if (obj is null)
        {
            Fail("Expected: not null got: null");
        }
    }

    /// <summary>
    /// Check that the passed object is not null.
    /// </summary>
    /// <param name="message">the message to print if the condition is false</param>
    /// <param name="obj">the object</param>
    public void AssertNotNull(string message, object? obj)
    {
        if (obj is null)
        {
            Fail(message);
        }
    }

    /// <summary>
    /// Check that the passed boolean is true.
    /// </summary>
    /// <param name="message">the message to print if the condition is false</param>
    /// <param name="condition">the condition</param>
    public void AssertTrue(string message, bool condition)
    {
        if (!condition)
        {
            Fail(message);
        }
    }

    /// <summary>
    /// Check that the passed boolean is false.
    /// </summary>
    /// <param name="value">the condition</param>
    protected void AssertFalse(bool value)
    {
        if (value)
        {
            Fail("Expected: false got: true");
        }
    }

    /// <summary>
    /// Check that the passed boolean is false.
    /// </summary>
    /// <param name="message">the message to print if the condition is false</param>
    /// <param name="value">the condition</param>
    protected void AssertFalse(string message, bool value)
    {
        if (value)
        {
            Fail(message);
        }
    }

    /// <summary>
    /// Check that the result set row count matches.
    /// </summary>
    /// <param name="expected">the number of expected rows</param>
    /// <param name="rs">the result set</param>
    protected void AssertResultRowCount(int expected, DbDataReader rs)
    {
        int i = 0;
        while (rs.Read())
        {
            i++;
        }

        AssertEquals(expected, i);
    }

    /// <summary>
    /// Check that the result set of a query is exactly this value.
    /// </summary>
    /// <param name="stat">the statement</param>
    /// <param name="sql">the SQL statement to execute</param>
    /// <param name="expected">the expected result value</param>
    protected void AssertSingleValue(DbCommand stat, string sql, int expected)
    {
        stat.CommandText = sql;
        using DbDataReader rs = stat.ExecuteReader();
        AssertTrue(rs.Read());
        AssertEquals(expected, rs.GetInt32(0));
        AssertFalse(rs.Read());
    }

    /// <summary>
    /// Check that the result set of a query is exactly this value.
    /// </summary>
    /// <param name="expected">the expected result value</param>
    /// <param name="stat">the statement</param>
    /// <param name="sql">the SQL statement to execute</param>
    protected void AssertResult(string? expected, DbCommand stat, string sql)
    {
        stat.CommandText = sql;
        using DbDataReader rs = stat.ExecuteReader();
        if (rs.Read())
        {
            string? actual = rs.IsDBNull(0) ? null : rs.GetString(0);
            AssertEquals(expected, actual);
        }
        else
        {
            AssertEquals(expected, null);
        }
    }

    /// <summary>
    /// Check that executing the specified query results in the specified error.
    /// </summary>
    /// <param name="expectedErrorCode">the expected error code</param>
    /// <param name="stat">the statement</param>
    /// <param name="sql">the SQL statement to execute</param>
    protected void AssertThrows(int expectedErrorCode, DbCommand stat, string sql)
    {
        try
        {
            Execute(stat, sql);
            Fail("Expected error: " + expectedErrorCode);
        }
        catch (H2SharpException ex)
        {
            AssertEquals(ex.Message ?? "", expectedErrorCode, ex.H2ErrorCode);
        }
    }

    /// <summary>
    /// A lambda the reference passes where a callable fits; the proxy overload of
    /// assertThrows has no .NET counterpart (a sealed class cannot be proxied), so the
    /// calls go through this delegate instead.
    /// </summary>
    /// <remarks>
    /// The reference wraps the object in a dynamic proxy and verifies the next call on it;
    /// ported tests write the call into the lambda: AssertThrows(code, () =&gt; prep.X()).
    /// </remarks>
    protected delegate void VoidCallable();

    /// <summary>
    /// Assert that the lambda function throws a SQLException or DbException with the
    /// expected error code.
    /// </summary>
    /// <param name="expectedErrorCode">SQL error code</param>
    /// <param name="c">lambda function</param>
    protected void AssertThrows(int expectedErrorCode, VoidCallable c)
    {
        try
        {
            c();
            Fail("Expected an SQLException or DbException with error code " + expectedErrorCode
                + " to be thrown, but the method returned successfully");
        }
        catch (Exception t)
        {
            CheckErrorCode(expectedErrorCode, t);
        }
    }

    /// <summary>
    /// Assert that the lambda function throws an exception of the expected class.
    /// </summary>
    /// <param name="expectedExceptionType">expected exception type</param>
    /// <param name="c">lambda function</param>
    protected void AssertThrows(Type expectedExceptionType, VoidCallable c)
    {
        try
        {
            c();
            Fail("Expected an exception of type " + expectedExceptionType.Name
                + " to be thrown, but the method returned successfully");
        }
        catch (Exception t)
        {
            CheckException(expectedExceptionType, t);
        }
    }

    /// <summary>
    /// Verify that the exception is of the expected class.
    /// </summary>
    /// <param name="expectedExceptionType">the expected exception type</param>
    /// <param name="t">the exception to check</param>
    private static void CheckException(Type expectedExceptionType, Exception t)
    {
        if (!expectedExceptionType.IsInstanceOfType(t))
        {
            throw new AssertionError("Expected an exception of type\n"
                + expectedExceptionType.Name + " to be thrown, but an exception of type\n"
                + t.GetType().Name + " was thrown", t);
        }
    }

    /// <summary>
    /// Verify that actual error code is the one expected.
    /// </summary>
    /// <param name="expectedErrorCode">to compare against</param>
    /// <param name="t">actual exception to extract error code from</param>
    public static void CheckErrorCode(int expectedErrorCode, Exception? t)
    {
        int errorCode;
        if (t is H2SharpException h2SharpException)
        {
            errorCode = h2SharpException.H2ErrorCode;
        }
        else if (t is MvStore.MvStoreException mvStoreException)
        {
            errorCode = mvStoreException.GetErrorCode();
        }
        else
        {
            errorCode = 0;
        }

        if (errorCode != expectedErrorCode)
        {
            throw new AssertionError("Expected an SQLException or DbException with error code " + expectedErrorCode
                + ", but got a "
                + (t is null ? "null" : t.GetType().Name + " exception " + " with error code " + errorCode), t!);
        }
    }

    /// <summary>
    /// Get the name of the test.
    /// </summary>
    /// <returns>the name of the test class</returns>
    public string GetTestName()
    {
        return GetType().Name;
    }

    /// <summary>
    /// Execute the statement.
    /// </summary>
    /// <param name="stat">the statement</param>
    public void Execute(DbCommand stat)
    {
        Execute(stat, null);
    }

    /// <summary>
    /// Execute the statement.
    /// </summary>
    /// <param name="stat">the statement</param>
    /// <param name="sql">the SQL command</param>
    /// <remarks>
    /// The reference calls Statement.execute() and, in the lazy configuration, drains the
    /// result set afterwards. The port's ExecuteNonQuery answers -1 for a query and reads
    /// and drops the rows it would have produced, so the drain is not a separate step
    /// here.
    /// </remarks>
    protected void Execute(DbCommand stat, string? sql)
    {
        if (sql is not null)
        {
            stat.CommandText = sql;
        }

        _ = stat.ExecuteNonQuery();
    }

    /// <summary>
    /// Check if the result set meta data is correct.
    /// </summary>
    /// <param name="rs">the result set</param>
    /// <param name="columnCount">the expected column count</param>
    /// <param name="labels">the expected column labels</param>
    /// <param name="datatypes">the expected data types</param>
    /// <param name="precision">the expected precisions</param>
    /// <param name="scale">the expected scales</param>
    /// <remarks>
    /// The reference compares java.sql.Types codes; the port has none, so the expected
    /// datatypes are the CLR types the reader answers through GetFieldType. The
    /// java.sql.Timestamp/BigDecimal classname checks of the reference have no CLR
    /// counterpart and are dropped.
    /// </remarks>
    protected void AssertResultSetMeta(DbDataReader rs, int columnCount,
        string[]? labels, Type?[]? datatypes, int[]? precision, int[]? scale)
    {
        int cc = rs.FieldCount;
        if (cc != columnCount)
        {
            Fail("result set contains " + cc + " columns not " + columnCount);
        }

        for (int i = 0; i < columnCount; i++)
        {
            if (labels is not null)
            {
                string l = rs.GetName(i);
                if (!labels[i].Equals(l, StringComparison.Ordinal))
                {
                    Fail("column label " + i + " is " + l + " not " + labels[i]);
                }
            }

            if (datatypes is not null)
            {
                Type t = rs.GetFieldType(i);
                if (datatypes[i] != t)
                {
                    Fail("column datatype " + i + " is " + t + " not " + datatypes[i]);
                }
            }

            if (precision is not null)
            {
                int p = GetPrecision(rs, i);
                if (precision[i] != p)
                {
                    Fail("column precision " + i + " is " + p + " not " + precision[i]);
                }
            }

            if (scale is not null)
            {
                int s = GetScale(rs, i);
                if (scale[i] != s)
                {
                    Fail("column scale " + i + " is " + s + " not " + scale[i]);
                }
            }
        }
    }

    private static int GetPrecision(DbDataReader rs, int ordinal)
    {
        DbColumn? column = FindColumn(rs, ordinal);
        return column?.NumericPrecision ?? -1;
    }

    private static int GetScale(DbDataReader rs, int ordinal)
    {
        DbColumn? column = FindColumn(rs, ordinal);
        return column?.NumericScale ?? -1;
    }

    private static DbColumn? FindColumn(DbDataReader rs, int ordinal)
    {
        foreach (DbColumn column in rs.GetColumnSchema())
        {
            if (column.ColumnOrdinal.GetValueOrDefault() == ordinal)
            {
                return column;
            }
        }

        return null;
    }

    /// <summary>
    /// Check if a result set contains the expected data.
    /// The sort order is significant
    /// </summary>
    /// <param name="rs">the result set</param>
    /// <param name="data">the expected data</param>
    /// <param name="ignoreColumns">columns to ignore, or <c>null</c></param>
    protected void AssertResultSetOrdered(DbDataReader rs, string?[][] data,
        int[]? ignoreColumns)
    {
        AssertResultSet(true, rs, data, ignoreColumns);
    }

    /// <summary>
    /// Check if a result set contains the expected data.
    /// The sort order is significant
    /// </summary>
    /// <param name="rs">the result set</param>
    /// <param name="data">the expected data</param>
    protected void AssertResultSetOrdered(DbDataReader rs, string?[][] data)
    {
        AssertResultSet(true, rs, data, null);
    }

    /// <summary>
    /// Check if a result set contains the expected data.
    /// </summary>
    /// <param name="ordered">if the sort order is significant</param>
    /// <param name="rs">the result set</param>
    /// <param name="data">the expected data</param>
    /// <param name="ignoreColumns">columns to ignore, or <c>null</c></param>
    private void AssertResultSet(bool ordered, DbDataReader rs, string?[][] data,
        int[]? ignoreColumns)
    {
        int len = rs.FieldCount;
        int rows = data.Length;
        if (rows == 0)
        {
            // special case: no rows
            if (rs.Read())
            {
                Fail("testResultSet expected rowCount:" + rows + " got:0");
            }
        }

        int len2 = data[0].Length;
        if (len < len2)
        {
            Fail("testResultSet expected columnCount:" + len2 + " got:" + len);
        }

        for (int i = 0; i < rows; i++)
        {
            if (!rs.Read())
            {
                Fail("testResultSet expected rowCount:" + rows + " got:" + i);
            }

            string?[] row = GetData(rs, len);
            if (ordered)
            {
                string?[] good = data[i];
                if (!TestRow(good, row, good.Length, ignoreColumns))
                {
                    Fail("testResultSet row not equal, got:\n" + FormatRow(row)
                        + "\n" + FormatRow(good));
                }
            }
            else
            {
                bool found = false;
                for (int j = 0; j < rows; j++)
                {
                    string?[] good = data[i];
                    if (TestRow(good, row, good.Length, ignoreColumns))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    Fail("testResultSet no match for row:" + FormatRow(row));
                }
            }
        }

        if (rs.Read())
        {
            string?[] row = GetData(rs, len);
            Fail("testResultSet expected rowcount:" + rows + " got:>="
                + (rows + 1) + " data:" + FormatRow(row));
        }
    }

    private static bool TestRow(string?[] a, string?[] b, int len, int[]? ignoreColumns)
    {
        for (int i = 0; i < len; i++)
        {
            if (ignoreColumns is not null)
            {
                bool skip = false;
                foreach (int c in ignoreColumns)
                {
                    if (c == i)
                    {
                        skip = true;
                        break;
                    }
                }

                if (skip)
                {
                    continue;
                }
            }

            string? sa = a[i];
            string? sb = b[i];
            if (sa is null || sb is null)
            {
                if (sa != sb)
                {
                    return false;
                }
            }
            else
            {
                if (!sa.Equals(sb, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static string?[] GetData(DbDataReader rs, int len)
    {
        string?[] data = new string?[len];
        for (int i = 0; i < len; i++)
        {
            data[i] = rs.IsDBNull(i) ? null : rs.GetString(i);
            // just check if it works
            _ = rs.GetValue(i);
        }

        return data;
    }

    private static string FormatRow(string?[] row)
    {
        StringBuilder sb = new();
        _ = sb.Append('{');
        foreach (string? r in row)
        {
            _ = sb.Append('{').Append(r).Append('}');
        }

        _ = sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Simulate a database crash. This method will also close the database
    /// files, but the files are in a state as the power was switched off. It
    /// doesn't throw an exception.
    /// </summary>
    /// <param name="conn">the database connection</param>
    /// <remarks>
    /// The reference counts down disk operations through the session's power-off counter;
    /// the port has no power-off seam yet (no Database.SetPowerOffCount), so this refuses
    /// until that seam is ported.
    /// </remarks>
    protected void Crash(DbConnection conn)
    {
        throw new TODOException(
            "the power-off seam (TestBase.setPowerOffCount through the session)");
    }

    /// <summary>
    /// Set the number of disk operations before power failure is simulated.
    /// To disable the countdown, use 0.
    /// </summary>
    /// <param name="conn">the connection</param>
    /// <param name="i">the number of operations</param>
    /// <remarks>See <see cref="Crash"/>.</remarks>
    public static void SetPowerOffCount(DbConnection conn, int i)
    {
        throw new TODOException(
            "the power-off seam (SessionLocal.SetPowerOffCount)");
    }

    /// <summary>
    /// Returns the number of disk operations before power failure is simulated.
    /// </summary>
    /// <param name="conn">the connection</param>
    /// <returns>the number of disk operations before power failure is simulated</returns>
    /// <remarks>See <see cref="Crash"/>.</remarks>
    protected static int GetPowerOffCount(DbConnection conn)
    {
        throw new TODOException(
            "the power-off seam (SessionLocal.GetPowerOffCount)");
    }

    /// <summary>
    /// Read a string from the reader. This method reads until end of file.
    /// </summary>
    /// <param name="reader">the reader</param>
    /// <returns>the string read</returns>
    protected string? ReadString(TextReader? reader)
    {
        if (reader is null)
        {
            return null;
        }

        StringBuilder buffer = new();
        try
        {
            while (true)
            {
                int c = reader.Read();
                if (c == -1)
                {
                    break;
                }

                _ = buffer.Append((char)c);
            }

            return buffer.ToString();
        }
        catch (Exception)
        {
            AssertTrue(false);
            return null;
        }
    }

    /// <summary>
    /// Check that a given exception is not an unexpected 'general error'
    /// exception.
    /// </summary>
    /// <param name="e">the error</param>
    public void AssertKnownException(DbException? e)
    {
        AssertKnownException("", e);
    }

    /// <summary>
    /// Check that a given exception is not an unexpected 'general error'
    /// exception.
    /// </summary>
    /// <param name="message">the message</param>
    /// <param name="e">the error</param>
    protected void AssertKnownException(string message, DbException? e)
    {
        if (e is not null && (e.SqlState ?? "").StartsWith("HY000", StringComparison.Ordinal))
        {
            LogError("Unexpected General error " + message, e);
        }
    }

    /// <summary>
    /// Check if two values are equal, and if not throw an exception.
    /// </summary>
    /// <param name="expected">the expected value</param>
    /// <param name="actual">the actual value</param>
    protected void AssertEquals(int? expected, int? actual)
    {
        if (expected is null || actual is null)
        {
            AssertEquals("" + expected, "" + actual);
        }
        else
        {
            AssertEquals(expected.GetValueOrDefault(), actual.GetValueOrDefault());
        }
    }

    /// <summary>
    /// Check if two databases contain the same meta data.
    /// </summary>
    /// <param name="stat1">the connection to the first database</param>
    /// <param name="stat2">the connection to the second database</param>
    protected void AssertEqualDatabases(DbCommand stat1, DbCommand stat2)
    {
        stat1.CommandText =
            "SELECT SETTING_VALUE FROM INFORMATION_SCHEMA.SETTINGS WHERE SETTING_NAME = 'ANALYZE_AUTO'";
        int analyzeAuto;
        using (DbDataReader rs = stat1.ExecuteReader())
        {
            analyzeAuto = rs.Read() ? rs.GetInt32(0) : 0;
        }

        if (analyzeAuto > 0)
        {
            Execute(stat1, "analyze");
            Execute(stat2, "analyze");
        }

        stat1.CommandText = "SCRIPT simple NOPASSWORDS";
        using DbDataReader rs1 = stat1.ExecuteReader();
        stat2.CommandText = "SCRIPT simple NOPASSWORDS";
        using DbDataReader rs2 = stat2.ExecuteReader();
        List<string> list1 = [];
        List<string> list2 = [];
        while (rs1.Read())
        {
            string s1 = RemoveRowCount(rs1.GetString(0));
            if (!rs2.Read())
            {
                Fail("expected: " + s1);
            }

            string s2 = RemoveRowCount(rs2.GetString(0));
            if (!s1.Equals(s2, StringComparison.Ordinal))
            {
                list1.Add(s1);
                list2.Add(s2);
            }
        }

        foreach (string s in list1)
        {
            if (!list2.Remove(s))
            {
                Fail("only found in first: " + s + " remaining: " + list2);
            }
        }

        AssertEquals("remaining: " + list2, 0, list2.Count);
        AssertFalse(rs2.Read());
    }

    private static string RemoveRowCount(string scriptLine)
    {
        int index = scriptLine.IndexOf("+/-", StringComparison.Ordinal);
        if (index >= 0)
        {
            scriptLine = scriptLine.Substring(index);
        }

        return scriptLine;
    }
}
