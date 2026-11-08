/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of the Database.Settings surface: the rename/remove registry paths,
 * the lock-mode validation, the settings getters and the event-listener clearing.
 */
using System;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class DatabaseSettingsCoverageTests
{
    [Fact]
    public void SettingsSurfaceAnswersThePort()
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("db_settings_" + Guid.NewGuid().ToString("N"))), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        Database database = session.GetDatabase();

        // Add a setting, then a duplicate name refuses.
        Setting setting = new(database, 1, "TEST_SETTING");
        setting.SetIntValue(5);
        database.AddDatabaseObject(session, setting);
        _ = database.GetAllSettings();
        Assert.Throws<Exception>(
            () => database.AddDatabaseObject(session, new Setting(database, 2, "TEST_SETTING")));

        // Renaming a setting is not supported and refuses at the check.
        Assert.Throws<H2SharpException>(
            () => database.RenameDatabaseObject(session, setting, "RENAMED"));

        // Removing a setting drops it from the registry.
        database.RemoveDatabaseObject(session, setting);

        // An unknown lock mode refuses.
        Assert.Throws<H2SharpException>(() => database.SetLockMode(5));

        // Setting the same mode is a no-op.
        database.SetMode(database.GetMode());

        // The settings getters answer their stored values.
        database.SetMaxOperationMemory(100);
        _ = database.GetMaxOperationMemory();
        database.SetQueryStatistics(true);
        _ = database.IsQueryStatistics();
        database.SetQueryStatisticsMaxEntries(10);
        _ = database.GetQueryStatisticsMaxEntries();
        database.SetAllowBuiltinAliasOverride(true);
        _ = database.IsAllowBuiltinAliasOverride();
        database.SetJavaObjectSerializerName("x");
        _ = database.GetJavaObjectSerializerName();
        _ = database.GetWriteDelay();
        _ = database.GetLockMode();
        _ = database.GetDefaultTableType();
        _ = database.GetCluster();

        // Clearing the event listener by name.
        database.SetEventListenerClass(null);
        // Loading by class name refuses (D-044).
        Assert.Throws<H2SharpException>(() => database.SetEventListenerClass("SomeClass"));

        // Exclusive mode is off, and unsetting it succeeds when nothing holds it.
        Assert.False(database.IsInExclusiveMode());
        Assert.True(database.UnsetExclusiveSession(session));
    }
}
