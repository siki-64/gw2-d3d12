using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2ModelFixedOffset(ulong Name, ulong ParentBone, Vector3 Translation);

public sealed record Gw2ModelProperty(ulong Id, uint Type, uint MergeIndex, float Time, ulong Value, string StringValue);

public sealed class Gw2ModelProperties
{
    public Gw2ModelProperties(IReadOnlyList<Gw2ModelFixedOffset> fixedOffsets, IReadOnlyList<Gw2ModelProperty> properties)
    {
        FixedOffsets = fixedOffsets;
        Properties = properties;
    }

    public IReadOnlyList<Gw2ModelFixedOffset> FixedOffsets { get; }
    public IReadOnlyList<Gw2ModelProperty> Properties { get; }

    public static Gw2ModelProperties? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "ModelFileProperties");
        if (root is null) return null;

        var offsets = new List<Gw2ModelFixedOffset>();
        if (root.Fields.GetValueOrDefault("fixedOffsetData") is object?[] offsetObjects)
            foreach (var offsetObject in offsetObjects)
                if (offsetObject is PackObject offset)
                    offsets.Add(new(
                        PackValue.AsU64(offset.Fields.GetValueOrDefault("name")),
                        PackValue.AsU64(offset.Fields.GetValueOrDefault("parentBone")),
                        Vec3(offset, "translation")));

        var properties = new List<Gw2ModelProperty>();
        if (root.Fields.GetValueOrDefault("properties") is object?[] propertyObjects)
            foreach (var propertyObject in propertyObjects)
                if (propertyObject is PackObject property)
                    properties.Add(new(
                        PackValue.AsU64(property.Fields.GetValueOrDefault("id")),
                        PackValue.AsU32(property.Fields.GetValueOrDefault("type")),
                        PackValue.AsU32(property.Fields.GetValueOrDefault("mergeIndex")),
                        PackValue.AsF32(property.Fields.GetValueOrDefault("time")),
                        PackValue.AsU64(property.Fields.GetValueOrDefault("val")),
                        property.Fields.GetValueOrDefault("strVal") as string ?? string.Empty));

        return new(offsets, properties);
    }

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}
