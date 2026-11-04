/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The isolation level and its maps: the SQL spelling, the JDBC constant, the LOCK_MODE
 * equivalent, the non-repeatable-read flag, and the three lookups back, checked against the
 * reference enum itself.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using H2Sharp;
using H2Sharp.Engine;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class IsolationLevelOracleTests
{
    [JavaOracleFact]
    public void TheMapsMatchTheReferenceEnum()
    {
        const string JavaBody = """
        {
        for (org.h2.engine.IsolationLevel v : org.h2.engine.IsolationLevel.values()) {
          System.out.println(v.name() + "|" + v.getSQL() + "|" + v.getJdbc() + "|" + v.getLockMode()
              + "|" + v.allowNonRepeatableRead());
        }
        int[] jdbc = {1, 2, 4, 6, 8};
        for (int i : jdbc) {
          System.out.println("jdbc" + i + "=" + org.h2.engine.IsolationLevel.fromJdbc(i).name());
        }
        int[] lockModes = {0, 1, 2, 3};
        for (int i : lockModes) {
          System.out.println("lock" + i + "=" + org.h2.engine.IsolationLevel.fromLockMode(i).name());
        }
        String[] sqls = {"READ UNCOMMITTED", "READ COMMITTED", "REPEATABLE READ", "SNAPSHOT", "SERIALIZABLE"};
        for (String s : sqls) {
          System.out.println("sql[" + s + "]=" + org.h2.engine.IsolationLevel.fromSql(s).name());
        }
        try {
          org.h2.engine.IsolationLevel.fromJdbc(3);
          System.out.println("badJdbc=none");
        } catch (RuntimeException e) {
          System.out.println("badJdbc=" + ((org.h2.message.DbException) e).getErrorCode());
        }
        try {
          org.h2.engine.IsolationLevel.fromSql("NOPE");
          System.out.println("badSql=none");
        } catch (RuntimeException e) {
          System.out.println("badSql=" + ((org.h2.message.DbException) e).getErrorCode());
        }
        }
        """;
        List<string> java = OracleRunner.Run([], JavaBody);

        List<string> port = [];
        foreach (IsolationLevel level in Enum.GetValues<IsolationLevel>())
        {
            port.Add(JavaName(level) + "|" + level.GetSql() + "|" + level.GetJdbc()
                + "|" + level.GetLockMode() + "|"
                + (level.AllowNonRepeatableRead() ? "true" : "false"));
        }

        foreach (int jdbc in (int[])[1, 2, 4, 6, 8])
        {
            port.Add("jdbc" + jdbc.ToString(CultureInfo.InvariantCulture) + "="
                + JavaName(IsolationLevels.FromJdbc(jdbc)));
        }

        foreach (int lockMode in (int[])[0, 1, 2, 3])
        {
            port.Add("lock" + lockMode.ToString(CultureInfo.InvariantCulture) + "="
                + JavaName(IsolationLevels.FromLockMode(lockMode)));
        }

        foreach (string sql in (string[])["READ UNCOMMITTED", "READ COMMITTED", "REPEATABLE READ",
            "SNAPSHOT", "SERIALIZABLE"])
        {
            port.Add("sql[" + sql + "]=" + JavaName(IsolationLevels.FromSql(sql)));
        }

        port.Add("badJdbc=" + ErrorCodeOf(() => IsolationLevels.FromJdbc(3)));
        port.Add("badSql=" + ErrorCodeOf(() => IsolationLevels.FromSql("NOPE")));

        Assert.True(java.Count == port.Count,
            "lines: jvm=" + java.Count.ToString(CultureInfo.InvariantCulture)
            + " port=" + port.Count.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == port[i], "line " + i.ToString(CultureInfo.InvariantCulture)
                + ":\n  jvm:  " + java[i] + "\n  port: " + port[i]);
        }
    }

    private static string JavaName(IsolationLevel level) => level switch
    {
        IsolationLevel.ReadUncommitted => "READ_UNCOMMITTED",
        IsolationLevel.ReadCommitted => "READ_COMMITTED",
        IsolationLevel.RepeatableRead => "REPEATABLE_READ",
        IsolationLevel.Snapshot => "SNAPSHOT",
        _ => "SERIALIZABLE",
    };

    private static string ErrorCodeOf(Func<IsolationLevel> call)
    {
        try
        {
            _ = call();
            return "none";
        }
        catch (H2SharpException error)
        {
            return error.H2ErrorCode.ToString(CultureInfo.InvariantCulture);
        }
    }
}
