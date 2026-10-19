// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — CREATE TRIGGER command, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class CreateTriggerCommandTests
{
    /// <summary>docs/commands.html:1850 — CREATE TRIGGER [ IF NOT EXISTS ] [schemaName.]triggerName</summary>
    [Fact]
    public void CreateTrigger_IfNotExists_RegistersItOnce() // CMD-1850
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("CREATE TRIGGER IF NOT EXISTS TRIG_INS BEFORE INSERT ON TEST FOR EACH ROW CALL 'MyTrigger'");
        _ = connection.Execute("CREATE TRIGGER IF NOT EXISTS TRIG_INS BEFORE INSERT ON TEST FOR EACH ROW CALL 'MyTrigger'");

        Assert.Equal(1, TriggerCount(connection, "TRIG_INS"));
    }

    /// <summary>docs/commands.html:1851 — { BEFORE | AFTER | INSTEAD OF }</summary>
    [Fact]
    public void CreateTrigger_AfterInsert_IsRegistered() // CMD-1851
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("CREATE TRIGGER TRIG_AFT AFTER INSERT ON TEST FOR EACH ROW CALL 'MyTrigger'");

        Assert.Equal(1, TriggerCount(connection, "TRIG_AFT"));
    }

    /// <summary>docs/commands.html:1852 — { INSERT | UPDATE | DELETE | { SELECT | ROLLBACK } }</summary>
    [Fact]
    public void CreateTrigger_TwoEvents_IsRegisteredForBoth() // CMD-1852
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("CREATE TRIGGER TRIG_MULTI BEFORE INSERT, UPDATE ON TEST FOR EACH ROW CALL 'MyTrigger'");

        Assert.Equal(1, TriggerCount(connection, "TRIG_MULTI"));
    }

    /// <summary>docs/commands.html:1853 — [,...] ON [schemaName.]tableName [ FOR EACH { ROW | STATEMENT } ]</summary>
    [Fact]
    public void CreateTrigger_ForEachStatement_IsRegistered() // CMD-1853
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("CREATE TRIGGER TRIG_STMT BEFORE INSERT ON TEST FOR EACH STATEMENT CALL 'MyTrigger'");

        Assert.Equal(1, TriggerCount(connection, "TRIG_STMT"));
    }

    /// <summary>docs/commands.html:1854 — [ QUEUE int ] [ NOWAIT ]</summary>
    [Fact]
    public void CreateTrigger_QueueNoWait_IsRegistered() // CMD-1854
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("CREATE TRIGGER TRIG_Q BEFORE INSERT ON TEST QUEUE 10 NOWAIT CALL 'MyTrigger'");

        Assert.Equal(1, TriggerCount(connection, "TRIG_Q"));
    }

    /// <summary>docs/commands.html:1855 — { CALL triggeredClassNameString | AS sourceCodeString }</summary>
    [Fact]
    public void CreateTrigger_AsSourceCode_IsRegistered() // CMD-1855
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute(
            "CREATE TRIGGER TRIG_SRC BEFORE INSERT ON TEST AS 'org.h2.api.Trigger create() { return new MyTrigger(\"constructorParam\"); }'");

        Assert.Equal(1, TriggerCount(connection, "TRIG_SRC"));
    }

    private static int TriggerCount(IDbConnection connection, string name) =>
        connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TRIGGERS WHERE TRIGGER_NAME = @Name",
            new { Name = name });

    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
    }
}
