/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Extensions;

/// <summary>One registered scalar CLR function.</summary>
internal sealed class UdfDefinition
{
    internal UdfDefinition(
        string name,
        int arity,
        Type[] parameterTypes,
        TypeInfo resultType,
        bool deterministic,
        Func<Value[], Value> invoke)
    {
        Name = name;
        Arity = arity;
        ParameterTypes = parameterTypes;
        ResultType = resultType;
        Deterministic = deterministic;
        Invoke = invoke;
    }

    internal string Name { get; }

    internal int Arity { get; }

    internal Type[] ParameterTypes { get; }

    internal TypeInfo ResultType { get; }

    internal bool Deterministic { get; }

    internal Func<Value[], Value> Invoke { get; }

    internal static UdfDefinition Create<TResult>(
        string name, Func<TResult> implementation, bool deterministic)
        => new(name, 0, [], ClrValueConverter.GetTypeInfo(typeof(TResult)), deterministic,
            _ => Call(name, () => implementation()));

    internal static UdfDefinition Create<T1, TResult>(
        string name, Func<T1, TResult> implementation, bool deterministic)
        => new(name, 1, [typeof(T1)], ClrValueConverter.GetTypeInfo(typeof(TResult)), deterministic,
            args => Call(name, () => implementation(Arg<T1>(args, 0))));

    internal static UdfDefinition Create<T1, T2, TResult>(
        string name, Func<T1, T2, TResult> implementation, bool deterministic)
        => new(name, 2, [typeof(T1), typeof(T2)], ClrValueConverter.GetTypeInfo(typeof(TResult)), deterministic,
            args => Call(name, () => implementation(Arg<T1>(args, 0), Arg<T2>(args, 1))));

    internal static UdfDefinition Create<T1, T2, T3, TResult>(
        string name, Func<T1, T2, T3, TResult> implementation, bool deterministic)
        => new(name, 3, [typeof(T1), typeof(T2), typeof(T3)], ClrValueConverter.GetTypeInfo(typeof(TResult)), deterministic,
            args => Call(name, () => implementation(Arg<T1>(args, 0), Arg<T2>(args, 1), Arg<T3>(args, 2))));

    internal static UdfDefinition Create<T1, T2, T3, T4, TResult>(
        string name, Func<T1, T2, T3, T4, TResult> implementation, bool deterministic)
        => new(name, 4, [typeof(T1), typeof(T2), typeof(T3), typeof(T4)], ClrValueConverter.GetTypeInfo(typeof(TResult)), deterministic,
            args => Call(name, () => implementation(Arg<T1>(args, 0), Arg<T2>(args, 1), Arg<T3>(args, 2), Arg<T4>(args, 3))));

    internal static UdfDefinition CreateRaw(
        string name, int arity, TypeInfo resultType, bool deterministic, Func<Value[], Value> implementation)
        => new(name, arity, [], resultType, deterministic, implementation);

    private static T Arg<T>(Value[] args, int index)
    {
        Value value = args[index];
        if (value is ValueNull)
        {
            if (ClrValueConverter.IsNullable(typeof(T)))
            {
                return default!;
            }

            throw NullArgument;
        }

        return (T)ClrValueConverter.FromValue(value, typeof(T))!;
    }

    private static Value Call<TResult>(string name, Func<TResult> call)
    {
        try
        {
            return ClrValueConverter.ToValue(call());
        }
        catch (H2SharpException)
        {
            throw;
        }
        catch (NullArgumentException)
        {
            return ValueNull.Instance;
        }
        catch (Exception e)
        {
            throw DbException.Get(ErrorCode.ExceptionInFunction1, e, name);
        }
    }

    private static readonly NullArgumentException NullArgument = new();

    private sealed class NullArgumentException : Exception
    {
    }
}

/// <summary>The mutable registration configuration of a closed connection.</summary>
internal sealed class ExtensionConfiguration
{
    private readonly Dictionary<string, List<UdfDefinition>> udfs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<HashAlgorithm>> hashes = new(StringComparer.OrdinalIgnoreCase);
    private Func<long?, Random>? randomGenerator;
    private Func<int, Guid>? uuidGenerator;

    internal IReadOnlyDictionary<string, List<UdfDefinition>> Udfs => udfs;

    internal IReadOnlyDictionary<string, Func<HashAlgorithm>> Hashes => hashes;

    internal Func<long?, Random>? RandomGenerator => randomGenerator;

    internal Func<int, Guid>? UuidGenerator => uuidGenerator;

    internal void Add(UdfDefinition definition)
    {
        if (!udfs.TryGetValue(definition.Name, out List<UdfDefinition>? list))
        {
            list = [];
            udfs.Add(definition.Name, list);
        }

        if (list.Exists(d => d.Arity == definition.Arity))
        {
            throw new InvalidOperationException(
                "A UDF named " + definition.Name + " with " + definition.Arity + " argument(s) is already registered");
        }

        list.Add(definition);
        list.Sort(static (a, b) => a.Arity.CompareTo(b.Arity));
    }

    internal void AddHash(string name, Func<HashAlgorithm> factory)
        => hashes[StringUtils.ToUpperEnglish(name)] = factory;

    internal void SetRandomGenerator(Func<long?, Random> factory) => randomGenerator = factory;

    internal void SetUuidGenerator(Func<int, Guid> factory) => uuidGenerator = factory;

    internal void AddRaw(string name, int arity, TypeInfo resultType, bool deterministic, Func<Value[], Value> implementation)
        => Add(UdfDefinition.CreateRaw(name, arity, resultType, deterministic, implementation));

    internal ExtensionConfiguration Clone()
    {
        ExtensionConfiguration clone = new();
        foreach ((string name, List<UdfDefinition> list) in udfs)
        {
            clone.udfs.Add(name, [.. list]);
        }

        foreach ((string name, Func<HashAlgorithm> factory) in hashes)
        {
            clone.hashes.Add(name, factory);
        }

        clone.randomGenerator = randomGenerator;
        clone.uuidGenerator = uuidGenerator;
        return clone;
    }

    internal ExtensionRuntime CreateRuntime() => ExtensionRuntime.Create(this);
}
