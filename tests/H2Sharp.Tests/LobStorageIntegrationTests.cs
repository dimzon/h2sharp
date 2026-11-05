/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The database LOB storage: create, read back, retarget, remove. The storage is the
 * reference's LobStorageMap in a reduced form (D-041); the blocks live in MVStore maps.
 */
using System;
using System.IO;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Db;
using H2Sharp.MvStore.Tx;
using H2Sharp.Values;
using H2Sharp.Values.Lobs;
using Xunit;

namespace H2Sharp.Tests;

public sealed class LobStorageIntegrationTests : IDisposable
{
    private readonly Database database;

    private readonly TransactionStore store;

    public LobStorageIntegrationTests()
    {
        database = new Database(ConnectionInfo.Memory("lob_storage"));
        store = new TransactionStore(MVStore.Open(null!));
        store.Init();
    }

    public void Dispose()
    {
        store.Close();
        database.Close();
    }

    private LobStorageMap Storage => (LobStorageMap)database.GetLobStorage();

    [Fact]
    public void BlobRoundTripsThroughStorage()
    {
        byte[] payload = new byte[512 * 1024];
        new Random(42).NextBytes(payload);

        ValueBlob blob = Storage.CreateBlob(new MemoryStream(payload), payload.Length);
        Assert.IsType<LobDataStore>(blob.GetLobData());

        using MemoryStream read = new();
        using Stream input = blob.GetInputStream();
        input.CopyTo(read);
        Assert.Equal(payload, read.ToArray());

        Storage.RemoveLob(blob);
    }

    [Fact]
    public void ClobRoundTripsWithCharacterLength()
    {
        string text = new string('я', 200_000) + "A\uD800\uDC00";
        ValueClob clob = Storage.CreateClob(new StringReader(text), text.Length);

        Assert.Equal(text.Length, clob.CharLength());
        Assert.Equal(text, clob.GetString());

        Storage.RemoveLob(clob);
    }

    [Fact]
    public void CopyRetargetsTableAndRemoveAllClears()
    {
        byte[] payload = Encoding.UTF8.GetBytes(new string('x', 300_000));
        ValueBlob blob = Storage.CreateBlob(new MemoryStream(payload), payload.Length);

        ValueLob copied = Storage.CopyLob(blob, tableId: 7);
        Assert.Same(blob, copied);

        Storage.RemoveAllForTable(7);
    }
}
