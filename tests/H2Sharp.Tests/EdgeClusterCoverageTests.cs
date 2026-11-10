/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 119: the cheap uncovered cluster — members SQL does not reach, answered directly:
 * the parameterless TODO refusal, NoOperation's answers, the oversized value refusals, the
 * any-array/any-row conversions, ValueNull's refusals, the versioned-value base answers, the
 * batch and fetched results, the command kinds, the DDL refusals, the sequence value visitor
 * answers, the expression delegates and refusals, and the utility answers.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using H2Sharp.Command;
using H2Sharp.Command.Ddl;
using H2Sharp.Command.Dml;
using H2Sharp.Compression;
using H2Sharp.Engine;
using H2Sharp.Api;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Aggregate;
using H2Sharp.Expressions.Conditions;
using H2Sharp.Expressions.Functions;
using H2Sharp.Message;
using H2Sharp.MvStore.RTree;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Store;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class EdgeClusterCoverageTests
{
    /// <summary>A versioned value that stays in the base class answers.</summary>
    private sealed class TestVersionedValue : VersionedValue<TestVersionedValue>
    {
    }

    /// <summary>A lazy result with nothing left to implement.</summary>
    private sealed class TestLazyResult : LazyResult
    {
        public TestLazyResult(SessionLocal session, Expression[] expressions)
            : base(session, expressions)
        {
        }

        protected override Value?[]? FetchNextRow() => null;

        public override int GetVisibleColumnCount() => 0;

        public override void Dispose()
        {
        }
    }

    /// <summary>An extended type that never ported its SQL rendering.</summary>
    private sealed class TestExtTypeInfo : ExtTypeInfo
    {
    }

    private static (Database Database, SessionLocal Session) Open()
    {
        Database database = new(ConnectionInfo.Memory("edge_cluster_" + Guid.NewGuid().ToString("N")));
        SessionLocal session = new(database, "sa");
        return (database, session);
    }

    [Fact]
    public void RefusalsAndNoOperationAnswerDirectly()
    {
        TODOException todo = new();
        Assert.Equal("Not ported yet", todo.Message);

        (Database database, SessionLocal session) = Open();
        NoOperation op = new(session);
        Assert.Equal(0L, op.Update());
        Assert.True(op.IsTransactional());
        Assert.False(op.NeedRecompile());
        Assert.True(op.IsReadOnly());
        Assert.Null(op.QueryMeta());
        Assert.Equal(CommandInterface.NoOperation, op.GetCommandType());
        database.Close();
    }

    [Fact]
    public void OversizedAndOutOfRangeValuesRefuse()
    {
        Assert.Throws<H2SharpException>(() =>
            ValueNumeric.Get(BigInteger.Parse("1" + new string('0', Constants.MaxNumericPrecision))));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ValueDate.FromDateValue(DateTimeUtils.MinDateValue - 1));
        Assert.Throws<H2SharpException>(() =>
            ValueArray.Get(ValueInteger.Get(1).GetTypeInfo(), new Value[Constants.MaxArrayCardinality + 1], null));
        Assert.NotNull(Record.Exception(() => ValueJson.FromJson(new byte[100])));
    }

    [Fact]
    public void AnyArrayAndAnyRowConversionsAnswer()
    {
        Value value = ValueInteger.Get(1);
        Value asArray = value.ConvertToForComparison(Value.Array, null);
        Assert.Equal(Value.Array, asArray.GetValueType());
        Value asRow = value.ConvertToForComparison(Value.Row, null);
        Assert.Equal(Value.Row, asRow.GetValueType());
    }

    [Fact]
    public void ValueNullRefusesReads()
    {
        Assert.NotNull(Record.Exception(() => ValueNull.Instance.GetBoolean()));
        Assert.NotNull(Record.Exception(() => ValueNull.Instance.GetInt()));
        Assert.NotNull(Record.Exception(() => ValueNull.Instance.GetLong()));
        Assert.NotNull(Record.Exception(() => ValueNull.Instance.GetDouble()));
    }

    [Fact]
    public void VersionedValueBaseAnswers()
    {
        TestVersionedValue value = new();
        Assert.True(value.IsCommitted());
        Assert.Equal(VersionedValue<TestVersionedValue>.NoEntryId, value.GetEntryId());
        Assert.Same(value, value.GetCurrentValue());
        Assert.Same(value, value.GetCommittedValue());
    }

    [Fact]
    public void BatchResultAndFetchedResultAnswerTheirEdges()
    {
        BatchResult batch = new(new long[] { 1 }, null, []);
        Assert.Null(batch.GetGeneratedKeys());
        Assert.Equal(new long[] { 1 }, batch.GetUpdateCounts());

        (Database database, SessionLocal session) = Open();
        TestLazyResult lazy = new(session, []);
        Assert.Null(lazy.CreateShallowCopy(session));
        database.Close();
    }

    [Fact]
    public void DirectCommandMembersAnswer()
    {
        (Database database, SessionLocal session) = Open();

        TransactionCommand transaction = new(session, CommandInterface.NoOperation);
        Assert.Throws<TODOException>(() => transaction.Update());
        Assert.Null(transaction.QueryMeta());
        Assert.Equal(CommandInterface.NoOperation, transaction.GetCommandType());

        Delete delete = new(session);
        Assert.Equal(CommandInterface.Delete, delete.GetCommandType());
        Assert.Throws<TODOException>(() => delete.CollectDependencies([]));
        Assert.True(delete.IsRetryable());

        SetComment comment = new(session);
        comment.SetObjectType(DbObject.Aggregate);
        comment.SetObjectName("X");
        Assert.Throws<H2SharpException>(() => comment.Update());
        Assert.Equal(CommandInterface.Comment, comment.GetCommandType());

        database.Close();
    }

    [Fact]
    public void DdlRefusalsMatchTheirCodes()
    {
        (Database database, SessionLocal session) = Open();
        session.PrepareCommand("CREATE TABLE T(ID INT)").ExecuteUpdate(false);
        session.PrepareCommand("CREATE VIEW V AS SELECT * FROM T").ExecuteUpdate(false);
        session.PrepareCommand("CREATE SEQUENCE S").ExecuteUpdate(false);

        // A missing view is refused by the parser when the statement names one; the
        // command's own refusals answer a name that vanished between prepare and update,
        // and a table named where a view was expected.
        DropView drop = new(session, database.GetSchema("PUBLIC"));
        drop.SetViewName("Missing");
        Assert.Throws<H2SharpException>(() => drop.Update());
        drop.SetViewName("T");
        Assert.Throws<H2SharpException>(() => drop.Update());
        Assert.Equal(CommandInterface.DropView, drop.GetCommandType());

        H2SharpException truncate = Assert.Throws<H2SharpException>(
            () => session.PrepareCommand("TRUNCATE TABLE V").ExecuteUpdate(false));
        Assert.Equal(ErrorCode.CannotTruncate1, truncate.H2ErrorCode);

        session.PrepareCommand("CREATE DOMAIN D AS INT").ExecuteUpdate(false);
        session.PrepareCommand("ALTER DOMAIN D ADD CONSTRAINT C1 CHECK (VALUE > 0)").ExecuteUpdate(false);
        H2SharpException rename = Assert.Throws<H2SharpException>(
            () => session.PrepareCommand("ALTER DOMAIN D RENAME CONSTRAINT C1 TO C1").ExecuteUpdate(false));
        Assert.Equal(ErrorCode.ConstraintAlreadyExists1, rename.H2ErrorCode);

        session.PrepareCommand("CREATE TABLE K(ID INT CONSTRAINT KC CHECK(ID > 0))").ExecuteUpdate(false);
        session.PrepareCommand("COMMENT ON CONSTRAINT KC IS 'the key check'").ExecuteUpdate(false);

        database.Close();
    }

    [Fact]
    public void SequenceValueAnswersItsVisitors()
    {
        (Database database, SessionLocal session) = Open();
        session.PrepareCommand("CREATE SEQUENCE S").ExecuteUpdate(false);
        Sequence sequence = database.GetSchema("PUBLIC").GetSequence("S");
        SequenceValue value = new(sequence);

        Assert.True(value.IsEverything(ExpressionVisitor.ReadOnlyVisitor));
        Assert.True(value.IsEverything(ExpressionVisitor.GetDependenciesVisitor([])));
        Assert.Equal(1, value.GetCost());

        CompatibilitySequenceValueFunction next = new(value, null, false);
        Assert.False(next.IsEverything(ExpressionVisitor.ReadOnlyVisitor));

        database.Close();
    }

    [Fact]
    public void ExpressionEdgesAnswerOrRefuse()
    {
        (Database database, SessionLocal session) = Open();
        session.PrepareCommand("CREATE TABLE T(ID INT)").ExecuteUpdate(false);

        // A null array element is the null expression.
        Expression nullElement = new ArrayElementReference(
            ValueExpression.Get(ValueNull.Instance), ValueExpression.Get(ValueInteger.Get(0)))
            .Optimize(session);
        Assert.Same(ValueExpression.Null, nullElement);

        // A non-array on the left refuses.
        Assert.Throws<H2SharpException>(() => new ArrayElementReference(
            ValueExpression.Get(ValueInteger.Get(1)), ValueExpression.Get(ValueInteger.Get(0)))
            .Optimize(session));

        // A JSON column subscript keeps the reference: the operands are not both constant.
        session.PrepareCommand("CREATE TABLE J(D JSON)").ExecuteUpdate(false);
        session.PrepareCommand("INSERT INTO J VALUES('[1,2]')").ExecuteUpdate(false);
        session.PrepareCommand("SELECT D[1] FROM J");

        // Format delegates to its argument.
        Expression arg = ValueExpression.Get(ValueInteger.Get(1));
        Format format = new(arg, Format.FormatEnum.Json);
        Assert.Equal(arg.IsIdentity(), format.IsIdentity());
        Assert.Equal(arg.GetNullable(), format.GetNullable());
        Assert.Equal(arg.GetTableName(), format.GetTableName());
        Assert.Equal(arg.GetColumnName(session, 0), format.GetColumnName(session, 0));

        // NULLIF keeps itself when the arguments are not both constant, and names itself.
        session.PrepareCommand("SELECT NULLIF(ID, 2) FROM T");
        Assert.Equal("NULLIF", new NullIfFunction(
            ValueExpression.Get(ValueInteger.Get(1)), ValueExpression.Get(ValueInteger.Get(2))).GetName());

        // Day and month names: the null answer and the refusal of an unknown function id.
        Assert.Same(ValueNull.Instance, new DayMonthNameFunction(
            ValueExpression.Get(ValueNull.Instance), DayMonthNameFunction.DayName).GetValue(session));
        Assert.Throws<Exception>(() => new DayMonthNameFunction(
            ValueExpression.Get(ValueDate.FromDateValue(DateTimeUtils.DateValue(2020, 1, 1))), 99)
            .GetValue(session));

        // An operation with two sides counts both, and refuses an out-of-range subexpression.
        TimeZoneOperation operation = new(
            ValueExpression.Get(ValueInteger.Get(1)), ValueExpression.Get(ValueInteger.Get(2)));
        Assert.Equal(1, operation.GetCost());
        Assert.Throws<IndexOutOfRangeException>(() => operation.GetSubexpression(2));

        // IN over an array answers its shapes.
        ConditionInArray condition = new(
            ValueExpression.Get(ValueInteger.Get(1)), true, ValueExpression.Get(ValueInteger.Get(1)), false, 0);
        Assert.True(condition.NeedParentheses());
        condition.UpdateAggregate(session, 0);

        database.Close();
    }

    [Fact]
    public void AggregateDataEdgesAnswer()
    {
        (Database database, SessionLocal session) = Open();

        AggregateDataDistinctWithCounts distinct = new(true, 10);
        distinct.Add(session, ValueNull.Instance);
        Assert.Null(distinct.GetValues());
        Assert.Throws<Exception>(() => distinct.GetValue(session));

        Assert.Same(ValueNull.Instance, new AggregateDataCovar(AggregateType.CovarPop).GetValue(session));
        Assert.Throws<Exception>(() => new AggregateDataCovar((AggregateType)999).GetValue(session));
        Assert.Same(ValueNull.Instance, new AggregateDataStdVar(AggregateType.StddevPop).GetValue(session));
        Assert.Throws<Exception>(() => new AggregateDataStdVar((AggregateType)999).GetValue(session));

        database.Close();
    }

    [Fact]
    public void UtilityEdgesAnswer()
    {
        Assert.Equal(0, new Random().NextNonNegativeInt32(0));
        Assert.Equal(0L, new Random().NextNonNegativeInt64(0L));

        CaseInsensitiveMap<string> map = new(4);
        map["A"] = "a";
        Assert.Equal("a", map["A"]);

        Assert.False(((System.Collections.IEnumerable)SmallLRUCache<string, int>.NewInstance(4))
            .GetEnumerator().MoveNext());
        Assert.False(new CacheHead().CanRemove());
        Assert.Equal(0, new CacheHead().GetMemory());

        CacheHead linked = new();
        linked.CachePrevious = linked;
        Assert.Throws<Exception>(() => linked.SetPos(0));

        Assert.Equal(": 0", new MergedResult().ToString());

        DefaultSpatial spatial = new(1, 1f, 2f, 3f, 4f);
        Assert.True(spatial.EqualsIgnoringId(new DefaultSpatial(2, 1f, 2f, 3f, 4f)));
        Assert.True(spatial.Equals(new DefaultSpatial(1, 1f, 2f, 3f, 4f)));
        Assert.True(spatial.Equals((object?)new DefaultSpatial(1, 1f, 2f, 3f, 4f)));
        Assert.Equal(spatial.GetHashCode(), new DefaultSpatial(1, 1f, 2f, 3f, 4f).GetHashCode());

        using RangeReader reader = new(new StringReader("abc"), 0, 10);
        Assert.Equal(0L, reader.Skip(0));
        Assert.True(reader.Ready());

        using ReadOnlyMemoryStream stream = new(new ReadOnlyMemory<byte>(new byte[8]));
        Assert.Equal(0L, stream.Position);
        stream.Flush();
        Assert.Throws<NotSupportedException>(() => stream.SetLength(1));

        Assert.Throws<NotSupportedException>(() => new TestExtTypeInfo().GetSql(new StringBuilder(), 0));

        Dictionary<string, string> properties = PropertiesFile.LoadJavaProperties(" key = value");
        Assert.Contains("key", properties.Keys);

        H2SharpException exception = DbException.Get(ErrorCode.GeneralError1, "x");
        _ = exception.SqlState;
        _ = exception.Kind;
        _ = exception.RecordedStackTrace;
        _ = exception.IsCommon;

        Assert.Equal(SqlExceptionKind.NonTransient, ErrorCodeKind.GetKind(2000));
        Assert.Equal(SqlExceptionKind.NonTransientConnection, ErrorCodeKind.GetKind(8000));
        Assert.Equal(SqlExceptionKind.InvalidAuthorizationSpec, ErrorCodeKind.GetKind(28000));
        Assert.Equal(SqlExceptionKind.TransactionRollback, ErrorCodeKind.GetKind(40000));
    }

    [Fact]
    public void TableSynonymAnswersItsMembers()
    {
        (Database database, SessionLocal session) = Open();
        Schema schema = database.GetSchema("PUBLIC");
        CreateSynonymData data = new()
        {
            Schema = schema,
            Id = 1,
            SynonymName = "S",
            SynonymFor = "T",
            SynonymForSchema = schema,
        };
        TableSynonym synonym = new(data);
        synonym.UpdateData(data);
        Assert.NotNull(synonym.GetDropSql());
        Assert.Throws<H2SharpException>(() => synonym.CheckRename());
        Assert.Throws<TODOException>(() => synonym.IsInvalid());

        database.Close();
    }
}
