using Gw2.Formats;
using Gw2.Dat.Archive;

namespace Gw2.Dat.Content;

public sealed class Gw2HighResolutionFiles
{
    private const uint RemapPropertyType = 0;
    private const int RemapPropertyLength = sizeof(uint);

    private readonly IReadOnlyDictionary<uint, uint> _highResolutionByBaseId;

    public Gw2HighResolutionFiles(IReadOnlyDictionary<uint, uint> highResolutionByBaseId)
    {
        _highResolutionByBaseId = highResolutionByBaseId;
    }

    public int Count => _highResolutionByBaseId.Count;

    public uint? HighResolutionOf(uint baseId) =>
        _highResolutionByBaseId.TryGetValue(baseId, out var highResolution) ? highResolution : null;

    public static Gw2HighResolutionFiles FromManifests(IEnumerable<Gw2AssetManifest> manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        var highResolutionByBaseId = new Dictionary<uint, uint>();
        foreach (var manifest in manifests)
            foreach (var row in manifest.PropertyTable)
                if (manifest.Properties[(int)row.PropertyIndex] is { Type: RemapPropertyType, Data.Length: RemapPropertyLength } property)
                    highResolutionByBaseId[row.BaseId] = BitConverter.ToUInt32(property.Data);
        return new(highResolutionByBaseId);
    }

    public static Gw2HighResolutionFiles Read(FileArchive local)
    {
        ArgumentNullException.ThrowIfNull(local);
        var root = Gw2ArchiveSync.FindRootManifest(local) ?? throw new InvalidDataException("No root asset manifest in the archive");
        return FromManifests(Gw2ArchiveSync.ReadManifests(local, root));
    }

    public static Gw2HighResolutionFiles ReadShared(string localDatPath)
    {
        using var file = new FileStream(localDatPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var local = FileArchive.Open(file, verifyChecksums: false);
        return Read(local);
    }

    public void Save(string path, uint sourceVersion)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        using (var writer = new BinaryWriter(File.Create(temporaryPath)))
        {
            writer.Write(sourceVersion);
            writer.Write(_highResolutionByBaseId.Count);
            foreach (var (baseId, highResolutionId) in _highResolutionByBaseId)
            {
                writer.Write(baseId);
                writer.Write(highResolutionId);
            }
        }
        File.Move(temporaryPath, path, overwrite: true);
    }

    public static Gw2HighResolutionFiles? TryLoad(string path, uint sourceVersion)
    {
        if (!File.Exists(path)) return null;
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadUInt32() != sourceVersion) return null;
        var count = reader.ReadInt32();
        var highResolutionByBaseId = new Dictionary<uint, uint>(count);
        for (var index = 0; index < count; index++) highResolutionByBaseId[reader.ReadUInt32()] = reader.ReadUInt32();
        return new(highResolutionByBaseId);
    }
}
