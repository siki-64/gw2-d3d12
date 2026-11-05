using System;
using System.Collections.Generic;

namespace Gw2.Dat.Content;

public static class Gw2ModelAnimationResolver
{
    public static Gw2ModelAnimation Resolve(uint rootId, Func<uint, Gw2ModelAnimation?> readBank, int maxDepth = 8)
    {
        ArgumentNullException.ThrowIfNull(readBank);

        var root = TryRead(readBank, rootId) ?? new Gw2ModelAnimation([], [], [], [], [], []);
        var memo = new Dictionary<uint, Dictionary<ulong, Gw2ModelAnimationData>>();
        var merged = FullSet(rootId, root, readBank, maxDepth, memo, 0);

        var animations = new List<Gw2ModelAnimationData>(merged.Count);
        foreach (var animation in merged.Values) animations.Add(animation);
        return new Gw2ModelAnimation(animations, root.CompoundAnimations, root.Fallbacks, root.Imports,
            root.GrannyAnimation, root.GrannyPointers);
    }

    private static Gw2ModelAnimation? TryRead(Func<uint, Gw2ModelAnimation?> readBank, uint id)
    {
        try { return readBank(id); }
        catch (Exception) { return null; }
    }

    private static Dictionary<ulong, Gw2ModelAnimationData> FullSet(
        uint id,
        Gw2ModelAnimation bank,
        Func<uint, Gw2ModelAnimation?> readBank,
        int maxDepth,
        Dictionary<uint, Dictionary<ulong, Gw2ModelAnimationData>> memo,
        int depth)
    {
        if (memo.TryGetValue(id, out var cached)) return cached;

        var set = new Dictionary<ulong, Gw2ModelAnimationData>();
        memo[id] = set;
        foreach (var animation in bank.Animations) Add(set, animation);

        if (depth >= maxDepth) return set;

        foreach (var import in bank.Imports)
        {
            if (!import.Filename.TryGetFileId(out var sourceId) || sourceId == 0 || sourceId == id) continue;
            var source = TryRead(readBank, sourceId);
            if (source is null) continue;

            var sourceSet = FullSet(sourceId, source, readBank, maxDepth, memo, depth + 1);
            foreach (var sequence in import.Sequences)
                if (sourceSet.TryGetValue(sequence.Name, out var animation))
                    Add(set, animation);
        }

        return set;
    }

    private static void Add(Dictionary<ulong, Gw2ModelAnimationData> set, Gw2ModelAnimationData animation)
    {
        if (!set.TryGetValue(animation.Token, out var existing))
        {
            set[animation.Token] = animation;
            return;
        }

        var existingHasData = existing.GrannyAnimation is { Length: > 0 } || existing.IsStreamed;
        var candidateHasData = animation.GrannyAnimation is { Length: > 0 } || animation.IsStreamed;
        if (!existingHasData && candidateHasData) set[animation.Token] = animation;
    }
}
