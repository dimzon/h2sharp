/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using CryptoHives.Foundation.Security.Cryptography.Hash;
using H2Sharp.Data;

namespace H2Sharp.Hashes;

/// <summary>Registers the hash algorithms that are absent from the BCL.</summary>
public static class H2HashesExtensions
{
#pragma warning disable CS0618
    private static readonly (string Name, Func<HashAlgorithm> Create)[] BclAlgorithms =
    {
        ("MD5", MD5.Create),
        ("SHA-1", SHA1.Create),
        ("SHA-256", SHA256.Create),
        ("SHA-384", SHA384.Create),
        ("SHA-512", SHA512.Create),
        ("SHA3-256", SHA3_256.Create),
        ("SHA3-384", SHA3_384.Create),
        ("SHA3-512", SHA3_512.Create),
    };
#pragma warning restore CS0618

    private static readonly (string Name, Func<HashAlgorithm> Create)[] Algorithms =
    {
        ("SHA-224", SHA224.Create),
        ("SHA3-224", SHA3_224.Create),
        ("SHA-512/224", SHA512_224.Create),
        ("SHA-512/256", SHA512_256.Create),
        ("SHAKE128", Shake128.Create),
        ("SHAKE256", Shake256.Create),
        ("CSHAKE128", CShake128.Create),
        ("CSHAKE256", CShake256.Create),
        ("TURBOSHAKE128", TurboShake128.Create),
        ("TURBOSHAKE256", TurboShake256.Create),
        ("KECCAK-256", Keccak256.Create),
        ("KECCAK-384", Keccak384.Create),
        ("KECCAK-512", Keccak512.Create),
        ("BLAKE2B", Blake2b.Create),
        ("BLAKE2S", Blake2s.Create),
        ("BLAKE3", Blake3.Create),
        ("KT128", KT128.Create),
        ("KT256", KT256.Create),
        ("ASCON-HASH256", AsconHash256.Create),
        ("ASCON-XOF128", AsconXof128.Create),
        ("KUPYNA", Kupyna.Create),
        ("KUPYNA-256", static () => Kupyna.Create(32)),
        ("LSH-256", Lsh256.Create),
        ("LSH-512", Lsh512.Create),
        ("STREEBOG", Streebog.Create),
        ("STREEBOG-256", static () => Streebog.Create(32)),
        ("RIPEMD-160", Ripemd160.Create),
        ("SM3", SM3.Create),
        ("WHIRLPOOL", Whirlpool.Create),
    };

    /// <summary>
    /// Registers every fixed-output hash algorithm supplied by the package that
    /// the BCL does not provide.
    /// </summary>
    /// <param name="extensions">the closed connection's extension surface</param>
    /// <param name="overrideBcl">
    /// Also replace the BCL implementations of MD5, SHA-1, SHA-2 and SHA3-256/384/512
    /// with the package implementations.
    /// </param>
    public static void UseH2Hashes(this IH2Extensions extensions, bool overrideBcl = false)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        foreach ((string name, Func<HashAlgorithm> create) in Algorithms)
        {
            extensions.RegisterHashAlgorithm(name, create);
        }

        if (overrideBcl)
        {
            foreach ((string name, Func<HashAlgorithm> create) in BclAlgorithms)
            {
                extensions.RegisterHashAlgorithm(name, create);
            }
        }
    }
}
