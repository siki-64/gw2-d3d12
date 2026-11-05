using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Graphics;

public sealed class GrTextureTransformAnimation
{
    private readonly IReadOnlyList<Gw2ModelTextureTransformAnimator> _animators;
    private readonly Dictionary<uint, Vector4> _inputs = [];
    private float _advancedSeconds = float.NaN;

    private GrTextureTransformAnimation(IReadOnlyList<Gw2ModelTextureTransformAnimator> animators) => _animators = animators;

    public static GrTextureTransformAnimation? For(IEnumerable<Gw2ModelTextureTransform> transforms)
    {
        ArgumentNullException.ThrowIfNull(transforms);
        var animators = transforms.Where(Gw2ModelTextureTransformAnimator.Animates).Select(transform => new Gw2ModelTextureTransformAnimator(transform)).ToArray();
        return animators.Length == 0 ? null : new GrTextureTransformAnimation(animators);
    }

    public bool TryGetInput(uint token, float clockSeconds, out Vector4 value)
    {
        lock (_inputs)
        {
            if (clockSeconds != _advancedSeconds) AdvanceTo(clockSeconds);
            return _inputs.TryGetValue(token, out value);
        }
    }

    private void AdvanceTo(float clockSeconds)
    {
        _advancedSeconds = clockSeconds;
        _inputs.Clear();
        foreach (var animator in _animators)
        {
            animator.AdvanceTo(clockSeconds);
            var (rowA, rowB) = animator.Transform;
            _inputs[GrUiMaterialCombine.TexTransformRowTokens[animator.TextureInput * 2]] = rowA;
            _inputs[GrUiMaterialCombine.TexTransformRowTokens[animator.TextureInput * 2 + 1]] = rowB;
            if (animator.FlipbookBlend is { } blend) _inputs[Gw2ModelTextureTransformAnimator.FlipbookBlendToken] = blend;
        }
    }
}
