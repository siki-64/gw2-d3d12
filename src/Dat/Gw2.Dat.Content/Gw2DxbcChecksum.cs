using System.Buffers.Binary;

namespace Gw2.Dat.Content;

public static class Gw2DxbcChecksum
{
    private const int HashOffset = 0x14;

    public static byte[] Compute(ReadOnlySpan<byte> container)
    {
        var length = container.Length - HashOffset;
        var numBits = (uint)length * 8;
        var numBitsPart2 = (numBits >> 2) | 1;

        var md5 = new Md5();
        var leftover = length % 64;
        md5.Update(container.Slice(HashOffset, length - leftover));

        var tail = container[(HashOffset + length - leftover)..];
        var block = new byte[64];
        if (leftover >= 56)
        {
            tail[..leftover].CopyTo(block);
            block[leftover] = 0x80;
            md5.Transform(block);

            Array.Clear(block, 0, block.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(block, numBits);
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(60), numBitsPart2);
            md5.Transform(block);
        }
        else
        {
            Span<byte> bits = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bits, numBits);
            md5.Update(bits);
            md5.Update(tail[..leftover]);

            var paddingBytes = 64 - leftover - 4;
            Array.Clear(block, 0, block.Length);
            block[0] = 0x80;
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(paddingBytes - 4), numBitsPart2);
            md5.Update(block.AsSpan(0, paddingBytes));
        }

        return md5.Digest();
    }

    private sealed class Md5
    {
        private static uint RotateLeft(uint value, int shift) => (value << shift) | (value >> (32 - shift));

        private static readonly int[] Shifts =
        {
            7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
            5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
            4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
            6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
        };

        private static readonly uint[] Constants =
        {
            0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501,
            0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be, 0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821,
            0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa, 0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
            0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed, 0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
            0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c, 0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
            0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05, 0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
            0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
            0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1, 0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391,
        };

        private uint _a = 0x67452301;
        private uint _b = 0xefcdab89;
        private uint _c = 0x98badcfe;
        private uint _d = 0x10325476;
        private readonly byte[] _buffer = new byte[64];
        private int _bufferLength;

        public void Update(ReadOnlySpan<byte> data)
        {
            while (!data.IsEmpty)
            {
                var take = Math.Min(64 - _bufferLength, data.Length);
                data[..take].CopyTo(_buffer.AsSpan(_bufferLength));
                _bufferLength += take;
                data = data[take..];
                if (_bufferLength == 64)
                {
                    Transform(_buffer);
                    _bufferLength = 0;
                }
            }
        }

        public void Transform(ReadOnlySpan<byte> block)
        {
            Span<uint> message = stackalloc uint[16];
            for (var i = 0; i < 16; i++) message[i] = BinaryPrimitives.ReadUInt32LittleEndian(block[(i * 4)..]);

            var a = _a; var b = _b; var c = _c; var d = _d;
            for (var i = 0; i < 64; i++)
            {
                uint f;
                int g;
                if (i < 16) { f = (b & c) | (~b & d); g = i; }
                else if (i < 32) { f = (d & b) | (~d & c); g = (5 * i + 1) % 16; }
                else if (i < 48) { f = b ^ c ^ d; g = (3 * i + 5) % 16; }
                else { f = c ^ (b | ~d); g = (7 * i) % 16; }

                f += a + Constants[i] + message[g];
                a = d; d = c; c = b; b += RotateLeft(f, Shifts[i]);
            }

            _a += a; _b += b; _c += c; _d += d;
        }

        public byte[] Digest()
        {
            var result = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(result, _a);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), _b);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), _c);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), _d);
            return result;
        }
    }
}
