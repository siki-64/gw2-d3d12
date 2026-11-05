using System;
using System.Collections.Generic;

namespace Gw2.Protocol.Framing
{
    public static class TransportFrame
    {
        public static byte[] Deframe(ReadOnlySpan<byte> input, out int consumed)
        {
            var stream = new List<byte>();
            int off = 0;
            while (off + 4 <= input.Length)
            {
                int compLen = input[off] | (input[off + 1] << 8);
                int decodedLen = input[off + 2] | (input[off + 3] << 8);
                int payloadSize = compLen != 0 ? compLen : decodedLen;
                if (off + 4 + payloadSize > input.Length) break;

                ReadOnlySpan<byte> payload = input.Slice(off + 4, payloadSize);
                if (compLen == 0)
                {
                    for (int k = 0; k < decodedLen; k++) stream.Add(payload[k]);
                }
                else
                {
                    stream.AddRange(Lz4Block.Decompress(payload, decodedLen));
                }
                off += 4 + payloadSize;
            }
            consumed = off;
            return stream.ToArray();
        }

        public static byte[] Deframe(ReadOnlySpan<byte> input) => Deframe(input, out _);

        public static IReadOnlyList<(int RawOffset, int DeframedOffset)> FrameStarts(ReadOnlySpan<byte> input)
        {
            var starts = new List<(int, int)>();
            int off = 0;
            int deframed = 0;
            while (off + 4 <= input.Length)
            {
                int compLen = input[off] | (input[off + 1] << 8);
                int decodedLen = input[off + 2] | (input[off + 3] << 8);
                int payloadSize = compLen != 0 ? compLen : decodedLen;
                if (off + 4 + payloadSize > input.Length) break;
                starts.Add((off, deframed));
                deframed += decodedLen;
                off += 4 + payloadSize;
            }
            return starts;
        }

        public static int RawOffsetOf(IReadOnlyList<(int RawOffset, int DeframedOffset)> frameStarts, int deframedOffset)
        {
            int low = 0, high = frameStarts.Count - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (frameStarts[middle].DeframedOffset <= deframedOffset) low = middle;
                else high = middle - 1;
            }
            return frameStarts.Count == 0 ? deframedOffset : frameStarts[low].RawOffset;
        }
    }
}
