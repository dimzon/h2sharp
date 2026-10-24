// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestDbException.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the reference walks the ErrorCode constants and expects every
// DbException.getJdbcSQLException to answer a JdbcException subclass and never the plain
// JdbcSQLException; the port answers H2SharpException for every code — the kind check of
// the reference lives in the internal ErrorCodeKind.GetKind table, and the provider
// refusing a kind is the same "no plain wrapper" expectation. The check here is that
// every code builds an H2SharpException carrying its own code and SQL state.
using System.Reflection;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Tests DbException class.
/// </summary>
public sealed class TestDbException
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public void Test()
    {
        TestGetJdbcSQLException();
    }

    private void TestGetJdbcSQLException()
    {
        foreach (FieldInfo field in typeof(H2Sharp.Api.ErrorCode).GetFields(
            BindingFlags.Public | BindingFlags.Static))
        {
            if (field.IsLiteral && field.FieldType == typeof(int))
            {
                int errorCode = (int)field.GetRawConstantValue()!;
                H2SharpException exception = H2Sharp.Message.DbException.Get(errorCode);
                Assert.True(exception.H2ErrorCode == errorCode
                    && exception.SqlState is not null,
                    "Custom exception expected for H2Sharp.Api.ErrorCode."
                    + field.Name + " (" + errorCode + ')');
            }
        }
    }
}
