/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using H2Sharp.Data;

namespace H2Sharp.Crypto;

/// <summary>Registers H2's optional ENCRYPT and DECRYPT functions.</summary>
public static class CryptoExtensions
{
    /// <summary>
    /// Registers ENCRYPT and DECRYPT for the closed connection's HASH-style SQL surface.
    /// </summary>
    /// <param name="extensions">the closed connection's extension surface</param>
    public static void UseCrypto(this IH2Extensions extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        if (extensions is not IH2RawExtensions raw)
        {
            throw new InvalidOperationException(
                "H2Sharp.Crypto requires the H2Sharp connection extension implementation");
        }

        CryptFunctions.Register(raw);
    }
}
