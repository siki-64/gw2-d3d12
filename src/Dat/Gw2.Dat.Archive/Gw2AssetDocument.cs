using Gw2.Formats;
namespace Gw2.Dat.Archive;

public sealed record Gw2ParsedChunk(Gw2PackChunk Chunk, PackObject? Root, string? UnsupportedReason);

public sealed record Gw2AssetDocument(Gw2PackFile Pack, IReadOnlyList<Gw2ParsedChunk> Chunks)
{
    public bool IsFullyParsed => Chunks.All(c => c.Root is not null);

    public static Gw2AssetDocument Parse(ReadOnlyMemory<byte> bytes, Gw2PackSchemaCatalog? catalog = null, Gw2PackReadLimits? limits = null)
    {
        var pack = Gw2PackFile.Parse(bytes);
        if (catalog is not null && !ReferenceEquals(catalog, Gw2PackSchemaCatalog.Default))
            return ParseWithCatalog(pack, catalog, limits);
        Packfile? packfile = null;
        string? openFailure = null;
        try { packfile = Packfile.Open(bytes.Span); }
        catch (Exception exception) when (exception is NotSupportedException or InvalidDataException) { openFailure = exception.Message; }
        using (packfile)
        {
            var chunks = new List<Gw2ParsedChunk>();
            foreach (var chunk in pack.Chunks)
            {
                if (packfile is null) { chunks.Add(new(chunk, null, openFailure ?? "Not a packfile")); continue; }
                chunks.Add(ReadCurrentChunk(packfile, chunk));
            }
            return new(pack, chunks.AsReadOnly());
        }
    }

    private static Gw2ParsedChunk ReadCurrentChunk(Packfile packfile, Gw2PackChunk chunk)
    {
        try
        {
            return packfile.ChunkObject(PackfileSchema.FourCc(chunk.Type)) is { } root
                ? new(chunk, root, null)
                : new(chunk, null, $"Chunk {chunk.Type} v{chunk.Version} has no registered schema");
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidDataException) { return new(chunk, null, exception.Message); }
    }

    private static Gw2AssetDocument ParseWithCatalog(Gw2PackFile pack, Gw2PackSchemaCatalog catalog, Gw2PackReadLimits? limits)
    {
        var reader = new Gw2PackReader(catalog);
        var chunks = new List<Gw2ParsedChunk>();
        foreach (var chunk in pack.Chunks)
        {
            try { chunks.Add(new(chunk, reader.Read(pack, chunk, limits), null)); }
            catch (NotSupportedException ex) { chunks.Add(new(chunk, null, ex.Message)); }
            catch (Gw2PackReadLimitExceededException ex) { chunks.Add(new(chunk, null, ex.Message)); }
        }
        return new(pack, chunks.AsReadOnly());
    }

    public PackObject? FindRoot(string family) =>
        Chunks.Select(chunk => chunk.Root)
            .FirstOrDefault(root => root is not null && IsOfFamily(root.TypeName, family));

    public static bool IsOfFamily(string typeName, string family)
    {
        if (!typeName.StartsWith(family, StringComparison.Ordinal)) return false;
        var versionSuffix = typeName.AsSpan(family.Length);
        return versionSuffix.IsEmpty ||
            (versionSuffix.Length > 1 && versionSuffix[0] == 'V' && IsAllDigits(versionSuffix[1..]));
    }

    private static bool IsAllDigits(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
            if (character is < '0' or > '9') return false;
        return true;
    }

    public IEnumerable<PackObject> EnumerateObjects()
    {
        var seen = new HashSet<PackObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object?>();
        foreach (var chunk in Chunks) pending.Push(chunk.Root);
        while (pending.TryPop(out var value))
        {
            if (value is PackObject obj && seen.Add(obj))
            {
                yield return obj;
                foreach (var member in obj.Members) pending.Push(member.Value);
            }
            else if (value is object?[] array)
                foreach (var element in array) pending.Push(element);
        }
    }

    public IEnumerable<uint> EnumerateFileIds()
    {
        var ids = new HashSet<uint>();
        foreach (var obj in EnumerateObjects())
            foreach (var member in obj.Members)
                foreach (var id in References(member.Value))
                    if (ids.Add(id)) yield return id;

        static IEnumerable<uint> References(object? value)
        {
            if (value is PackFileReference reference && reference != default) yield return reference.FileId;
            else if (value is object?[] array)
                foreach (var item in array)
                    foreach (var id in References(item)) yield return id;
        }
    }

    public IEnumerable<PackObject> EnumerateModelEffects() => EnumerateObjects().Where(o =>
        o.TypeName.StartsWith("ModelParticle", StringComparison.Ordinal) ||
        o.TypeName.StartsWith("ModelCloud", StringComparison.Ordinal) ||
        o.TypeName.StartsWith("ModelStreak", StringComparison.Ordinal) ||
        o.TypeName.StartsWith("ModelLightning", StringComparison.Ordinal));
}
