namespace Gw2.Dat.Archive;

public static class Gw2AtexBlit
{
    private const int TwoChannelBlockBytes = 16;
    private const int ChannelBlockBytes = 8;
    private static readonly byte[] WhiteColorBlock = [0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0];

    public static void ThreeDcxToBc5x(Span<byte> blocks)
    {
        Span<byte> firstChannel = stackalloc byte[ChannelBlockBytes];
        for (var offset = 0; offset + TwoChannelBlockBytes <= blocks.Length; offset += TwoChannelBlockBytes)
        {
            var block = blocks.Slice(offset, TwoChannelBlockBytes);
            block[..ChannelBlockBytes].CopyTo(firstChannel);
            block[ChannelBlockBytes..].CopyTo(block);
            firstChannel.CopyTo(block[ChannelBlockBytes..]);
        }
    }

    public static byte[] DxtaToDxt5(ReadOnlySpan<byte> blocks)
    {
        var dxt5 = new byte[blocks.Length * 2];
        for (var block = 0; block * ChannelBlockBytes < blocks.Length; block++)
        {
            blocks.Slice(block * ChannelBlockBytes, ChannelBlockBytes).CopyTo(dxt5.AsSpan(block * TwoChannelBlockBytes));
            WhiteColorBlock.CopyTo(dxt5.AsSpan(block * TwoChannelBlockBytes + ChannelBlockBytes));
        }
        return dxt5;
    }

    public static byte[] DxtnToDxt5(ReadOnlySpan<byte> blocks, int width, int height) =>
        Gw2Dxt5Encoder.EncodeOpaque(Gw2DxtnDecoder.DecodeToBgra(blocks, width, height), width, height);
}
