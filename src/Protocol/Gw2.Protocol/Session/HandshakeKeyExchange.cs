using System;
using System.Numerics;

namespace Gw2.Protocol.Session
{
    public readonly struct HandshakeKeyPair
    {
        public byte[] SharedSecret { get; }

        public byte[] PublicValue { get; }

        internal HandshakeKeyPair(byte[] sharedSecret, byte[] publicValue)
        {
            SharedSecret = sharedSecret;
            PublicValue = publicValue;
        }

        public byte[] SharedSecretPrefix()
        {
            var prefix = new byte[0x14];
            Array.Copy(SharedSecret, prefix, prefix.Length);
            return prefix;
        }
    }

    public static class HandshakeKeyExchange
    {
        public const int SeedLength = 0x14;

        public const int ValueLength = 0x40;

        private const uint PmA = 0xBC8F;

        private const uint PmQ = 0xADC8;

        private const uint PmMask = 0x7FFFFFFF;
        private const uint XorSeed = 0x075BD924;

        public static byte[] ExpandExponent(ReadOnlySpan<byte> seed)
        {
            if (seed.Length == 0)
                throw new ArgumentException("seed must be non-empty", nameof(seed));

            uint[] words = ExpandExponentWords(seed);
            var bytes = new byte[words.Length * 4];
            for (int i = 0; i < words.Length; i++)
            {
                bytes[i * 4 + 0] = (byte)words[i];
                bytes[i * 4 + 1] = (byte)(words[i] >> 8);
                bytes[i * 4 + 2] = (byte)(words[i] >> 16);
                bytes[i * 4 + 3] = (byte)(words[i] >> 24);
            }
            return bytes;
        }

        public static HandshakeKeyPair Derive(ReadOnlySpan<byte> seed, HandshakeGroup group) =>
            Derive(seed, group, group.ServerPublicValue);

        public static HandshakeKeyPair Derive(ReadOnlySpan<byte> seed, HandshakeGroup group,
                                              ReadOnlySpan<byte> serverPublicValue)
        {
            BigInteger r = Decode(ExpandExponent(seed));
            BigInteger p = Decode(group.Modulus);
            BigInteger g = Decode(serverPublicValue);

            BigInteger shared = BigInteger.ModPow(g, r, p);
            BigInteger publicValue = BigInteger.ModPow(HandshakeGroup.Generator, r, p);
            return new HandshakeKeyPair(
                ToLittleEndian(shared, ValueLength),
                ToLittleEndian(publicValue, ValueLength));
        }

        public static byte[] ServerPublicValue(HandshakeGroup group, ReadOnlySpan<byte> serverSecretExponent)
        {
            BigInteger p = Decode(group.Modulus);
            return ToLittleEndian(BigInteger.ModPow(HandshakeGroup.Generator, Decode(serverSecretExponent), p), ValueLength);
        }

        public static byte[] ServerSharedSecret(HandshakeGroup group, ReadOnlySpan<byte> serverSecretExponent,
                                                ReadOnlySpan<byte> clientPublicValue)
        {
            BigInteger p = Decode(group.Modulus);
            BigInteger shared = BigInteger.ModPow(Decode(clientPublicValue), Decode(serverSecretExponent), p);
            return ToLittleEndian(shared, ValueLength);
        }

        private static uint[] ExpandExponentWords(ReadOnlySpan<byte> seed)
        {
            const int wordCount = 16;

            var limbs = new uint[(seed.Length + 3) / 4];
            for (int i = 0; i < seed.Length; i++)
                limbs[i >> 2] |= (uint)seed[i] << ((i & 3) * 8);

            int size = limbs.Length;
            while (size > 0 && limbs[size - 1] == 0) size--;
            if (size == 0)
            {
                limbs = new uint[] { 0 };
                size = 1;
            }

            var output = new uint[wordCount];
            int idx = 0;
            for (int k = 0; k < wordCount; k++)
            {
                uint x = (k == idx ? XorSeed : 0u) ^ limbs[idx];
                uint word = 0;
                uint state = 0;
                for (int step = 0; step < 2; step++)
                {
                    x = (uint)(((x / PmQ) + (ulong)x * PmA) & 0xFFFFFFFF);
                    state = x & PmMask;
                    word |= (x & 0xFFFF) << (16 * step);
                    x = state;
                }
                output[k] = word;
                limbs[idx] = state;
                idx = (idx + 1) % size;
            }
            return output;
        }

        private static BigInteger Decode(ReadOnlySpan<byte> littleEndian) =>
            new BigInteger(littleEndian, isUnsigned: true, isBigEndian: false);

        private static byte[] ToLittleEndian(BigInteger value, int length)
        {
            byte[] raw = value.ToByteArray(isUnsigned: true, isBigEndian: false);
            var result = new byte[length];
            Array.Copy(raw, result, Math.Min(raw.Length, length));
            return result;
        }
    }
}
