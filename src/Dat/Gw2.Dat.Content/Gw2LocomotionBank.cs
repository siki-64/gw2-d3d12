namespace Gw2.Dat.Content;

public static class Gw2LocomotionBank
{
    public const uint EmoteTableFileId = 0x0002D1E3;

    private static readonly uint[] ExtraCandidateFileIds = [0x0019836, 0x0019840, 0x0019863];

    private static readonly string[] CreatureBoneMarkers =
        ["Tail", "Feather", "Beak", "Mush", "Tongue", "Belly", "Flap", "Wing", "Claw", "Pod", "Mordrem", "Ring", "Tusk", "Horn"];

    public static Gw2ModelAnimation? Read(Gw2ContentReader reader, Gw2ModelSkeleton? skeleton)
    {
        return Select(reader, skeleton,
            static kind => kind is Gw2LocomotionKind.Idle or Gw2LocomotionKind.Walk or Gw2LocomotionKind.Run or Gw2LocomotionKind.Swim,
            null);
    }

    public static bool HasLocomotion(Gw2ModelAnimation? bank)
    {
        if (bank is null) return false;
        foreach (var animation in bank.Animations)
            if (Gw2AnimationRoles.Classify(animation.Token) is Gw2LocomotionKind.Idle or Gw2LocomotionKind.Walk or Gw2LocomotionKind.Run or Gw2LocomotionKind.Swim)
                return true;
        return false;
    }

    public static bool HasWalkRun(Gw2ModelAnimation? bank)
    {
        if (bank is null) return false;
        var walk = false;
        var run = false;
        foreach (var animation in bank.Animations)
        {
            switch (Gw2AnimationRoles.Classify(animation.Token))
            {
                case Gw2LocomotionKind.Walk:
                    walk = true;
                    break;
                case Gw2LocomotionKind.Run:
                    run = true;
                    break;
            }
        }

        return walk && run;
    }

    public static bool HasAirborne(Gw2ModelAnimation? bank)
    {
        if (bank is null) return false;
        foreach (var animation in bank.Animations)
            if (Gw2AnimationRoles.Classify(animation.Token) is Gw2LocomotionKind.Jump or Gw2LocomotionKind.Fall or Gw2LocomotionKind.Land or Gw2LocomotionKind.Dodge)
                return true;
        return false;
    }

    public static Gw2ModelAnimation? ReadAcrobatics(Gw2ContentReader reader, Gw2ModelSkeleton? skeleton)
    {
        return Select(reader, skeleton,
            static kind => kind is Gw2LocomotionKind.Jump or Gw2LocomotionKind.Fall or Gw2LocomotionKind.Land or Gw2LocomotionKind.Dodge,
            static name => name.StartsWith("uam", StringComparison.Ordinal));
    }

    private static Gw2ModelAnimation? Select(Gw2ContentReader reader, Gw2ModelSkeleton? skeleton, Func<Gw2LocomotionKind, bool> include, Func<string, bool>? preferredRole)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (skeleton is null || skeleton.Bones.Count == 0) return null;

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bone in skeleton.Bones) names.Add(bone.Name);

        Gw2ModelAnimation? best = null;
        var bestScore = -1f;
        Gw2ModelAnimation? preferred = null;
        var preferredScore = -1f;
        Gw2ModelAnimation? fallback = null;
        var fallbackScore = -1f;

        foreach (var id in Candidates(reader))
        {
            var candidate = ReadCandidate(reader, id);
            if (candidate is null) continue;

            var considered = 0;
            var matched = 0;
            var creature = false;
            foreach (var (kind, tracks) in candidate.TracksByKind)
            {
                if (!include(kind)) continue;
                foreach (var (name, isCreature) in tracks)
                {
                    considered++;
                    if (names.Contains(name)) matched++;
                    else if (isCreature) creature = true;
                }
            }

            if (considered == 0) continue;
            var score = (float)matched / considered;

            if (score > fallbackScore)
            {
                fallbackScore = score;
                fallback = reader.ReadAnimationBank(id);
            }

            if (preferredRole is not null && candidate.Roles.Any(preferredRole) && matched > preferredScore)
            {
                preferredScore = matched;
                preferred = reader.ReadAnimationBank(id);
            }

            if (creature) continue;
            if (score > bestScore)
            {
                bestScore = score;
                best = reader.ReadAnimationBank(id);
            }
        }

        return preferred ?? best ?? fallback;
    }

    private static IEnumerable<uint> Candidates(Gw2ContentReader reader)
    {
        foreach (var id in ExtraCandidateFileIds)
            if (reader.Contains(id)) yield return id;

        if (!reader.Contains(EmoteTableFileId)) yield break;
        var emotes = Gw2EmoteAnimations.TryParse(reader.ReadDocument(EmoteTableFileId));
        if (emotes is null) yield break;
        foreach (var animation in emotes.Animations)
            if (animation.AnimationFileId != 0 && reader.Contains(animation.AnimationFileId))
                yield return animation.AnimationFileId;
    }

    private sealed record Candidate(
        IReadOnlyDictionary<Gw2LocomotionKind, List<(string Name, bool Creature)>> TracksByKind,
        List<string> Roles);

    private static readonly Dictionary<uint, Candidate> CandidatesCache = [];

    private static Candidate? ReadCandidate(Gw2ContentReader reader, uint fileId)
    {
        lock (CandidatesCache)
        {
            if (CandidatesCache.TryGetValue(fileId, out var cached)) return cached;
        }

        var bank = reader.ReadAnimationBank(fileId);
        if (bank is null) return null;

        var tracksByKind = new Dictionary<Gw2LocomotionKind, List<(string, bool)>>();
        var roles = new List<string>();
        foreach (var data in bank.Animations)
        {
            roles.Add(Gw2AnimationRoles.Name(data.Token));
            var kind = Gw2AnimationRoles.Classify(data.Token);
            if (kind == Gw2LocomotionKind.None) continue;
            var loaded = reader.ReadStreamedAnimation(data);
            var granny = Gw2GrannyAnimation.TryParse(loaded.GrannyAnimation, loaded.PointerSize);
            if (granny is null) continue;

            if (!tracksByKind.TryGetValue(kind, out var tracks))
                tracksByKind[kind] = tracks = [];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var track in granny.TrackGroups.SelectMany(group => group.TransformTracks))
            {
                if (!seen.Add(track.Name)) continue;
                tracks.Add((track.Name, IsCreature(track.Name)));
            }
        }

        var candidate = new Candidate(tracksByKind, roles);
        lock (CandidatesCache)
        {
            CandidatesCache[fileId] = candidate;
        }

        return candidate;
    }

    private static bool IsCreature(string name)
    {
        foreach (var marker in CreatureBoneMarkers)
            if (name.Contains(marker, StringComparison.Ordinal)) return true;
        return false;
    }
}
