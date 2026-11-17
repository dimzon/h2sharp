/*
 * Sprint 243: the async bridge and the generated-keys result of the ADO.NET surface.
 */
using System;
using System.Threading;
using System.Threading.Tasks;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class AdoBridgeSurfaceTests
{
    [Fact]
    public async Task SyncTaskReturnsTheWorksOutcomeAsATask()
    {
        Assert.Equal(42, await SyncTask.Run(() => 42, CancellationToken.None));
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SyncTask.Run(() => 42, canceled.Token));
        Task<int> faulted = SyncTask.Run<int>(() => throw new InvalidOperationException("boom"),
            CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => faulted);
    }

    [Fact]
    public void GeneratedKeysResultCarriesTheKeysWhenGiven()
    {
        ResultWithGeneratedKeys plain = ResultWithGeneratedKeys.Of(7);
        Assert.Null(plain.GetGeneratedKeys());
        Assert.Equal(7, plain.GetUpdateCount());

        Database database = new(ConnectionInfo.Memory("gen_keys_surface"));
        SessionLocal session = new(database, "sa");
        LocalResult keys = new(session,
            [new ExpressionColumn(database, new Column("ID", TypeInfo.TypeInteger))], 1, 1);
        keys.Done();
        ResultWithGeneratedKeys withKeys = new ResultWithGeneratedKeys.WithKeys(2, keys);
        Assert.Same(keys, withKeys.GetGeneratedKeys());
        Assert.Equal(2, withKeys.GetUpdateCount());
        database.Close();
    }
}
