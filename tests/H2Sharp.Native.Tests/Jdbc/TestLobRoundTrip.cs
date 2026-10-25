// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from the content half of org.h2.test.jdbc.TestLobApi.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// New test, per the discussion decision (see docs/NATIVE_TESTS_DISCUSSION.md): the
// JDBC LOB object model (Blob/Clob/NClob handles, position/truncate) has no ADO.NET
// shape, so the content half ports as a LOB-value round-trip test — byte[]/string
// values submitted through parameters in all shapes (bytes, strings, Stream,
// TextReader) and read back through the reader, at lengths 0, 1, 100 and 100000.
using Xunit;

namespace H2Sharp.Native.Tests.Jdbc;

/// <summary>
/// The LOB-value round-trip of the reference's TestLobApi.
/// </summary>
public sealed class TestLobRoundTrip : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        DeleteDb(GetTestName());
        using (DbConnection conn = GetConnection(GetTestName()))
        {
            DbCommand stat = conn.CreateCommand();
            Execute(stat, "create table test(id int, x blob)");
            TestBlob(conn, stat, 0);
            TestBlob(conn, stat, 1);
            TestBlob(conn, stat, 100);
            TestBlob(conn, stat, 100000);
            Execute(stat, "drop table test");
            Execute(stat, "create table test(id int, x clob)");
            TestClob(conn, stat, 0);
            TestClob(conn, stat, 1);
            TestClob(conn, stat, 100);
            TestClob(conn, stat, 100000);
            Execute(stat, "drop table test");
        }

        DeleteDb(GetTestName());
    }

    private void TestBlob(DbConnection conn, DbCommand stat, int length)
    {
        Random r = new(length);
        byte[] data = new byte[length];
        r.NextBytes(data);

        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "insert into test values(?, ?)";
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters.Add(new H2Parameter());

            // bytes
            prep.Parameters[0].Value = 1;
            prep.Parameters[1].Value = data;
            _ = prep.ExecuteNonQuery();

            // MemoryStream
            prep.Parameters[0].Value = 2;
            prep.Parameters[1].Value = new MemoryStream(data);
            _ = prep.ExecuteNonQuery();

            // Stream with unknown length (-1 in the reference)
            prep.Parameters[0].Value = 3;
            prep.Parameters[1].Value = new MemoryStream(data);
            _ = prep.ExecuteNonQuery();
        }

        stat.CommandText = "select * from test order by id";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            for (int id = 1; id <= 3; id++)
            {
                AssertTrue(rs.Read());
                AssertEquals(id, rs.GetInt32(0));
                byte[] read = (byte[])rs.GetValue(1);
                AssertEquals(data.Length, read.Length);
                AssertEquals(data, read);
                using Stream stream = rs.GetStream(1);
                byte[] buffer = new byte[length];
                int readCount = stream.Read(buffer, 0, buffer.Length);
                AssertEquals(length, readCount);
                AssertEquals(data, buffer);
            }

            AssertFalse(rs.Read());
        }

        DbCommand cleanup = conn.CreateCommand();
        Execute(cleanup, "delete from test");
    }

    private void TestClob(DbConnection conn, DbCommand stat, int length)
    {
        Random r = new(length);
        char[] chars = new char[length];

        // Unicode problem:
        // The UCS code values 0xd800-0xdfff (UTF-16 surrogates)
        // as well as 0xfffe and 0xffff (UCS non-characters)
        // should not appear in conforming UTF-8 streams.
        // (String.getBytes("UTF-8") only returns 1 byte for 0xd800-0xdfff)
        for (int i = 0; i < length; i++)
        {
            char c;
            do
            {
                c = (char)r.Next();
            } while (c >= 0xd800 && c <= 0xdfff);
            chars[i] = c;
        }

        string data = new(chars);

        using (DbCommand prep = conn.CreateCommand())
        {
            prep.CommandText = "insert into test values(?, ?)";
            prep.Parameters.Add(new H2Parameter());
            prep.Parameters.Add(new H2Parameter());

            // string
            prep.Parameters[0].Value = 1;
            prep.Parameters[1].Value = data;
            _ = prep.ExecuteNonQuery();

            // TextReader
            prep.Parameters[0].Value = 2;
            prep.Parameters[1].Value = new StringReader(data);
            _ = prep.ExecuteNonQuery();

            // TextReader with unknown length
            prep.Parameters[0].Value = 3;
            prep.Parameters[1].Value = new StringReader(data);
            _ = prep.ExecuteNonQuery();
        }

        stat.CommandText = "select * from test order by id";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            for (int id = 1; id <= 3; id++)
            {
                AssertTrue(rs.Read());
                AssertEquals(id, rs.GetInt32(0));
                string read = rs.GetString(1);
                AssertEquals(data.Length, read.Length);
                AssertEquals(data, read);
                using TextReader reader = rs.GetTextReader(1);
                char[] buffer = new char[length];
                int readCount = reader.Read(buffer, 0, buffer.Length);
                AssertEquals(length, readCount);
                AssertEquals(data, new string(buffer));
            }

            AssertFalse(rs.Read());
        }

        DbCommand cleanup = conn.CreateCommand();
        Execute(cleanup, "delete from test");
    }
}
