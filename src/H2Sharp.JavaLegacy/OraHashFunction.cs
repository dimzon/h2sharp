/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using H2Sharp.Data;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.JavaLegacy;

/// <summary>ORA_HASH as an optional Java-compatible function.</summary>
internal static class OraHashFunction
{
    private const long HighMask = unchecked((long)0xFFFF_FFFF_0000_0000UL);

    internal static void Register(IH2RawExtensions extensions)
    {
        extensions.RegisterRawUdf("ORA_HASH", 1, TypeInfo.TypeBigint,
            args => Hash(args[0], 0xFFFF_FFFFL, 0L), deterministic: false);
        extensions.RegisterRawUdf("ORA_HASH", 2, TypeInfo.TypeBigint,
            args => Hash(args[0], args[1].GetLong(), 0L), deterministic: false);
        extensions.RegisterRawUdf("ORA_HASH", 3, TypeInfo.TypeBigint,
            args => Hash(args[0], args[1].GetLong(), args[2].GetLong()), deterministic: false);
    }

    private static Value Hash(Value value, long bucket, long seed)
    {
        if (value is ValueNull)
        {
            return ValueNull.Instance;
        }

        if ((bucket & HighMask) != 0L)
        {
            throw DbException.GetInvalidValueException("bucket", bucket);
        }

        if ((seed & HighMask) != 0L)
        {
            throw DbException.GetInvalidValueException("seed", seed);
        }

        using SHA1 sha = SHA1.Create();
        using MemoryStream buffer = new();
        switch (value.GetValueType())
        {
            case Value.Char:
            case Value.Varchar:
            case Value.VarcharIgnorecase:
                buffer.Write(Encoding.UTF8.GetBytes(value.GetString()!));
                break;
            case Value.Blob:
            case Value.Clob:
                using (Stream input = value.GetInputStream())
                {
                    input.CopyTo(buffer);
                }

                break;
            default:
                buffer.Write(value.GetBytes() ?? []);
                break;
        }

        if (seed != 0L)
        {
            Span<byte> seedBytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(seedBytes, (int)seed);
            buffer.Write(seedBytes);
        }

        byte[] digest = sha.ComputeHash(buffer.ToArray());
        long hash = BinaryPrimitives.ReadInt64BigEndian(digest);
        return ValueBigint.Get((hash & long.MaxValue) % (bucket + 1));
    }
}
