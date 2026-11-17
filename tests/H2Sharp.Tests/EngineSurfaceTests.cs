/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 248: engine-side surfaces that SQL reaches only indirectly — meta record ordering,
 * the parameter collection, a sparse row, the lazy result's metadata, and a command list's
 * dependencies and retryability.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using H2Sharp.Api;
using H2Sharp.Command;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class EngineSurfaceTests
{
    private static Row MetaRow(int id, int type, string sql)
        => Row.Get(
            [
                ValueInteger.Get(id),
                ValueInteger.Get(0),
                ValueInteger.Get(type),
                ValueVarchar.Get(sql),
            ],
            1);

    [Fact]
    public void MetaRecordsOrderByCreationAndThenById()
    {
        int[] types =
        [
            DbObject.Comment, DbObject.Aggregate, DbObject.Right, DbObject.Role,
            DbObject.Synonym, DbObject.Trigger, DbObject.Constraint, DbObject.Index,
            DbObject.TableOrView, DbObject.Constant, DbObject.Sequence, DbObject.Domain,
            DbObject.FunctionAlias, DbObject.Schema, DbObject.User, DbObject.Setting,
        ];
        List<MetaRecord> records = [];
        for (int i = 0; i < types.Length; i++)
        {
            records.Add(new MetaRecord(MetaRow(100 + i, types[i], "CREATE X " + i)));
        }

        records.Sort();
        int[] expected =
        [
            DbObject.Setting, DbObject.User, DbObject.Schema, DbObject.FunctionAlias,
            DbObject.Domain, DbObject.Sequence, DbObject.Constant, DbObject.TableOrView,
            DbObject.Index, DbObject.Constraint, DbObject.Trigger, DbObject.Synonym,
            DbObject.Role, DbObject.Right, DbObject.Aggregate, DbObject.Comment,
        ];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], records[i].GetObjectType());
            Assert.Equal(100 + Array.IndexOf(types, expected[i]), records[i].GetId());
            Assert.Equal("CREATE X " + Array.IndexOf(types, expected[i]), records[i].GetSql());
            Assert.Contains("objectType=" + expected[i], records[i].ToString(), StringComparison.Ordinal);
        }

        MetaRecord first = new(MetaRow(5, DbObject.TableOrView, "CREATE A"));
        MetaRecord second = new(MetaRow(6, DbObject.TableOrView, "CREATE B"));
        Assert.True(first.CompareTo(second) < 0);
        Assert.True(second.CompareTo(first) > 0);
        Assert.Equal(0, first.CompareTo(new MetaRecord(MetaRow(5, DbObject.TableOrView, "CREATE C"))));
        Assert.Throws<ArgumentNullException>(() => first.CompareTo(null));

        MetaRecord unknown = new(MetaRow(7, 99, "CREATE X"));
        Assert.ThrowsAny<Exception>(() => unknown.CompareTo(first));
    }

    [Fact]
    public void MetaRecordPreparesOneStatementAndReportsFailures()
    {
        Database database = new(ConnectionInfo.Memory("meta_record_surface"));
        SessionLocal session = new(database, "sa");
        try
        {
            MetaRecord create = new(MetaRow(1000, DbObject.TableOrView, "CREATE TABLE META_OK(ID INT)"));
            Assert.NotNull(create.Prepare(database, session, null, throwIt: false));
            create.PrepareAndExecute(database, session, null, throwIt: false);

            // A statement that parses but cannot run: the failure is reported, not thrown.
            MetaRecord failing = new(MetaRow(1001, DbObject.TableOrView, "INSERT INTO META_MISSING VALUES 1"));
            failing.PrepareAndExecute(database, session, null, throwIt: false);

            // The same failure with throwIt set comes back out.
            Assert.ThrowsAny<H2SharpException>(
                () => failing.PrepareAndExecute(database, session, null, throwIt: true));

            // A meta row has to be exactly one statement.
            MetaRecord two = new(MetaRow(1002, DbObject.TableOrView, "SELECT 1; SELECT 2"));
            Assert.Throws<TODOException>(() => two.Prepare(database, session, null, throwIt: false));
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void ParametersAreOneListByNameAndPosition()
    {
        using H2Connection connection = new("Data Source=parameter_surface;Storage=Memory");
        connection.Open();
        using H2Command command = (H2Command)connection.CreateCommand();
        H2ParameterCollection parameters = (H2ParameterCollection)command.Parameters;

        H2Parameter first = new() { ParameterName = "p1", Value = 1 };
        H2Parameter second = new() { ParameterName = "p2", Value = 2 };
        H2Parameter third = new() { ParameterName = "p3", Value = 3 };
        Assert.Same(command, parameters.Command);
        Assert.Equal(0, parameters.Add(first));
        parameters.AddRange(new object[] { second, third });
        Assert.Equal(3, parameters.Count);
        Assert.Same(first, parameters["p1"]);
        Assert.Same(second, parameters[1]);
        Assert.True(parameters.Contains("p2"));
        Assert.True(parameters.Contains((object)third));
        Assert.False(parameters.Contains("p9"));
        Assert.False(parameters.Contains("not a parameter"));
        Assert.Equal(1, parameters.IndexOf("p2"));
        Assert.Equal(1, parameters.IndexOf((object)second));
        Assert.Equal(-1, parameters.IndexOf("p9"));

        object[] copy = new object[3];
        parameters.CopyTo(copy, 0);
        Assert.Same(first, copy[0]);

        parameters.RemoveAt("p2");
        Assert.Equal(2, parameters.Count);
        Assert.Throws<IndexOutOfRangeException>(() => parameters.RemoveAt("p2"));
        Assert.Throws<IndexOutOfRangeException>(() => _ = parameters["p2"]);

        H2Parameter fourth = new() { ParameterName = "p4" };
        parameters["p4"] = fourth;
        Assert.Same(fourth, parameters["p4"]);
        H2Parameter replaced = new() { ParameterName = "p4", Value = 4 };
        parameters["p4"] = replaced;
        Assert.Same(replaced, parameters[2]);

        parameters.Insert(0, first);
        Assert.Same(first, parameters[0]);
        parameters.Remove((object)first);
        Assert.Equal(3, parameters.Count);
        Assert.Throws<ArgumentException>(() => parameters.Add("not a parameter"));
        Assert.Throws<ArgumentNullException>(() => parameters.AddRange(null!));
        Assert.Throws<ArgumentNullException>(() => parameters.Add(null!));

        IEnumerator enumerator = parameters.GetEnumerator();
        Assert.True(enumerator.MoveNext());

        parameters[0] = third;
        Assert.Same(third, parameters[0]);
        parameters.Clear();
        Assert.Equal(0, parameters.Count);

        H2Parameter defaults = new();
        Assert.Equal(System.Data.DbType.String, defaults.DbType);
        defaults.DbType = System.Data.DbType.Int32;
        defaults.ResetDbType();
        Assert.Equal(System.Data.DbType.String, defaults.DbType);
        defaults.ParameterName = null!;
        Assert.Equal(string.Empty, defaults.ParameterName);
        defaults.SourceColumn = null!;
        Assert.Equal(string.Empty, defaults.SourceColumn);
    }

    [Fact]
    public void SparseRowStoresOnlyIndexedColumns()
    {
        Sparse row = new(5, 2, [2, 0, 1, 0, 0]);
        Assert.Equal(5, row.GetColumnCount());
        row.SetValue(0, ValueInteger.Get(10));
        row.SetValue(1, ValueInteger.Get(11));
        row.SetValue(2, ValueInteger.Get(12));
        row.SetKey(77);

        Assert.Equal(10, row.GetValue(0)!.GetInt());
        Assert.Null(row.GetValue(1));
        Assert.Equal(12, row.GetValue(2)!.GetInt());
        Assert.Null(row.GetValue(3));
        Assert.Equal(77, row.GetValue(SearchRow.RowidIndex)!.GetLong());

        Row source = Row.Get(
            [
                ValueInteger.Get(21),
                ValueInteger.Get(22),
                ValueInteger.Get(23),
                ValueInteger.Get(24),
                ValueInteger.Get(25),
            ],
            1,
            99);
        row.CopyFrom(source);
        Assert.Equal(99, row.GetKey());
        Assert.Equal(21, row.GetValue(0)!.GetInt());
        Assert.Equal(23, row.GetValue(2)!.GetInt());
        Assert.Null(row.GetValue(1));
        Assert.Null(row.GetValue(3));
    }

    [Fact]
    public void LazyResultAnswersMetadataFromItsExpressions()
    {
        Database database = new(ConnectionInfo.Memory("lazy_metadata"));
        SessionLocal session = new(database, "sa");
        try
        {
            Expression expression = ValueExpression.Get(ValueInteger.Get(0));
            MetadataLazyResult result = new(session, [expression]);
            Assert.Equal("0", result.GetAlias(0));
            Assert.Null(result.GetSchemaName(0));
            Assert.Null(result.GetTableName(0));
            Assert.Equal("0", result.GetColumnName(0));
            Assert.Equal(Value.Integer, result.GetColumnType(0).GetValueType());
            Assert.False(result.IsIdentity(0));
            Assert.Equal(H2Sharp.Tables.Column.NullableUnknown, result.GetNullable(0));
            result.SetFetchSize(100);
            Assert.Equal(1, result.GetFetchSize());
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void CommandListAnswersDependenciesAndRetryability()
    {
        Database database = new(ConnectionInfo.Memory("command_list_surface"));
        SessionLocal session = new(database, "sa");
        try
        {
            session.PrepareCommand("CREATE TABLE CL_REASON(ID INT)").ExecuteUpdate(null!);
            H2Sharp.Tables.Table reason =
                database.GetSchema("PUBLIC")!.FindTableOrView(session, "CL_REASON")!;
            CommandInterface command = session.PrepareCommand(
                "INSERT INTO CL_REASON VALUES 1; INSERT INTO CL_REASON VALUES 2");
            CommandList list = Assert.IsType<CommandList>(command);
            Assert.Equal(2, list.GetCommands().Count);
            Assert.Equal(CommandInterface.Insert, list.GetCommandType());
            Assert.False(list.IsQuery());
            Assert.False(list.IsReadOnly());
            Assert.True(list.IsTransactional());
            Assert.Empty(list.GetParameters());
            Assert.True(list.IsRetryableForList());
            // D-011: a table is not a DbObject, so a plain INSERT has no database-object
            // dependencies to report; the walk still runs over every container.
            Assert.Empty(list.GetDependencies());
            list.InvalidateCachedResult(reason);
            _ = list.UpdateRows(null!);
            list.Close();
        }
        finally
        {
            database.Close();
        }
    }

    private sealed class MetadataLazyResult : LazyResult
    {
        internal MetadataLazyResult(SessionLocal session, Expression[] expressions)
            : base(session, expressions)
        {
        }

        public override int GetVisibleColumnCount() => 1;

        public override void Dispose()
        {
        }

        protected override Value?[]? FetchNextRow() => null;
    }
}
