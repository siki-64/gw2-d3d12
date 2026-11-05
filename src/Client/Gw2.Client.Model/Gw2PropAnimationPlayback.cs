using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Model;

public sealed class Gw2PropAnimationPlayback
{
    private readonly Gw2EffectModelClip _start;
    private readonly Gw2EffectModelClip? _loop;

    private Gw2PropAnimationPlayback(Gw2EffectModelClip start, Gw2EffectModelClip? loop)
    {
        _start = start;
        _loop = loop;
    }

    public static Gw2PropAnimationPlayback? For(Gw2ModelAnimation? bank, ulong animationToken)
    {
        if (bank is null || animationToken == 0) return null;
        var compound = bank.CompoundAnimations.FirstOrDefault(candidate => candidate.Token == animationToken);
        var start = ClipOf(bank, compound?.Start ?? animationToken);
        if (start is null) return null;
        var loop = compound is null ? null : ClipOf(bank, compound.Loop);
        var playback = new Gw2PropAnimationPlayback(start, loop);
        return playback.Animates ? playback : null;
    }

    public bool Animates => ChangesSurface(_start) || (_loop is { } loop && ChangesSurface(loop));

    public (Gw2EffectModelClip Clip, float Seconds) ClipAt(float seconds, bool loops = true)
    {
        seconds = Math.Max(seconds, 0f);
        if (!loops)
        {
            if (seconds < _start.Duration || _loop is null) return (_start, Math.Min(seconds, _start.Duration));
            return (_loop, Math.Min(seconds - _start.Duration, _loop.Duration));
        }
        if (_loop is { Duration: > 0f } loop)
            return seconds < _start.Duration ? (_start, seconds) : (loop, (seconds - _start.Duration) % loop.Duration);
        return (_start, _start.Duration > 0f ? seconds % _start.Duration : 0f);
    }

    public void WriteSurface(Gw2Material material, IDictionary<uint, Vector4> constants, float seconds, bool loops = true)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(constants);
        var (clip, clipSeconds) = ClipAt(seconds, loops);
        foreach (var constant in clip.Data.MaterialConstantAnimations)
            if (constant.MaterialId == material.MaterialId)
                constants[constant.ConstantToken] = Gw2ModelMaterialConstantSampler.ValueAt(clip.Granny, constant, clipSeconds);
        foreach (var uvAnimation in clip.Data.UvAnimations)
            foreach (var texture in material.Textures)
            {
                if (texture.UvAnimId != uvAnimation.UvAnimId || texture.UvPSInputIndex is < 0 or >= Gw2ModelUvAnimationSampler.TextureInputCount) continue;
                var (rowA, rowB) = Gw2ModelUvAnimationSampler.TransformAt(clip.Granny, uvAnimation, clipSeconds);
                constants[Gw2ModelUvAnimationSampler.TexTransformRowTokens[texture.UvPSInputIndex * 2]] = rowA;
                constants[Gw2ModelUvAnimationSampler.TexTransformRowTokens[texture.UvPSInputIndex * 2 + 1]] = rowB;
            }
    }

    private static bool ChangesSurface(Gw2EffectModelClip clip) =>
        clip.Data.UvAnimations.Count > 0 || clip.Data.MaterialConstantAnimations.Count > 0;

    private static Gw2EffectModelClip? ClipOf(Gw2ModelAnimation bank, ulong token)
    {
        if (token == 0 || bank.Animations.FirstOrDefault(animation => animation.Token == token) is not { } data) return null;
        return new Gw2EffectModelClip(data, null, Gw2GrannyAnimation.TryParse(data.GrannyAnimation, data.PointerSize));
    }
}
