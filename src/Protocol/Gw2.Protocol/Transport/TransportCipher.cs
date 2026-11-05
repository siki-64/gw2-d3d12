using System;

namespace Gw2.Protocol.Transport
{
    public sealed class TransportCipherState
    {
        internal byte IndexI;
        internal byte IndexJ;
        internal readonly byte[] SBox = new byte[256];

        public int I => IndexI;

        public int J => IndexJ;

        public byte[] CopySBox() => (byte[])SBox.Clone();

        public static TransportCipherState FromCapturedState(int i, int j, ReadOnlySpan<byte> sbox)
        {
            if ((uint)i > 0xFF) throw new ArgumentOutOfRangeException(nameof(i));
            if ((uint)j > 0xFF) throw new ArgumentOutOfRangeException(nameof(j));
            if (sbox.Length != 256) throw new ArgumentException("S-box must be 256 bytes", nameof(sbox));
            var state = new TransportCipherState { IndexI = (byte)i, IndexJ = (byte)j };
            sbox.CopyTo(state.SBox);
            return state;
        }
    }

    public static class TransportCipher
    {
        public const int KeyLength = 0x14;

        private const uint C_A0 = 0x9FB498B3u;
        private const uint C_A1 = 0x66B0CD0Du;
        private const uint C_A2_F = 0x7BF36AE2u;
        private const uint C_A2 = 0xF33D5697u;
        private const uint C_A3 = 0xD675E47Bu;
        private const uint C_A3_B = 0x59D148C0u;
        private const uint C_W0 = 0xB453C259u;
        private const uint C_W0_MASK = 0x22222222u;

        private static uint Rol(uint x, int n) => (x << n) | (x >> (32 - n));

        public static uint[] MixFiveWords(uint w0, uint w1, uint w2, uint w3, uint w4)
        {
            uint a0 = w0 + C_A0;
            uint b0 = Rol(a0, 30);
            uint a1 = w1 + C_A1 + Rol(a0, 5);
            uint b1 = Rol(a1, 30);
            uint f2 = ~(a0 & C_W0_MASK) & C_A2_F;
            uint a2 = Rol(a1, 5) + w2 + f2 + C_A2;
            uint b2 = Rol(a2, 30);
            uint f3 = ((b0 ^ C_A3_B) & a1) ^ C_A3_B;
            uint a3 = Rol(a2, 5) + w3 + f3 + C_A3;
            uint f4 = ((b0 ^ b1) & a2) ^ b0;
            return new[]
            {
                f4 + w0 + w4 + Rol(a3, 5) + C_W0,
                w1 + a3,
                w2 + b2,
                w3 + b1,
                w4 + b0,
            };
        }

        public static byte[] DeriveKey(ReadOnlySpan<byte> key)
        {
            Span<byte> buf = stackalloc byte[KeyLength];
            buf.Clear();
            int n = Math.Min(key.Length, KeyLength);
            for (int i = 0; i < n; i++) buf[i] = key[i];

            uint[] mixed = MixFiveWords(
                ReadU32(buf, 0), ReadU32(buf, 4), ReadU32(buf, 8), ReadU32(buf, 12), ReadU32(buf, 16));

            var result = new byte[KeyLength];
            for (int i = 0; i < 5; i++)
            {
                result[i * 4 + 0] = (byte)(mixed[i]);
                result[i * 4 + 1] = (byte)(mixed[i] >> 8);
                result[i * 4 + 2] = (byte)(mixed[i] >> 16);
                result[i * 4 + 3] = (byte)(mixed[i] >> 24);
            }
            return result;
        }

        public static void KeySchedule(ReadOnlySpan<byte> key, TransportCipherState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            byte[] mixed = DeriveKey(key);
            byte[] s = state.SBox;
            for (int i = 0; i < 256; i++) s[i] = (byte)i;
            byte j = 0;
            for (int i = 0; i < 256; i++)
            {
                j = (byte)(j + s[i] + mixed[i % KeyLength]);
                (s[i], s[j]) = (s[j], s[i]);
            }
            state.IndexI = 0;
            state.IndexJ = 0;
        }

        public static void Crypt(TransportCipherState state, ReadOnlySpan<byte> input, Span<byte> output)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (output.Length < input.Length)
                throw new ArgumentException("output shorter than input", nameof(output));
            byte[] s = state.SBox;
            byte i = state.IndexI;
            byte j = state.IndexJ;
            for (int k = 0; k < input.Length; k++)
            {
                i = (byte)(i + 1);
                j = (byte)(j + s[i]);
                (s[i], s[j]) = (s[j], s[i]);
                output[k] = (byte)(s[(s[i] + s[j]) & 0xFF] ^ input[k]);
            }
            state.IndexI = i;
            state.IndexJ = j;
        }

        public static byte[] Crypt(TransportCipherState state, ReadOnlySpan<byte> input)
        {
            var result = new byte[input.Length];
            Crypt(state, input, result);
            return result;
        }

        private static uint ReadU32(ReadOnlySpan<byte> b, int off) =>
            (uint)(b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24));
    }
}
