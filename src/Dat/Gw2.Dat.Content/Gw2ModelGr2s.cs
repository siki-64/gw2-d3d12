using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2ModelGr2Data(string Filename, uint Flags, byte[] Data);

public sealed class Gw2ModelGr2s
{
    public Gw2ModelGr2s(IReadOnlyList<Gw2ModelGr2Data> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<Gw2ModelGr2Data> Entries { get; }

    public static Gw2ModelGr2s? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("ModelFileGr2s");
        if (root is null) return null;

        var entries = new List<Gw2ModelGr2Data>();
        if (root.Fields.GetValueOrDefault("gr2Data") is object?[] dataObjects)
            foreach (var dataObject in dataObjects)
                if (dataObject is PackObject data)
                    entries.Add(new(
                        data.Fields.GetValueOrDefault("filename") as string ?? string.Empty,
                        PackValue.AsU32(data.Fields.GetValueOrDefault("flags")),
                        PackValue.AsBytes(data.Fields.GetValueOrDefault("data"))));

        return new(entries);
    }
}
