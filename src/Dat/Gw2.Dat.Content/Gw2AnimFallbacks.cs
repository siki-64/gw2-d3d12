using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2AnimFallback(ulong SourceAnim, IReadOnlyList<ulong> TargetAnims);

public sealed class Gw2AnimFallbacks
{
    public Gw2AnimFallbacks(IReadOnlyList<Gw2AnimFallback> fallbacks)
    {
        Fallbacks = fallbacks;
    }

    public IReadOnlyList<Gw2AnimFallback> Fallbacks { get; }

    public static Gw2AnimFallbacks? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackAnimFallbacks");
        if (root is null) return null;

        var fallbacks = new List<Gw2AnimFallback>();
        if (root.Fields.GetValueOrDefault("fallbacks") is object?[] fallbackObjects)
            foreach (var fallbackObject in fallbackObjects)
                if (fallbackObject is PackObject fallback)
                {
                    var targets = new List<ulong>();
                    if (fallback.Fields.GetValueOrDefault("targetAnims") is object?[] targetObjects)
                        foreach (var target in targetObjects) targets.Add(PackValue.AsU64(target));
                    fallbacks.Add(new(PackValue.AsU64(fallback.Fields.GetValueOrDefault("sourceAnim")), targets));
                }

        return new(fallbacks);
    }
}
