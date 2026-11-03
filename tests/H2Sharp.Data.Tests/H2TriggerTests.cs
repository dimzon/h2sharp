/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The first stage of the trigger boundary: registration by predicate through H2DataSource,
 * BEFORE/AFTER, row/statement, mutation write-back, the same-transaction query access,
 * and the lifecycle (Remove on unregister, Close on close).
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2TriggerTests
{
    /// <summary>
    /// A registration that the predicate accepts for a table of the given stored name, in
    /// any schema, and a trigger that records its calls.
    /// </summary>
    private static (H2DataSource DataSource, H2Connection Connection, List<string> Log) OpenDb()
    {
        H2DataSource dataSource = new H2DataSourceBuilder(
            $"Data Source=trigger_{Guid.NewGuid():N};IGNORE_UNKNOWN_SETTINGS=TRUE;Storage=Memory").Build();
        H2Connection connection = (H2Connection)dataSource.CreateConnection();
        connection.Open();
        return (dataSource, connection, []);
    }

    private static void Exec(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Scalar(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? "-"
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "-";
    }

    private static int Count(H2Connection connection, string table)
        => int.Parse(Scalar(connection, "SELECT COUNT(*) FROM " + table), CultureInfo.InvariantCulture);

    [Fact]
    public void RegistrationAppliesByPredicateAndIsVisibleToAllConnections()
    {
        (H2DataSource dataSource, H2Connection connection, List<string> log) = OpenDb();
        Exec(connection, "CREATE TABLE T1(ID INT)");
        Exec(connection, "CREATE TABLE T2(ID INT)");

        dataSource.RegisterTrigger(
            "ON_T1", (schema, name) => name == "T1", H2TriggerEvent.Insert,
            before: true, rowBased: true, () => new RecordingTrigger(log));

        Exec(connection, "INSERT INTO T1 VALUES (1)");
        Assert.Equal(1, log.Count(line => line.StartsWith("fire:", StringComparison.Ordinal)));

        // The predicate decided: T2 is not attached.
        Exec(connection, "INSERT INTO T2 VALUES (1)");
        Assert.Equal(1, log.Count(line => line.StartsWith("fire:", StringComparison.Ordinal)));

        // The trigger is database-wide: a second connection of the same data source sees it.
        using H2Connection second = (H2Connection)dataSource.CreateConnection();
        second.Open();
        Exec(second, "INSERT INTO T1 VALUES (2)");
        Assert.Equal(2, log.Count(line => line.StartsWith("fire:", StringComparison.Ordinal)));
    }

    [Fact]
    public void BeforeInsertTriggerSeesNoOldRowAndWritesItsChangesBack()
    {
        (H2DataSource dataSource, H2Connection connection, _) = OpenDb();
        Exec(connection, "CREATE TABLE TEST(ID INT, VAL VARCHAR)");
        string? seenNew = null;
        var seen = new RecordingTrigger([]);
        seen.OnFire = (_, oldRow, newRow) =>
        {
            Assert.Null(oldRow);
            Assert.NotNull(newRow);
            seenNew = Convert.ToString(newRow.Values[1], CultureInfo.InvariantCulture);
            int index = 0;
            for (int i = 0; i < newRow.ColumnNames.Count; i++)
            {
                if (string.Equals(newRow.ColumnNames[i], "VAL", StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                }
            }

            newRow.SetValue(index, "changed");
        };
        dataSource.RegisterTrigger(
            "REWRITE", (_, name) => name == "TEST", H2TriggerEvent.Insert,
            before: true, rowBased: true, () => seen);

        Exec(connection, "INSERT INTO TEST VALUES (1, 'x')");
        Assert.Equal("x", seenNew);
        Assert.Equal("changed", Scalar(connection, "SELECT VAL FROM TEST WHERE ID=1"));
    }

    [Fact]
    public void BeforeUpdateTriggerSeesOldAndNewRows()
    {
        (H2DataSource dataSource, H2Connection connection, List<string> log) = OpenDb();
        Exec(connection, "CREATE TABLE TEST(ID INT, VAL INT)");
        Exec(connection, "INSERT INTO TEST VALUES (1, 10)");

        dataSource.RegisterTrigger(
            "SEE_UPDATE", (_, name) => name == "TEST", H2TriggerEvent.Update,
            before: true, rowBased: true, () => new RecordingTrigger(log));

        Exec(connection, "UPDATE TEST SET VAL=20 WHERE ID=1");
        Assert.Contains(log, line => line.Contains("old=[1,10]", StringComparison.Ordinal)
            && line.Contains("new=[1,20]", StringComparison.Ordinal));
    }

    [Fact]
    public void BeforeDeleteTriggerSeesOldRowAndNoNewRow()
    {
        (H2DataSource dataSource, H2Connection connection, List<string> log) = OpenDb();
        Exec(connection, "CREATE TABLE TEST(ID INT)");
        Exec(connection, "INSERT INTO TEST VALUES (1)");

        dataSource.RegisterTrigger(
            "SEE_DELETE", (_, name) => name == "TEST", H2TriggerEvent.Delete,
            before: true, rowBased: true, () => new RecordingTrigger(log));

        Exec(connection, "DELETE FROM TEST WHERE ID=1");
        Assert.Contains(log, line => line.Contains("old=[1]", StringComparison.Ordinal)
            && line.Contains("new=null", StringComparison.Ordinal));
        Assert.Equal(0, Count(connection, "TEST"));
    }

    [Fact]
    public void AfterTriggerReadsTheUncommittedChangesOfTheOuterStatement()
    {
        (H2DataSource dataSource, H2Connection connection, _) = OpenDb();
        Exec(connection, "CREATE TABLE TEST(ID INT)");
        var readBack = new ReadBackTrigger();

        dataSource.RegisterTrigger(
            "READ_BACK", (_, name) => name == "TEST", H2TriggerEvent.Insert,
            before: false, rowBased: true, () => readBack);

        Exec(connection, "INSERT INTO TEST VALUES (5)");
        Assert.Equal(1, Count(connection, "TEST"));
        Exec(connection, "INSERT INTO TEST VALUES (7)");
        Assert.Equal(2, Count(connection, "TEST"));
        // Each fire saw its own statement's row inside the same transaction: the first saw
        // row 5, the second saw both.
        Assert.Equal([1, 2], readBack.SeenByFire);
    }

    [Fact]
    public void StatementTriggerFiresOncePerStatementWithNoRows()
    {
        (H2DataSource dataSource, H2Connection connection, List<string> log) = OpenDb();
        Exec(connection, "CREATE TABLE TEST(ID INT)");

        dataSource.RegisterTrigger(
            "ON_STMT", (_, name) => name == "TEST", H2TriggerEvent.Insert,
            before: false, rowBased: false, () => new RecordingTrigger(log));

        Exec(connection, "INSERT INTO TEST VALUES (1),(2),(3)");
        Assert.Equal(1, log.Count(line => line.StartsWith("fire:", StringComparison.Ordinal)));
        Assert.Contains(log, line => line.Contains("old=null", StringComparison.Ordinal)
            && line.Contains("new=null", StringComparison.Ordinal));
        Assert.Equal(3, Count(connection, "TEST"));
    }

    [Fact]
    public void AThrowingBeforeTriggerAbortsTheStatement()
    {
        (H2DataSource dataSource, H2Connection connection, _) = OpenDb();
        Exec(connection, "CREATE TABLE TEST(ID INT)");

        dataSource.RegisterTrigger(
            "THROW", (_, name) => name == "TEST", H2TriggerEvent.Insert,
            before: true, rowBased: true, () => new ThrowingTrigger());

        Assert.ThrowsAny<Exception>(() => Exec(connection, "INSERT INTO TEST VALUES (1)"));
        Assert.Equal(0, Count(connection, "TEST"));
    }

    [Fact]
    public void AThrowingAfterTriggerRollsBackTheWrittenRow()
    {
        (H2DataSource dataSource, H2Connection connection, _) = OpenDb();
        Exec(connection, "CREATE TABLE TEST(ID INT)");

        dataSource.RegisterTrigger(
            "THROW_AFTER", (_, name) => name == "TEST", H2TriggerEvent.Insert,
            before: false, rowBased: true, () => new ThrowingTrigger());

        Assert.ThrowsAny<Exception>(() => Exec(connection, "INSERT INTO TEST VALUES (1)"));
        Assert.Equal(0, Count(connection, "TEST"));
    }

    [Fact]
    public void TriggersFireBeforeTheConstraintsOfTheirSide()
    {
        (H2DataSource dataSource, H2Connection connection, List<string> log) = OpenDb();
        Exec(connection, "CREATE TABLE PARENT(ID INT PRIMARY KEY)");
        Exec(connection, "CREATE TABLE CHILD(PID INT REFERENCES PARENT(ID))");

        dataSource.RegisterTrigger(
            "BEFORE_CHILD", (_, name) => name == "CHILD", H2TriggerEvent.Insert,
            before: true, rowBased: true, () => new RecordingTrigger(log));
        dataSource.RegisterTrigger(
            "AFTER_CHILD", (_, name) => name == "CHILD", H2TriggerEvent.Insert,
            before: false, rowBased: true, () => new RecordingTrigger(log));

        // No parent: the referential after-constraint fails. The before and after triggers
        // of the write ran before it did, so the after trigger was called even though the
        // row is rolled back.
        Assert.ThrowsAny<Exception>(() => Exec(connection, "INSERT INTO CHILD VALUES (999)"));
        Assert.Equal(0, Count(connection, "CHILD"));
        Assert.Contains(log, line => line.StartsWith("fire:", StringComparison.Ordinal)
            && line.Contains("old=null", StringComparison.Ordinal)
            && line.Contains("new=[999]", StringComparison.Ordinal));
    }

    [Fact]
    public void ATableCreatedAfterRegistrationIsPickedUpOnItsFirstFire()
    {
        (H2DataSource dataSource, H2Connection connection, List<string> log) = OpenDb();

        dataSource.RegisterTrigger(
            "LATE", (_, name) => name == "LATE", H2TriggerEvent.Insert,
            before: true, rowBased: true, () => new RecordingTrigger(log));

        Exec(connection, "CREATE TABLE LATE(ID INT)");
        Exec(connection, "INSERT INTO LATE VALUES (1)");
        Assert.Equal(1, log.Count(line => line.StartsWith("fire:", StringComparison.Ordinal)));
    }

    [Fact]
    public void ColumnNamesAreHandedOverAsStoredWithoutFolding()
    {
        (H2DataSource dataSource, H2Connection connection, _) = OpenDb();
        Exec(connection, "CREATE TABLE \"MixCase\" (\"MyVal\" INT)");
        var seen = new RecordingTrigger([]);
        seen.OnFire = (_, _, newRow) =>
        {
            IH2TriggerRow row = newRow!;
            Assert.Single(row.ColumnNames);
            Assert.Equal("MyVal", row.ColumnNames[0]);
            row.SetValue(0, 42);
        };

        dataSource.RegisterTrigger(
            "MIXED", (_, name) => name == "MixCase", H2TriggerEvent.Insert,
            before: true, rowBased: true, () => seen);

        Exec(connection, "INSERT INTO \"MixCase\" (\"MyVal\") VALUES (0)");
        Assert.Equal("42", Scalar(connection, "SELECT \"MyVal\" FROM \"MixCase\""));
    }

    [Fact]
    public void GetCurrentTransactionIsNeverNullInsideFireAndRefusesCommit()
    {
        (H2DataSource dataSource, H2Connection connection, _) = OpenDb();
        Exec(connection, "CREATE TABLE TEST(ID INT)");
        var trigger = new TransactionProbeTrigger();

        dataSource.RegisterTrigger(
            "PROBE", (_, name) => name == "TEST", H2TriggerEvent.Insert,
            before: true, rowBased: true, () => trigger);

        Exec(connection, "INSERT INTO TEST VALUES (1)");
        Assert.True(trigger.SawTransaction);
        Assert.True(trigger.CommitRefused);
    }

    [Fact]
    public void UnregisterTriggerDetachesAndCallsRemove()
    {
        (H2DataSource dataSource, H2Connection connection, _) = OpenDb();
        Exec(connection, "CREATE TABLE TEST(ID INT)");
        var trigger = new RecordingTrigger([]);

        dataSource.RegisterTrigger(
            "GONE", (_, name) => name == "TEST", H2TriggerEvent.Insert,
            before: true, rowBased: true, () => trigger);

        Exec(connection, "INSERT INTO TEST VALUES (1)");
        Assert.Equal(1, trigger.Fires);

        Assert.True(dataSource.UnregisterTrigger("GONE"));
        Exec(connection, "INSERT INTO TEST VALUES (2)");
        Assert.Equal(1, trigger.Fires);
        Assert.Equal(1, trigger.Removes);
        Assert.False(dataSource.UnregisterTrigger("GONE"));
    }

    [Fact]
    public void RemovingTheDataSourceClosesTheTriggers()
    {
        (H2DataSource dataSource, H2Connection connection, _) = OpenDb();
        Exec(connection, "CREATE TABLE TEST(ID INT)");
        var trigger = new RecordingTrigger([]);

        dataSource.RegisterTrigger(
            "CLOSE_ME", (_, name) => name == "TEST", H2TriggerEvent.Insert,
            before: true, rowBased: true, () => trigger);

        Exec(connection, "INSERT INTO TEST VALUES (1)");
        connection.Close();
        dataSource.Dispose();
        Assert.Equal(1, trigger.Closes);
    }

    [Fact]
    public void DuplicateTriggerNameIsRefused()
    {
        (H2DataSource dataSource, _, _) = OpenDb();
        dataSource.RegisterTrigger(
            "TWICE", (_, _) => true, H2TriggerEvent.Insert,
            before: true, rowBased: true, () => new RecordingTrigger([]));

        Assert.Throws<InvalidOperationException>(() => dataSource.RegisterTrigger(
            "TWICE", (_, _) => true, H2TriggerEvent.Insert,
            before: true, rowBased: true, () => new RecordingTrigger([])));
    }

    [Fact]
    public void ABeforeTriggerCannotOverwriteAGeneratedColumn()
    {
        (H2DataSource dataSource, H2Connection connection, _) = OpenDb();
        Exec(connection, "CREATE TABLE GEN(ID INT PRIMARY KEY, A INT, B INT GENERATED ALWAYS AS (A * 2))");

        RecordingTrigger trigger = new([]);
        trigger.OnFire = (_, _, newRow) =>
        {
            Assert.NotNull(newRow);
            for (int i = 0; i < newRow.ColumnNames.Count; i++)
            {
                if (string.Equals(newRow.ColumnNames[i], "B", StringComparison.OrdinalIgnoreCase))
                {
                    // The reference discards a value a trigger writes into a generated
                    // column and computes the column instead.
                    newRow.SetValue(i, 999);
                }
            }
        };
        dataSource.RegisterTrigger(
            "GEN_REWRITE", (_, name) => name == "GEN", H2TriggerEvent.Insert,
            before: true, rowBased: true, () => trigger);

        Exec(connection, "INSERT INTO GEN(ID, A) VALUES (1, 21)");
        Assert.Equal("42", Scalar(connection, "SELECT B FROM GEN WHERE ID=1"));
    }

    [Fact]
    public void MergeFiresTheRegisteredInsertAndUpdateTriggers()
    {
        (H2DataSource dataSource, H2Connection connection, List<string> log) = OpenDb();
        Exec(connection, "CREATE TABLE MERGED(ID INT PRIMARY KEY, V INT)");

        dataSource.RegisterTrigger(
            "MERGED_ROWS", (_, name) => name == "MERGED",
            H2TriggerEvent.Insert | H2TriggerEvent.Update,
            before: true, rowBased: true, () => new RecordingTrigger(log));

        // The first MERGE inserts, the second one updates the row with the same key.
        Exec(connection, "MERGE INTO MERGED KEY(ID) VALUES (1, 10)");
        Exec(connection, "MERGE INTO MERGED KEY(ID) VALUES (1, 20)");

        Assert.Equal(
            ["init:PUBLIC.MERGED", "fire:old=null|new=[1,10]", "fire:old=[1,10]|new=[1,20]"],
            log);
        Assert.Equal("20", Scalar(connection, "SELECT V FROM MERGED WHERE ID=1"));
    }

    private sealed class RecordingTrigger : IH2Trigger
    {
        private readonly List<string> log;

        internal int Inits;

        internal int Fires;

        internal int Closes;

        internal int Removes;

        internal Action<IH2TriggerContext?, IH2TriggerRow?, IH2TriggerRow?>? OnFire;

        internal RecordingTrigger(List<string> log) => this.log = log;

        public void Init(IH2TriggerContext context)
        {
            Inits++;
            log.Add("init:" + context.SchemaName + "." + context.TableName);
        }

        public void Fire(IH2TriggerRow? oldRow, IH2TriggerRow? newRow)
        {
            Fires++;
            log.Add("fire:" + RowString(oldRow, "old") + "|" + RowString(newRow, "new"));
            OnFire?.Invoke(null, oldRow, newRow);
        }

        public void Close() => Closes++;

        public void Remove() => Removes++;

        private static string RowString(IH2TriggerRow? row, string which)
        {
            if (row is null)
            {
                return which + "=null";
            }

            return which + "=["
                + string.Join(",", row.Values.Select(v => v is null
                    ? "-"
                    : Convert.ToString(v, CultureInfo.InvariantCulture)))
                + "]";
        }
    }

    private sealed class ThrowingTrigger : IH2Trigger
    {
        public void Init(IH2TriggerContext context)
        {
        }

        public void Fire(IH2TriggerRow? oldRow, IH2TriggerRow? newRow)
            => throw new InvalidOperationException("boom");

        public void Close()
        {
        }

        public void Remove()
        {
        }
    }

    private sealed class ReadBackTrigger : IH2Trigger
    {
        private IH2TriggerContext? context;

        internal List<int> SeenByFire = [];

        public void Init(IH2TriggerContext context) => this.context = context;

        public void Fire(IH2TriggerRow? oldRow, IH2TriggerRow? newRow)
        {
            H2Transaction transaction = context!.GetCurrentTransaction();
            using var command = new H2Command("SELECT COUNT(*) FROM TEST", transaction.Connection!);
            command.Transaction = transaction;
            object? value = command.ExecuteScalar();
            SeenByFire.Add(value is null or DBNull
                ? 0
                : Convert.ToInt32(value, CultureInfo.InvariantCulture));
        }

        public void Close()
        {
        }

        public void Remove()
        {
        }
    }

    private sealed class TransactionProbeTrigger : IH2Trigger
    {
        private IH2TriggerContext? context;

        internal bool SawTransaction;

        internal bool CommitRefused;

        public void Init(IH2TriggerContext context) => this.context = context;

        public void Fire(IH2TriggerRow? oldRow, IH2TriggerRow? newRow)
        {
            H2Transaction transaction = context!.GetCurrentTransaction();
            SawTransaction = transaction.Connection is not null;
            try
            {
                transaction.Commit();
            }
            catch (InvalidOperationException)
            {
                CommitRefused = true;
            }
            finally
            {
                transaction.Dispose();
            }
        }

        public void Close()
        {
        }

        public void Remove()
        {
        }
    }
}
