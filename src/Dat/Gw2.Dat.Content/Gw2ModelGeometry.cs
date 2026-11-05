using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed class Gw2ModelGeometry
{
    public Gw2ModelGeometry(PackObject meshes)
    {
        Meshes = meshes;
    }

    public PackObject Meshes { get; }

    public static Gw2ModelGeometry? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("ModelFileGeometry");
        if (root is null) return null;

        return root.Fields.GetValueOrDefault("meshes") is PackObject meshes ? new(meshes) : null;
    }
}
