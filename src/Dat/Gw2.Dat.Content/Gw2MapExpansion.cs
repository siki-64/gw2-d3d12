using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2MapExpansionProperty(uint Type, ulong Value, string StringValue);

public sealed class Gw2MapExpansionProperties
{
    public Gw2MapExpansionProperties(IReadOnlyList<Gw2MapExpansionProperty> properties)
    {
        Properties = properties;
    }

    public const uint CombinedShadowSamplingToken = 0xB00910EB;

    public IReadOnlyList<Gw2MapExpansionProperty> Properties { get; }

    public bool CombinesShadowSamples => Properties.Any(property => property.Type == CombinedShadowSamplingToken && property.Value != 0);

    public static Gw2MapExpansionProperties? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "MapExpansionProperties");
        if (root is null) return null;

        var properties = new List<Gw2MapExpansionProperty>();
        if (root.Fields.GetValueOrDefault("properties") is object?[] propertyObjects)
            foreach (var propertyObject in propertyObjects)
                if (propertyObject is PackObject property)
                    properties.Add(new(
                        PackValue.AsU32(property.Fields.GetValueOrDefault("type")),
                        PackValue.AsU64(property.Fields.GetValueOrDefault("val")),
                        property.Fields.GetValueOrDefault("strVal") as string ?? string.Empty));

        return new(properties);
    }
}
