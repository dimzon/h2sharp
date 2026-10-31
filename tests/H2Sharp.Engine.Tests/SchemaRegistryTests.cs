/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The rules the schema registry is judged by.
 *
 * A differential test for these would have to run statements on both sides, and the port cannot
 * yet, so these are checked directly against the registry instead — and the table used to do it
 * is a test double, named as one, so nobody reads a passing test here as evidence that tables
 * exist. What is real is the three rules the registry is responsible for: a name is found
 * whatever case it is written in, a name already in use cannot be taken again, and a name that
 * is not there fails with the error code callers see rather than with null.
 *
 * The error code is the point of the last two. The JDBC driver turns it into a SQLException
 * whose code a client may branch on, so a port that returned null would produce
 * NullReferenceException where the reference produces 42102 — the difference between "no such
 * table" and "the driver is broken".
 *
 * The case corpus includes lower case forms of the same names deliberately: object names are
 * folded with the database's fold, not the current culture's, so a registry that used the
 * current culture would look up "t" differently on a Turkish machine. Per D-033 the fold of
 * unquoted names belongs to the parser (upper before the registry sees them), so the registry
 * itself is exact under the default comparer; the case-insensitive corpus runs against a
 * CASE_INSENSITIVE_IDENTIFIERS=TRUE database, the one setting that makes the comparer fold.
 */
using System;
using H2Sharp;
using H2Sharp.Engine;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using Index = H2Sharp.Indexes.Index;
using Xunit;

namespace H2Sharp.Engine.Tests;

public sealed class SchemaRegistryTests
{
    /// <remarks>
    /// The registry is exact under the default comparer: D-033 folds unquoted identifiers to
    /// upper at parse, so a lower-case spelling that reached the registry anyway is a different
    /// identifier and must not find the table. The case-insensitive branch of the comparer is
    /// what CASE_INSENSITIVE_IDENTIFIERS=TRUE buys, and that is the database these tests run.
    /// </remarks>
    [Fact]
    public void ANameIsFoundWhateverCaseItIsWrittenIn()
    {
        Schema schema = NewSchema(caseInsensitiveIdentifiers: true);
        Table table = new TestTable("PERSON");
        schema.Add(table);

        Assert.Same(table, schema.FindTableOrView(null, "PERSON"));
        Assert.Same(table, schema.FindTableOrView(null, "person"));
        Assert.Same(table, schema.FindTableOrView(null, "Person"));

        // Under the default comparer the registry is exact: the same spellings are the same
        // identifier, a different one is simply not there.
        Schema exact = NewSchema();
        Table exactTable = new TestTable("PERSON");
        exact.Add(exactTable);
        Assert.Same(exactTable, exact.FindTableOrView(null, "PERSON"));
        Assert.Null(exact.FindTableOrView(null, "person"));
    }

    [Fact]
    public void ANameInUseCannotBeTakenAgain()
    {
        Schema schema = NewSchema(caseInsensitiveIdentifiers: true);
        schema.Add(new TestTable("PERSON"));

        H2SharpException ex = Assert.Throws<H2SharpException>(
            () => schema.Add(new TestTable("person")));
        Assert.Equal(Api.ErrorCode.TableOrViewAlreadyExists1, ex.H2ErrorCode);
    }

    [Fact]
    public void AMissingTableFailsWithTheCodeCallersSee()
    {
        Schema schema = NewSchema();

        Assert.Null(schema.FindTableOrView(null, "MISSING"));
        H2SharpException ex = Assert.Throws<H2SharpException>(() => schema.GetTableOrView(null, "MISSING"));
        Assert.Equal(Api.ErrorCode.TableOrViewNotFound1, ex.H2ErrorCode);
    }

    [Fact]
    public void RemovingATableFreesItsName()
    {
        Schema schema = NewSchema();
        Table table = new TestTable("PERSON");
        schema.Add(table);
        schema.Remove(table);

        Assert.Null(schema.FindTableOrView(null, "PERSON"));
        Assert.True(schema.IsEmpty());

        // The name is free again, which is what DROP TABLE means.
        schema.Add(new TestTable("person"));
        Assert.False(schema.IsEmpty());
    }

    private static Schema NewSchema(bool caseInsensitiveIdentifiers = false)
    {
        ConnectionInfo info = ConnectionInfo.Memory("schema_test");
        info.Options.CaseInsensitiveIdentifiers = caseInsensitiveIdentifiers;
        return new Schema(new Database(info), 1, "PUBLIC", false);
    }

    /// <summary>
    /// A table that is only a name. It exists so the registry has something to hold; every
    /// member the registry does not use refuses, so a passing test here cannot be mistaken for a
    /// working table.
    /// </summary>
    private sealed class TestTable : Table
    {
        private readonly string name;

        public TestTable(string name)
        {
            this.name = name;
        }

        public override string GetTableName() => name;

        public override int GetTableType() => TABLE;

        public override int GetColumnCount() => throw new TODOException("TestTable has no columns");

        public override Column GetColumn(int index) => throw new TODOException("TestTable has no columns");

        public override Column[] GetColumns() => throw new TODOException("TestTable has no columns");

        public override Index? FindPrimaryKey() => null;

        public override Index? GetScanIndex(SessionLocal session) => null;

        public override Index? GetIndexFor(SessionLocal session, H2Sharp.Result.SearchRow row) => null;

        public override long GetRowCountApproximation(SessionLocal session) => 0;

        public override bool CanGetRowCount(SessionLocal session) => false;

        public override long GetRowCount(SessionLocal session) => throw new TODOException("TestTable holds nothing");

        public override string GetCreateSql() => throw new TODOException("TestTable cannot create itself");

        public override string GetSql(int sqlFlags) => name;

        public override System.Text.StringBuilder GetSql(System.Text.StringBuilder builder, int sqlFlags)
            => builder.Append(name);

        public override void Truncate(SessionLocal session) => throw new TODOException("TestTable holds nothing");

        public override void RemoveChildrenAndResources(SessionLocal session)
            => throw new TODOException("TestTable has no children");

        public override void Close()
        {
        }

        public override bool IsClosed() => false;
    }
}
