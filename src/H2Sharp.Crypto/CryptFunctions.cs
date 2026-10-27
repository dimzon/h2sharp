/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * ENCRYPT and DECRYPT as optional UDFs. AES uses the BCL; XTEA and FOG are
 * ports of the H2 2.5 block ciphers, because the BCL has no counterparts.
 */
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using H2Sharp.Api;
using H2Sharp.Data;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Crypto;

internal static class CryptFunctions
{
    internal static void Register(IH2RawExtensions extensions)
    {
        extensions.RegisterRawUdf("ENCRYPT", 3, TypeInfo.TypeVarbinary,
            args => Apply(args, encrypt: true), deterministic: true);
        extensions.RegisterRawUdf("DECRYPT", 3, TypeInfo.TypeVarbinary,
            args => Apply(args, encrypt: false), deterministic: true);
    }

    private static Value Apply(Value[] args, bool encrypt)
    {
        if (args[0] is ValueNull || args[1] is ValueNull || args[2] is ValueNull)
        {
            return ValueNull.Instance;
        }

        BlockCipher cipher = Create(args[0].GetString()!);
        byte[] key = ToBytes(args[1]);
        cipher.SetKey(Padded(key, 16));
        byte[] data = Padded(ToBytes(args[2]), 16);
        return ValueVarbinary.GetNoCopy(cipher.Transform(data, encrypt));
    }

    private static byte[] ToBytes(Value value)
        => value.ConvertToType(TypeInfo.TypeVarbinary).GetBytesNoCopy()!;

    private static byte[] Padded(byte[] data, int blockSize)
    {
        int length = (data.Length + blockSize - 1) & -blockSize;
        if (length == data.Length)
        {
            return data;
        }

        byte[] result = new byte[length];
        data.CopyTo(result, 0);
        return result;
    }

    private static BlockCipher Create(string algorithm)
    {
        if (algorithm.Equals("AES", StringComparison.OrdinalIgnoreCase))
        {
            return new AesCipher();
        }

        if (algorithm.Equals("XTEA", StringComparison.OrdinalIgnoreCase))
        {
            return new XteaCipher();
        }

        if (algorithm.Equals("FOG", StringComparison.OrdinalIgnoreCase))
        {
            return new FogCipher();
        }

        throw DbException.Get(ErrorCode.UnsupportedCipher, algorithm);
    }

    private abstract class BlockCipher
    {
        internal abstract int BlockSize { get; }

        internal abstract void SetKey(byte[] key);

        internal abstract byte[] Transform(byte[] data, bool encrypt);
    }

    private sealed class AesCipher : BlockCipher
    {
        private byte[] key = [];

        internal override int BlockSize => 16;

        internal override void SetKey(byte[] value) => key = value;

        internal override byte[] Transform(byte[] data, bool encrypt)
        {
            using Aes aes = Aes.Create();
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = key;
            return encrypt
                ? aes.EncryptEcb(data, PaddingMode.None)
                : aes.DecryptEcb(data, PaddingMode.None);
        }
    }

    private sealed class XteaCipher : BlockCipher
    {
        private const int Delta = unchecked((int)0x9E37_79B9);
        private readonly int[] roundKeys = new int[32];

        internal override int BlockSize => 8;

        internal override void SetKey(byte[] value)
        {
            int[] key = new int[4];
            for (int i = 0; i < 16; i += 4)
            {
                key[i / 4] = BinaryPrimitives.ReadInt32BigEndian(value.AsSpan(i, 4));
            }

            int sum = 0;
            for (int i = 0; i < 32;)
            {
                roundKeys[i++] = unchecked(sum + key[sum & 3]);
                sum = unchecked(sum + Delta);
                roundKeys[i++] = unchecked(sum + key[(int)((uint)sum >> 11) & 3]);
            }
        }

        internal override byte[] Transform(byte[] data, bool encrypt)
        {
            for (int offset = 0; offset < data.Length; offset += 8)
            {
                int y = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
                int z = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset + 4, 4));
                if (encrypt)
                {
                    for (int i = 0; i < 32; i++)
                    {
                        if ((i & 1) == 0)
                        {
                            y = unchecked(y + ((((z << 4) ^ (int)((uint)z >> 5)) + z) ^ roundKeys[i]));
                        }
                        else
                        {
                            z = unchecked(z + ((((y << 4) ^ (int)((uint)y >> 5)) + y) ^ roundKeys[i]));
                        }
                    }
                }
                else
                {
                    for (int i = 31; i >= 0; i--)
                    {
                        if ((i & 1) == 0)
                        {
                            y = unchecked(y - ((((z << 4) ^ (int)((uint)z >> 5)) + z) ^ roundKeys[i]));
                        }
                        else
                        {
                            z = unchecked(z - ((((y << 4) ^ (int)((uint)y >> 5)) + y) ^ roundKeys[i]));
                        }
                    }
                }

                BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset, 4), y);
                BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset + 4, 4), z);
            }

            return data;
        }
    }

    private sealed class FogCipher : BlockCipher
    {
        private int key;

        internal override int BlockSize => 16;

        internal override void SetKey(byte[] value)
            => key = (int)BinaryPrimitives.ReadInt64BigEndian(value.AsSpan(0, 8));

        internal override byte[] Transform(byte[] data, bool encrypt)
        {
            for (int offset = 0; offset < data.Length; offset += 16)
            {
                int x0 = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
                int x1 = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset + 4, 4));
                int x2 = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset + 8, 4));
                int x3 = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset + 12, 4));
                if (encrypt)
                {
                    x0 = RotateLeft(x0 ^ key, x1);
                    x2 = RotateLeft(x2 ^ key, x1);
                    x1 = RotateLeft(x1 ^ key, x0);
                    x3 = RotateLeft(x3 ^ key, x0);
                }
                else
                {
                    x1 = RotateRight(x1, x0) ^ key;
                    x3 = RotateRight(x3, x0) ^ key;
                    x0 = RotateRight(x0, x1) ^ key;
                    x2 = RotateRight(x2, x1) ^ key;
                }

                BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset, 4), x0);
                BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset + 4, 4), x1);
                BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset + 8, 4), x2);
                BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset + 12, 4), x3);
            }

            return data;
        }

        private static int RotateLeft(int value, int distance)
            => (int)BitOperations.RotateLeft((uint)value, distance);

        private static int RotateRight(int value, int distance)
            => (int)BitOperations.RotateRight((uint)value, distance);
    }
}
