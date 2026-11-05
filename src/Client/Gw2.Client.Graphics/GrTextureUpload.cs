using System;
using System.Collections.Generic;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed record GrTextureData(int Width, int Height, SurfaceFormat Format, IReadOnlyList<ArraySegment<byte>> Mips);

public static class GrTextureUpload
{
    private const int DdsHeaderBytes = 128;
    private const int BgraBitCount = 32;
    private const uint BgraRedMask = 0x00FF0000;
    private const int MinimumDroppedDimension = 4;
    private const int BlockEdge = 4;

    public static Texture2D? FromAsset(GraphicsDevice device, Gw2TextureAsset asset) =>
        Prepare(asset) is { } data ? Upload(device, data) : null;

    public static GrTextureData? Prepare(Gw2TextureAsset asset, int topMipsToDrop = 0)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Texture is { } texture) return PrepareAtex(texture, topMipsToDrop);
        if (asset.RawDds is { } dds) return PrepareRawDds(dds, asset.Bytes, topMipsToDrop);
        return null;
    }

    public static int DroppableTopMips(int width, int height, int mipCount, int requested)
    {
        var dropped = 0;
        while (dropped < requested && dropped + 1 < mipCount
               && width >> (dropped + 1) >= MinimumDroppedDimension && height >> (dropped + 1) >= MinimumDroppedDimension)
            dropped++;
        return dropped;
    }

    public static Texture2D Upload(GraphicsDevice device, GrTextureData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var uploaded = new Texture2D(device, new TextureDescription { Width = data.Width, Height = data.Height, MipLevels = data.Mips.Count, Format = data.Format });
        for (var mip = 0; mip < data.Mips.Count; mip++)
            uploaded.SetData<byte>(data.Mips[mip], mip);
        return uploaded;
    }

    public static GrTextureData? PrepareUpResolved(Gw2TextureAsset baseAsset, Gw2TextureAsset? highResolutionAsset, int topMipsToDrop)
    {
        ArgumentNullException.ThrowIfNull(baseAsset);
        if (baseAsset.Texture is not { } baseTexture || SurfaceFormatOf(baseTexture) is not { } format) return null;
        if (topMipsToDrop > 0) return PrepareAtex(baseTexture, topMipsToDrop - 1);
        if (highResolutionAsset?.Texture is not { } highResolution || SurfaceFormatOf(highResolution) != format
            || highResolution.Width != baseTexture.Width * 2 || highResolution.Height != baseTexture.Height * 2) return null;
        if (PrepareAtex(baseTexture, 0) is not { } below || below.Mips.Count != FullChainLength(baseTexture.Width, baseTexture.Height)) return null;
        var mips = new ArraySegment<byte>[below.Mips.Count + 1];
        mips[0] = new ArraySegment<byte>(SupportedBlocks(highResolution, 0), 0, BlockBytes(format, highResolution.Width, highResolution.Height));
        for (var mip = 0; mip < below.Mips.Count; mip++) mips[mip + 1] = below.Mips[mip];
        return new GrTextureData(highResolution.Width, highResolution.Height, format, mips);
    }

    private static SurfaceFormat? SurfaceFormatOf(Gw2Texture texture) => texture.AtexFormat?.Id switch
    {
        Gw2AtexFormat.Dxt1Id => SurfaceFormat.Bc1Unorm,
        Gw2AtexFormat.Dxt2Id or Gw2AtexFormat.Dxt3Id => SurfaceFormat.Bc2Unorm,
        Gw2AtexFormat.Dxt4Id or Gw2AtexFormat.Dxt5Id or Gw2AtexFormat.DxtaId or Gw2AtexFormat.DxtlId or Gw2AtexFormat.DxtnId => SurfaceFormat.Bc3Unorm,
        Gw2AtexFormat.ThreeDcxId or Gw2AtexFormat.Bc5xId => SurfaceFormat.Bc5Unorm,
        Gw2AtexFormat.Bc7Id => SurfaceFormat.Bc7Unorm,
        _ => (SurfaceFormat?)null,
    };

    private static GrTextureData? PrepareAtex(Gw2Texture texture, int topMipsToDrop)
    {
        if (SurfaceFormatOf(texture) is not { } format) return null;
        var authoredCount = AuthoredChainLength(texture);
        var mipCount = EndsAtBlockEdge(texture, authoredCount) ? FullChainLength(texture.Width, texture.Height) : authoredCount;
        var dropped = DroppableTopMips(texture.Width, texture.Height, mipCount, topMipsToDrop);
        var mips = new ArraySegment<byte>[mipCount - dropped];
        for (var mip = dropped; mip < mipCount; mip++)
        {
            var width = Math.Max(1, texture.Width >> mip);
            var height = Math.Max(1, texture.Height >> mip);
            mips[mip - dropped] = mip < authoredCount
                ? new ArraySegment<byte>(SupportedBlocks(texture, mip), 0, BlockBytes(format, width, height))
                : RepeatedFirstBlock(mips[authoredCount - 1 - dropped], format, width, height);
        }
        return new GrTextureData(Math.Max(1, texture.Width >> dropped), Math.Max(1, texture.Height >> dropped), format, mips);
    }

    private static byte[] SupportedBlocks(Gw2Texture texture, int mip)
    {
        var blocks = texture.DecodeGpuBlocks(mip);
        switch (texture.AtexFormat?.Id)
        {
            case Gw2AtexFormat.ThreeDcxId:
                Gw2AtexBlit.ThreeDcxToBc5x(blocks);
                return blocks;
            case Gw2AtexFormat.DxtaId:
                return Gw2AtexBlit.DxtaToDxt5(blocks);
            case Gw2AtexFormat.DxtnId:
                return Gw2AtexBlit.DxtnToDxt5(blocks, texture.Mips[mip].Width, texture.Mips[mip].Height);
            default:
                return blocks;
        }
    }

    private static int FullChainLength(int width, int height) => 1 + (int)Math.Floor(Math.Log2(Math.Max(width, height)));

    private static int AuthoredChainLength(Gw2Texture texture)
    {
        var full = FullChainLength(texture.Width, texture.Height);
        if (texture.Mips.Count == 0 || texture.Mips.Count > full) return 1;
        for (var mip = 0; mip < texture.Mips.Count; mip++)
            if (texture.Mips[mip].Width != Math.Max(1, texture.Width >> mip) || texture.Mips[mip].Height != Math.Max(1, texture.Height >> mip)) return 1;
        if (texture.Mips.Count == full) return full;
        return EndsAtBlockEdge(texture, texture.Mips.Count) ? texture.Mips.Count : 1;
    }

    private static bool EndsAtBlockEdge(Gw2Texture texture, int authoredCount) =>
        authoredCount == texture.Mips.Count && authoredCount < FullChainLength(texture.Width, texture.Height)
        && (texture.Mips[^1].Width <= BlockEdge || texture.Mips[^1].Height <= BlockEdge);

    private static ArraySegment<byte> RepeatedFirstBlock(ArraySegment<byte> source, SurfaceFormat format, int width, int height)
    {
        var blockBytes = BlockBytes(format, 1, 1);
        var bytes = new byte[BlockBytes(format, width, height)];
        for (var offset = 0; offset < bytes.Length; offset += blockBytes) source.Slice(0, blockBytes).CopyTo(bytes, offset);
        return new ArraySegment<byte>(bytes);
    }

    private static int BlockBytes(SurfaceFormat format, int width, int height) =>
        Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * (format == SurfaceFormat.Bc1Unorm ? 8 : 16);

    private static GrTextureData? PrepareRawDds(Gw2RawDds dds, byte[] bytes, int topMipsToDrop)
    {
        if (!dds.IsUncompressed || dds.BitCount != BgraBitCount || dds.RBitMask != BgraRedMask) return null;
        var expected = 1 + (int)Math.Floor(Math.Log2(Math.Max(dds.Width, dds.Height)));
        var mipCount = dds.MipCount == expected ? expected : 1;
        var dropped = DroppableTopMips(dds.Width, dds.Height, mipCount, topMipsToDrop);
        var mips = new ArraySegment<byte>[mipCount - dropped];
        var offset = DdsHeaderBytes;
        for (var mip = 0; mip < mipCount; mip++)
        {
            var length = Math.Max(1, dds.Width >> mip) * Math.Max(1, dds.Height >> mip) * 4;
            if (mip >= dropped) mips[mip - dropped] = new ArraySegment<byte>(bytes, offset, length);
            offset += length;
        }

        return new GrTextureData(dds.Width >> dropped, dds.Height >> dropped, SurfaceFormat.Bgra8Unorm, mips);
    }
}
