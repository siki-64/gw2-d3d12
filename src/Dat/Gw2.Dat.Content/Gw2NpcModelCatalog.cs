using System.Text.Json;

namespace Gw2.Dat.Content;

public sealed record Gw2NpcModelEntry(uint ModelId, string Name, string Race);

public sealed class Gw2NpcModelCatalog
{
    private readonly Dictionary<uint, Gw2NpcModelEntry> _byId;
    private readonly Dictionary<string, Gw2NpcModelEntry> _byName;

    private Gw2NpcModelCatalog(IReadOnlyList<Gw2NpcModelEntry> entries)
    {
        Entries = entries;
        _byId = new Dictionary<uint, Gw2NpcModelEntry>(entries.Count);
        _byName = new Dictionary<string, Gw2NpcModelEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            _byId[entry.ModelId] = entry;
            _byName[entry.Name] = entry;
        }
    }

    private static readonly Lazy<Gw2NpcModelCatalog> BuiltIn = new(() =>
    {
        using var stream = typeof(Gw2NpcModelCatalog).Assembly.GetManifestResourceStream("Gw2.Dat.gw2-npc-models.json")!;
        return Load(stream);
    });
    private static Gw2NpcModelCatalog? _configured;

    public static bool IsConfigured => _configured is not null;

    public static Gw2NpcModelCatalog Default => _configured ?? BuiltIn.Value;

    public static void Configure(Gw2NpcModelCatalog? catalog) => _configured = catalog;

    public IReadOnlyList<Gw2NpcModelEntry> Entries { get; }

    public bool TryGetById(uint id, out Gw2NpcModelEntry entry) => _byId.TryGetValue(id, out entry!);

    public bool TryResolve(string name, out Gw2NpcModelEntry entry) => _byName.TryGetValue(name, out entry!);

    public static Gw2NpcModelCatalog Load(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("npcs", out var npcs) ||
            npcs.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Unrecognized npc model catalog");

        var entries = new List<Gw2NpcModelEntry>();
        foreach (var element in npcs.EnumerateArray())
            entries.Add(new(
                element.GetProperty("modelId").GetUInt32(),
                element.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                element.TryGetProperty("race", out var race) ? race.GetString() ?? string.Empty : string.Empty));

        return new(entries);
    }
}
