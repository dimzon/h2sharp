// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestSQLInjection.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands, PreparedStatement.setString is a
// parameter value, and the proxy form of assertThrows becomes the lambda form, because a
// sealed class cannot be proxied. The reference's main() entry is the runner; xUnit runs
// Test() directly.
using H2Sharp.Api;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Tests the ALLOW_LITERALS feature (protection against SQL injection).
/// </summary>
public sealed class TestSQLInjection : TestDb
{
    private DbConnection? conn;

    private DbCommand? stat;

    /// <summary>
    /// Whether this test is enabled in the current configuration.
    /// </summary>
    /// <returns><c>true</c> when the test runs</returns>
    public override bool IsEnabled()
    {
        if (Config.Reopen)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb("sqlInjection");
        Reconnect("sqlInjection");
        Execute(stat!, "DROP TABLE IF EXISTS USERS");
        Execute(stat!, "CREATE TABLE USERS(NAME VARCHAR PRIMARY KEY, "
            + "PASSWORD VARCHAR, TYPE VARCHAR)");
        Execute(stat!, "CREATE SCHEMA CONST");
        Execute(stat!, "CREATE CONSTANT CONST.ACTIVE VALUE 'Active'");
        Execute(stat!, "INSERT INTO USERS VALUES('James', '123456', CONST.ACTIVE)");
        AssertTrue(CheckPasswordInsecure("123456"));
        AssertFalse(CheckPasswordInsecure("abcdef"));
        AssertTrue(CheckPasswordInsecure("' OR ''='"));
        AssertTrue(CheckPasswordSecure("123456"));
        AssertFalse(CheckPasswordSecure("abcdef"));
        AssertFalse(CheckPasswordSecure("' OR ''='"));
        Execute(stat!, "CALL 123");
        Execute(stat!, "CALL 'Hello'");
        Execute(stat!, "CALL $$Hello World$$");
        Execute(stat!, "SET ALLOW_LITERALS NUMBERS");
        Execute(stat!, "CALL 123");
        AssertThrows(ErrorCode.LiteralsAreNotAllowed, () => Execute(stat!, "CALL 'Hello'"));
        AssertThrows(ErrorCode.LiteralsAreNotAllowed, () => Execute(stat!, "CALL $$Hello World$$"));
        Execute(stat!, "SET ALLOW_LITERALS NONE");
        try
        {
            _ = CheckPasswordInsecure("123456");
            Fail();
        }
        catch (DbException e)
        {
            AssertKnownException(e);
        }

        AssertTrue(CheckPasswordSecure("123456"));
        AssertFalse(CheckPasswordSecure("' OR ''='"));
        conn!.Close();

        if (Config.Memory)
        {
            return;
        }

        Reconnect("sqlInjection");

        try
        {
            _ = CheckPasswordInsecure("123456");
            Fail();
        }
        catch (DbException e)
        {
            AssertKnownException(e);
        }

        AssertTrue(CheckPasswordSecure("123456"));
        AssertFalse(CheckPasswordSecure("' OR ''='"));
        conn!.Close();
        DeleteDb("sqlInjection");
    }

    private bool CheckPasswordInsecure(string pwd)
    {
        string sql = "SELECT * FROM USERS WHERE PASSWORD='" + pwd + "'";
        using DbCommand command = conn!.CreateCommand();
        command.CommandText = sql;
        using DbDataReader rs = command.ExecuteReader();
        return rs.Read();
    }

    private bool CheckPasswordSecure(string pwd)
    {
        using DbCommand prep = conn!.CreateCommand();
        prep.CommandText = "SELECT * FROM USERS WHERE PASSWORD=?";
        _ = prep.Parameters.Add(new H2Parameter { Value = pwd });
        using DbDataReader rs = prep.ExecuteReader();
        return rs.Read();
    }

    private void Reconnect(string name)
    {
        if (!Config.Memory)
        {
            if (conn is not null)
            {
                conn.Close();
                conn = null;
            }
        }

        if (conn is null)
        {
            conn = GetConnection(name);
            stat = conn.CreateCommand();
        }
    }
}
