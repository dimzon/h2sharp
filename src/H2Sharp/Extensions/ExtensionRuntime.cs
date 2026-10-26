/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Extensions;

/// <summary>The immutable extension snapshot of one opened session.</summary>
internal sealed class ExtensionRuntime
{
    private readonly Dictionary<string, List<UdfDefinition>> udfs;
    private readonly Dictionary<string, Func<HashAlgorithm>> hashes;

    private ExtensionRuntime(
        Dictionary<string, List<UdfDefinition>> udfs,
        Dictionary<string, Func<HashAlgorithm>> hashes,
        Func<long?, Random>? randomGenerator,
        Func<int, Guid>? uuidGenerator)
    {
        this.udfs = udfs;
        this.hashes = hashes;
        RandomGenerator = randomGenerator;
        UuidGenerator = uuidGenerator;
    }

    internal Func<long?, Random>? RandomGenerator { get; }

    internal Func<int, Guid>? UuidGenerator { get; }

    internal static ExtensionRuntime Create(ExtensionConfiguration? configuration)
    {
        Dictionary<string, List<UdfDefinition>> udfs = new(StringComparer.OrdinalIgnoreCase);
        if (configuration is not null)
        {
            foreach ((string name, List<UdfDefinition> list) in configuration.Udfs)
            {
                udfs.Add(name, [.. list]);
            }
        }

        Dictionary<string, Func<HashAlgorithm>> hashes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["MD5"] = MD5.Create,
            ["SHA-1"] = SHA1.Create,
            ["SHA-256"] = SHA256.Create,
            ["SHA-384"] = SHA384.Create,
            ["SHA-512"] = SHA512.Create,
            ["SHA3-256"] = SHA3_256.Create,
            ["SHA3-384"] = SHA3_384.Create,
            ["SHA3-512"] = SHA3_512.Create,
        };
        if (configuration is not null)
        {
            foreach ((string name, Func<HashAlgorithm> factory) in configuration.Hashes)
            {
                hashes[name] = factory;
            }
        }

        return new ExtensionRuntime(
            udfs, hashes, configuration?.RandomGenerator, configuration?.UuidGenerator);
    }

    internal bool HasUdf(string name) => udfs.ContainsKey(name);

    internal UdfDefinition? FindUdf(string name, int arity)
    {
        if (!udfs.TryGetValue(name, out List<UdfDefinition>? list))
        {
            return null;
        }

        foreach (UdfDefinition definition in list)
        {
            if (definition.Arity == arity)
            {
                return definition;
            }
        }

        return null;
    }

    internal int[] GetUdfArities(string name)
    {
        if (!udfs.TryGetValue(name, out List<UdfDefinition>? list))
        {
            return [];
        }

        int[] arities = new int[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            arities[i] = list[i].Arity;
        }

        return arities;
    }

    internal HashAlgorithm CreateHashAlgorithm(string name)
    {
        string key = NormalizeHashName(name);
        if (!hashes.TryGetValue(key, out Func<HashAlgorithm>? factory))
        {
            throw DbException.GetInvalidValueException("algorithm", name);
        }

        return factory();
    }

    internal static string NormalizeHashName(string name)
    {
        string upper = StringUtils.ToUpperEnglish(name);
        return upper switch
        {
            "SHA1" => "SHA-1",
            "SHA256" => "SHA-256",
            "SHA384" => "SHA-384",
            "SHA512" => "SHA-512",
            _ => upper,
        };
    }
}
