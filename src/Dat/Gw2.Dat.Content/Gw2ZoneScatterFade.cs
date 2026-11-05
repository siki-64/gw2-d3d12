using System.Numerics;

namespace Gw2.Dat.Content;

public sealed class Gw2ZoneScatterFade
{
    public const float FadeBand = 500f;
    public const float BlockSize = Gw2ZoneScatter.CellSize * Gw2ZoneScatter.CellsPerBlock;
    public const float FadeOpaque = 1f;
    public const float FadeHidden = 0f;
    private const float UnsetFade = -1f;

    private readonly float _hideDistanceSquared;
    private readonly float _showDistanceSquared;
    private readonly bool _cutsHard;
    private bool _fadingOut;
    private bool _fadingIn;

    public Gw2ZoneScatterFade(Vector2 blockCenter, float fadeDistance, bool cutsHard)
    {
        BlockCenter = blockCenter;
        _hideDistanceSquared = fadeDistance * fadeDistance;
        _showDistanceSquared = (fadeDistance - FadeBand) * (fadeDistance - FadeBand);
        _cutsHard = cutsHard;
    }

    public Vector2 BlockCenter { get; }

    public float Value { get; private set; } = UnsetFade;

    public bool IsVisible => Value > FadeHidden;

    public bool IsFading => Value is > FadeHidden and < FadeOpaque;

    public static Vector2 CenterOf(Gw2ZoneScatterBlock block, Vector2 terrainOrigin) =>
        terrainOrigin + new Vector2(block.X * BlockSize + BlockSize / 2f, block.Y * BlockSize + BlockSize / 2f);

    public static bool CutsHard(uint layerFlags) => (layerFlags & Gw2ZoneScatter.LayerFlagRandomAlpha) != 0;

    public void Advance(Vector3 eye, float elapsedSeconds)
    {
        Step(elapsedSeconds);
        Test(Vector2.DistanceSquared(new Vector2(eye.X, eye.Y), BlockCenter));
    }

    private void Step(float amount)
    {
        if (!_fadingOut && !_fadingIn) return;
        var target = _fadingIn ? FadeOpaque : FadeHidden;
        var remaining = target - Value;
        Value = Math.Clamp(Value + MathF.Sign(remaining) * MathF.Min(amount, MathF.Abs(remaining)), FadeHidden, FadeOpaque);
        if (Value >= FadeOpaque) _fadingIn = false;
        if (Value <= FadeHidden) _fadingOut = false;
    }

    private void Test(float distanceSquared)
    {
        if (_cutsHard)
        {
            Value = distanceSquared > _hideDistanceSquared ? FadeHidden : FadeOpaque;
            return;
        }

        if (distanceSquared > _showDistanceSquared || Value == FadeOpaque)
        {
            if (distanceSquared >= _hideDistanceSquared && Value != FadeHidden) (_fadingOut, _fadingIn) = (true, false);
            return;
        }

        (_fadingOut, _fadingIn) = (false, true);
    }
}
