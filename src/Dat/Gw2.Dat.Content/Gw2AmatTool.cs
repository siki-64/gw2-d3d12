using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2AmatToolConstant(uint Token, string DisplayName, Vector4 DefaultValue, uint Flags, Vector4 MinValue, Vector4 MaxValue);

public sealed record Gw2AmatToolTexture(string TextureName, string DefaultFile, uint Flags);

public sealed class Gw2AmatTool
{
    public Gw2AmatTool(
        string description,
        uint flags,
        uint decompressedTextCount,
        IReadOnlyList<Gw2AmatToolConstant> constants,
        IReadOnlyList<Gw2AmatToolTexture> textures)
    {
        Description = description;
        Flags = flags;
        DecompressedTextCount = decompressedTextCount;
        Constants = constants;
        Textures = textures;
    }

    public string Description { get; }
    public uint Flags { get; }
    public uint DecompressedTextCount { get; }
    public IReadOnlyList<Gw2AmatToolConstant> Constants { get; }
    public IReadOnlyList<Gw2AmatToolTexture> Textures { get; }

    public static Gw2AmatTool? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("AmatToolParams");
        if (root is null) return null;

        var constants = new List<Gw2AmatToolConstant>();
        if (root.Fields.GetValueOrDefault("constants") is object?[] constantObjects)
            foreach (var constantObject in constantObjects)
                if (constantObject is PackObject constant)
                    constants.Add(new(
                        PackValue.AsU32(constant.Fields.GetValueOrDefault("token")),
                        constant.Fields.GetValueOrDefault("displayName") as string ?? string.Empty,
                        Vec4(constant, "defaultValue"),
                        PackValue.AsU32(constant.Fields.GetValueOrDefault("flags")),
                        Vec4(constant, "minValue"),
                        Vec4(constant, "maxValue")));

        var textures = new List<Gw2AmatToolTexture>();
        if (root.Fields.GetValueOrDefault("textures") is object?[] textureObjects)
            foreach (var textureObject in textureObjects)
                if (textureObject is PackObject texture)
                    textures.Add(new(
                        texture.Fields.GetValueOrDefault("texName") as string ?? string.Empty,
                        texture.Fields.GetValueOrDefault("texDefaultFile") as string ?? string.Empty,
                        PackValue.AsU32(texture.Fields.GetValueOrDefault("flags"))));

        return new(
            root.Fields.GetValueOrDefault("description") as string ?? string.Empty,
            PackValue.AsU32(root.Fields.GetValueOrDefault("flags")),
            PackValue.AsU32(root.Fields.GetValueOrDefault("decompressedTextCount")),
            constants,
            textures);
    }

    private static Vector4 Vec4(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 4
            ? new Vector4(values[0], values[1], values[2], values[3])
            : Vector4.Zero;
}
