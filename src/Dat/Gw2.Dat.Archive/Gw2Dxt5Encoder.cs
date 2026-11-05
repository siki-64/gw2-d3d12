using System.Buffers.Binary;

namespace Gw2.Dat.Archive;

public static class Gw2Dxt5Encoder
{
    private const int TexelCount = 16;
    private const int PaletteSize = 4;
    private const uint GoodEnoughErrorPerTexel = 1u << 19;
    private const int ChannelCount = 3;
    private static readonly int[] ChannelStep = [1, 32, 2048];
    private static readonly int[] ChannelMask = [31, 2016, 63488];
    private static readonly byte[] OpaqueAlphaBlock = [254, 255, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

    public static byte[] EncodeOpaque(ReadOnlySpan<uint> bgra, int width, int height)
    {
        var blocksWide = (width + 3) / 4;
        var blocksHigh = (height + 3) / 4;
        var output = new byte[blocksWide * blocksHigh * 16];
        Span<uint> tile = stackalloc uint[TexelCount];
        for (var blockY = 0; blockY < blocksHigh; blockY++)
        for (var blockX = 0; blockX < blocksWide; blockX++)
        {
            GatherTile(bgra, width, height, blockX * 4, blockY * 4, tile);
            var block = output.AsSpan((blockY * blocksWide + blockX) * 16, 16);
            OpaqueAlphaBlock.CopyTo(block);
            EncodeColor(tile, block[8..]);
        }

        return output;
    }

    private static void GatherTile(ReadOnlySpan<uint> bgra, int width, int height, int left, int top, Span<uint> tile)
    {
        uint previous = 0;
        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 4; column++)
        {
            int x = left + column, y = top + row;
            if (x < width && y < height) previous = bgra[y * width + x];
            tile[row * 4 + column] = previous;
        }
    }

    public static int ExpandFiveBits(int value) => (value << 3) | (value >> 2);

    public static int ExpandSixBits(int value) => (value << 2) | (value >> 4);

    private static (uint Y, uint U, uint V) ToYuv(uint bgra)
    {
        int blue = (byte)bgra, green = (byte)(bgra >> 8), red = (byte)(bgra >> 16);
        return ((uint)(4748 * blue + 13992 * red + 47053 * green),
            (uint)(32897 * blue - 25356 * green - 7540 * red + 8388480),
            (uint)(32897 * red - 29881 * green - 3015 * blue + 8388480));
    }

    private static void EncodeColor(ReadOnlySpan<uint> tile, Span<byte> output)
    {
        Span<uint> yuv = stackalloc uint[TexelCount * 3];
        for (var texel = 0; texel < TexelCount; texel++)
        {
            var (y, u, v) = ToYuv(tile[texel]);
            yuv[texel * 3] = y;
            yuv[texel * 3 + 1] = u;
            yuv[texel * 3 + 2] = v;
        }

        Span<uint> minimum = [yuv[0], yuv[1], yuv[2]];
        Span<uint> maximum = [yuv[0], yuv[1], yuv[2]];
        for (var texel = 1; texel < TexelCount; texel++)
        for (var axis = 0; axis < 3; axis++)
        {
            var value = yuv[texel * 3 + axis];
            if (value < minimum[axis]) minimum[axis] = value;
            else if (value > maximum[axis]) maximum[axis] = value;
        }

        Span<int> best = stackalloc int[2];
        FitEndpoints(yuv, minimum, maximum, best);
        RefineEndpoints(tile, yuv, best, out var indices);
        BinaryPrimitives.WriteUInt16LittleEndian(output, (ushort)best[0]);
        BinaryPrimitives.WriteUInt16LittleEndian(output[2..], (ushort)best[1]);
        BinaryPrimitives.WriteUInt32LittleEndian(output[4..], indices);
    }

    private static void FitEndpoints(ReadOnlySpan<uint> yuv, ReadOnlySpan<uint> minimum, ReadOnlySpan<uint> maximum, Span<int> endpoints)
    {
        var doubledLumaRange = 2 * (maximum[0] - minimum[0]);
        var uRange = maximum[1] - minimum[1];
        var dominant = uRange > doubledLumaRange ? 1 : 0;
        if (maximum[2] - minimum[2] > Math.Max(uRange, doubledLumaRange)) dominant = 2;
        int[] others = dominant switch { 0 => [1, 2], 1 => [0, 2], _ => [0, 1] };

        Span<uint> low = stackalloc uint[3];
        Span<uint> high = stackalloc uint[3];
        low[dominant] = minimum[dominant];
        high[dominant] = maximum[dominant];
        double count = TexelCount;
        foreach (var axis in others)
        {
            double dominantSum = 0, axisSum = 0;
            for (var texel = 0; texel < TexelCount; texel++)
            {
                dominantSum += (int)yuv[texel * 3 + dominant];
                axisSum += (int)yuv[texel * 3 + axis];
            }

            double covariance = 0, variance = 0;
            for (var texel = 0; texel < TexelCount; texel++)
            {
                var centered = (int)yuv[texel * 3 + dominant] - dominantSum / count;
                covariance += (int)yuv[texel * 3 + axis] * centered;
                variance += centered * centered;
            }

            double slope, intercept;
            if (variance < 1.0 || Math.Abs(slope = covariance / variance) > 100.0)
            {
                slope = 0.0;
                intercept = axisSum / count;
            }
            else
            {
                intercept = (axisSum - dominantSum * slope) / count;
            }

            low[axis] = (uint)(int)Math.Max(0.0, Math.Min(4294967295.0, (int)minimum[dominant] * slope + intercept));
            high[axis] = (uint)(int)Math.Max(0.0, Math.Min(4294967295.0, (int)maximum[dominant] * slope + intercept));
        }

        var (lowRed, lowGreen, lowBlue) = ToRgb(low);
        var (highRed, highGreen, highBlue) = ToRgb(high);
        var first = (31 * lowRed / 255) << 11 | (63 * lowGreen / 255) << 5 | 31 * lowBlue / 255;
        var highBlue5 = (31 * highBlue + 254) / 255;
        var second = (31 * highRed + 254) / 255 << 11 | (63 * highGreen + 254) / 255 << 5 | highBlue5;
        if (first == second)
        {
            if ((highBlue5 & 0x1F) == 0x1F) first--;
            else second++;
        }

        if (first < second) (first, second) = (second, first);
        endpoints[0] = first;
        endpoints[1] = second;
    }

    private static (int Red, int Green, int Blue) ToRgb(ReadOnlySpan<uint> yuv)
    {
        var luma = (long)yuv[0] << 16;
        var red = (int)((luma + 103197L * yuv[2] - 0xC98E600000L) >> 32);
        var green = (int)((luma - 12272L * yuv[1] - 30688L * yuv[2] + 0x53E8400000L) >> 32);
        var blue = (int)((luma + 121613L * yuv[1] - 0xED86500000L) >> 32);
        return (ClampByte(red), ClampByte(green), ClampByte(blue));
    }

    private static int ClampByte(int value) => value > 255 ? 255 : value < 0 ? 0 : value;

    private static void RefineEndpoints(ReadOnlySpan<uint> tile, ReadOnlySpan<uint> yuv, Span<int> best, out uint bestIndices)
    {
        Span<int> candidate = [best[0], best[1]];
        Span<uint> palette = stackalloc uint[PaletteSize];
        Span<uint> paletteYuv = stackalloc uint[PaletteSize * 3];
        Span<int> residual = stackalloc int[ChannelCount];
        var bestError = uint.MaxValue;
        bestIndices = 0;
        var step = 0;
        while (true)
        {
            BuildPalette(candidate, palette, paletteYuv);
            uint indices = 0, error = 0;
            int blueResidual = 0, greenResidual = 0, redResidual = 0;
            for (var texel = 0; texel < TexelCount; texel++)
            {
                uint y = yuv[texel * 3], u = yuv[texel * 3 + 1], v = yuv[texel * 3 + 2];
                var chosen = 0;
                var chosenError = uint.MaxValue;
                for (var entry = 0; entry < PaletteSize; entry++)
                {
                    var entryError = Distance(u, paletteYuv[entry * 3 + 1]) + Distance(v, paletteYuv[entry * 3 + 2]) +
                                     2 * Distance(y, paletteYuv[entry * 3]);
                    if (entryError < chosenError)
                    {
                        chosenError = entryError;
                        chosen = entry;
                    }
                }

                indices |= (uint)chosen << (2 * texel);
                error += chosenError;
                var color = tile[texel];
                var match = palette[chosen];
                blueResidual += (byte)color - (byte)match;
                greenResidual += (byte)(color >> 8) - (byte)(match >> 8);
                redResidual += (byte)(color >> 16) - (byte)(match >> 16);
            }

            var tryNext = step;
            if (error < bestError)
            {
                best[0] = candidate[0];
                best[1] = candidate[1];
                residual[0] = blueResidual;
                residual[1] = greenResidual;
                residual[2] = redResidual;
                bestError = error;
                bestIndices = indices;
                if (error < TexelCount * GoodEnoughErrorPerTexel) return;
                if (step != 0) tryNext = step - 1;
            }

            var endpoint = tryNext & 1;
            var channel = tryNext >> 1;
            if (channel == ChannelCount) return;
            var current = best[endpoint];
            var nudged = residual[channel] <= 0 ? current - ChannelStep[channel] : current + ChannelStep[channel];
            candidate[endpoint] = (current & ~ChannelMask[channel]) + (nudged & ChannelMask[channel]);
            candidate[1 - endpoint] = best[1 - endpoint];
            if ((uint)candidate[0] < (uint)candidate[1]) (candidate[0], candidate[1]) = (candidate[1], candidate[0]);
            step = tryNext + 1;
        }
    }

    private static uint Distance(uint a, uint b) => a > b ? a - b : b - a;

    private static void BuildPalette(ReadOnlySpan<int> endpoints, Span<uint> palette, Span<uint> paletteYuv)
    {
        var first = Expand565(endpoints[0]);
        var second = Expand565(endpoints[1]);
        uint nearFirst, nearSecond;
        if ((uint)endpoints[0] <= (uint)endpoints[1])
        {
            var sum = first + second + 0x100401u;
            nearFirst = nearSecond = ((sum & 0x3FE) | (((sum & 0xFF800) | ((sum >> 2) & 0xFF80000)) >> 2)) >> 1;
        }
        else
        {
            nearSecond = ThirdOf(first + 2 * second + 0x100401u);
            nearFirst = ThirdOf(second + 2 * first + 0x100401u);
        }

        palette[0] = Pack(first);
        palette[1] = Pack(second);
        palette[2] = nearFirst;
        palette[3] = nearSecond;
        for (var entry = 0; entry < PaletteSize; entry++)
        {
            var (y, u, v) = ToYuv(palette[entry]);
            paletteYuv[entry * 3] = y;
            paletteYuv[entry * 3 + 1] = u;
            paletteYuv[entry * 3 + 2] = v;
        }
    }

    private static uint Expand565(int endpoint) =>
        (uint)ExpandFiveBits(endpoint & 0x1F) | ((uint)ExpandSixBits((endpoint >> 5) & 0x3F) | (uint)ExpandFiveBits((endpoint >> 11) & 0x1F) << 10) << 10;

    private static uint ThirdOf(uint sum) =>
        (sum & 0x3FF) / 3 | ((((sum & 0xFFC00) / 3) & 0xFFC00) | (((sum & 0x3FF00000) / 0xC) & 0xFFC0000)) >> 2;

    private static uint Pack(uint tenBitFields) =>
        (byte)tenBitFields | ((tenBitFields & 0x3FC00) | ((tenBitFields >> 2) & 0x3FC0000)) >> 2;
}
