/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * A parameter of a command, and the collection they live in.
 *
 * The reference's JdbcParameter stores its value as a Value, converting on the way in; here the
 * value stays as the caller gave it and the conversion happens when the statement runs, in the
 * session, because that is where the type of the column it is going into is known. Storing it
 * converted would mean guessing a type at the moment the parameter is created, and a parameter
 * created before the statement is prepared has nothing to guess from.
 *
 * The collection is real rather than a wrapper around one, because Dapper drives it: it creates
 * parameters through the command, fills them, and adds them by name. Its indexer answers by
 * position and by name, and the two are the same list — a collection that answered a name from
 * a different store than a position would fail only for callers that mix the two.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace H2Sharp.Data;

/// <summary>
/// A parameter of a command.
/// </summary>
public sealed class H2Parameter : DbParameter
{
    private string parameterName = string.Empty;

    private string sourceColumn = string.Empty;

    private DbType? dbType;

    /// <inheritdoc/>
    public override DbType DbType
    {
        get => dbType ?? DbType.Object;
        set => dbType = value;
    }

    /// <inheritdoc/>
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

    /// <inheritdoc/>
    public override bool IsNullable { get; set; } = true;

    /// <inheritdoc/>
    [AllowNull]
    public override string ParameterName
    {
        get => parameterName;
        set => parameterName = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <inheritdoc/>
    public override int Size { get; set; }

    /// <inheritdoc/>
    [AllowNull]
    public override string SourceColumn
    {
        get => sourceColumn;
        set => sourceColumn = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <inheritdoc/>
    public override bool SourceColumnNullMapping { get; set; }

    /// <inheritdoc/>
    public override object? Value { get; set; }

    /// <inheritdoc/>
    public override DataRowVersion SourceVersion { get; set; } = DataRowVersion.Current;

    /// <inheritdoc/>
    public override void ResetDbType()
    {
        dbType = null;
    }
}

/// <summary>
/// The parameters of a command.
/// </summary>
public sealed class H2ParameterCollection : DbParameterCollection
{
    private readonly List<H2Parameter> parameters;

    private readonly H2Command command;

    internal H2ParameterCollection(H2Command command, List<H2Parameter> parameters)
    {
        this.command = command;
        this.parameters = parameters;
    }

    /// <inheritdoc/>
    public override int Count => parameters.Count;

    /// <inheritdoc/>
    public override object SyncRoot { get; } = new();

    /// <inheritdoc/>
    public override int Add(object value)
    {
        parameters.Add(AsParameter(value));
        return parameters.Count - 1;
    }

    /// <inheritdoc/>
    public override void AddRange(Array values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (object? value in values)
        {
            Add(value!);
        }
    }

    /// <inheritdoc/>
    public override void Clear()
    {
        parameters.Clear();
    }

    /// <inheritdoc/>
    public override bool Contains(object value)
    {
        return value is H2Parameter p && parameters.Contains(p);
    }

    /// <inheritdoc/>
    public override bool Contains(string value)
    {
        return IndexOf(value) >= 0;
    }

    /// <inheritdoc/>
    public override void CopyTo(Array array, int index)
    {
        ((ICollection)parameters).CopyTo(array, index);
    }

    /// <inheritdoc/>
    public override IEnumerator GetEnumerator()
    {
        return parameters.GetEnumerator();
    }

    /// <inheritdoc/>
    public override int IndexOf(object value)
    {
        return value is H2Parameter p ? parameters.IndexOf(p) : -1;
    }

    /// <inheritdoc/>
    public override int IndexOf(string parameterName)
    {
        for (int i = 0; i < parameters.Count; i++)
        {
            if (NameEquals(parameters[i].ParameterName, parameterName))
            {
                return i;
            }
        }

        return -1;
    }

    /// <inheritdoc/>
    public override void Insert(int index, object value)
    {
        parameters.Insert(index, AsParameter(value));
    }

    /// <inheritdoc/>
    public override void Remove(object value)
    {
        if (value is H2Parameter p)
        {
            _ = parameters.Remove(p);
        }
    }

    /// <inheritdoc/>
    public override void RemoveAt(int index)
    {
        parameters.RemoveAt(index);
    }

    /// <inheritdoc/>
    public override void RemoveAt(string parameterName)
    {
        int index = IndexOf(parameterName);
        if (index < 0)
        {
            throw new IndexOutOfRangeException(parameterName);
        }

        parameters.RemoveAt(index);
    }

    /// <inheritdoc/>
    protected override DbParameter GetParameter(int index)
    {
        return parameters[index];
    }

    /// <inheritdoc/>
    protected override DbParameter GetParameter(string parameterName)
    {
        int index = IndexOf(parameterName);
        if (index < 0)
        {
            throw new IndexOutOfRangeException(parameterName);
        }

        return parameters[index];
    }

    /// <inheritdoc/>
    protected override void SetParameter(int index, DbParameter value)
    {
        parameters[index] = AsParameter(value);
    }

    /// <inheritdoc/>
    protected override void SetParameter(string parameterName, DbParameter value)
    {
        int index = IndexOf(parameterName);
        if (index < 0)
        {
            Add(value);
            return;
        }

        parameters[index] = AsParameter(value);
    }

    /// <summary>
    /// Whether a parameter name matches. The leading '@' and ':' that a caller may write is
    /// theirs, not the parameter's: Dapper names parameters without them and the SQL keeps them,
    /// so the comparison drops the prefix on both sides rather than making the caller agree.
    /// </summary>
    private static bool NameEquals(string name, string other)
    {
        return string.Equals(Trim(name), Trim(other), StringComparison.OrdinalIgnoreCase);
    }

    private static string Trim(string name)
    {
        ReadOnlySpan<char> span = name.AsSpan();
        if (span.Length > 0 && (span[0] == '@' || span[0] == ':'))
        {
            span = span[1..];
        }

        return span.ToString();
    }

    private static H2Parameter AsParameter(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value as H2Parameter
            ?? throw new ArgumentException("The parameter is not an H2Parameter", nameof(value));
    }

    /// <summary>Gets the command these parameters belong to.</summary>
    internal H2Command Command => command;
}
