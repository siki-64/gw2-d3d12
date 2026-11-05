using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2MapBlockRecord(string Filename);

public sealed class Gw2MapBlock
{
    public Gw2MapBlock(IReadOnlyList<uint> blockDims, IReadOnlyList<Gw2MapBlockRecord> records)
    {
        BlockDims = blockDims;
        Records = records;
    }

    public IReadOnlyList<uint> BlockDims { get; }
    public IReadOnlyList<Gw2MapBlockRecord> Records { get; }

    public static Gw2MapBlock? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "PackMapBlock");
        if (root is null) return null;

        var records = new List<Gw2MapBlockRecord>();
        if (root.Fields.GetValueOrDefault("blockRecordArray") is object?[] recordObjects)
            foreach (var recordObject in recordObjects)
                if (recordObject is PackObject record)
                    records.Add(new(record.Fields.GetValueOrDefault("filename") as string ?? string.Empty));

        var dims = new List<uint>();
        if (root.Fields.GetValueOrDefault("blockDims") is object?[] dimObjects)
            foreach (var dim in dimObjects) dims.Add(PackValue.AsU32(dim));

        return new(dims, records);
    }
}
