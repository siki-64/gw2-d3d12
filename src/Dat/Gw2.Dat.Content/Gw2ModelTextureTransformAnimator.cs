using System.Numerics;
using Gw2.Dat.Identity;

namespace Gw2.Dat.Content;

public sealed class Gw2ModelTextureTransformAnimator
{
    public const uint RandomStartFrameFlag = 0x1;
    public const uint FlipbookBlendFlag = 0x2;
    public const uint BounceModeFlag = 0x4;
    public const uint BounceScaleFlag = 0x8;
    public const int TextureInputCount = 4;
    public static readonly uint FlipbookBlendToken = Gw2NameHash.Compute("grsfbmv");

    private readonly Gw2ModelTextureTransform _transform;
    private readonly uint _startFrame;
    private Vector2 _bounceScale = Vector2.One;
    private Vector2 _bounceScaleSpeed;
    private float? _lastSeconds;

    public Gw2ModelTextureTransformAnimator(Gw2ModelTextureTransform transform, uint startFrame = 0)
    {
        _transform = transform ?? throw new ArgumentNullException(nameof(transform));
        _startFrame = transform.Count > 1 ? startFrame % transform.Count : 0;
        _bounceScaleSpeed = transform.Scale;
    }

    public static bool Animates(Gw2ModelTextureTransform transform) =>
        transform.Columns != 0 && transform.UvIndex < TextureInputCount
        && (transform.Fps != 0f || transform.Scroll != Vector2.Zero || transform.Scale != Vector2.Zero || transform.Rotate != 0f);

    public int TextureInput => _transform.UvIndex;

    public (Vector4 RowA, Vector4 RowB) Transform { get; private set; } = (Vector4.UnitX, Vector4.UnitY);

    public Vector4? FlipbookBlend { get; private set; }

    public void AdvanceTo(float seconds)
    {
        var elapsedSeconds = _lastSeconds is { } last ? Math.Max(seconds - last, 0f) : 0f;
        _lastSeconds = seconds;
        if ((_transform.Flags & BounceModeFlag) != 0) AdvanceBounce(seconds, elapsedSeconds);
        else AdvanceWave(seconds);
    }

    private void AdvanceWave(float seconds)
    {
        var offset = Vector2.Zero;
        var scale = Vector2.One;
        FlipbookBlend = null;
        if (_transform.Fps != 0f && _transform.Count != 0)
        {
            var frames = _transform.Fps * seconds;
            var frame = (uint)((_startFrame + (long)MathF.Floor(frames)) % _transform.Count);
            offset = FlipbookCell(frame);
            if ((_transform.Flags & FlipbookBlendFlag) != 0)
            {
                var next = FlipbookCell((frame + 1) % _transform.Count) - offset;
                FlipbookBlend = new(next.X, next.Y, frames - (int)frames, -1f);
            }
        }

        offset.X += _transform.Scroll.X * Wave(seconds, _transform.ScrollFrequency.X);
        offset.Y += _transform.Scroll.Y * Wave(seconds, _transform.ScrollFrequency.Y);
        if (_transform.ScaleFrequency.X != 0f)
        {
            scale.X = PulsedScale(MathF.Sin(_transform.ScaleFrequency.X * seconds) * _transform.Scale.X);
            offset.X += (scale.X - 1f) * -0.5f;
        }
        if (_transform.ScaleFrequency.Y != 0f)
        {
            scale.Y = PulsedScale(MathF.Sin(_transform.ScaleFrequency.Y * seconds) * _transform.Scale.Y);
            offset.Y += (scale.Y - 1f) * -0.5f;
        }

        var rowA = new Vector4(scale.X, 0f, 0f, offset.X);
        var rowB = new Vector4(0f, scale.Y, 0f, offset.Y);
        var angle = _transform.RotateFrequency != 0f
            ? MathF.Sin(seconds * _transform.RotateFrequency) * _transform.Rotate
            : _transform.Rotate * seconds;
        if (_transform.RotateFrequency != 0f || _transform.Rotate != 0f) (rowA, rowB) = RotateAboutCenter(rowA, rowB, angle);
        Transform = (rowA, rowB);
    }

    private void AdvanceBounce(float seconds, float elapsedSeconds)
    {
        var rangeU = _transform.ScaleFrequency;
        var rangeV = _transform.ScrollFrequency;
        if ((_transform.Flags & BounceScaleFlag) != 0)
        {
            var scale = _bounceScale + _bounceScaleSpeed * elapsedSeconds;
            if (scale.X < rangeU.X || scale.X > rangeU.Y) _bounceScaleSpeed.X = -_bounceScaleSpeed.X;
            if (scale.Y < rangeV.X || scale.Y > rangeV.Y) _bounceScaleSpeed.Y = -_bounceScaleSpeed.Y;
            _bounceScale = new(Math.Clamp(scale.X, rangeU.X, Math.Max(rangeU.X, rangeU.Y)), Math.Clamp(scale.Y, rangeV.X, Math.Max(rangeV.X, rangeV.Y)));
        }
        else
        {
            _bounceScale = Vector2.One;
            var widthU = MathF.Abs(rangeU.Y - rangeU.X);
            if (widthU > 0f) _bounceScale.X = MathF.Sin(_transform.Scale.X / widthU * seconds) * widthU + rangeU.Y;
            var widthV = MathF.Abs(rangeV.Y - rangeV.X);
            if (widthV > 0f) _bounceScale.Y = MathF.Sin(_transform.Scale.Y / widthV * seconds) * widthV + rangeV.Y;
        }

        var offset = new Vector2(seconds * _transform.Scroll.X % 1f, seconds * _transform.Scroll.Y % 1f);
        var angle = _transform.Rotate * seconds;
        var cosine = _transform.Rotate != 0f ? MathF.Cos(angle) : 1f;
        var sine = _transform.Rotate != 0f ? MathF.Sin(angle) : 0f;
        var scaledCosineU = _bounceScale.X * cosine;
        var scaledSineV = _bounceScale.Y * sine;
        var scaledCosineV = _bounceScale.Y * cosine;
        var scaledSineU = _bounceScale.X * sine;
        FlipbookBlend = null;
        Transform = (
            new(scaledCosineU, -scaledSineV, 0f, offset.X - 0.5f - (scaledCosineU - scaledSineV) * 0.5f + 0.5f),
            new(scaledSineU, scaledCosineV, 0f, offset.Y - 0.5f - (scaledCosineV + scaledSineU) * 0.5f + 0.5f));
    }

    private Vector2 FlipbookCell(uint frame) =>
        new((float)(frame % _transform.Columns) / _transform.Columns, (float)(frame / _transform.Columns) / Math.Max(_transform.Rows, (byte)1));

    private static float Wave(float seconds, float frequency) => frequency != 0f ? MathF.Sin(seconds * frequency) : seconds;

    private static float PulsedScale(float pulse) => pulse == -1f ? 1f : pulse + 1f;

    private static (Vector4 RowA, Vector4 RowB) RotateAboutCenter(Vector4 rowA, Vector4 rowB, float angle)
    {
        var cosine = MathF.Cos(angle);
        var sine = MathF.Sin(angle);
        var centeredA = rowA - new Vector4(0f, 0f, 0f, 0.5f);
        var centeredB = rowB - new Vector4(0f, 0f, 0f, 0.5f);
        return (
            cosine * centeredA - sine * centeredB + new Vector4(0f, 0f, 0f, 0.5f),
            sine * centeredA + cosine * centeredB + new Vector4(0f, 0f, 0f, 0.5f));
    }
}
