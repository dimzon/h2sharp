/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The API surface of a parsed MERGE USING: the command type, the source filter, the ON
 * condition, the surviving WHEN clauses, their SQL text, and the dependencies each clause
 * reports. No SQL of its own runs here; the clauses' behavior is under the oracle tests.
 */
using System;
using System.Collections.Generic;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Command.Dml;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Tables;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class MergeUsingApiPortTests
{
    [Fact]
    public void ParsedMergeUsingAnswersItsClausesAndDependencies()
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("merge_api_" + Guid.NewGuid().ToString("N"))), "sa")
        {
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        session.CastDataProvider = new SessionCastDataProvider(session, DefaultCastDataProvider.Instance);
        try
        {
            CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(
                "CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(10))"));
            _ = container.ExecuteUpdate(false);

            container = Assert.IsType<CommandContainer>(session.PrepareCommand(
                "MERGE INTO T AS P USING (VALUES (1, 'a')) AS S(ID, NAME) ON (P.ID = S.ID) "
                + "WHEN MATCHED AND P.ID > 0 THEN UPDATE SET P.NAME = S.NAME "
                + "WHEN MATCHED AND P.ID < 0 THEN DELETE "
                + "WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (S.ID, S.NAME)"));
            MergeUsing merge = Assert.IsType<MergeUsing>(container.GetPreparedCommand());

            Assert.Equal(CommandInterface.Merge, merge.GetCommandType());
            Assert.NotNull(merge.GetSourceTableFilter());
            Assert.NotNull(merge.GetOnCondition());
            Assert.Equal(3, merge.GetWhen().Count);

            // The command and each clause write their own SQL back.
            Assert.Contains("MERGE INTO", merge.ToString(), StringComparison.Ordinal);
            foreach (MergeUsing.When when in merge.GetWhen())
            {
                string sql = when.ToString();
                Assert.Contains("WHEN ", sql, StringComparison.Ordinal);
                Assert.Contains("MATCHED", sql, StringComparison.Ordinal);
            }

            // Each clause reaches the tables its conditions name. The statement's own
            // CollectDependencies stays a documented stub: Table is not a DbObject here yet
            // (D-011), so there is no way to add the tables to a DbObject set.
            HashSet<object> dependencies = [];
            ExpressionVisitor visitor = ExpressionVisitor.GetDependenciesVisitor(dependencies);
            foreach (MergeUsing.When when in merge.GetWhen())
            {
                when.CollectDependencies(visitor);
            }

            Assert.Contains(merge.GetTargetTableFilter()!.GetTable()!, dependencies);
            Assert.Contains(merge.GetSourceTableFilter()!.GetTable()!, dependencies);
        }
        finally
        {
            session.Close();
        }
    }
}
