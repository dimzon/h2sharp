/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The ADO.NET batch surface of the provider: DbBatch commands are collected and run in
 * one round trip per distinct statement, where the engine executes the parameter sets of
 * a repeated statement as one batch.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace H2Sharp.Data;

/// <summary>
/// A batch of commands over a connection.
/// </summary>
public sealed class H2Batch : DbBatch
{
    private readonly List<DbBatchCommand> commands = [];

    private H2Connection? connection;

    private H2Transaction? transaction;

    internal H2Batch(H2Connection? connection = null)
    {
        this.connection = connection;
    }

    /// <inheritdoc/>
    protected override DbBatchCommand CreateDbBatchCommand()
    {
        H2BatchCommand command = new(this);
        commands.Add(command);
        return command;
    }

    /// <summary>Gets the commands of this batch.</summary>
    public new IReadOnlyList<DbBatchCommand> BatchCommands => commands;

    /// <inheritdoc/>
    protected override DbBatchCommandCollection DbBatchCommands => new BatchCommandCollection(commands);

    /// <inheritdoc/>
    protected override DbConnection? DbConnection
    {
        get => connection;
        set => connection = (H2Connection?)value;
    }

    /// <summary>Gets or sets the transaction this batch runs in.</summary>
    public new DbTransaction? Transaction
    {
        get => transaction;
        set => transaction = (H2Transaction?)value;
    }

    /// <inheritdoc/>
    protected override DbTransaction? DbTransaction
    {
        get => transaction;
        set => transaction = (H2Transaction?)value;
    }

    /// <inheritdoc/>
    public override int Timeout { get; set; }

    /// <inheritdoc/>
    public override void Prepare()
    {
    }

    /// <inheritdoc/>
    public override Task PrepareAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public override void Cancel()
    {
    }

    /// <inheritdoc/>
    public override int ExecuteNonQuery()
    {
        H2Connection? owner = connection ?? throw new InvalidOperationException("The batch has no connection");
        long total = 0;
        foreach ((string? text, List<H2BatchCommand> group) in GroupByText())
        {
            if (group.Count == 1)
            {
                total += RunSingle(owner, text!, group[0]);
            }
            else
            {
                total += RunEngineBatch(owner, text!, group);
            }
        }

        return Convert.ToInt32(total);
    }

    /// <inheritdoc/>
    public override object? ExecuteScalar()
    {
        H2Connection? owner = connection ?? throw new InvalidOperationException("The batch has no connection");
        if (commands.Count == 0)
        {
            return null;
        }

        using H2Command command = BuildCommand(owner, (H2BatchCommand)commands[0]);
        return command.ExecuteScalar();
    }

    /// <inheritdoc/>
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        H2Connection? owner = connection ?? throw new InvalidOperationException("The batch has no connection");
        if (commands.Count == 0)
        {
            throw new InvalidOperationException("The batch has no commands");
        }

        using H2Command command = BuildCommand(owner, (H2BatchCommand)commands[0]);
        for (int i = 1; i < commands.Count; i++)
        {
            using H2Command rest = BuildCommand(owner, (H2BatchCommand)commands[i]);
            rest.ExecuteNonQuery();
        }

        return command.ExecuteReader(behavior);
    }

    /// <inheritdoc/>
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        => Task.FromResult(ExecuteNonQuery());

    /// <inheritdoc/>
    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        => Task.FromResult(ExecuteScalar());

    /// <inheritdoc/>
    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        => Task.FromResult(ExecuteDbDataReader(behavior));

    private IEnumerable<(string? Text, List<H2BatchCommand> Group)> GroupByText()
    {
        Dictionary<string, List<H2BatchCommand>> byText = new(StringComparer.Ordinal);
        foreach (H2BatchCommand command in commands)
        {
            if (!byText.TryGetValue(command.CommandText, out List<H2BatchCommand>? group))
            {
                group = [];
                byText.Add(command.CommandText, group);
            }

            group.Add(command);
        }

        foreach ((string text, List<H2BatchCommand> group) in byText)
        {
            yield return (text, group);
        }
    }

    private static long RunSingle(H2Connection owner, string text, H2BatchCommand batchCommand)
    {
        using H2Command command = BuildCommand(owner, batchCommand);
        return command.ExecuteNonQuery();
    }

    private static long RunEngineBatch(H2Connection owner, string text, List<H2BatchCommand> group)
    {
        List<object?[]> parameterSets = [];
        foreach (H2BatchCommand batchCommand in group)
        {
            object?[] set = new object?[batchCommand.Parameters.Count];
            for (int i = 0; i < set.Length; i++)
            {
                set[i] = ((H2Parameter)batchCommand.Parameters[i]).Value;
            }

            parameterSets.Add(set);
        }

        using H2Command command = new(text, owner) { Transaction = (H2Transaction?)group[0].Transaction };
        foreach (H2Parameter parameter in group[0].Parameters)
        {
            command.Parameters.Add(parameter);
        }

        H2Sharp.Result.BatchResult result = command.ExecuteBatch(parameterSets);
        long total = 0;
        foreach (long count in result.GetUpdateCounts())
        {
            if (count >= 0)
            {
                total += count;
            }
        }

        if (result.GetExceptions().Count > 0)
        {
            throw result.GetExceptions()[0];
        }

        return total;
    }

    private static H2Command BuildCommand(H2Connection owner, H2BatchCommand batchCommand)
    {
        H2Command command = new(batchCommand.CommandText, owner)
        {
            Transaction = (H2Transaction?)batchCommand.Transaction,
        };
        foreach (H2Parameter parameter in batchCommand.Parameters)
        {
            command.Parameters.Add(parameter);
        }

        return command;
    }

    /// <summary>
    /// The commands of a batch, as the base class expects them.
    /// </summary>
    private sealed class BatchCommandCollection : DbBatchCommandCollection
    {
        private readonly List<DbBatchCommand> commands;

        internal BatchCommandCollection(List<DbBatchCommand> commands) => this.commands = commands;

        public override int Count => commands.Count;

        public override bool IsReadOnly => false;

        protected override DbBatchCommand GetBatchCommand(int index) => commands[index];

        protected override void SetBatchCommand(int index, DbBatchCommand item) => commands[index] = item;

        public override void Add(DbBatchCommand item) => commands.Add(item);

        public override void Clear() => commands.Clear();

        public override bool Contains(DbBatchCommand item) => commands.Contains(item);

        public override void CopyTo(DbBatchCommand[] array, int index) => commands.CopyTo(array, index);

        public override System.Collections.Generic.IEnumerator<DbBatchCommand> GetEnumerator() => commands.GetEnumerator();

        public override int IndexOf(DbBatchCommand item) => commands.IndexOf(item);

        public override void Insert(int index, DbBatchCommand item) => commands.Insert(index, item);

        public override bool Remove(DbBatchCommand item) => commands.Remove(item);

        public override void RemoveAt(int index) => commands.RemoveAt(index);
    }

    /// <summary>
    /// One command of a batch.
    /// </summary>
    public sealed class H2BatchCommand : DbBatchCommand
    {
        private readonly H2Batch batch;

        private readonly H2ParameterCollection parameters;

        private string commandText = string.Empty;

        internal H2BatchCommand(H2Batch batch)
        {
            this.batch = batch;
            parameters = new H2ParameterCollection(new H2Command(), []);
        }

        /// <inheritdoc/>
        public override string CommandText
        {
            get => commandText;
            set => commandText = value ?? string.Empty;
        }

        /// <inheritdoc/>
        public override CommandType CommandType { get; set; } = CommandType.Text;

        /// <inheritdoc/>
        protected override DbParameterCollection DbParameterCollection => parameters;

        /// <inheritdoc/>
        public override int RecordsAffected { get; }

        internal H2Transaction? Transaction => (H2Transaction?)batch.Transaction;
    }
}
