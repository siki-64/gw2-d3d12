using System;

namespace Gw2.Protocol.Framing
{
    public static class Lz4Block
    {
        public static byte[] Decompress(ReadOnlySpan<byte> source, int decodedLength)
        {
            if (decodedLength < 0)
                throw new ArgumentOutOfRangeException(nameof(decodedLength));

            var dst = new byte[decodedLength];
            int di = 0;
            int si = 0;
            int n = source.Length;

            while (si < n)
            {
                int token = source[si++];

                int literalLength = token >> 4;
                if (literalLength == 15)
                {
                    int b;
                    do
                    {
                        if (si >= n) throw new FormatException("lz4: literal length overrun");
                        b = source[si++];
                        literalLength += b;
                    } while (b == 255);
                }

                if (si + literalLength > n) throw new FormatException("lz4: literal overrun");
                if (di + literalLength > decodedLength) throw new FormatException("lz4: output overrun");
                source.Slice(si, literalLength).CopyTo(dst.AsSpan(di));
                si += literalLength;
                di += literalLength;

                if (si >= n) break;

                if (si + 2 > n) throw new FormatException("lz4: offset overrun");
                int offset = source[si] | (source[si + 1] << 8);
                si += 2;
                if (offset == 0 || offset > di) throw new FormatException("lz4: bad match offset");

                int matchLength = token & 0x0F;
                if (matchLength == 15)
                {
                    int b;
                    do
                    {
                        if (si >= n) throw new FormatException("lz4: match length overrun");
                        b = source[si++];
                        matchLength += b;
                    } while (b == 255);
                }
                matchLength += 4;

                if (di + matchLength > decodedLength) throw new FormatException("lz4: output overrun (match)");
                int start = di - offset;
                for (int k = 0; k < matchLength; k++) dst[di + k] = dst[start + k];
                di += matchLength;
            }

            if (di != decodedLength)
                throw new FormatException($"lz4: produced {di} bytes, expected {decodedLength}");
            return dst;
        }
    }
}
