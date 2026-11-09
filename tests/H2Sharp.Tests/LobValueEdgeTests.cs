/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 110: the LOB value members SQL does not exercise — lazy character/octet
 * lengths (in memory and through a reader), comparisons of two LOBs, the copy
 * refusal for a stored LOB, tie-to-table and result-lifetime answers.
 */
using System;
using System.IO;
using H2Sharp.Engine;
using H2Sharp.MvStore.Db;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class LobValueEdgeTests
{
    [Fact]
    public void SmallLobsAnswerTheirLengthsAndCompare()
    {
        ValueBlob a = ValueBlob.CreateSmall([1, 2, 3]);
        ValueBlob b = ValueBlob.CreateSmall([1, 2, 4]);
        ValueBlob c = ValueBlob.CreateSmall([1, 2, 3, 4]);
        ValueBlob samePrefix = ValueBlob.CreateSmall([1, 2, 3]);

        Assert.Equal(3L, a.OctetLength());
        Assert.Equal(3, a.CharLength());
        Assert.False(a.IsLinkedToTable());
        Assert.Same(a, a.CopyToResult());
        Assert.Same(a, a.Copy(null!, 0));

        CompareMode mode = CompareMode.GetInstance(null, 0);
        Assert.Equal(0, a.CompareTo(samePrefix, null, mode));
        Assert.True(a.CompareTo(b, null, mode) < 0);
        Assert.True(c.CompareTo(a, null, mode) > 0);

        ValueClob text = ValueClob.CreateSmall("abc");
        ValueClob later = ValueClob.CreateSmall("abd");
        Assert.Equal("abc", text.GetString());
        Assert.Equal(3, text.CharLength());
        Assert.Equal(3L, text.OctetLength());
        Assert.False(text.IsLinkedToTable());
        Assert.Same(text, text.CopyToResult());
        Assert.Same(text, text.Copy(null!, 0));
        Assert.Equal(0, text.CompareTo(ValueClob.CreateSmall("abc"), null, mode));
        Assert.True(text.CompareTo(later, null, mode) < 0);
        Assert.True(text.CompareTo(ValueClob.CreateSmall("ab"), null, mode) > 0);

        // A CLOB whose byte length is not known yet counts it from the text.
        ValueClob lazy = new(new H2Sharp.Values.Lobs.LobDataInMemory([0x61, 0x62]), -1, 2);
        Assert.Equal(2L, lazy.OctetLength());
        // A LOB tied to a file cannot outlive the statement without the LOB storage.
        ValueBlob fileLob = new(new H2Sharp.Values.Lobs.LobDataFile("x", 10), 10);
        _ = fileLob.CopyToResult();
    }

    [Fact]
    public void StoredLobsCountCharactersThroughTheReaderAndRefuseCopying()
    {
        Database database = new(ConnectionInfo.Memory("lob_edge_" + Guid.NewGuid().ToString("N")));
        LobStorageMap storage = (LobStorageMap)database.GetLobStorage();
        try
        {
            byte[] payload = new byte[100];
            new Random(3).NextBytes(payload);
            ValueBlob blob = storage.CreateBlob(new MemoryStream(payload), payload.Length);
            ValueClob clob = storage.CreateClob(new StringReader("stored text"), 11);

            // The character length of a stored CLOB is counted through its reader.
            Assert.Equal(11, clob.CharLength());
            Assert.True(blob.CharLength() >= 0);
            Assert.True(blob.IsLinkedToTable());
            Assert.Same(clob, clob.CopyToResult());

            // Copying a stored LOB needs the LOB storage, which this path does not have.
            Assert.Equal(
                H2Sharp.Api.ErrorCode.FeatureNotSupported1,
                Assert.Throws<H2SharpException>(() => blob.Copy(database, 0)).H2ErrorCode);

            storage.RemoveLob(blob);
            storage.RemoveLob(clob);
        }
        finally
        {
            database.Close();
        }
    }
}
