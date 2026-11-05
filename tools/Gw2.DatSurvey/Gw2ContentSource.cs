using Gw2.Formats;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;

namespace Gw2.DatSurvey;

public interface IGw2ContentSource
{
    IReadOnlyDictionary<uint, uint> FileIndex { get; }
    FileArchiveIds Ids { get; }
    byte ArchiveVersion { get; }
    uint MftEntryCount { get; }

    void DecodeFileId(uint contentFileId, Stream destination);
    Gw2AssetDocument DecodeAsset(uint contentFileId);
}

public sealed class Gw2DatContentSource : IGw2ContentSource
{
    private readonly FileArchive _archive;
    private FileArchiveIds? _ids;

    public Gw2DatContentSource(FileArchive archive) =>
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));

    public IReadOnlyDictionary<uint, uint> FileIndex => _archive.FileIndex;
    public FileArchiveIds Ids => _ids ??= new FileArchiveIds(_archive.FileIndex);
    public byte ArchiveVersion => _archive.Version;
    public uint MftEntryCount => _archive.MftEntryCount;

    public void DecodeFileId(uint contentFileId, Stream destination) =>
        _archive.DecodeFileId(contentFileId, destination);

    public Gw2AssetDocument DecodeAsset(uint contentFileId)
    {
        using var decoded = new MemoryStream();
        _archive.DecodeFileId(contentFileId, decoded);
        return Gw2AssetDocument.Parse(decoded.ToArray());
    }
}
