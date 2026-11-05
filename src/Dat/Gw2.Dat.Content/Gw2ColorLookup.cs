namespace Gw2.Dat.Content;

public sealed class Gw2ColorLookup
{
    public const int Width = 256;
    public const int Height = 16;
    public const int SliceCount = 16;
    public const int EntryCount = Width * Height;

    private const int DdsMagic = 0x20534444;
    private const int DdsHeaderSize = 128;
    private const int DdsWidthOffset = 16;
    private const int DdsHeightOffset = 12;
    private const int DdsBitCountOffset = 88;
    private const int DdsRedMaskOffset = 92;
    private const int DdsBlueMaskOffset = 100;
    private const uint BgraRedMask = 0x00FF0000;
    private const uint BgraBlueMask = 0x000000FF;
    private const int BytesPerEntry = 4;
    private const int IdentityStep = 17;

    private Gw2ColorLookup(byte[] rgba) => Rgba = rgba;

    public byte[] Rgba { get; }

    public static Gw2ColorLookup Identity { get; } = CreateIdentity();

    public static Gw2ColorLookup? FromDds(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < DdsHeaderSize + EntryCount * BytesPerEntry || BitConverter.ToInt32(bytes, 0) != DdsMagic) return null;
        if (BitConverter.ToInt32(bytes, DdsWidthOffset) != Width || BitConverter.ToInt32(bytes, DdsHeightOffset) != Height) return null;
        if (BitConverter.ToInt32(bytes, DdsBitCountOffset) != BytesPerEntry * 8) return null;
        if (BitConverter.ToUInt32(bytes, DdsRedMaskOffset) != BgraRedMask || BitConverter.ToUInt32(bytes, DdsBlueMaskOffset) != BgraBlueMask) return null;
        var rgba = new byte[EntryCount * BytesPerEntry];
        for (var entry = 0; entry < EntryCount; entry++)
        {
            var source = DdsHeaderSize + entry * BytesPerEntry;
            var target = entry * BytesPerEntry;
            rgba[target] = bytes[source + 2];
            rgba[target + 1] = bytes[source + 1];
            rgba[target + 2] = bytes[source];
            rgba[target + 3] = bytes[source + 3];
        }
        return new(rgba);
    }

    public static Gw2ColorLookup Blend(IReadOnlyList<(Gw2ColorLookup Lookup, float Weight)> weightedLookups)
    {
        ArgumentNullException.ThrowIfNull(weightedLookups);
        var totalWeight = weightedLookups.Sum(weighted => weighted.Weight);
        if (weightedLookups.Count == 0 || totalWeight <= 0f) return Identity;
        if (weightedLookups.Count == 1) return weightedLookups[0].Lookup;
        var accumulated = new float[EntryCount * BytesPerEntry];
        foreach (var (lookup, weight) in weightedLookups)
        {
            var normalized = weight / totalWeight;
            for (var index = 0; index < accumulated.Length; index++) accumulated[index] += lookup.Rgba[index] * normalized;
        }
        return new(accumulated.Select(value => (byte)Math.Clamp(MathF.Round(value), 0f, 255f)).ToArray());
    }

    public Gw2ColorRgb Lookup(int red, int green, int blue)
    {
        var entry = (green * Width + blue * SliceCount + red) * BytesPerEntry;
        return new(Rgba[entry], Rgba[entry + 1], Rgba[entry + 2]);
    }

    private static Gw2ColorLookup CreateIdentity()
    {
        var rgba = new byte[EntryCount * BytesPerEntry];
        for (var green = 0; green < Height; green++)
        for (var blue = 0; blue < SliceCount; blue++)
        for (var red = 0; red < SliceCount; red++)
        {
            var entry = (green * Width + blue * SliceCount + red) * BytesPerEntry;
            rgba[entry] = (byte)(red * IdentityStep);
            rgba[entry + 1] = (byte)(green * IdentityStep);
            rgba[entry + 2] = (byte)(blue * IdentityStep);
            rgba[entry + 3] = byte.MaxValue;
        }
        return new(rgba);
    }
}

public readonly record struct Gw2ColorRgb(byte Red, byte Green, byte Blue);
