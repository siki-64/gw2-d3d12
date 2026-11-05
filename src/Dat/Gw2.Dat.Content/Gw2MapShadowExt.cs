using Gw2.Formats;
using Gw2.Dat.Archive;

namespace Gw2.Dat.Content;

public sealed record Gw2MapShadowExt(PackFileReference File, uint Width, uint Height)
{
    public static Gw2MapShadowExt? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.FindRoot("PackMapShadowExt") is not { } root) return null;
        var dims = PackValue.AsU32List(root.Fields.GetValueOrDefault("shadowDims"));
        return new(
            root.Fields.GetValueOrDefault("filename") is PackFileReference file ? file : default,
            dims.Count > 0 ? dims[0] : 0,
            dims.Count > 1 ? dims[1] : 0);
    }
}
