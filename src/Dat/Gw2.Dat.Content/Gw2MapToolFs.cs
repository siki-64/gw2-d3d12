using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2ToolFsFile(string Filename, ulong Time, byte[] Data);

public sealed class Gw2MapToolFs
{
    public Gw2MapToolFs(IReadOnlyList<Gw2ToolFsFile> files)
    {
        Files = files;
    }

    public IReadOnlyList<Gw2ToolFsFile> Files { get; }

    public static Gw2MapToolFs? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "PackMapToolFs");
        if (root is null) return null;

        var files = new List<Gw2ToolFsFile>();
        if (root.Fields.GetValueOrDefault("filePtr") is object?[] fileObjects)
            foreach (var fileObject in fileObjects)
                if (fileObject is PackObject file)
                    files.Add(new(
                        file.Fields.GetValueOrDefault("filename") as string ?? string.Empty,
                        PackValue.AsU64(file.Fields.GetValueOrDefault("time")),
                        PackValue.AsBytes(file.Fields.GetValueOrDefault("dataPtr"))));

        return new(files);
    }
}
