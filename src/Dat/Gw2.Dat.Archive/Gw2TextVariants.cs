using Gw2.Formats;
namespace Gw2.Dat.Archive;

public sealed record Gw2TextVariant(uint TextId, IReadOnlyList<uint> VariantTextIds);

public sealed class Gw2TextVariants
{
    public Gw2TextVariants(IReadOnlyList<Gw2TextVariant> variants)
    {
        Variants = variants;
    }

    public IReadOnlyList<Gw2TextVariant> Variants { get; }

    public static Gw2TextVariants? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "TextPackVariants");
        if (root is null) return null;

        var variants = new List<Gw2TextVariant>();
        if (root.Fields.GetValueOrDefault("variants") is object?[] variantObjects)
            foreach (var variantObject in variantObjects)
                if (variantObject is PackObject variant)
                {
                    var ids = new List<uint>();
                    if (variant.Fields.GetValueOrDefault("variantTextIds") is object?[] idObjects)
                        foreach (var id in idObjects) ids.Add(PackValue.AsU32(id));
                    variants.Add(new(PackValue.AsU32(variant.Fields.GetValueOrDefault("textId")), ids));
                }

        return new(variants);
    }
}
