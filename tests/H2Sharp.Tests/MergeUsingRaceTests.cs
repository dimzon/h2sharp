/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 294: the MERGE USING pending-missed-source path is only reachable when the row returned
 * by the target filter changes between the scan and lockAndRecheckCondition. Two sessions on one
 * in-memory database let one session hold the row lock and commit a new value while the other
 * session's merge is waiting on that row.
 */
using System;
using System.Threading;
using System.Threading.Tasks;
using H2Sharp.Engine;
using Xunit;

namespace H2Sharp.Tests;

public sealed class MergeUsingRaceTests
{
    [Fact]
    public void AChangedRowDefersTheSourceAndFlushesIt()
    {
        Database database = new(ConnectionInfo.Memory("merge_race"));
        SessionLocal holder = new(database, "sa");
        SessionLocal merger = new(database, "sa");
        try
        {
            holder.PrepareCommand("CREATE TABLE P(ID INT, V INT)").ExecuteUpdate(false);
            holder.PrepareCommand("INSERT INTO P VALUES(1, 10), (2, 10)").ExecuteUpdate(false);

            holder.Begin();
            holder.PrepareCommand("UPDATE P SET V=20 WHERE ID=1").ExecuteUpdate(false);

            Task mergeTask = Task.Run(() => merger.PrepareCommand(
                "MERGE INTO P USING (SELECT X AS ID FROM SYSTEM_RANGE(1, 2)) S "
                + "ON (P.ID = S.ID AND P.V = 10) "
                + "WHEN MATCHED THEN UPDATE SET P.V = P.V "
                + "WHEN NOT MATCHED THEN INSERT (ID, V) VALUES (S.ID, S.ID)").ExecuteUpdate(false));

            // Give the merge time to reach the locked row and block on it.
            Thread.Sleep(500);
            holder.Commit(false);

            Assert.True(mergeTask.Wait(TimeSpan.FromSeconds(10)), "merge did not finish after commit");
            mergeTask.GetAwaiter().GetResult();

            using var result = merger.PrepareCommand("SELECT 1 FROM P WHERE ID = 1 AND V = 1")
                .ExecuteQuery(0, 0, false);
            Assert.True(result.Next());
        }
        finally
        {
            database.Close();
        }
    }
}
