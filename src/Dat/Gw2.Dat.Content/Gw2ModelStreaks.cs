using Gw2.Formats;
using System.Collections;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2StreakAnchor(ulong BoneToken, uint ColorStart, uint ColorEnd, float Falloff, float LifetimeSeconds, uint Flags, float TexV);

public sealed record Gw2Streak(
    ulong BoneToken,
    uint Flags,
    int MaterialIndex,
    IReadOnlyList<int> AnchorIndices,
    float SpawnDistance,
    float TexScale,
    float Jitter,
    float Noise,
    float Wind,
    Vector3 Velocity,
    Vector3 Acceleration);

public sealed class Gw2ModelStreaks
{
    private Gw2ModelStreaks(IReadOnlyList<Gw2Streak> streaks, IReadOnlyList<Gw2StreakAnchor> anchors)
    {
        Streaks = streaks;
        Anchors = anchors;
    }

    public IReadOnlyList<Gw2Streak> Streaks { get; }
    public IReadOnlyList<Gw2StreakAnchor> Anchors { get; }
    public bool HasStreaks => Streaks.Count != 0 && Anchors.Count != 0;

    public static Gw2ModelStreaks Parse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var modelRoot = document.FindRoot("ModelFileData");
        if (modelRoot?.Fields.GetValueOrDefault("streakData") is not PackObject streakData) return new([], []);

        return new(
            Objects(streakData, "streaks").Select(ReadStreak).ToList(),
            Objects(streakData, "anchors").Select(ReadAnchor).ToList());
    }

    private static Gw2Streak ReadStreak(PackObject streak) => new(
        PackValue.AsU64(streak.Fields.GetValueOrDefault("bone")),
        PackValue.AsU32(streak.Fields.GetValueOrDefault("flags")),
        (int)PackValue.AsU32(streak.Fields.GetValueOrDefault("materialIndex")),
        Floats(streak.Fields.GetValueOrDefault("anchorIndices")).Select(value => (int)value).ToList(),
        F32(streak.Fields.GetValueOrDefault("spawnDist")),
        F32(streak.Fields.GetValueOrDefault("texScale")),
        F32(streak.Fields.GetValueOrDefault("jitter")),
        F32(streak.Fields.GetValueOrDefault("noise")),
        F32(streak.Fields.GetValueOrDefault("wind")),
        Vec3(streak.Fields.GetValueOrDefault("velocity")),
        Vec3(streak.Fields.GetValueOrDefault("acceleration")));

    private static Gw2StreakAnchor ReadAnchor(PackObject anchor) => new(
        PackValue.AsU64(anchor.Fields.GetValueOrDefault("bone")),
        PackValue.AsU32(anchor.Fields.GetValueOrDefault("colorStart")),
        PackValue.AsU32(anchor.Fields.GetValueOrDefault("colorEnd")),
        F32(anchor.Fields.GetValueOrDefault("falloff")),
        F32(anchor.Fields.GetValueOrDefault("lifetime")),
        PackValue.AsU32(anchor.Fields.GetValueOrDefault("flags")),
        F32(anchor.Fields.GetValueOrDefault("texV")));

    private static IEnumerable<PackObject> Objects(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is IEnumerable items and not string ? items.Cast<object?>().OfType<PackObject>() : [];

    private static IReadOnlyList<float> Floats(object? value) => value switch
    {
        float[] floats => floats,
        null or string => [],
        IEnumerable items => items.Cast<object?>().Select(F32).ToList(),
        _ => [],
    };

    private static Vector3 Vec3(object? value)
    {
        var floats = Floats(value);
        return floats.Count >= 3 ? new(floats[0], floats[1], floats[2]) : Vector3.Zero;
    }

    private static float F32(object? value) => value switch
    {
        float single => single,
        double wide => (float)wide,
        uint unsigned => unsigned,
        int signed => signed,
        ushort shortValue => shortValue,
        byte byteValue => byteValue,
        _ => 0,
    };
}
