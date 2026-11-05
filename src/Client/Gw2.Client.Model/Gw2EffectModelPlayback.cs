using System.Numerics;
using Gw2.Dat.Content;
using Gw2.Formats;

namespace Gw2.Client.Model;

public sealed record Gw2EffectModelEmitter(Gw2ParticleSimulator Simulator, int CloudIndex, int BoneIndex)
{
    public Matrix4x4 World { get; internal set; } = Matrix4x4.Identity;
}

public sealed record Gw2EffectModelStreak(Gw2Streak Streak, IReadOnlyList<Gw2StreakAnchor> Anchors, int[] AnchorBoneIndices, Gw2StreakSimulator Simulator)
{
    internal Vector3[] AnchorPositions { get; } = new Vector3[AnchorBoneIndices.Length];
}

public sealed record Gw2EffectModelClip(Gw2ModelAnimationData Data, Gw2AnimationClip? Clip, Gw2GrannyAnimation? Granny)
{
    public float Duration => Clip?.Duration ?? Granny?.Duration ?? 0f;
}

public sealed class Gw2EffectModelPlayback
{
    private static readonly ulong ZeroPoseToken = Token64.Encode("zeropose");

    private readonly Gw2ModelSkeleton? _skeleton;
    private readonly Gw2EffectModelClip? _zeroPose;
    private readonly Gw2EffectModelClip? _start;
    private readonly Gw2EffectModelClip? _loop;
    private readonly Gw2EffectModelClip? _end;
    private readonly Gw2EffectModelEmitter[] _emitters;
    private readonly Gw2EffectModelStreak[] _streaks;
    private readonly Matrix4x4[] _identityBones = [Matrix4x4.Identity];
    private Matrix4x4[] _bones;

    public Gw2EffectModelPlayback(Gw2EffectModelAsset asset, ulong animationToken, Random random)
    {
        ArgumentNullException.ThrowIfNull(asset);
        _skeleton = asset.Skeleton is { Bones.Count: > 0 } skeleton ? skeleton : null;
        _bones = _identityBones;
        var animations = asset.Animations?.Animations ?? [];
        var compound = asset.Animations?.CompoundAnimations.FirstOrDefault(candidate => candidate.Token == animationToken && animationToken != 0);
        _zeroPose = ClipOf(animations, ZeroPoseToken);
        _start = ClipOf(animations, compound?.Start ?? animationToken);
        _loop = compound is null ? null : ClipOf(animations, compound.Loop);
        _end = compound is null ? null : ClipOf(animations, compound.End);
        _emitters = [.. asset.Particles.Clouds.SelectMany((cloud, cloudIndex) => cloud.EmitterIndices
            .Where(index => index >= 0 && index < asset.Particles.Emitters.Count)
            .Select(index => asset.Particles.Emitters[index])
            .Select(emitter => new Gw2EffectModelEmitter(new Gw2ParticleSimulator(emitter, random), cloudIndex, BoneIndexOf(emitter.BoneToken))))];
        _streaks = [.. asset.Streaks.Streaks.Select(streak => StreakOf(streak, asset.Streaks.Anchors)).OfType<Gw2EffectModelStreak>()];
    }

    public IReadOnlyList<Gw2EffectModelStreak> Streaks => _streaks;

    public IReadOnlyList<Gw2EffectModelEmitter> Emitters => _emitters;

    public Gw2EffectModelClip? CurrentClip { get; private set; }

    public float CurrentClipSeconds { get; private set; }

    public Matrix4x4 ModelWorld { get; private set; } = Matrix4x4.Identity;

    public float? FinishingSeconds { get; private set; }

    public void Advance(float deltaSeconds, float? eventAnimationSeconds, Matrix4x4 modelWorld, bool isFinishing = false)
    {
        ModelWorld = modelWorld;
        FinishingSeconds = isFinishing ? (FinishingSeconds ?? -deltaSeconds) + deltaSeconds : null;
        (CurrentClip, CurrentClipSeconds) = FinishingSeconds is { } finishing && _end is { } end && eventAnimationSeconds is not null
            ? (end, Math.Min(finishing, end.Duration))
            : ClipAt(eventAnimationSeconds);
        _bones = _skeleton is null ? _identityBones : Gw2SkeletonPose.WorldMatricesAt(_skeleton, CurrentClip?.Clip, CurrentClipSeconds);
        foreach (var emitter in _emitters)
        {
            var bone = emitter.BoneIndex >= 0 && emitter.BoneIndex < _bones.Length ? _bones[emitter.BoneIndex] : Matrix4x4.Identity;
            emitter.World = bone * modelWorld;
            var visible = IsBoneVisible(CurrentClip?.Data, emitter.Simulator.Emitter.BoneToken, CurrentClipSeconds);
            emitter.Simulator.Advance(deltaSeconds, visible, emitter.Simulator.IsWorldSpace ? emitter.World : Matrix4x4.Identity);
        }
        foreach (var streak in _streaks)
        {
            for (var anchor = 0; anchor < streak.AnchorBoneIndices.Length; anchor++)
            {
                var bone = streak.AnchorBoneIndices[anchor];
                var boneWorld = bone >= 0 && bone < _bones.Length ? _bones[bone] : Matrix4x4.Identity;
                streak.AnchorPositions[anchor] = Vector3.Transform(boneWorld.Translation, modelWorld);
            }
            var spawning = eventAnimationSeconds is not null && IsBoneVisible(CurrentClip.Data, streak.Streak.BoneToken, CurrentClipSeconds);
            streak.Simulator.Advance(deltaSeconds, spawning, streak.AnchorPositions);
        }
    }

    public bool IsMeshVisible(Gw2Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        return mesh.VisBone == 0 || IsBoneVisible(CurrentClip?.Data, unchecked((ulong)mesh.VisBone), CurrentClipSeconds);
    }

    public Matrix4x4 MeshWorld(Gw2Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (_skeleton is null || mesh.BoneBindings.Count == 0) return ModelWorld;
        var bone = _skeleton.FindBoneIndex(unchecked((ulong)mesh.BoneBindings[0]));
        if (bone < 0 || bone >= _bones.Length) return ModelWorld;
        return _skeleton.Bones[bone].InverseWorld * _bones[bone] * ModelWorld;
    }

    public IEnumerable<(uint Token, Vector4 Value)> MaterialConstants(uint materialId)
    {
        if (CurrentClip is not { } clip) yield break;
        foreach (var constant in clip.Data.MaterialConstantAnimations)
            if (constant.MaterialId == materialId)
                yield return (constant.ConstantToken, Gw2ModelMaterialConstantSampler.ValueAt(clip.Granny, constant, CurrentClipSeconds));
    }

    public IEnumerable<(uint Token, Vector4 Value)> TextureTransformRows(Gw2Material material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (CurrentClip is not { } clip) yield break;
        foreach (var uvAnimation in clip.Data.UvAnimations)
            foreach (var texture in material.Textures)
            {
                if (texture.UvAnimId != uvAnimation.UvAnimId || texture.UvPSInputIndex is < 0 or >= Gw2ModelUvAnimationSampler.TextureInputCount) continue;
                var (rowA, rowB) = Gw2ModelUvAnimationSampler.TransformAt(clip.Granny, uvAnimation, CurrentClipSeconds);
                yield return (Gw2ModelUvAnimationSampler.TexTransformRowTokens[texture.UvPSInputIndex * 2], rowA);
                yield return (Gw2ModelUvAnimationSampler.TexTransformRowTokens[texture.UvPSInputIndex * 2 + 1], rowB);
            }
    }

    public static bool IsBoneVisible(Gw2ModelAnimationData? animation, ulong boneToken, float seconds)
    {
        if (animation is null) return true;
        foreach (var track in animation.VisibilityTracksOrEmpty)
            if (track.BoneToken == boneToken) return track.IsVisibleAt(seconds);
        return true;
    }

    private (Gw2EffectModelClip? Clip, float Seconds) ClipAt(float? eventAnimationSeconds)
    {
        if (eventAnimationSeconds is not { } seconds || _start is null) return (_zeroPose, 0f);
        seconds = Math.Max(seconds, 0f);
        if (seconds < _start.Duration || _loop is not { Duration: > 0f } loop) return (_start, Math.Min(seconds, _start.Duration));
        return (loop, (seconds - _start.Duration) % loop.Duration);
    }

    private Gw2EffectModelClip? ClipOf(IReadOnlyList<Gw2ModelAnimationData> animations, ulong token)
    {
        if (token == 0 || animations.FirstOrDefault(animation => animation.Token == token) is not { } data) return null;
        return new Gw2EffectModelClip(data, Gw2ModelAnimationBuilder.BuildClip(data, _skeleton), Gw2GrannyAnimation.TryParse(data.GrannyAnimation, data.PointerSize));
    }

    private Gw2EffectModelStreak? StreakOf(Gw2Streak streak, IReadOnlyList<Gw2StreakAnchor> anchors)
    {
        var streakAnchors = streak.AnchorIndices.Where(index => index >= 0 && index < anchors.Count).Select(index => anchors[index]).ToList();
        if (streakAnchors.Count < 2) return null;
        var lifetime = streakAnchors.Max(anchor => anchor.LifetimeSeconds);
        return new Gw2EffectModelStreak(streak, streakAnchors, [.. streakAnchors.Select(anchor => BoneIndexOf(anchor.BoneToken))],
            new Gw2StreakSimulator(streak.SpawnDistance, streak.TexScale, lifetime));
    }

    private int BoneIndexOf(ulong boneToken)
    {
        if (_skeleton is null || boneToken == 0) return -1;
        for (var index = 0; index < _skeleton.Bones.Count; index++)
            if (Token64.EncodeAfterColon(_skeleton.Bones[index].Name) == boneToken) return index;
        return -1;
    }
}
