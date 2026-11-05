using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed class Gw2AmatGr
{
    public Gw2AmatGr(int textureCount, IReadOnlyList<ulong> textureTokens, uint flags, uint debugFlags, byte sortOrder)
    {
        TextureCount = textureCount;
        TextureTokens = textureTokens;
        Flags = flags;
        DebugFlags = debugFlags;
        SortOrder = sortOrder;
    }

    public int TextureCount { get; }
    public IReadOnlyList<ulong> TextureTokens { get; }
    public uint Flags { get; }
    public uint DebugFlags { get; }
    public byte SortOrder { get; }
    public byte TextureArrayRange { get; init; }
    public byte SortTriangles { get; init; }
    public byte ProceduralAnimation { get; init; }

    public static Gw2AmatGr? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("AmatGr");
        if (root is null) return null;

        var tokens = new List<ulong>();
        if (root.Fields.GetValueOrDefault("texTokens") is object?[] values)
            foreach (var value in values)
                tokens.Add(value is ulong token ? token : 0);

        return new(
            PackValue.AsU8(root.Fields.GetValueOrDefault("texCount")),
            tokens,
            PackValue.AsU32(root.Fields.GetValueOrDefault("flags")),
            PackValue.AsU32(root.Fields.GetValueOrDefault("debugFlags")),
            PackValue.AsU8(root.Fields.GetValueOrDefault("sortOrder")))
        {
            TextureArrayRange = PackValue.AsU8(root.Fields.GetValueOrDefault("texArrayRange")),
            SortTriangles = PackValue.AsU8(root.Fields.GetValueOrDefault("sortTri")),
            ProceduralAnimation = PackValue.AsU8(root.Fields.GetValueOrDefault("procAnim")),
        };
    }
}
