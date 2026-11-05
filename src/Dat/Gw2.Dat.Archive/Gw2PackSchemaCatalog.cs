using System.Text.Json;
using Gw2.Formats;

namespace Gw2.Dat.Archive;

public sealed record Gw2PackFieldDefinition(string Name, int Kind, ulong Count, string? Type, IReadOnlyList<string>? Variants = null);
public sealed record Gw2PackTypeDefinition(string Id, string Name, IReadOnlyList<Gw2PackFieldDefinition> Fields);

public sealed class Gw2PackSchemaCatalog
{
    private readonly Dictionary<string, Gw2PackTypeDefinition> _types = [];
    private readonly Dictionary<(string Chunk, ushort Version), HashSet<string>> _roots = [];
    private static readonly Lazy<Gw2PackSchemaCatalog> BuiltIn = new(() => FromSchema(PackfileSchema.Default));
    private static Gw2PackSchemaCatalog? _configured;
    public static bool IsConfigured => _configured is not null;
    public static Gw2PackSchemaCatalog Default => _configured ?? BuiltIn.Value;
    public static void Configure(Gw2PackSchemaCatalog? catalog) => _configured = catalog;
    public string ExecutableSha256 { get; private init; } = "";
    public IEnumerable<Gw2PackTypeDefinition> Types => _types.Values;
    public Gw2PackTypeDefinition GetType(string id) => _types.TryGetValue(id, out var type)
        ? type : throw new NotSupportedException($"Unknown pack schema {id}");

    public IReadOnlyList<Gw2PackTypeDefinition> FindRoots(string chunk, ushort version) =>
        _roots.TryGetValue((chunk, version), out var roots)
            ? roots.Select(GetType).ToArray() : [];

    private static readonly (string Chunk, ushort Version, string TypeName)[] UnregisteredRoots =
    [
        ("scal", 0, "PackCompositeBoneScaleV20"),
    ];

    internal static Gw2PackSchemaCatalog FromSchema(PackfileSchema schema)
    {
        var result = new Gw2PackSchemaCatalog();
        var pending = new Stack<PackType>();
        foreach (var (chunkType, registration) in schema.Registrations)
        {
            string chunk = string.Create(4, chunkType, (characters, value) =>
            {
                for (int index = 0; index < 4; index++) characters[index] = (char)(value >> 8 * index & 0xFF);
            });
            foreach (var (version, type) in registration.Versions)
            {
                if (!result._roots.TryGetValue((chunk, version), out var roots)) result._roots[(chunk, version)] = roots = [];
                roots.Add(type.Id);
                pending.Push(type);
            }
        }
        while (pending.TryPop(out var type))
        {
            if (result._types.ContainsKey(type.Id)) continue;
            var fields = type.Fields.Select(field => new Gw2PackFieldDefinition(field.Name, (int)field.Kind, (ulong)field.Count, field.Reference?.Id,
                field.Kind == PackFieldKind.Variant ? Array.AsReadOnly(field.Variants.Select(member => member.Id).ToArray()) : null)).ToArray();
            result._types.Add(type.Id, new(type.Id, type.Name, Array.AsReadOnly(fields)));
            foreach (var field in type.Fields)
            {
                if (field.Reference is { } reference) pending.Push(reference);
                foreach (var member in field.Variants) pending.Push(member);
            }
        }
        return result;
    }

    public static Gw2PackSchemaCatalog Load(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.GetProperty("format").GetString() != "gw2-native-pack-schemas-v1")
            throw new InvalidDataException("Unrecognized pack schema catalog");
        var result = new Gw2PackSchemaCatalog { ExecutableSha256 = root.GetProperty("executableSha256").GetString()! };
        foreach (var type in root.GetProperty("types").EnumerateObject())
        {
            var fields = type.Value.GetProperty("fields").EnumerateArray().Select(field =>
                new Gw2PackFieldDefinition(field.GetProperty("name").GetString()!, field.GetProperty("kind").GetInt32(),
                    field.GetProperty("count").GetUInt64(), field.TryGetProperty("type", out var child) ? child.GetString() : null,
                    field.TryGetProperty("variants", out var variants) ? Array.AsReadOnly(variants.EnumerateArray().Select(member => member.GetString()!).ToArray()) : null)).ToArray();
            result._types.Add(type.Name, new(type.Name, type.Value.GetProperty("name").GetString()!, Array.AsReadOnly(fields)));
        }
        foreach (var registration in root.GetProperty("registrations").EnumerateArray())
        {
            var chunk = registration.GetProperty("chunk").GetString()!;
            foreach (var version in registration.GetProperty("versions").EnumerateObject())
            {
                var key = (chunk, ushort.Parse(version.Name, System.Globalization.CultureInfo.InvariantCulture));
                if (!result._roots.TryGetValue(key, out var roots)) result._roots[key] = roots = [];
                roots.Add(version.Value.GetString()!);
            }
        }
        foreach (var (chunk, version, typeName) in UnregisteredRoots)
            if (result._types.Values.FirstOrDefault(type => type.Name == typeName) is { } type && !result._roots.ContainsKey((chunk, version)))
                result._roots[(chunk, version)] = [type.Id];
        return result;
    }
}
