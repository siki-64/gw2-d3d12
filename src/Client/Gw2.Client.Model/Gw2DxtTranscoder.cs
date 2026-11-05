namespace Gw2.Client.Model;

public static class Gw2DxtTranscoder
{
    private const int Dxt1BlockSize = 8;
    private const int Dxt5BlockSize = 16;
    private const byte OpaqueAlpha = 255;
    private const byte TransparentAlpha = 0;
    private const int TransparentAlphaIndex = 1;

    public static byte[] Dxt1ToDxt5(byte[] dxt1)
    {
        if (dxt1 is null) throw new ArgumentNullException(nameof(dxt1));
        if (dxt1.Length % Dxt1BlockSize != 0) throw new ArgumentException("DXT1 data must be whole 8-byte blocks", nameof(dxt1));

        var blockCount = dxt1.Length / Dxt1BlockSize;
        var dxt5 = new byte[blockCount * Dxt5BlockSize];
        for (var block = 0; block < blockCount; block++)
            TranscodeBlock(dxt1.AsSpan(block * Dxt1BlockSize, Dxt1BlockSize), dxt5.AsSpan(block * Dxt5BlockSize, Dxt5BlockSize));
        return dxt5;
    }

    private static void TranscodeBlock(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var color0 = (ushort)(source[0] | (source[1] << 8));
        var color1 = (ushort)(source[2] | (source[3] << 8));
        var colorIndices = (uint)(source[4] | (source[5] << 8) | (source[6] << 16) | (source[7] << 24));

        destination[0] = OpaqueAlpha;
        destination[1] = TransparentAlpha;
        var colorBlock = destination.Slice(8);

        if (color0 > color1)
        {
            source.CopyTo(colorBlock);
            return;
        }

        ulong alphaIndices = 0;
        uint remappedIndices = 0;
        for (var pixel = 0; pixel < 16; pixel++)
        {
            var index = (colorIndices >> (pixel * 2)) & 3;
            uint remapped = index switch
            {
                0 => 1,
                1 => 0,
                2 => 3,
                _ => 1,
            };
            remappedIndices |= remapped << (pixel * 2);
            if (index == 3) alphaIndices |= (ulong)TransparentAlphaIndex << (pixel * 3);
        }

        for (var i = 0; i < 6; i++) destination[2 + i] = (byte)(alphaIndices >> (i * 8));
        colorBlock[0] = (byte)color1;
        colorBlock[1] = (byte)(color1 >> 8);
        colorBlock[2] = (byte)color0;
        colorBlock[3] = (byte)(color0 >> 8);
        for (var i = 0; i < 4; i++) colorBlock[4 + i] = (byte)(remappedIndices >> (i * 8));
    }
}
