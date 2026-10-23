// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.db.TestLargeBlob.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): JDBC statements are ADO.NET commands; setBinaryStream is a Stream
// parameter value, and rs.getBinaryStream is reader.GetStream. The reference's
// anonymous InputStream answers -1 at the end and is read through read(byte[], int,
// int); a .NET Stream answers 0 at the end through Read, which is the same contract.
// The reference's main() entry is the runner; xUnit runs Test() directly.
using H2Sharp.Data;

namespace H2Sharp.Native.Tests.Db;

/// <summary>
/// Test a BLOB larger than Integer.MAX_VALUE.
/// </summary>
public sealed class TestLargeBlob : TestDb
{
    /// <summary>
    /// Whether this test is enabled in the current configuration.
    /// </summary>
    /// <returns><c>true</c> when the test runs</returns>
    public override bool IsEnabled()
    {
        if (!Config.Big || Config.Memory || Config.Networked)
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
        DeleteDb("largeBlob");
        string url = GetURL("largeBlob;TRACE_LEVEL_FILE=0", true);
        using DbConnection conn = new H2Connection(url);
        conn.Open();
        long testLength = int.MaxValue + 110L;
        DbCommand stat = conn.CreateCommand();
        Execute(stat, "create table test(x blob)");
        using DbCommand prep = conn.CreateCommand();
        prep.CommandText = "insert into test values(?)";
        prep.Parameters.Add(new H2Parameter { Value = new TestInputStream(this, testLength) });
        _ = prep.ExecuteNonQuery();
        stat.CommandText = "select length(x) from test";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(testLength, rs.GetInt64(0));
        }

        stat.CommandText = "select x from test";
        using (DbDataReader rs = stat.ExecuteReader())
        {
            AssertTrue(rs.Read());
            using Stream input = rs.GetStream(0);
            byte[] buff = new byte[4 * 1024];
            long length = 0;
            while (true)
            {
                int len = input.Read(buff, 0, buff.Length);
                if (len <= 0)
                {
                    break;
                }

                length += len;
            }

            AssertEquals(testLength, length);
        }
    }

    /// <summary>
    /// The reference's anonymous InputStream: counts down the remaining length and
    /// answers nothing but the length.
    /// </summary>
    private sealed class TestInputStream : Stream
    {
        private long remaining;
        private int p;

        private readonly TestBase owner;

        /// <summary>
        /// Builds the stream of the given length.
        /// </summary>
        /// <param name="owner">the test the progress lines print through; the Java
        /// anonymous class reaches it as the enclosing instance</param>
        /// <param name="length">the length the stream answers</param>
        public TestInputStream(TestBase owner, long length)
        {
            this.owner = owner;
            remaining = length;
        }

        /// <inheritdoc/>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => false;

        /// <inheritdoc/>
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc/>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Flush()
        {
            // ignore
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            long len = Math.Min(remaining, count);
            remaining -= len;
            if (p++ % 5000 == 0)
            {
                owner.Println("" + remaining);
            }

            return len == 0 ? 0 : (int)len;
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
