using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed class Gw2MapSound
{
    public Gw2MapSound(byte[] data, IReadOnlyList<string> files)
    {
        Data = data;
        Files = files;
    }

    public byte[] Data { get; }
    public IReadOnlyList<string> Files { get; }

    public static Gw2MapSound? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "MapLegacy");
        if (root is null) return null;

        var files = new List<string>();
        if (root.Fields.GetValueOrDefault("files") is object?[] fileObjects)
            foreach (var fileObject in fileObjects)
                if (fileObject is string name)
                    files.Add(name);

        return new(PackValue.AsBytes(root.Fields.GetValueOrDefault("data")), files);
    }
}
