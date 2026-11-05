using System.Numerics;

namespace Gw2.Dat.Content;

public sealed class Gw2SkyCardTextureAnimator
{
    public const uint AnimatedCardFlag = 0x20;
    public const int UvSetCount = 4;

    private readonly Gw2MapEnvSkyCardAnimation _animation;
    private Vector2 _scaleSpeed;
    private Vector2 _scale = Vector2.One;
    private Vector2 _translation;
    private float _rotation;

    public Gw2SkyCardTextureAnimator(Gw2MapEnvSkyCardAnimation animation)
    {
        _animation = animation ?? throw new ArgumentNullException(nameof(animation));
        _scaleSpeed = animation.ScaleSpeed;
    }

    public static bool Animates(uint cardFlags, Gw2MapEnvSkyCardAnimation? animation) =>
        (cardFlags & AnimatedCardFlag) != 0 && animation is { TexCoords: not 0 };

    public bool AppliesTo(int uvSet) => (_animation.TexCoords & (1u << uvSet)) != 0;

    public (Vector4 RowA, Vector4 RowB) Transform { get; private set; } = (Vector4.UnitX, Vector4.UnitY);

    public void Advance(float elapsedSeconds)
    {
        var scale = _scale + _scaleSpeed * elapsedSeconds;
        if (scale.X < _animation.ScaleRangeX.X || scale.X > _animation.ScaleRangeX.Y) _scaleSpeed.X = -_scaleSpeed.X;
        if (scale.Y < _animation.ScaleRangeY.X || scale.Y > _animation.ScaleRangeY.Y) _scaleSpeed.Y = -_scaleSpeed.Y;
        _scale = new(
            Math.Max(Math.Min(scale.X, _animation.ScaleRangeX.Y), _animation.ScaleRangeX.X),
            Math.Max(Math.Min(scale.Y, _animation.ScaleRangeY.Y), _animation.ScaleRangeY.X));
        _translation = new(
            FractionOf(_translation.X + elapsedSeconds * _animation.Translation.X),
            FractionOf(_translation.Y + elapsedSeconds * _animation.Translation.Y));
        _rotation += _animation.Rotation * elapsedSeconds;
        var cosine = _animation.Rotation != 0f ? MathF.Cos(_rotation) : 1f;
        var sine = _animation.Rotation != 0f ? MathF.Sin(_rotation) : 0f;
        var scaledCosineX = _scale.X * cosine;
        var scaledSineY = _scale.Y * sine;
        var scaledCosineY = _scale.Y * cosine;
        var scaledSineX = _scale.X * sine;
        Transform = (
            new(scaledCosineX, -scaledSineY, 0f, _translation.X - 0.5f - (scaledCosineX - scaledSineY) * 0.5f + 0.5f),
            new(scaledSineX, scaledCosineY, 0f, _translation.Y - 0.5f - (scaledCosineY + scaledSineX) * 0.5f + 0.5f));
    }

    private static float FractionOf(float value) => value % 1f;
}
