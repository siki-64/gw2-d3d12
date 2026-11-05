using System;
using System.Collections.Generic;

namespace Gw2.Dat.Content;

public static class Gw2RaceAnimations
{
    public const uint CompositeFileId = 154681;

    private static readonly Dictionary<string, Gw2ModelAnimation?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Gw2ModelAnimation? Read(Gw2ContentReader reader, string race) =>
        Read(reader, CompositeFileId, race);

    public static Gw2ModelAnimation? Read(Gw2ContentReader reader, uint compositeFileId, string race)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (string.IsNullOrEmpty(race)) return null;

        var key = $"{compositeFileId:X}:{race}";
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }

        var resolved = Resolve(reader, compositeFileId, race);
        lock (Cache)
        {
            Cache[key] = resolved;
        }

        return resolved;
    }

    private static Gw2ModelAnimation? Resolve(Gw2ContentReader reader, uint compositeFileId, string race)
    {
        if (!reader.Contains(compositeFileId)) return null;
        var composite = reader.ReadComposite(compositeFileId);
        if (composite is null) return null;

        var entry = FindRace(composite, race);
        if (entry is null || !entry.SkeletonFile.TryGetFileId(out var skeletonId) || skeletonId == 0) return null;
        return reader.ReadModelAnimations(skeletonId);
    }

    private static Gw2CompositeRace? FindRace(Gw2Composite composite, string race)
    {
        foreach (var candidate in new[] { race + "Male", race })
            foreach (var entry in composite.Races)
                if (string.Equals(entry.Name, candidate, StringComparison.OrdinalIgnoreCase))
                    return entry;
        return null;
    }
}
