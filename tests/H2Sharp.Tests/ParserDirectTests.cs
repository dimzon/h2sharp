/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of Parser.cs: record types exercised through SQL parsing
 * (FOREIGN KEY constraints, ALTER TABLE constraints, CREATE TABLE options).
 */
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ParserDirectTests
{
    private readonly SessionLocal session;
    private readonly Database database;

    public ParserDirectTests()
    {
        database = new Database(
            ConnectionInfo.Memory("parser_" + Guid.NewGuid().ToString("N")));
        session = new SessionLocal(database, "sa");
    }

    private CommandContainer Prepare(string sql)
        => Assert.IsType<CommandContainer>(session.PrepareCommand(sql));

    private void Execute(string sql)
    {
        CommandContainer container = Prepare(sql);
        _ = container.ExecuteUpdate(false);
    }

    private string Scalar(string sql)
    {
        CommandContainer container = Prepare(sql);
        using ResultInterface result = container.ExecuteQuery(10, 0, false);
        Assert.True(result.Next());
        return result.CurrentRow()![0]!.GetString()!;
    }

    [Fact]
    public void CreateTableWithForeignKeyReferences()
    {
        // FOREIGN KEY with REFERENCES exercises ColumnDefinition.RefTableSchema,
        // RefTable, RefColumns, RefUpdateAction, RefDeleteAction (lines 100-111)
        Execute("CREATE TABLE parent(id INT PRIMARY KEY)");
        Execute("INSERT INTO parent VALUES (1)");
        Execute("CREATE TABLE child(id INT, parent_id INT, CONSTRAINT fk_parent FOREIGN KEY (parent_id) REFERENCES parent(id))");
        Execute("INSERT INTO child VALUES (1, 1)");

        // Verify the FK works
        Assert.Equal("1", Scalar("SELECT COUNT(*) FROM child"));
    }

    [Fact]
    public void AlterTableAddConstraint()
    {
        // ALTER TABLE ADD CONSTRAINT exercises AlterTableStatement.GetCommandType()
        // returning AddConstraintPrimaryKey (lines 154-156)
        Execute("CREATE TABLE t1(a INT PRIMARY KEY)");
        Execute("INSERT INTO t1 VALUES (1)");
        Execute("CREATE TABLE t2(b INT)");
        Execute("INSERT INTO t2 VALUES (1)");
        Execute("ALTER TABLE t2 ADD CONSTRAINT fk_t1 FOREIGN KEY (b) REFERENCES t1(a)");

        Assert.Equal("1", Scalar("SELECT COUNT(*) FROM t2"));
    }

    [Fact]
    public void AlterTableDropConstraint()
    {
        // ALTER TABLE DROP CONSTRAINT exercises AlterTableStatement.GetCommandType()
        // returning DropConstraint (line 154-156)
        Execute("CREATE TABLE t1(a INT PRIMARY KEY)");
        Execute("INSERT INTO t1 VALUES (1)");
        Execute("CREATE TABLE t2(b INT)");
        Execute("INSERT INTO t2 VALUES (1)");
        Execute("ALTER TABLE t2 ADD CONSTRAINT fk_t1 FOREIGN KEY (b) REFERENCES t1(a)");
        Execute("ALTER TABLE t2 DROP CONSTRAINT fk_t1");

        // After dropping the FK, inserts should work without constraint
        Execute("INSERT INTO t2 VALUES (999)");
        Assert.Equal("2", Scalar("SELECT COUNT(*) FROM t2"));
    }
}
