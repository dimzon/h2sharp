// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestConnectionInfo.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): ConnectionInfo is the ported engine class; GetUrl answers the URL the
// settings were read from, GetProperty(key) answers with null when unset (the default
// value of the reference is supplied by the test), and DeleteDbFiles.execute is the
// file walk of this test (the tool is not ported). The canonical path expectation of
// testName is System.IO.Path.GetFullPath with the separator normalized, which is the
// same canonical answer the JDK's File.getCanonicalPath gives. The reference's main()
// entry is the runner; xUnit runs Test() directly.
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Store.FileSystem;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Test the ConnectionInfo class.
/// </summary>
public sealed class TestConnectionInfo : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestImplicitRelativePath();
        TestConnectInitError();
        TestConnectionInfo_();
        TestName();
    }

    private void TestImplicitRelativePath()
    {
        AssertThrows(ErrorCode.UrlRelativeToCwd,
            () => _ = GetConnection("jdbc:h2:" + GetTestName()));
        AssertThrows(ErrorCode.UrlRelativeToCwd,
            () => _ = GetConnection("jdbc:h2:data/" + GetTestName()));

        using (DbConnection conn = GetConnection("jdbc:h2:./data/" + GetTestName()))
        {
        }

        DeleteDataFiles(GetTestName());
    }

    private void TestConnectInitError()
    {
        AssertThrows(ErrorCode.SyntaxError2,
            () => _ = GetConnection("jdbc:h2:mem:;init=error"));
        AssertThrows(ErrorCode.IoException2,
            () => _ = GetConnection("jdbc:h2:mem:;init=runscript from 'wrong.file'"));
    }

    private void TestConnectionInfo_()
    {
        ConnectionInfo connectionInfo = new(
            "jdbc:h2:mem:" + GetTestName()
            + ";ACCESS_MODE_DATA=rws"
            + ";INIT=CREATE this...\\;INSERT that..."
            + ";IFEXISTS=TRUE");

        AssertEquals("jdbc:h2:mem:" + GetTestName(),
            connectionInfo.GetUrl());

        AssertEquals("rws",
            connectionInfo.GetProperty("ACCESS_MODE_DATA") ?? "");
        AssertEquals("CREATE this...;INSERT that...",
            connectionInfo.GetProperty("INIT") ?? "");
        AssertEquals("TRUE",
            connectionInfo.GetProperty("IFEXISTS") ?? "");
        AssertEquals("undefined",
            connectionInfo.GetProperty("CACHE_TYPE") ?? "undefined");
    }

    private void TestName()
    {
        char differentFileSeparator = Path.DirectorySeparatorChar == '/' ? '\\' : '/';
        ConnectionInfo connectionInfo = new("./test"
            + differentFileSeparator + "subDir");
        string expected = Path.GetFullPath(
            "test" + Path.DirectorySeparatorChar + "subDir")
            .Replace('\\', '/');
        AssertEquals(expected, connectionInfo.GetName());
    }

    /// <summary>
    /// The reference's DeleteDbFiles.execute(dir, db, true): the database files of the
    /// given name go, silently. The tool is not ported; the walk is the same one the
    /// ported TestDb uses.
    /// </summary>
    /// <param name="name">the database name</param>
    private static void DeleteDataFiles(string name)
    {
        string start = name + '.';
        foreach (FilePath path in FilePath.Get("data").NewDirectoryStream())
        {
            if (path.GetName().StartsWith(start, StringComparison.Ordinal)
                && path.ToString().EndsWith(Constants.SuffixMvFile, StringComparison.Ordinal))
            {
                path.Delete();
            }
        }
    }
}
