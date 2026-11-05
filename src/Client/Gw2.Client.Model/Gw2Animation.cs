using System.Numerics;

namespace Gw2.Client.Model;

public sealed record Gw2AnimationKey(float Time, Vector3 Position, Quaternion Orientation, Vector3 Scale);

public sealed record Gw2AnimationTrack(int BoneIndex, string Name, IReadOnlyList<Gw2AnimationKey> Keys);

public sealed class Gw2AnimationClip
{
    public ulong Token { get; init; }
    public string Name { get; init; } = string.Empty;
    public float Duration { get; init; }
    public float MoveSpeed { get; init; }
    public bool IsAdditive { get; init; }
    public IReadOnlyList<Gw2AnimationTrack> Tracks { get; init; } = Array.Empty<Gw2AnimationTrack>();
}

public sealed record Gw2AnimationPoseKey(Vector3 Position, Quaternion Orientation, Vector3 Scale);

public sealed class Gw2AnimationPose
{
    public IReadOnlyDictionary<string, Gw2AnimationPoseKey> Bones { get; init; } =
        new Dictionary<string, Gw2AnimationPoseKey>(StringComparer.Ordinal);
}

public sealed class Gw2ModelAnimations
{
    public IReadOnlyList<Gw2AnimationClip> Clips { get; init; } = Array.Empty<Gw2AnimationClip>();

    public Gw2AnimationPose? Rest { get; init; }
}
