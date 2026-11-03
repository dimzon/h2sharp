// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// LOBs big enough to leave the row: the value is written to the store, read back, measured,
// windowed by SUBSTRING, compared and cast — the paths a small in-memory LOB never takes.
using System;
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Dml;

public sealed class LobStorageTests
{
    private static string BigText() => new string('x', 300_000) + "END";

    private static byte[] BigBytes()
    {
        byte[] bytes = new byte[300_000];
        new Random(7).NextBytes(bytes);
        return bytes;
    }

    [Fact]
    public void AClobLargerThanARowRoundTrips()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY, V CLOB)");
        string big = BigText();
        _ = connection.Execute("INSERT INTO T VALUES (1, $v)", new { v = big });

        Assert.Equal(big.Length, connection.QuerySingle<long>("SELECT LENGTH(V) FROM T"));
        Assert.Equal(big.Length, connection.QuerySingle<long>("SELECT CHAR_LENGTH(V) FROM T"));
        string read = connection.QuerySingle<string>("SELECT V FROM T")!;
        Assert.Equal(big.Length, read.Length);
        Assert.StartsWith("xxx", read, StringComparison.Ordinal);
        Assert.EndsWith("END", read, StringComparison.Ordinal);

        // The character stream is the same text, read from where it lives.
        using (IDataReader reader = connection.ExecuteReader("SELECT V FROM T"))
        {
            Assert.True(reader.Read());
            Assert.Equal(big.Length,
                new System.IO.StreamReader(((System.Data.Common.DbDataReader)reader).GetStream(0))
                    .ReadToEnd().Length);
        }

        // A window of the value answers without materialising the whole of it.
        Assert.Equal("xxx", connection.QuerySingle<string>("SELECT SUBSTRING(V FROM 1 FOR 3) FROM T"));
        Assert.Equal("END", connection.QuerySingle<string>("SELECT SUBSTRING(V FROM 300001 FOR 3) FROM T"));
        Assert.Equal(300_000, connection.QuerySingle<int>("SELECT POSITION('END', V) - 1 FROM T"));
        // Casting to a shorter character type keeps the prefix.
        Assert.Equal("xxxxx", connection.QuerySingle<string>("SELECT CAST(V AS VARCHAR(5)) FROM T"));
        // Two big values of the same text are one value, even though each is far past a row.
        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(DISTINCT V) FROM (SELECT V FROM T UNION ALL SELECT V FROM T) X"));
        // A big value is found by its own text.
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM T WHERE V = $v", new { v = big }));
        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM T WHERE V = $v", new { v = big + "!" }));
    }

    [Fact]
    public void ABlobLargerThanARowRoundTrips()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY, B BLOB)");
        byte[] big = BigBytes();
        _ = connection.Execute("INSERT INTO T VALUES (1, $b)", new { b = big });

        Assert.Equal(big.Length, connection.QuerySingle<long>("SELECT OCTET_LENGTH(B) FROM T"));
        byte[] read = connection.QuerySingle<byte[]>("SELECT B FROM T")!;
        Assert.Equal(big.Length, read.Length);
        Assert.Equal(big, read);

        // The octet window of a big value is cut where it is asked for.
        byte[] window = connection.QuerySingle<byte[]>("SELECT SUBSTRING(B FROM 2 FOR 4) FROM T")!;
        Assert.Equal(big.Skip(1).Take(4), window);
        // Casting to a shorter binary type keeps the prefix.
        byte[] prefix = connection.QuerySingle<byte[]>("SELECT CAST(B AS VARBINARY(3)) FROM T")!;
        Assert.Equal(big.Take(3), prefix);
        // Two big values of the same bytes are one value.
        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(DISTINCT B) FROM (SELECT B FROM T UNION ALL SELECT B FROM T) X"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM T WHERE B = $b", new { b = big }));
    }

    [Fact]
    public void ACastToAShortClobKeepsThePrefix()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY, V CLOB)");

        _ = connection.Execute("INSERT INTO T VALUES (1, $v)", new { v = BigText() });
        Assert.Equal("xxx", connection.QuerySingle<string>("SELECT CAST(V AS CLOB(3)) FROM T"));

        // A small CLOB keeps its exact length; the big one reports the whole of it.
        Assert.Equal(2, connection.QuerySingle<int>("SELECT LENGTH(CAST('ab' AS CLOB(10)))"));
    }
}
