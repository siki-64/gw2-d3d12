using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2MapVariant(string File, string Name, ulong Token, uint Flags);

public sealed class Gw2MapPackage
{
    public Gw2MapPackage(string baseFile, IReadOnlyList<Gw2MapVariant> variants, uint flags)
    {
        BaseFile = baseFile;
        Variants = variants;
        Flags = flags;
    }

    public string BaseFile { get; }
    public IReadOnlyList<Gw2MapVariant> Variants { get; }
    public uint Flags { get; }

    public static Gw2MapPackage? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "MapPackage");
        if (root is null) return null;

        var variants = new List<Gw2MapVariant>();
        if (root.Fields.GetValueOrDefault("variants") is object?[] variantObjects)
            foreach (var variantObject in variantObjects)
                if (variantObject is PackObject variant)
                    variants.Add(new(
                        variant.Fields.GetValueOrDefault("file") as string ?? string.Empty,
                        variant.Fields.GetValueOrDefault("name") as string ?? string.Empty,
                        PackValue.AsU64(variant.Fields.GetValueOrDefault("token")),
                        PackValue.AsU32(variant.Fields.GetValueOrDefault("flags"))));

        return new(
            root.Fields.GetValueOrDefault("baseFile") as string ?? string.Empty,
            variants,
            PackValue.AsU32(root.Fields.GetValueOrDefault("flags")));
    }
}
