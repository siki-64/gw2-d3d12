using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed class Gw2ModelIcon
{
    public Gw2ModelIcon(byte[] jpgData)
    {
        JpgData = jpgData;
    }

    public byte[] JpgData { get; }

    public static Gw2ModelIcon? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "ModelFileIcon");
        if (root is null) return null;

        return new(PackValue.AsBytes(root.Fields.GetValueOrDefault("jpgData")));
    }
}
