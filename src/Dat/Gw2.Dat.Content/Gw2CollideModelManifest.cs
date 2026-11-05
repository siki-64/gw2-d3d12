using Gw2.Formats;
using Gw2.Dat.Archive;

namespace Gw2.Dat.Content;

public sealed record Gw2CollideModelManifestFile(string ModelFileStr, uint ModelFileId, uint CollisionFileId, IReadOnlyList<float> Scales);

public sealed class Gw2CollideModelManifest
{
    public Gw2CollideModelManifest(IReadOnlyList<Gw2CollideModelManifestFile> files) => Files = files;

    public IReadOnlyList<Gw2CollideModelManifestFile> Files { get; }

    public static Gw2CollideModelManifest? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "CollideModelManifest");
        if (root is null) return null;

        var files = new List<Gw2CollideModelManifestFile>();
        if (root.Fields.GetValueOrDefault("files") is object?[] fileObjects)
            foreach (var fileObject in fileObjects)
                if (fileObject is PackObject file)
                    files.Add(new(
                        file.Fields.GetValueOrDefault("modelFileStr") as string ?? string.Empty,
                        FileId(file, "modelFile"),
                        FileId(file, "collisionFile"),
                        file.Fields.GetValueOrDefault("scales") is object?[] scales ? scales.Select(PackValue.AsF32).ToArray() : []));

        return new(files);
    }

    private static uint FileId(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is PackFileReference reference && reference.TryGetFileId(out var fileId) ? fileId : 0;
}
